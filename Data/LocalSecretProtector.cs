using System.Security.Cryptography;
using System.Text;

namespace Lana.Data;

/// <summary>
/// 本地敏感字符串保护（AES-256-GCM + 应用目录密钥文件），用于 MQTT 密码等落库字段。
/// 密文带 <c>lana1:</c> 前缀；无前缀视为旧版明文，读取时原样返回，下次保存时加密。
/// </summary>
public static class LocalSecretProtector
{
    private const string Prefix = "lana1:";
    private const string KeyFileName = ".lana-secret-key";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static readonly object KeyGate = new();
    private static byte[]? _cachedKey;

    /// <summary>加密明文；空字符串不落密文。</summary>
    public static string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(GetOrCreateKey(), TagSize))
            aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, payload, NonceSize + cipher.Length, TagSize);

        return Prefix + Convert.ToBase64String(payload);
    }

    /// <summary>解密密文；非密文格式则按旧版明文返回。</summary>
    public static string Unprotect(string storedText)
    {
        if (string.IsNullOrEmpty(storedText))
            return string.Empty;

        if (!storedText.StartsWith(Prefix, StringComparison.Ordinal))
            return storedText;

        try
        {
            var payload = Convert.FromBase64String(storedText[Prefix.Length..]);
            if (payload.Length <= NonceSize + TagSize)
                return string.Empty;

            var nonce = payload.AsSpan(0, NonceSize);
            var tag = payload.AsSpan(payload.Length - TagSize, TagSize);
            var cipherLength = payload.Length - NonceSize - TagSize;
            var cipher = payload.AsSpan(NonceSize, cipherLength);
            var plain = new byte[cipherLength];

            using (var aes = new AesGcm(GetOrCreateKey(), TagSize))
                aes.Decrypt(nonce, cipher, tag, plain);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    /// <summary>是否为已加密格式。</summary>
    public static bool IsProtected(string storedText)
        => storedText.StartsWith(Prefix, StringComparison.Ordinal);

    private static byte[] GetOrCreateKey()
    {
        if (_cachedKey is not null)
            return _cachedKey;

        lock (KeyGate)
        {
            if (_cachedKey is not null)
                return _cachedKey;

            var path = Path.Combine(AppContext.BaseDirectory, KeyFileName);
            if (File.Exists(path))
            {
                var existing = File.ReadAllBytes(path);
                if (existing.Length == 32)
                {
                    _cachedKey = existing;
                    return _cachedKey;
                }
            }

            var key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(path, key);
            _cachedKey = key;
            return _cachedKey;
        }
    }
}
