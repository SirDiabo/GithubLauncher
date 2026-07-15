using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GithubLauncher
{
    /// <summary>
    /// Securely stores the GitHub API token using AES-256-GCM encryption.
    /// The encrypted blob is stored outside the app directory with restricted
    /// file permissions (0600 on Unix, inherited ACL on Windows).
    /// This prevents casual token theft via settings.json exposure.
    /// </summary>
    public static class SecretStore
    {
        private const string DataDirName = "GithubLauncher";
        private const string TokenFileName = "github_token.enc";
        private const string KeyFileName = "github_token.key";

        private static readonly string DataDir;
        private static readonly string TokenPath;
        private static readonly string KeyPath;

        private static byte[]? _cachedKey;
        private static string? _cachedToken;

        static SecretStore()
        {
            var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share");
            DataDir = Path.Combine(baseDir, DataDirName);
            TokenPath = Path.Combine(DataDir, TokenFileName);
            KeyPath = Path.Combine(DataDir, KeyFileName);
        }

        public static string ReadToken()
        {
            if (_cachedToken != null)
                return _cachedToken;

            try
            {
                if (!File.Exists(TokenPath) || !File.Exists(KeyPath))
                    return string.Empty;

                byte[] key = File.ReadAllBytes(KeyPath);
                byte[] ciphertext = File.ReadAllBytes(TokenPath);

                string? token = Decrypt(ciphertext, key);
                _cachedToken = token ?? string.Empty;
                return _cachedToken;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void WriteToken(string? token)
        {
            try
            {
                Directory.CreateDirectory(DataDir);

                if (string.IsNullOrEmpty(token))
                {
                    if (File.Exists(TokenPath)) File.Delete(TokenPath);
                    if (File.Exists(KeyPath)) File.Delete(KeyPath);
                    _cachedToken = string.Empty;
                    return;
                }

                byte[] key = GenerateOrLoadKey();
                byte[] ciphertext = Encrypt(token, key);
                File.WriteAllBytes(TokenPath, ciphertext);
                SetRestrictedPermissions(TokenPath);

                _cachedToken = token;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecretStore: failed to write token: {ex.Message}");
                throw;
            }
        }

        public static void ClearToken()
        {
            WriteToken(null);
        }

        public static bool HasToken()
        {
            return !string.IsNullOrEmpty(ReadToken());
        }

        private static byte[] GenerateOrLoadKey()
        {
            if (_cachedKey != null)
                return _cachedKey;

            if (File.Exists(KeyPath))
            {
                _cachedKey = File.ReadAllBytes(KeyPath);
                if (_cachedKey.Length == 32)
                    return _cachedKey;
            }

            byte[] key = RandomNumberGenerator.GetBytes(32);
            File.WriteAllBytes(KeyPath, key);
            SetRestrictedPermissions(KeyPath);
            _cachedKey = key;
            return key;
        }

        private static byte[] Encrypt(string plaintext, byte[] key)
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
            byte[] ciphertext = new byte[nonce.Length + plainBytes.Length + 16];

            using var aes = new AesGcm(key);
            Array.Copy(nonce, 0, ciphertext, 0, nonce.Length);
            aes.Encrypt(nonce, plainBytes,
                ciphertext.AsSpan(nonce.Length, plainBytes.Length),
                ciphertext.AsSpan(nonce.Length + plainBytes.Length, 16));

            return ciphertext;
        }

        private static string? Decrypt(byte[] ciphertext, byte[] key)
        {
            try
            {
                const int nonceSize = 12;
                const int tagSize = 16;

                if (ciphertext.Length < nonceSize + tagSize)
                    return null;

                byte[] nonce = new byte[nonceSize];
                byte[] tag = new byte[tagSize];
                int dataLen = ciphertext.Length - nonceSize - tagSize;
                byte[] data = new byte[dataLen];

                Array.Copy(ciphertext, 0, nonce, 0, nonceSize);
                Array.Copy(ciphertext, nonceSize, data, 0, dataLen);
                Array.Copy(ciphertext, nonceSize + dataLen, tag, 0, tagSize);

                byte[] plainBytes = new byte[dataLen];
                using var aes = new AesGcm(key);
                aes.Decrypt(nonce, data, tag, plainBytes);

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return null;
            }
        }

        private static void SetRestrictedPermissions(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    return;
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (PlatformNotSupportedException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecretStore: failed to set permissions on {path}: {ex.Message}");
            }
        }
    }
}
