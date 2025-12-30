using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SamFirm.Utils
{
    internal static class File
    {
        public static long FileSize { get; set; } = 0;
        private static byte[] KEY;

        // Buffer size for extraction (1MB)
        private const int ExtractBufferSize = 1024 * 1024;

        public static void UnzipFromStream(Stream zipStream, string outFolder, string[] components = null)
        {
            using (var zipInputStream = new ZipInputStream(zipStream))
            {
                int fileCount = 0;
                int skippedCount = 0;
                // Reuse buffer across all file extractions to reduce memory allocations
                var buffer = new byte[ExtractBufferSize];

                while (zipInputStream.GetNextEntry() is ZipEntry zipEntry)
                {
                    // Check if we should extract this file based on component filter
                    if (components != null && components.Length > 0)
                    {
                        bool shouldExtract = false;
                        foreach (var component in components)
                        {
                            // Check if filename starts with component prefix (e.g., "AP_", "BL_", "CP_", "CSC_", "HOME_CSC_")
                            if (zipEntry.Name.StartsWith(component + "_", StringComparison.OrdinalIgnoreCase))
                            {
                                shouldExtract = true;
                                break;
                            }
                        }

                        if (!shouldExtract)
                        {
                            skippedCount++;
                            Logger.Raw($"  Skipping: {zipEntry.Name}");
                            continue;
                        }
                    }

                    var fullZipToPath = Path.Combine(outFolder, zipEntry.Name);
                    var directoryName = Path.GetDirectoryName(fullZipToPath);
                    if (!string.IsNullOrEmpty(directoryName)) Directory.CreateDirectory(directoryName);

                    if (zipEntry.IsDirectory) continue;

                    fileCount++;
                    Logger.Raw($"  Extracting: {zipEntry.Name} ({zipEntry.Size / (1024.0 * 1024.0):F2} MB)");

                    try
                    {
                        using (FileStream streamWriter = System.IO.File.Create(fullZipToPath))
                        {
                            StreamUtils.Copy(zipInputStream, streamWriter, buffer);
                        }
                    }
                    catch (IOException ex) when (ex.Message.Contains("No space left on device"))
                    {
                        // Clean up partial file on disk space error
                        try
                        {
                            if (System.IO.File.Exists(fullZipToPath))
                            {
                                System.IO.File.Delete(fullZipToPath);
                            }
                        }
                        catch { /* Ignore cleanup errors */ }

                        Logger.ErrorExit($"No space left on device: '{fullZipToPath}'", 1);
                        throw;
                    }
                }
                if (skippedCount > 0)
                {
                    Logger.Raw($"  Total files skipped: {skippedCount}");
                }
                Logger.Raw($"  Total files extracted: {fileCount}");

                // Warn if component filter was specified but no files were extracted
                if (components != null && components.Length > 0 && fileCount == 0)
                {
                    Logger.Warn($"No files matched the selected components: {string.Join(", ", components)}");
                    Logger.Warn("Firmware files should start with one of: AP_, BL_, CP_, CSC_, HOME_CSC_");
                }
            }
        }

        public static void HandleEncryptedFile(Stream networkStream, string outputDir, string[] components = null)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = KEY;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                using (CryptoStream cryptoStream = new CryptoStream(networkStream, decryptor, CryptoStreamMode.Read))
                {
                    UnzipFromStream(cryptoStream, outputDir, components);
                }
            }
        }

        public static void SetDecryptKey(string version, string LogicValue)
        {
            string logicCheck = Auth.GetLogicCheck(version, LogicValue);
            byte[] bytes = Encoding.ASCII.GetBytes(logicCheck);
            using (MD5 md = MD5.Create())
            {
                KEY = md.ComputeHash(bytes);
            }
        }
    }
}
