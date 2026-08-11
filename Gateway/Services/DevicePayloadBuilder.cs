using Lana.Gateway.Models;
using Lana.Gateway.Protocol;

namespace Lana.Gateway.Services;

/// <summary>
/// 设备采集 payload 构建（Poll / MQTT 即时读共用）。
/// </summary>
public static class DevicePayloadBuilder
{
    /// <summary>
    /// 填充设备读点 payload。HttpClient 按 JSON 路径；其它协议按 IncludeInPoll 变量逐点 Read。
    /// </summary>
    public static void FillPollPayload(
        Device device,
        IDeviceProtocolSession session,
        Dictionary<string, object> payload)
    {
        if (device.ProtocolType == ProtocolType.HttpClient && session is HttpClientDeviceSession httpSession)
        {
            foreach (var variable in device.Variables)
            {
                if (!DeviceVariablePollRules.ShouldPollHttpPath(variable))
                    continue;

                try
                {
                    var map = httpSession.ReadKeyValueMap(variable.HttpKeyJsonPath, variable.HttpValueJsonPath);
                    if (!map.Success || map.Value == null)
                    {
                        Console.WriteLine($"[Device {device.Id}] HttpClient KV map failed ({variable.HttpKeyJsonPath}/{variable.HttpValueJsonPath}): {map.Error}");
                        continue;
                    }

                    foreach (var kv in map.Value)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                        payload[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Device {device.Id}] Exception reading HttpClient KV map: {ex.Message}");
                }
            }

            return;
        }

        foreach (var variable in device.Variables)
        {
            if (!DeviceVariablePollRules.ShouldPoll(variable))
                continue;

            object? val = null;
            try
            {
                var read = session.Read(variable.Address, (ProtocolDataType)(int)variable.DataType);
                if (read.Success)
                    val = read.Value;
            }
            catch
            {
                /* 单点失败跳过 */
            }

            if (val != null)
                payload[variable.Alias] = val;
        }
    }

    /// <summary>MQTT 指令读：按 Reads 子集或全部变量。</summary>
    public static void FillQueryPayload(
        Device device,
        IDeviceProtocolSession session,
        IReadOnlyList<string>? readsFilter,
        Dictionary<string, object> payload)
    {
        if (device.ProtocolType == ProtocolType.HttpClient)
        {
            FillPollPayload(device, session, payload);
            return;
        }

        var variablesToQuery = readsFilter is { Count: > 0 }
            ? device.Variables.Where(v => readsFilter.Contains(v.Alias))
            : device.Variables;

        foreach (var variable in variablesToQuery)
        {
            object? val = null;
            try
            {
                var read = session.Read(variable.Address, (ProtocolDataType)(int)variable.DataType);
                if (read.Success)
                    val = read.Value;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MQTT] Exception reading {variable.Alias} ({variable.Address}): {ex.Message}");
            }

            if (val != null)
                payload[variable.Alias] = val;
        }
    }
}
