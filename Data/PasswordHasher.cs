using System.Security.Cryptography;
using System.Text;

namespace Lana.Data;

/// <summary>
/// 密码哈希：SHA256 十六进制（无盐）。仅适合本地桌面场景；勿用于公网高安全要求。
/// </summary>
public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    public static bool Verify(string password, string passwordHash)
        => string.Equals(Hash(password), passwordHash, StringComparison.OrdinalIgnoreCase);
}
