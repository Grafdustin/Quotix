using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quotix.Models;

namespace Quotix.Services;

/// <summary>
/// 加密与压缩服务（静态类）。
/// 提供 AES-256-CBC 加密/解密 + GZip/Deflate 压缩/解压功能。
/// </summary>
/// <remarks>
/// 备份文件格式：魔术头(6字节) + Salt(16字节) + IV(16字节) + 密文。
/// 兼容旧版 Base64 格式和新版二进制格式。
/// </remarks>
public static class CryptoService
{
    /// <summary>PBKDF2 迭代次数</summary>
    public const int Pbkdf2Iterations = 100_000;

    /// <summary>Salt 字节长度</summary>
    public const int SaltSize = 16;

    /// <summary>IV 字节长度（AES 块大小 = 128位）</summary>
    public const int IvSize = 16;

    /// <summary>AES 密钥字节长度（256位）</summary>
    public const int KeySize = 32;

    /// <summary>备份文件魔术头（"QUOX01"）</summary>
    public static readonly byte[] FileMagic = "QUOX01"u8.ToArray();

    /// <summary>通用错误消息（密码错误或文件损坏）</summary>
    public const string ErrorMessage = "密码错误，或备份文件已损坏";

    // ============ AES-256-CBC 加密/解密 ============

    /// <summary>
    /// AES-256-CBC 加密。
    /// 返回：Salt(16) + IV(16) + 密文。
    /// </summary>
    public static byte[] Encrypt(byte[] plainBytes, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[SaltSize + IvSize + cipherBytes.Length];
        Buffer.BlockCopy(salt, 0, result, 0, SaltSize);
        Buffer.BlockCopy(iv, 0, result, SaltSize, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, result, SaltSize + IvSize, cipherBytes.Length);

        return result;
    }

    /// <summary>
    /// AES-256-CBC 解密。
    /// 输入格式：Salt(16) + IV(16) + 密文。
    /// </summary>
    public static byte[] Decrypt(byte[] data, string password)
    {
        if (data.Length < SaltSize + IvSize + 1)
            throw new InvalidOperationException(ErrorMessage);

        var salt = data[..SaltSize];
        var iv = data[SaltSize..(SaltSize + IvSize)];
        var cipherBytes = data[(SaltSize + IvSize)..];

        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
    }

    /// <summary>AES 解密（捕获 CryptographicException，转为通用错误消息）</summary>
    public static byte[] SafeDecrypt(byte[] data, string password)
    {
        try { return Decrypt(data, password); }
        catch (CryptographicException) { throw new InvalidOperationException(ErrorMessage); }
    }

    // ============ GZip / Deflate 压缩/解压 ============

    /// <summary>GZip 压缩字符串 → 字节数组</summary>
    public static byte[] Compress(string text)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
        {
            writer.Write(text);
        }
        return output.ToArray();
    }

    /// <summary>GZip 解压字节数组 → 字符串</summary>
    public static string Decompress(byte[] compressed)
    {
        try
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException(ErrorMessage);
        }
    }

    // ============ 文件魔术头检测 ============

    /// <summary>检测字节数组是否以备份文件魔术头开头</summary>
    public static bool StartsWithMagic(byte[] data)
    {
        if (data.Length < FileMagic.Length) return false;
        for (int i = 0; i < FileMagic.Length; i++)
            if (data[i] != FileMagic[i]) return false;
        return true;
    }

    // ============ 组合操作（加密 + 写入文件）============

    /// <summary>
    /// JSON 字符串 → GZip → AES 加密 → 写入 .quox 二进制文件。
    /// 使用流式写入，不生成大内存数组。
    /// </summary>
    public static void EncryptToFile(string filePath, string json, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        // 写入魔术头
        fileStream.Write(FileMagic, 0, FileMagic.Length);
        // 写入 Salt + IV（明文前置，供解密时提取）
        fileStream.Write(salt, 0, SaltSize);
        fileStream.Write(iv, 0, IvSize);

        // 管道：JSON → StreamWriter → GZipStream → CryptoStream → FileStream
        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(fileStream, encryptor, CryptoStreamMode.Write);
        using var gzipStream = new GZipStream(cryptoStream, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzipStream, Encoding.UTF8);
        writer.Write(json);
    }

    /// <summary>
    /// FullBackupData → JSON → Deflate → AES 加密 → 写入文件（全流式）。
    /// </summary>
    /// <remarks>
    /// 使用 DeflateStream 而非 GZipStream：
    /// GZipStream 内部用 Int32 跟踪输入字节数写 GZIP 尾部 ISIZE，
    /// 大数据量时会 OverflowException。Deflate 无此限制。
    /// </remarks>
    public static void EncryptToFile(string filePath, FullBackupData backup, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        fileStream.Write(FileMagic, 0, FileMagic.Length);
        fileStream.Write(salt, 0, SaltSize);
        fileStream.Write(iv, 0, IvSize);

        // 管道：JsonSerializer → Utf8JsonWriter → DeflateStream → CryptoStream → FileStream
        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(fileStream, encryptor, CryptoStreamMode.Write);
        using var deflateStream = new DeflateStream(cryptoStream, CompressionLevel.SmallestSize);
        using var jsonWriter = new Utf8JsonWriter(deflateStream);
        JsonSerializer.Serialize(jsonWriter, backup, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }

    // ============ 组合操作（从文件读取 + 解密）============

    /// <summary>
    /// 从文件读取 → 检测格式 → 解密/解压 → 返回 JSON 字符串。
    /// 自动兼容：明文 JSON / 新二进制格式 / 旧 Base64 格式。
    /// </summary>
    public static string DecryptFromFile(string filePath, string password)
    {
        var fileBytes = File.ReadAllBytes(filePath);

        // 明文 JSON（旧格式，未加密）
        if (fileBytes.Length > 0 && (fileBytes[0] == (byte)'[' || fileBytes[0] == (byte)'{'))
            return Encoding.UTF8.GetString(fileBytes);

        // 新二进制 .quox 格式（有魔术头）
        if (StartsWithMagic(fileBytes))
        {
            var cipherBytes = fileBytes[FileMagic.Length..];
            var decrypted = SafeDecrypt(cipherBytes, password);
            // 兼容旧 GZip 格式和新 Deflate 格式
            return TryDecompress(decrypted) ?? Encoding.UTF8.GetString(decrypted);
        }

        // 旧 Base64 .quox 格式兼容
        byte[] legacyCipherBytes;
        try
        {
            legacyCipherBytes = Convert.FromBase64String(Encoding.UTF8.GetString(fileBytes).Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(ErrorMessage);
        }

        var plainBytes = SafeDecrypt(legacyCipherBytes, password);
        return TryDecompress(plainBytes) ?? Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// 尝试用 GZip 解压，失败则尝试 Deflate 解压。
    /// 都失败返回 null（说明数据未压缩，是纯明文）。
    /// </summary>
    private static string? TryDecompress(byte[] data)
    {
        // 先试 GZip（旧备份格式）
        try
        {
            using var input = new MemoryStream(data);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (InvalidDataException) { }

        // 再试 Deflate（新备份格式）
        try
        {
            using var input = new MemoryStream(data);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (InvalidDataException) { }

        return null;
    }
}
