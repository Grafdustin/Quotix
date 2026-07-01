using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quotix.Models;

namespace Quotix.Services;

/// <summary>
/// AES-256-CBC 加密 / 解密 + GZip 压缩 / 解压 — 从 ProductService/HeaderService 提取
/// </summary>
public static class CryptoService
{
    public const int Pbkdf2Iterations = 100_000;
    public const int SaltSize = 16;
    public const int IvSize = 16;
    public const int KeySize = 32; // AES-256
    public static readonly byte[] FileMagic = "QUOX01"u8.ToArray();
    public const string ErrorMessage = "密码错误，或备份文件已损坏";

    // ============ AES-256-CBC ============

    /// <summary>加密 → 返回 salt(16) + iv(16) + ciphertext</summary>
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

    /// <summary>解密 — data 格式: salt(16) + iv(16) + ciphertext</summary>
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

    public static byte[] SafeDecrypt(byte[] data, string password)
    {
        try { return Decrypt(data, password); }
        catch (CryptographicException) { throw new InvalidOperationException(ErrorMessage); }
    }

    // ============ GZip ============

    /// <summary>GZip 压缩字符串 → byte[]</summary>
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

    /// <summary>GZip 解压 byte[] → 字符串</summary>
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

    // ============ 文件魔术头 ============

    public static bool StartsWithMagic(byte[] data)
    {
        if (data.Length < FileMagic.Length) return false;
        for (int i = 0; i < FileMagic.Length; i++)
            if (data[i] != FileMagic[i]) return false;
        return true;
    }

    // ============ 组合操作（加密 + 写入 / 解密 + 读取）============

    /// <summary>JSON 字符串 → GZip → AES 加密 → 写入 .quox 二进制文件（流式，无大数组）</summary>
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

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536);
        // 魔数头
        fileStream.Write(FileMagic, 0, FileMagic.Length);
        // salt + iv（明文前置，供解密时提取）
        fileStream.Write(salt, 0, SaltSize);
        fileStream.Write(iv, 0, IvSize);

        // 管道：JSON → StreamWriter → GZipStream → CryptoStream → FileStream
        using var encryptor = aes.CreateEncryptor();
        using var cryptoStream = new CryptoStream(fileStream, encryptor, CryptoStreamMode.Write);
        using var gzipStream = new GZipStream(cryptoStream, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzipStream, Encoding.UTF8);
        writer.Write(json);
    }

    /// <summary>FullBackupData → JSON → Deflate → AES 加密 → 写入文件（全流式，零大对象）</summary>
    /// <remarks>
    /// 使用 DeflateStream 而非 GZipStream：
    ///   GZipStream 内部用 Int32 跟踪输入字节数写 GZIP 尾部 ISIZE，大数据量会 OverflowException。
    ///   Deflate 是同样的压缩算法但无包装头/尾，不存在此限制。
    ///   加密文件无需外部工具直接读取，不需要 GZIP 格式。
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

        using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536);
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

    /// <summary>从文件读取 → 检测格式 → 解密/解压 → 返回 JSON</summary>
    public static string DecryptFromFile(string filePath, string password)
    {
        var fileBytes = File.ReadAllBytes(filePath);

        if (fileBytes.Length > 0 && (fileBytes[0] == (byte)'[' || fileBytes[0] == (byte)'{'))
        {
            // 明文 JSON
            return Encoding.UTF8.GetString(fileBytes);
        }

        if (StartsWithMagic(fileBytes))
        {
            // 新二进制 .quox 格式
            var cipherBytes = fileBytes[FileMagic.Length..];
            var decrypted = SafeDecrypt(cipherBytes, password);
            // 兼容旧 GZip 格式和新的 Deflate 格式
            return TryDecompress(decrypted) ?? Encoding.UTF8.GetString(decrypted);
        }

        // 旧 Base64 .quox 兼容
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
        // 兼容旧 GZip 格式
        return TryDecompress(plainBytes) ?? Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>尝试用 GZip 解压，失败则尝试 Deflate 解压，都失败返回 null</summary>
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
