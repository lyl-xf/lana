using System.Security.Cryptography;
using System.Text;

namespace Lana.Data;

/// <summary>
/// 密码哈希：SHA256 十六进制（无盐）。仅适合本地桌面场景；勿用于公网高安全要求。
/// </summary>
public static class PasswordHasher
{
    /// <summary>
    /// 将明文密码计算为 SHA256 十六进制字符串。
    /// </summary>
    /// <param name="password">待哈希的明文密码。</param>
    /// <returns>大写十六进制哈希值。</returns>
    public static string Hash(string password)
    {
        // UTF-8 编码后计算 SHA256 摘要
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 校验明文密码是否与已存储的哈希值匹配。
    /// </summary>
    /// <param name="password">用户输入的明文密码。</param>
    /// <param name="passwordHash">数据库中存储的哈希值。</param>
    /// <returns>匹配返回 <see langword="true"/>，否则 <see langword="false"/>。</returns>
    public static bool Verify(string password, string passwordHash)
        => string.Equals(Hash(password), passwordHash, StringComparison.OrdinalIgnoreCase);
}
