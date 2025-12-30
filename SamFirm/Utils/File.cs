using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Tar;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SamFirm.Utils
{
    /// <summary>
    /// Wrapper stream that prevents the underlying stream from being closed/disposed
    /// Used for nested stream processing (e.g., TAR within ZIP)
    /// </summary>
    internal class NonClosingStreamWrapper : Stream
    {
        private readonly Stream _baseStream;

        public NonClosingStreamWrapper(Stream baseStream)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => _baseStream.CanSeek;
        public override bool CanWrite => _baseStream.CanWrite;
        public override long Length => _baseStream.Length;
        public override long Position
        {
            get => _baseStream.Position;
            set => _baseStream.Position = value;
        }

        public override void Flush() => _baseStream.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _baseStream.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _baseStream.Seek(offset, origin);
        public override void SetLength(long value) => _baseStream.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _baseStream.Write(buffer, offset, count);

        // Override Close and Dispose to prevent closing the underlying stream
        public override void Close()
        {
            // Do nothing - don't close the base stream
        }

        protected override void Dispose(bool disposing)
        {
            // Do nothing - don't dispose the base stream
        }
    }

    internal static class File
    {
        public static long FileSize { get; set; } = 0;
        private static byte[] KEY;

        // Buffer size for extraction (4MB for better performance on large files)
        private const int ExtractBufferSize = 4 * 1024 * 1024;

        /// <summary>
        /// Check if an IOException is due to insufficient disk space (cross-platform)
        /// </summary>
        private static bool IsDiskFullException(IOException ex)
        {
            // Check for Linux/Unix message
            if (ex.Message.Contains("No space left on device")) return true;
            
            // Check for Windows message
            if (ex.Message.Contains("not enough space", StringComparison.OrdinalIgnoreCase)) return true;
            if (ex.Message.Contains("disk is full", StringComparison.OrdinalIgnoreCase)) return true;
            
            // Check for Windows error code: 0x80070070 = ERROR_DISK_FULL
            if (ex.HResult == unchecked((int)0x80070070)) return true;
            
            return false;
        }

        /// <summary>
        /// Extract TAR archive directly from a stream (optimized - no intermediate disk write)
        /// </summary>
        private static void ExtractTarFromStream(Stream tarStream, string outputDir, string tarFileName)
        {
            Logger.Info($"Extracting TAR archive: {tarFileName} (streaming mode - faster)");
            
            // Wrap the stream to prevent TarInputStream from closing it
            using (var nonClosingWrapper = new NonClosingStreamWrapper(tarStream))
            using (var tarInputStream = new TarInputStream(nonClosingWrapper, System.Text.Encoding.UTF8))
            {
                var buffer = new byte[ExtractBufferSize];
                TarEntry tarEntry;
                int fileCount = 0;

                while ((tarEntry = tarInputStream.GetNextEntry()) != null)
                {
                    if (tarEntry.IsDirectory) continue;

                    var entryFileName = tarEntry.Name;
                    var fullOutputPath = Path.Combine(outputDir, entryFileName);
                    var directoryName = Path.GetDirectoryName(fullOutputPath);
                    
                    if (!string.IsNullOrEmpty(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    fileCount++;
                    // Only log larger files to reduce overhead
                    if (tarEntry.Size > 10 * 1024 * 1024) // > 10MB
                    {
                        Logger.Raw($"    Extracting from TAR: {entryFileName} ({tarEntry.Size / (1024.0 * 1024.0):F2} MB)");
                    }

                    try
                    {
                        using (var outputStream = System.IO.File.Create(fullOutputPath))
                        {
                            StreamUtils.Copy(tarInputStream, outputStream, buffer);
                        }
                    }
                    catch (IOException ex) when (IsDiskFullException(ex))
                    {
                        // Clean up partial file on disk space error
                        try
                        {
                            if (System.IO.File.Exists(fullOutputPath))
                            {
                                System.IO.File.Delete(fullOutputPath);
                            }
                        }
                        catch { /* Ignore cleanup errors */ }

                        Logger.ErrorExit($"No space left on device: '{fullOutputPath}'", 1);
                        throw;
                    }
                }
                
                Logger.Info($"  Extracted {fileCount} file(s) from TAR");
            }
        }

        private static void ExtractTarFile(string tarPath, string outputDir)
        {
            Logger.Info($"Extracting TAR archive: {Path.GetFileName(tarPath)}");
            
            using (var fileStream = System.IO.File.OpenRead(tarPath))
            using (var tarInputStream = new TarInputStream(fileStream, System.Text.Encoding.UTF8))
            {
                var buffer = new byte[ExtractBufferSize];
                TarEntry tarEntry;

                while ((tarEntry = tarInputStream.GetNextEntry()) != null)
                {
                    if (tarEntry.IsDirectory) continue;

                    var entryFileName = tarEntry.Name;
                    var fullOutputPath = Path.Combine(outputDir, entryFileName);
                    var directoryName = Path.GetDirectoryName(fullOutputPath);
                    
                    if (!string.IsNullOrEmpty(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    Logger.Raw($"    Extracting from TAR: {entryFileName} ({tarEntry.Size / (1024.0 * 1024.0):F2} MB)");

                    try
                    {
                        using (var outputStream = System.IO.File.Create(fullOutputPath))
                        {
                            StreamUtils.Copy(tarInputStream, outputStream, buffer);
                        }
                    }
                    catch (IOException ex) when (IsDiskFullException(ex))
                    {
                        // Clean up partial file on disk space error
                        try
                        {
                            if (System.IO.File.Exists(fullOutputPath))
                            {
                                System.IO.File.Delete(fullOutputPath);
                            }
                        }
                        catch { /* Ignore cleanup errors */ }

                        Logger.ErrorExit($"No space left on device: '{fullOutputPath}'", 1);
                        throw;
                    }
                }
            }

            Logger.Info($"TAR extraction complete, deleting TAR file to save space...");
        }

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

                    // Check if this is a TAR or TAR.md5 file - extract directly from stream for better performance
                    bool isTarFile = zipEntry.Name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) || 
                                     zipEntry.Name.EndsWith(".tar.md5", StringComparison.OrdinalIgnoreCase);
                    
                    if (isTarFile)
                    {
                        try
                        {
                            // OPTIMIZED: Extract TAR contents directly from the ZIP stream
                            // This avoids writing the TAR file to disk and reading it back
                            // Saves time and disk space (can reduce extraction time by 50-70%)
                            ExtractTarFromStream(zipInputStream, outFolder, zipEntry.Name);
                            Logger.Info($"  TAR extraction complete (no intermediate file created)");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn($"Failed to extract TAR stream {zipEntry.Name}: {ex.Message}");
                            // If streaming TAR extraction fails, try the old method
                            Logger.Info("Falling back to disk-based TAR extraction...");
                            try
                            {
                                using (FileStream streamWriter = System.IO.File.Create(fullZipToPath))
                                {
                                    StreamUtils.Copy(zipInputStream, streamWriter, buffer);
                                }
                                ExtractTarFile(fullZipToPath, outFolder);
                                System.IO.File.Delete(fullZipToPath);
                            }
                            catch (Exception fallbackEx)
                            {
                                Logger.Warn($"Fallback TAR extraction also failed: {fallbackEx.Message}");
                            }
                        }
                    }
                    else
                    {
                        // Not a TAR file, just extract normally
                        try
                        {
                            using (FileStream streamWriter = System.IO.File.Create(fullZipToPath))
                            {
                                StreamUtils.Copy(zipInputStream, streamWriter, buffer);
                            }
                        }
                        catch (IOException ex) when (IsDiskFullException(ex))
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
