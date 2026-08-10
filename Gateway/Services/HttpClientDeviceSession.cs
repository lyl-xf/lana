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
    /// <summary>底层 HTTP 客户端，用于发送登录与查询请求。</summary>
    private readonly System.Net.Http.HttpClient _http;

    /// <summary>从设备 PluginConfigJson 反序列化得到的协议配置。</summary>
    private readonly HttpClientConfig _config;

    /// <summary>登录接口按 TokenJsonPath 提取的认证令牌；无登录时为 null。</summary>
    private string? _token;

    /// <summary>会话是否已通过 Open() 成功打开。</summary>
    private bool _opened;

    /// <summary>最近一次查询响应解析后的逻辑 body 节点，供 Read/ReadKeyValueMap 复用。</summary>
    private JsonNode? _cachedBody;

    /// <summary>查询 body 缓存写入时间（UTC），用于 30 秒 TTL 判定。</summary>
    private DateTime _cacheTimestamp = DateTime.MinValue;

    /// <summary>是否已释放 HttpClient 等资源。</summary>
    private bool _disposed;

    /// <summary>HTTP 请求头键值对，用于 LoginHeaders / QueryHeaders 配置项。</summary>
    private sealed class HeaderItem
    {
        /// <summary>请求头名称。</summary>
        public string Key { get; set; } = "";

        /// <summary>请求头值；查询头在 bindLoginToken 模式下可留空，由登录 Token 自动填充。</summary>
        public string Value { get; set; } = "";
    }

    /// <summary>
    /// HttpClient 协议插件配置，对应设备 PluginConfigJson 字段结构。
    /// </summary>
    private sealed class HttpClientConfig
    {
        /// <summary>登录接口 URL；为空则跳过登录，直接视为已连接。</summary>
        public string LoginUrl { get; set; } = "";

        /// <summary>登录 HTTP 方法，默认 POST。</summary>
        public string LoginMethod { get; set; } = "POST";

        /// <summary>登录请求体（通常为 JSON 字符串）。</summary>
        public string LoginBody { get; set; } = "";

        /// <summary>登录请求附加头列表。</summary>
        public List<HeaderItem> LoginHeaders { get; set; } = new();

        /// <summary>兼容旧配置；查询头请用 <see cref="QueryHeaders"/>，Value 自动绑定登录 Token。</summary>
        public string AuthHeaderName { get; set; } = "";

        /// <summary>登录响应中提取 Token 的 JSON 路径，默认 data.token。</summary>
        public string TokenJsonPath { get; set; } = "data.token";

        /// <summary>数据查询接口 URL。</summary>
        public string QueryUrl { get; set; } = "";

        /// <summary>查询 HTTP 方法，默认 GET。</summary>
        public string QueryMethod { get; set; } = "GET";

        /// <summary>查询请求体（POST 时使用）。</summary>
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

    /// <summary>
    /// 根据设备物模型配置创建 HttpClient 协议会话。
    /// </summary>
    /// <param name="device">设备实体，PluginConfigJson 含登录/查询 URL、JSON 路径等配置。</param>
    public HttpClientDeviceSession(Device device)
    {
        // 初始化 HTTP 客户端，统一 8 秒超时
        _http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        // 反序列化插件配置；空配置时使用默认 HttpClientConfig
        _config = string.IsNullOrWhiteSpace(device.PluginConfigJson)
            ? new HttpClientConfig()
            : JsonSerializer.Deserialize<HttpClientConfig>(device.PluginConfigJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new HttpClientConfig();
    }

    /// <summary>会话是否已成功打开（登录完成或未配置登录 URL）。</summary>
    public bool IsConnected => _opened;

    /// <summary>
    /// 打开会话：若配置了登录 URL 则先登录并提取 Token，否则直接标记为已连接。
    /// </summary>
    /// <returns>成功返回 <see cref="ProtocolResult.Ok"/>；登录或 Token 提取失败返回 Fail。</returns>
    public ProtocolResult Open()
    {
        // 无登录 URL：跳过认证，直接进入可查询状态
        if (string.IsNullOrWhiteSpace(_config.LoginUrl))
        {
            _token = null;
            _opened = true;
            return ProtocolResult.Ok();
        }

        try
        {
            // 协议 IO：发送登录请求，不绑定 Token 到请求头
            var loginResponse = SendRequestAsync(
                    _config.LoginUrl,
                    _config.LoginMethod,
                    _config.LoginBody,
                    headers: _config.LoginHeaders,
                    bindLoginToken: false)
                .GetAwaiter().GetResult();

            // JSON 解析：按 TokenJsonPath 从响应中提取 Token
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
    /// <param name="address">JSON 路径，如 body.temperature 或 data[0].value。</param>
    /// <param name="dataType">数据类型（HttpClient 路径读取时未做类型转换）。</param>
    /// <returns>成功时返回路径对应值；失败时返回 Fail 及错误信息。</returns>
    public ProtocolResult<object?> Read(string address, ProtocolDataType dataType)
    {
        // 缓存：确保查询 body 已加载（30 秒 TTL）
        var ensure = EnsureBodyCached();
        if (!ensure.Success)
            return ProtocolResult<object?>.Fail(ensure.Error ?? "查询失败");

        if (string.IsNullOrWhiteSpace(address))
            return ProtocolResult<object?>.Fail("JSON 路径不能为空");

        // JSON 路径导航：去掉 body 前缀后从缓存 body 定位节点
        var path = NormalizeBodyPath(address);
        var valueNode = NavigateFromNode(_cachedBody, path);
        if (valueNode == null)
            return ProtocolResult<object?>.Fail($"未找到 JSON 路径 '{address}'");

        // 映射：JsonNode → CLR 对象
        return ProtocolResult<object?>.Ok(NodeToValue(valueNode));
    }

    /// <summary>
    /// 按 key/value JSON 路径从 body（及可选嵌套明细）展开键值对。
    /// </summary>
    /// <param name="keyJsonPath">键字段相对 body 的路径，如 body.registerName。</param>
    /// <param name="valueJsonPath">值字段相对 body 的路径，如 body.data。</param>
    /// <returns>成功时返回上报字典；失败时返回 Fail 及错误信息。</returns>
    public ProtocolResult<Dictionary<string, object?>> ReadKeyValueMap(string keyJsonPath, string valueJsonPath)
    {
        // 缓存：确保查询 body 已加载
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

            // 定点下标路径：直接从 body 根取一对，不遍历嵌套明细
            if (PathHasIndex(keyPath) || PathHasIndex(valuePath))
            {
                if (_cachedBody != null)
                    TryAddPair(result, _cachedBody, keyPath, valuePath);
                return ProtocolResult<Dictionary<string, object?>>.Ok(result);
            }

            // 映射：遍历 NestedItemsJsonPath 展开后的每个明细节点，提取 key/value 对
            foreach (var item in CollectItems(_cachedBody, _config.NestedItemsJsonPath))
                TryAddPair(result, item, keyPath, valuePath);

            return ProtocolResult<Dictionary<string, object?>>.Ok(result);
        }
        catch (Exception ex)
        {
            // 解析异常时清缓存，下次强制重新查询
            _cachedBody = null;
            return ProtocolResult<Dictionary<string, object?>>.Fail($"解析键值对失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 body 收集明细节点。nestedPath 如 <c>dataItem.registerItem</c>：遇数组则展开合并。
    /// nestedPath 为空时：body 为数组则取其元素，否则取 body 自身。
    /// </summary>
    /// <param name="body">逻辑 body 根节点。</param>
    /// <param name="nestedPath">相对 body 的嵌套路径，点号分段。</param>
    /// <returns>可用于 key/value 提取的 JsonNode 列表。</returns>
    private static List<JsonNode> CollectItems(JsonNode? body, string? nestedPath)
    {
        var result = new List<JsonNode>();
        if (body == null) return result;

        // body 为数组时展开为多个元素；否则视为单元素列表
        List<JsonNode> current = body is JsonArray rootArr
            ? rootArr.Where(n => n != null).Cast<JsonNode>().ToList()
            : new List<JsonNode> { body };

        if (string.IsNullOrWhiteSpace(nestedPath))
            return current;

        // 逐段导航 nestedPath，遇 JsonArray 自动展开合并
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

    /// <summary>
    /// HttpClient 协议不支持写入操作。
    /// </summary>
    /// <param name="address">目标地址（未使用）。</param>
    /// <param name="dataType">数据类型（未使用）。</param>
    /// <param name="value">写入值（未使用）。</param>
    /// <returns>始终返回 Fail。</returns>
    public ProtocolResult Write(string address, ProtocolDataType dataType, string? value)
    {
        return ProtocolResult.Fail("HttpClient 协议暂不支持写入操作");
    }

    /// <summary>
    /// 关闭会话：清除 Token、连接状态及查询 body 缓存。
    /// </summary>
    public void Close()
    {
        _token = null;
        _opened = false;
        _cachedBody = null;
        _cacheTimestamp = DateTime.MinValue;
    }

    /// <summary>
    /// 释放 HttpClient 等资源；重复调用安全。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
        try { _http.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>
    /// 确保查询响应 body 已缓存；缓存超过 30 秒或未命中时重新发起查询请求。
    /// </summary>
    /// <returns>body 可用时返回 Ok；未打开、未配置 URL 或查询失败时返回 Fail。</returns>
    private ProtocolResult EnsureBodyCached()
    {
        if (!_opened)
            return ProtocolResult.Fail("会话未打开，请先调用 Open()");

        if (string.IsNullOrWhiteSpace(_config.QueryUrl))
            return ProtocolResult.Fail("HttpClient 协议未配置查询接口地址 (queryUrl)");

        try
        {
            // 缓存失效：无缓存或超过 30 秒 TTL 时重新查询
            if (_cachedBody == null || (DateTime.UtcNow - _cacheTimestamp).TotalSeconds > 30)
            {
                // 协议 IO：发送查询请求，查询头自动绑定登录 Token
                var queryResponse = SendRequestAsync(
                        _config.QueryUrl,
                        _config.QueryMethod,
                        _config.QueryBody,
                        headers: ResolveQueryHeaderKeys(),
                        bindLoginToken: true)
                    .GetAwaiter().GetResult();

                // JSON 解析：按 ResponseBodyJsonPath 定位逻辑 body 并写入缓存
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

    /// <summary>
    /// 从单个 JsonNode 按 key/value 路径提取一对键值并写入结果字典；key 经规范化处理。
    /// </summary>
    /// <param name="result">累积的上报字典。</param>
    /// <param name="item">当前明细节点。</param>
    /// <param name="keyPath">键字段路径（已 NormalizeBodyPath）。</param>
    /// <param name="valuePath">值字段路径（已 NormalizeBodyPath）。</param>
    private static void TryAddPair(Dictionary<string, object?> result, JsonNode item, string keyPath, string valuePath)
    {
        // JSON 路径导航：定位 key 节点
        var keyNode = NavigateFromNode(item, keyPath);
        if (keyNode == null) return;

        // 映射：key 转字符串并规范化（去括号、中文转拼音）
        var key = NormalizeReportKey(NodeToKeyString(keyNode));
        if (string.IsNullOrWhiteSpace(key)) return;

        // JSON 路径导航：定位 value 节点并映射为 CLR 值
        var valueNode = NavigateFromNode(item, valuePath);
        result[key] = valueNode == null ? null : NodeToValue(valueNode);
    }

    /// <summary>
    /// 上报 key 规范化：去掉括号及括号内内容；若含中文则转拼音（小写），否则原样保留。
    /// 例：当前雨量（今日雨量）→ dangqianyuliang；temp → temp
    /// </summary>
    /// <param name="key">原始 key 字符串。</param>
    /// <returns>规范化后的上报 key；空或无效时返回空字符串。</returns>
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
            // 映射：中文 key 转拼音小写，便于 MQTT/物模型统一标识
            return ToolGood.Words.Pinyin.WordsHelper.GetPinyin(s).ToLowerInvariant();
        }
        catch
        {
            return s;
        }
    }

    /// <summary>
    /// 判断字符串是否包含 CJK 汉字（基本区、扩展 A、兼容区）。
    /// </summary>
    /// <param name="text">待检测文本。</param>
    /// <returns>含中文返回 true，否则 false。</returns>
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
    /// <param name="node">JSON 节点。</param>
    /// <returns>适合作为上报 key 的字符串；非标量时返回 JSON 字符串或反序列化后的字符串。</returns>
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
    /// <returns>仅含 Key 的 HeaderItem 列表，Value 由 SendRequestAsync 绑定 Token。</returns>
    private List<HeaderItem> ResolveQueryHeaderKeys()
    {
        var keys = (_config.QueryHeaders ?? new List<HeaderItem>())
            .Where(h => !string.IsNullOrWhiteSpace(h.Key))
            .Select(h => new HeaderItem { Key = h.Key.Trim() })
            .ToList();

        // 兼容旧配置：未配 QueryHeaders 时回退 AuthHeaderName
        if (keys.Count == 0 && !string.IsNullOrWhiteSpace(_config.AuthHeaderName))
            keys.Add(new HeaderItem { Key = _config.AuthHeaderName.Trim() });

        return keys;
    }

    /// <summary>
    /// 异步发送 HTTP 请求并返回响应体字符串。
    /// </summary>
    /// <param name="url">请求 URL。</param>
    /// <param name="method">HTTP 方法（GET/POST）。</param>
    /// <param name="body">POST 请求体；GET 时可为 null。</param>
    /// <param name="headers">附加请求头列表。</param>
    /// <param name="bindLoginToken">为 true 时 Header Value 自动使用登录 Token；为 false 时使用配置 Value。</param>
    /// <returns>响应体 UTF-8 字符串。</returns>
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
                // Content-Type 由 StringContent 自动设置，跳过重复添加
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    continue;

                // 查询接口：HeaderValue 自动绑定登录 Token；登录接口：使用配置的 Value
                var value = bindLoginToken ? (_token ?? "") : (h.Value ?? "");
                if (bindLoginToken && string.IsNullOrEmpty(_token))
                    continue;

                request.Headers.TryAddWithoutValidation(key, value);
            }
        }

        // POST 且 body 非空时设置 JSON 请求体
        if (string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(body))
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // 协议 IO：发送请求并校验 HTTP 状态码
        using var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// 解析 JSON 响应并按 bodyPath 定位逻辑 body 节点。
    /// </summary>
    /// <param name="json">原始 JSON 响应字符串。</param>
    /// <param name="bodyPath">body 根路径；为空则返回整棵 JSON 根。</param>
    /// <returns>定位到的 JsonNode；解析或导航失败时返回 null。</returns>
    private static JsonNode? ResolveBodyNode(string json, string? bodyPath)
    {
        // JSON 解析：加载完整响应树
        var root = JsonNode.Parse(json);
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(bodyPath)) return root;
        return NavigateFromNode(root, bodyPath.Trim());
    }

    /// <summary>
    /// 去掉逻辑 body 前缀。<c>body.name</c> → <c>name</c>；<c>body[0].name</c> → <c>[0].name</c>。
    /// </summary>
    /// <param name="address">物模型配置的 JSON 路径。</param>
    /// <returns>相对 body 根的路径片段。</returns>
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

    /// <summary>
    /// 判断路径是否含数组下标（如 [0] 或 items[1]）。
    /// </summary>
    /// <param name="path">JSON 路径字符串。</param>
    /// <returns>含 '[' 返回 true。</returns>
    private static bool PathHasIndex(string path) => path.Contains('[', StringComparison.Ordinal);

    /// <summary>
    /// 从 JSON 字符串按点号/下标路径提取标量值字符串。
    /// </summary>
    /// <param name="json">JSON 文本。</param>
    /// <param name="path">JSON 路径，如 data.token。</param>
    /// <returns>路径对应值的字符串形式；未找到时返回 null。</returns>
    private static string? ExtractJsonPathValue(string json, string path)
    {
        var node = NavigateFromNode(JsonNode.Parse(json), path);
        if (node == null) return null;
        return NodeToKeyString(node);
    }

    /// <summary>
    /// 支持 <c>name</c>、<c>0</c>、<c>[0]</c>、<c>items[0]</c>、<c>items[0].name</c>、<c>[0].name</c>。
    /// </summary>
    /// <param name="start">起始 JsonNode。</param>
    /// <param name="path">点号分段路径，可含数组下标。</param>
    /// <returns>导航到的节点；任一段失败时返回 null。</returns>
    private static JsonNode? NavigateFromNode(JsonNode? start, string? path)
    {
        if (start == null) return null;
        if (string.IsNullOrWhiteSpace(path)) return start;

        // 按 '.' 分段，逐段 NavigateSegment
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = start;
        foreach (var seg in segments)
        {
            if (current == null) return null;
            current = NavigateSegment(current, seg);
        }
        return current;
    }

    /// <summary>
    /// 导航单个路径段：支持下标前缀 [n]、属性带下标 prop[n]、纯数字段（数组索引）及普通属性名。
    /// </summary>
    /// <param name="current">当前节点。</param>
    /// <param name="seg">单段路径，如 name、[0]、items[1]。</param>
    /// <returns>下一段起始节点；无法导航时返回 null。</returns>
    private static JsonNode? NavigateSegment(JsonNode current, string seg)
    {
        // [0] 或 [0][1]：纯下标段
        if (seg.StartsWith('['))
            return ApplyIndexers(current, seg);

        var bracketIdx = seg.IndexOf('[');
        if (bracketIdx > 0)
        {
            // prop[n]：先取属性再应用下标
            var propName = seg[..bracketIdx];
            var next = current[propName];
            if (next == null) return null;
            return ApplyIndexers(next, seg[bracketIdx..]);
        }

        // 纯数字段且当前为数组：视为数组下标
        if (int.TryParse(seg, out var onlyIdx) && current is JsonArray onlyArr)
            return onlyIdx >= 0 && onlyIdx < onlyArr.Count ? onlyArr[onlyIdx] : null;

        return current[seg];
    }

    /// <summary>依次应用一段或多段下标，如 <c>[0]</c>、<c>[0][1]</c>。</summary>
    /// <param name="current">当前 JsonNode（通常为 JsonArray）。</param>
    /// <param name="indexerPart">下标片段，如 [0] 或 [0][1]。</param>
    /// <returns>应用全部下标后的节点；越界或格式错误时返回 null。</returns>
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

    /// <summary>
    /// 将 JsonNode 映射为 CLR 对象，供上报字典使用。
    /// </summary>
    /// <param name="node">JSON 节点。</param>
    /// <returns>字符串、数值、布尔、null 或对象/数组的 DeepClone。</returns>
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
                // 对象/数组：深拷贝保留结构
                return node.DeepClone();
        }
    }

    /// <summary>
    /// 截断过长字符串，用于错误信息中附加响应片段。
    /// </summary>
    /// <param name="s">原始字符串。</param>
    /// <param name="maxLen">最大保留长度。</param>
    /// <returns>截断后字符串，超长时末尾追加 "..."。</returns>
    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";
}
