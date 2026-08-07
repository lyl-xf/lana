using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// HttpClient 协议会话：登录（可选）+ 查询，按物模型配置的 key/value JSON 路径展开为上报字典。
///
/// 物模型示例：HttpKeyJsonPath=<c>body.name</c>，HttpValueJsonPath=<c>body.value</c>。
/// 当 responseBodyJsonPath 指向数组时，对每个元素取 name→key、value→value，合并进设备上报 data。
/// </summary>
public sealed class HttpClientDeviceSession : IDeviceProtocolSession
{
    private readonly System.Net.Http.HttpClient _http;
    private readonly HttpClientConfig _config;

    private string? _token;
    private bool _opened;
    private JsonNode? _cachedBody;
    private DateTime _cacheTimestamp = DateTime.MinValue;
    private bool _disposed;

    private sealed class HeaderItem
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    private sealed class HttpClientConfig
    {
        public string LoginUrl { get; set; } = "";
        public string LoginMethod { get; set; } = "POST";
        public string LoginBody { get; set; } = "";
        public List<HeaderItem> LoginHeaders { get; set; } = new();
        /// <summary>兼容旧配置；查询头请用 <see cref="QueryHeaders"/>，Value 自动绑定登录 Token。</summary>
        public string AuthHeaderName { get; set; } = "";
        public string TokenJsonPath { get; set; } = "data.token";
        public string QueryUrl { get; set; } = "";
        public string QueryMethod { get; set; } = "GET";
        public string QueryBody { get; set; } = "";
        /// <summary>查询请求头：只配 Key，Value 自动使用登录接口按 TokenJsonPath 提取的 Token。</summary>
        public List<HeaderItem> QueryHeaders { get; set; } = new();
        /// <summary>逻辑 body 根路径，如 data 或 data[0]。</summary>
        public string ResponseBodyJsonPath { get; set; } = "data";
        /// <summary>
        /// 相对 body 的嵌套明细路径（点号分段，遇数组自动展开）。
        /// 例：dataItem.registerItem → 遍历 body 下所有 registerItem 元素再取 key/value。
        /// </summary>
        public string NestedItemsJsonPath { get; set; } = "";
    }

    public HttpClientDeviceSession(Device device)
    {
        _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        _config = string.IsNullOrWhiteSpace(device.PluginConfigJson)
            ? new HttpClientConfig()
            : JsonSerializer.Deserialize<HttpClientConfig>(device.PluginConfigJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new HttpClientConfig();
    }

    public bool IsConnected => _opened;

    public ProtocolResult Open()
    {
        if (string.IsNullOrWhiteSpace(_config.LoginUrl))
        {
            _token = null;
            _opened = true;
            return ProtocolResult.Ok();
        }

        try
        {
            var loginResponse = SendRequestAsync(
                    _config.LoginUrl,
                    _config.LoginMethod,
                    _config.LoginBody,
                    headers: _config.LoginHeaders,
                    bindLoginToken: false)
                .GetAwaiter().GetResult();

            _token = ExtractJsonPathValue(loginResponse, _config.TokenJsonPath);

            if (string.IsNullOrEmpty(_token))
                return ProtocolResult.Fail($"无法从登录响应中提取 token，路径: {_config.TokenJsonPath}，响应: {Truncate(loginResponse, 500)}");

            _opened = true;
            return ProtocolResult.Ok();
        }
        catch (Exception ex)
        {
            _token = null;
            _opened = false;
            return ProtocolResult.Fail($"登录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 兼容接口：按单个 JSON 路径取值。HttpClient 上报请优先用 <see cref="ReadKeyValueMap"/>。
    /// </summary>
    public ProtocolResult<object?> Read(string address, ProtocolDataType dataType)
    {
        var ensure = EnsureBodyCached();
        if (!ensure.Success)
            return ProtocolResult<object?>.Fail(ensure.Error ?? "查询失败");

        if (string.IsNullOrWhiteSpace(address))
            return ProtocolResult<object?>.Fail("JSON 路径不能为空");

        var path = NormalizeBodyPath(address);
        var valueNode = NavigateFromNode(_cachedBody, path);
        if (valueNode == null)
            return ProtocolResult<object?>.Fail($"未找到 JSON 路径 '{address}'");

        return ProtocolResult<object?>.Ok(NodeToValue(valueNode));
    }

    /// <summary>
    /// 按 key/value JSON 路径从 body（及可选嵌套明细）展开键值对。
    /// </summary>
    public ProtocolResult<Dictionary<string, object?>> ReadKeyValueMap(string keyJsonPath, string valueJsonPath)
    {
        var ensure = EnsureBodyCached();
        if (!ensure.Success)
            return ProtocolResult<Dictionary<string, object?>>.Fail(ensure.Error ?? "查询失败");

        if (string.IsNullOrWhiteSpace(keyJsonPath) || string.IsNullOrWhiteSpace(valueJsonPath))
            return ProtocolResult<Dictionary<string, object?>>.Fail("请配置 HttpKeyJsonPath 与 HttpValueJsonPath（如 body.registerName / body.data）");

        try
        {
            var keyPath = NormalizeBodyPath(keyJsonPath);
            var valuePath = NormalizeBodyPath(valueJsonPath);
            var result = new Dictionary<string, object?>();

            // 定点下标路径：直接从 body 根取一对
            if (PathHasIndex(keyPath) || PathHasIndex(valuePath))
            {
                if (_cachedBody != null)
                    TryAddPair(result, _cachedBody, keyPath, valuePath);
                return ProtocolResult<Dictionary<string, object?>>.Ok(result);
            }

            foreach (var item in CollectItems(_cachedBody, _config.NestedItemsJsonPath))
                TryAddPair(result, item, keyPath, valuePath);

            return ProtocolResult<Dictionary<string, object?>>.Ok(result);
        }
        catch (Exception ex)
        {
            _cachedBody = null;
            return ProtocolResult<Dictionary<string, object?>>.Fail($"解析键值对失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 body 收集明细节点。nestedPath 如 <c>dataItem.registerItem</c>：遇数组则展开合并。
    /// nestedPath 为空时：body 为数组则取其元素，否则取 body 自身。
    /// </summary>
    private static List<JsonNode> CollectItems(JsonNode? body, string? nestedPath)
    {
        var result = new List<JsonNode>();
        if (body == null) return result;

        List<JsonNode> current = body is JsonArray rootArr
            ? rootArr.Where(n => n != null).Cast<JsonNode>().ToList()
            : new List<JsonNode> { body };

        if (string.IsNullOrWhiteSpace(nestedPath))
            return current;

        foreach (var seg in nestedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = new List<JsonNode>();
            foreach (var node in current)
            {
                var child = node[seg];
                if (child is JsonArray arr)
                    next.AddRange(arr.Where(n => n != null).Cast<JsonNode>());
                else if (child != null)
                    next.Add(child);
            }
            current = next;
        }

        return current;
    }

    public ProtocolResult Write(string address, ProtocolDataType dataType, string? value)
    {
        return ProtocolResult.Fail("HttpClient 协议暂不支持写入操作");
    }

    public void Close()
    {
        _token = null;
        _opened = false;
        _cachedBody = null;
        _cacheTimestamp = DateTime.MinValue;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        try { _http.Dispose(); } catch { /* ignore */ }
    }

    private ProtocolResult EnsureBodyCached()
    {
        if (!_opened)
            return ProtocolResult.Fail("会话未打开，请先调用 Open()");

        if (string.IsNullOrWhiteSpace(_config.QueryUrl))
            return ProtocolResult.Fail("HttpClient 协议未配置查询接口地址 (queryUrl)");

        try
        {
            if (_cachedBody == null || (DateTime.UtcNow - _cacheTimestamp).TotalSeconds > 30)
            {
                var queryResponse = SendRequestAsync(
                        _config.QueryUrl,
                        _config.QueryMethod,
                        _config.QueryBody,
                        headers: ResolveQueryHeaderKeys(),
                        bindLoginToken: true)
                    .GetAwaiter().GetResult();

                _cachedBody = ResolveBodyNode(queryResponse, _config.ResponseBodyJsonPath);
                _cacheTimestamp = DateTime.UtcNow;
            }

            if (_cachedBody == null)
                return ProtocolResult.Fail($"无法定位响应 body，路径: {_config.ResponseBodyJsonPath}");

            return ProtocolResult.Ok();
        }
        catch (Exception ex)
        {
            _cachedBody = null;
            return ProtocolResult.Fail($"查询失败: {ex.Message}");
        }
    }

    private static void TryAddPair(Dictionary<string, object?> result, JsonNode item, string keyPath, string valuePath)
    {
        var keyNode = NavigateFromNode(item, keyPath);
        if (keyNode == null) return;
        var key = NormalizeReportKey(NodeToKeyString(keyNode));
        if (string.IsNullOrWhiteSpace(key)) return;

        var valueNode = NavigateFromNode(item, valuePath);
        result[key] = valueNode == null ? null : NodeToValue(valueNode);
    }

    /// <summary>
    /// 上报 key 规范化：去掉括号及括号内内容；若含中文则转拼音（小写），否则原样保留。
    /// 例：当前雨量（今日雨量）→ dangqianyuliang；temp → temp
    /// </summary>
    private static string NormalizeReportKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";

        // 去掉 () / （）及其内容
        var s = System.Text.RegularExpressions.Regex.Replace(key.Trim(), @"\([^()]*\)|（[^（）]*）", "");
        s = s.Trim();
        if (s.Length == 0) return "";

        if (!ContainsChinese(s))
            return s;

        try
        {
            // WoAiZhongGuo → woaizhongguo
            return ToolGood.Words.Pinyin.WordsHelper.GetPinyin(s).ToLowerInvariant();
        }
        catch
        {
            return s;
        }
    }

    private static bool ContainsChinese(string text)
    {
        foreach (var ch in text)
        {
            if (ch >= 0x3400 && ch <= 0x4DBF) return true; // 扩展 A
            if (ch >= 0x4E00 && ch <= 0x9FFF) return true; // 基本汉字
            if (ch >= 0xF900 && ch <= 0xFAFF) return true; // 兼容汉字
        }
        return false;
    }

    /// <summary>取作字典 key 的明文（避免 JsonNode.ToString 产出 \uXXXX）。</summary>
    private static string? NodeToKeyString(JsonNode node)
    {
        if (node is JsonValue jv)
        {
            if (jv.TryGetValue(out string? s)) return s;
            if (jv.TryGetValue(out long l)) return l.ToString();
            if (jv.TryGetValue(out double d)) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (jv.TryGetValue(out bool b)) return b ? "true" : "false";
        }
        // 非标量极少作为 key；去掉多余引号
        var raw = node.ToJsonString();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return JsonSerializer.Deserialize<string>(raw);
        return raw;
    }

    /// <summary>查询头 Key 列表；兼容旧配置 AuthHeaderName。</summary>
    private List<HeaderItem> ResolveQueryHeaderKeys()
    {
        var keys = (_config.QueryHeaders ?? new List<HeaderItem>())
            .Where(h => !string.IsNullOrWhiteSpace(h.Key))
            .Select(h => new HeaderItem { Key = h.Key.Trim() })
            .ToList();

        if (keys.Count == 0 && !string.IsNullOrWhiteSpace(_config.AuthHeaderName))
            keys.Add(new HeaderItem { Key = _config.AuthHeaderName.Trim() });

        return keys;
    }

    private async Task<string> SendRequestAsync(
        string url,
        string method,
        string? body,
        IEnumerable<HeaderItem>? headers,
        bool bindLoginToken)
    {
        using var request = new HttpRequestMessage(
            string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Post
                : HttpMethod.Get,
            url);

        if (headers != null)
        {
            foreach (var h in headers)
            {
                if (string.IsNullOrWhiteSpace(h.Key)) continue;
                var key = h.Key.Trim();
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 查询接口：HeaderValue 自动绑定登录 Token；登录接口：使用配置的 Value
                var value = bindLoginToken ? (_token ?? "") : (h.Value ?? "");
                if (bindLoginToken && string.IsNullOrEmpty(_token))
                    continue;

                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static JsonNode? ResolveBodyNode(string json, string? bodyPath)
    {
        var root = JsonNode.Parse(json);
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(bodyPath)) return root;
        return NavigateFromNode(root, bodyPath.Trim());
    }

    /// <summary>
    /// 去掉逻辑 body 前缀。<c>body.name</c> → <c>name</c>；<c>body[0].name</c> → <c>[0].name</c>。
    /// </summary>
    private static string NormalizeBodyPath(string address)
    {
        var path = address.Trim();
        if (string.Equals(path, "body", StringComparison.OrdinalIgnoreCase))
            return "";
        if (path.StartsWith("body.", StringComparison.OrdinalIgnoreCase))
            return path[5..];
        if (path.StartsWith("body[", StringComparison.OrdinalIgnoreCase))
            return path[4..]; // body[0].name → [0].name
        return path;
    }

    private static bool PathHasIndex(string path) => path.Contains('[', StringComparison.Ordinal);

    private static string? ExtractJsonPathValue(string json, string path)
    {
        var node = NavigateFromNode(JsonNode.Parse(json), path);
        if (node == null) return null;
        return NodeToKeyString(node);
    }

    /// <summary>
    /// 支持 <c>name</c>、<c>0</c>、<c>[0]</c>、<c>items[0]</c>、<c>items[0].name</c>、<c>[0].name</c>。
    /// </summary>
    private static JsonNode? NavigateFromNode(JsonNode? start, string? path)
    {
        if (start == null) return null;
        if (string.IsNullOrWhiteSpace(path)) return start;

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = start;
        foreach (var seg in segments)
        {
            if (current == null) return null;
            current = NavigateSegment(current, seg);
        }
        return current;
    }

    private static JsonNode? NavigateSegment(JsonNode current, string seg)
    {
        // [0] 或 [0][1]
        if (seg.StartsWith('['))
            return ApplyIndexers(current, seg);

        var bracketIdx = seg.IndexOf('[');
        if (bracketIdx > 0)
        {
            var propName = seg[..bracketIdx];
            var next = current[propName];
            if (next == null) return null;
            return ApplyIndexers(next, seg[bracketIdx..]);
        }

        if (int.TryParse(seg, out var onlyIdx) && current is JsonArray onlyArr)
            return onlyIdx >= 0 && onlyIdx < onlyArr.Count ? onlyArr[onlyIdx] : null;

        return current[seg];
    }

    /// <summary>依次应用一段或多段下标，如 <c>[0]</c>、<c>[0][1]</c>。</summary>
    private static JsonNode? ApplyIndexers(JsonNode current, string indexerPart)
    {
        var i = 0;
        JsonNode? node = current;
        while (i < indexerPart.Length && node != null)
        {
            if (indexerPart[i] != '[') return null;
            var end = indexerPart.IndexOf(']', i + 1);
            if (end < 0) return null;
            var indexStr = indexerPart[(i + 1)..end];
            if (!int.TryParse(indexStr, out var idx)) return null;
            if (node is not JsonArray arr || idx < 0 || idx >= arr.Count)
                return null;
            node = arr[idx];
            i = end + 1;
        }
        return node;
    }

    private static object? NodeToValue(JsonNode node)
    {
        switch (node.GetValueKind())
        {
            case JsonValueKind.String:
                return node.GetValue<string>();
            case JsonValueKind.Number:
                if (node is JsonValue jv)
                {
                    if (jv.TryGetValue(out long l)) return l;
                    if (jv.TryGetValue(out double d)) return d;
                }
                return node.ToString();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null;
            default:
                return node.DeepClone();
        }
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
