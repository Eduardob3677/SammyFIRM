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

        public static void UnzipFromStream(Stream zipStream, string outFolder)
        {
            using (var zipInputStream = new ZipInputStream(zipStream))
            {
                int fileCount = 0;
                while (zipInputStream.GetNextEntry() is ZipEntry zipEntry)
                {
                    var fullZipToPath = Path.Combine(outFolder, zipEntry.Name);
                    var directoryName = Path.GetDirectoryName(fullZipToPath);
                    if (!string.IsNullOrEmpty(directoryName)) Directory.CreateDirectory(directoryName);

                    if (zipEntry.IsDirectory) continue;

                    fileCount++;
                    Logger.Raw($"  Extracting: {zipEntry.Name} ({zipEntry.Size / (1024.0 * 1024.0):F2} MB)");

                    var buffer = new byte[4 * 1024 * 1024]; // 4MB buffer for faster extraction

                    try
                    {
                        using (FileStream streamWriter = System.IO.File.Create(fullZipToPath))
                        {
                            StreamUtils.Copy(zipInputStream, streamWriter, buffer);
                        }
                    }
                    catch (IOException ex) when (ex.Message.Contains("No space left on device"))
                    {
                        Logger.ErrorExit($"No space left on device: '{fullZipToPath}'", 1);
                        throw;
                    }
                }
                Logger.Raw($"  Total files extracted: {fileCount}");
            }
        }

        public static void HandleEncryptedFile(Stream networkStream, string outputDir)
        {

            using (Aes aes = Aes.Create())
            {
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = KEY;

                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                using (CryptoStream cryptoStream = new CryptoStream(networkStream, decryptor, CryptoStreamMode.Read))
                {
                    UnzipFromStream(cryptoStream, outputDir);
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
