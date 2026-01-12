using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Faborite.Core.Authentication;

/// <summary>
/// Two-Factor Authentication (2FA/MFA) implementation. Issue #86
/// </summary>
public class TwoFactorAuthManager
{
    private readonly ILogger<TwoFactorAuthManager> _logger;
    private readonly Dictionary<string, string> _secrets = new();

    public TwoFactorAuthManager(ILogger<TwoFactorAuthManager> logger)
    {
        _logger = logger;
    }

    public string GenerateSecret(string userId)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(20));
        _secrets[userId] = secret;
        _logger.LogInformation("Generated 2FA secret for user {User}", userId);
        return secret;
    }

    public bool VerifyCode(string userId, string code)
    {
        if (!_secrets.TryGetValue(userId, out var secret))
            return false;

        var validCode = GenerateTOTP(secret);
        return code == validCode;
    }

    private string GenerateTOTP(string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var hash = HMACSHA1.HashData(Convert.FromBase64String(secret), BitConverter.GetBytes(timestamp));
        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24 | (hash[offset + 1] & 0xFF) << 16 | (hash[offset + 2] & 0xFF) << 8 | hash[offset + 3] & 0xFF) % 1000000;
        return code.ToString("D6");
    }
}

/// <summary>
/// Row-level security implementation. Issue #87
/// </summary>
public class RowLevelSecurity
{
    private readonly ILogger<RowLevelSecurity> _logger;

    public RowLevelSecurity(ILogger<RowLevelSecurity> logger)
    {
        _logger = logger;
    }

    public string ApplyRowFilter(string userId, string tableName, List<string> userRoles)
    {
        // Generate SQL WHERE clause based on user permissions
        var filters = new List<string>();

        if (userRoles.Contains("admin"))
        {
            return "1=1"; // No restrictions
        }

        if (userRoles.Contains("manager"))
        {
            filters.Add($"department_id IN (SELECT department_id FROM user_departments WHERE user_id = '{userId}')");
        }
        else
        {
            filters.Add($"owner_id = '{userId}'");
        }

        return string.Join(" OR ", filters);
    }
}

/// <summary>
/// Column-level encryption for sensitive data. Issue #88
/// </summary>
public class ColumnEncryption
{
    private readonly ILogger<ColumnEncryption> _logger;
    private readonly byte[] _encryptionKey;

    public ColumnEncryption(ILogger<ColumnEncryption> logger)
    {
        _logger = logger;
        _encryptionKey = RandomNumberGenerator.GetBytes(32);
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        var result = new byte[aes.IV.Length + ciphertext.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(ciphertext, 0, result, aes.IV.Length, ciphertext.Length);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string ciphertext)
    {
        var buffer = Convert.FromBase64String(ciphertext);
        
        using var aes = Aes.Create();
        aes.Key = _encryptionKey;

        var iv = new byte[aes.IV.Length];
        var cipher = new byte[buffer.Length - iv.Length];

        Buffer.BlockCopy(buffer, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(buffer, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return System.Text.Encoding.UTF8.GetString(plaintext);
    }
}
