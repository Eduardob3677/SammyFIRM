#pragma warning disable SYSLIB0014
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

namespace SamFirm.Utils
{
    internal class FUSRequest : WebRequest
    {
        public static new HttpWebRequest Create(string requestUriString)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestUriString);
            request.Headers["Cache-Control"] = "no-cache";
            request.UserAgent = "Kies2.0_FUS";
            request.Headers.Add("Authorization",
                                $"FUS nonce=\"{FUSClient.Nonce}\", signature=\"{(FUSClient.NonceDecrypted.Length > 0 ? Auth.GetAuthorization(FUSClient.NonceDecrypted) : "")}\", nc=\"\", type=\"\", realm=\"\", newauth=\"1\"");
            request.CookieContainer = FUSClient.Cookies;
            return request;
        }
    }

    internal static class FUSClient
    {
        public static CookieContainer Cookies = new CookieContainer();
        public static string Nonce { get; set; } = string.Empty;
        public static string NonceDecrypted { get; set; } = string.Empty;

        private const int Aria2Connections = 16;
        private static readonly TimeSpan Aria2DownloadTimeout = TimeSpan.FromHours(6);

        // Aria2c download configuration constants
        private const int Aria2MaxTries = 5;
        private const int Aria2RetryWait = 3;
        private const int Aria2TimeoutSeconds = 60;
        private const int Aria2ConnectTimeoutSeconds = 30;

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromHours(6)
        };


        public static async Task DownloadBinary(string path, string file, string saveTo, string[] components = null)
        {
            string url = "http://cloud-neofussvr.samsungmobile.com/NF_DownloadBinaryForMass.do?file=" + path + file;

            Directory.CreateDirectory(saveTo);
            string sanitizedFileName = Path.GetFileName(file);
            string encryptedPath = Path.Combine(saveTo, $"{sanitizedFileName}.enc2");

            Logger.Info($"Downloading firmware: {sanitizedFileName}");
            Logger.Info($"File size: {File.FileSize / (1024.0 * 1024.0 * 1024.0):F2} GB");

            // Try streaming mode first (minimizes disk usage - no encrypted file stored)
            bool streamingSuccess = await TryStreamingDownload(url, saveTo, components);
            if (streamingSuccess)
            {
                return;
            }

            // Fallback to aria2c if streaming failed
            Logger.Warn("Streaming download failed, trying aria2c...");
            if (!await TryDownloadWithAria2c(url, encryptedPath))
            {
                // Clean up any partial downloads before exiting
                CleanupTemporaryFiles(encryptedPath, saveTo);
                Logger.ErrorExit("All download methods failed", 1);
                return;
            }

            Logger.Done("Download complete, now decrypting and extracting...");
            try
            {
                using (var stream = System.IO.File.OpenRead(encryptedPath))
                {
                    File.HandleEncryptedFile(stream, saveTo, components);
                }
                Logger.Done("Decryption and extraction complete!");
            }
            finally
            {
                // Clean up all temporary files after extraction
                CleanupTemporaryFiles(encryptedPath, saveTo);
            }
        }

        private static void CleanupTemporaryFiles(string encryptedPath, string saveDir)
        {
            Logger.Info("Cleaning up temporary files...");
            int cleanedCount = 0;

            try
            {
                // Clean up the encrypted file (.enc2, .enc4, etc.)
                if (System.IO.File.Exists(encryptedPath))
                {
                    System.IO.File.Delete(encryptedPath);
                    Logger.Info($"  Deleted: {Path.GetFileName(encryptedPath)}");
                    cleanedCount++;
                }

                // Clean up aria2c control files (.aria2)
                string aria2ControlFile = encryptedPath + ".aria2";
                if (System.IO.File.Exists(aria2ControlFile))
                {
                    System.IO.File.Delete(aria2ControlFile);
                    Logger.Info($"  Deleted: {Path.GetFileName(aria2ControlFile)}");
                    cleanedCount++;
                }

                // Clean up any other .enc* files in the directory (in case of previous failed downloads)
                if (Directory.Exists(saveDir))
                {
                    var encFiles = Directory.GetFiles(saveDir, "*.enc*");
                    foreach (var encFile in encFiles)
                    {
                        try
                        {
                            System.IO.File.Delete(encFile);
                            Logger.Info($"  Deleted: {Path.GetFileName(encFile)}");
                            cleanedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"  Could not delete {Path.GetFileName(encFile)}: {ex.Message}");
                        }
                    }

                    // Clean up aria2c control files in the directory
                    var aria2Files = Directory.GetFiles(saveDir, "*.aria2");
                    foreach (var aria2File in aria2Files)
                    {
                        try
                        {
                            System.IO.File.Delete(aria2File);
                            Logger.Info($"  Deleted: {Path.GetFileName(aria2File)}");
                            cleanedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"  Could not delete {Path.GetFileName(aria2File)}: {ex.Message}");
                        }
                    }

                    // Clean up aria2c config files in the directory
                    var aria2ConfigFiles = Directory.GetFiles(saveDir, ".aria2_*.conf");
                    foreach (var configFile in aria2ConfigFiles)
                    {
                        try
                        {
                            System.IO.File.Delete(configFile);
                            Logger.Info($"  Deleted: {Path.GetFileName(configFile)}");
                            cleanedCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug($"  Could not delete {Path.GetFileName(configFile)}: {ex.Message}");
                        }
                    }
                }

                if (cleanedCount > 0)
                {
                    Logger.Done($"Cleaned up {cleanedCount} temporary file(s)");
                }
                else
                {
                    Logger.Info("No temporary files to clean up");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Error during cleanup: {ex.Message}");
            }
        }

        private static async Task<bool> TryStreamingDownload(string url, string saveTo, string[] components = null)
        {
            try
            {
                Logger.Info("Using streaming mode (download + decrypt + extract in one pass)...");
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Kies2.0_FUS");
                request.Headers.Add("Authorization", $"FUS nonce=\"{Nonce}\", signature=\"{Auth.GetAuthorization(NonceDecrypted)}\"");

                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        Logger.Info("Decrypting and extracting firmware...");
                        File.HandleEncryptedFile(stream, saveTo, components);
                    }
                }
                Logger.Done("Download, decryption, and extraction complete!");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn($"Streaming download failed: {ex.Message}");
                return false;
            }
        }

        public static int DownloadBinaryInform(string xml, out string xmlresponse) =>
        XMLFUSRequest("https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInform.do", xml, out xmlresponse);

        public static int DownloadBinaryInit(string xml, out string xmlresponse) =>
        XMLFUSRequest("https://neofussvr.sslcs.cdngc.net/NF_DownloadBinaryInitForMass.do", xml, out xmlresponse);

        public static int GenerateNonce()
        {
            HttpWebRequest wr = FUSRequest.Create("https://neofussvr.sslcs.cdngc.net/NF_DownloadGenerateNonce.do");
            wr.Method = "POST";
            wr.ContentLength = 0L;
            using (HttpWebResponse response = (HttpWebResponse)wr.GetFUSResponse())
            {
                if (response == null)
                {
                    return 0x385;
                }
                return (int)response.StatusCode;
            }
        }

        public static void SetReconnect()
        {
            // TODO: Not implemented.
        }

        private static async Task<bool> TryDownloadWithAria2c(string url, string outputPath)
        {
            string configPath = string.Empty;
            try
            {
                string dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Directory.GetCurrentDirectory();
                }

                string fileName = Path.GetFileName(outputPath);

                var configLines = new[]
                {
                    "continue=true",
                    $"max-connection-per-server={Aria2Connections}",
                    $"split={Aria2Connections}",
                    "min-split-size=1M",
                    "max-download-limit=0",
                    $"max-tries={Aria2MaxTries}",
                    $"retry-wait={Aria2RetryWait}",
                    $"timeout={Aria2TimeoutSeconds}",
                    $"connect-timeout={Aria2ConnectTimeoutSeconds}",
                    "allow-overwrite=true",
                    "auto-file-renaming=false",
                    "disable-ipv6=true",
                    "no-conf=true",
                    "file-allocation=none",
                    "console-log-level=warn",
                    $"dir={dir}",
                    $"out={fileName}",
                    "header=User-Agent: Kies2.0_FUS",
                    $"header=Authorization: FUS nonce=\"{Nonce}\", signature=\"{Auth.GetAuthorization(NonceDecrypted)}\""
                };

                configPath = Path.Combine(dir, $".aria2_{Guid.NewGuid():N}.conf");
                using (var configStream = new FileStream(configPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(configStream, Encoding.UTF8))
                {
                    foreach (var line in configLines)
                    {
                        writer.WriteLine(line);
                    }
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "aria2c",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                psi.ArgumentList.Add($"--conf-path={configPath}");
                psi.ArgumentList.Add(url);

                Process process = null;
                try
                {
                    process = Process.Start(psi);
                    if (process == null)
                    {
                        Logger.Warn("aria2c failed to start. Ensure aria2c is installed and available in PATH.");
                        return false;
                    }

                    var waitTask = process.WaitForExitAsync();
                    var completedTask = await Task.WhenAny(waitTask, Task.Delay(Aria2DownloadTimeout));
                    if (completedTask != waitTask)
                    {
                        Logger.Warn($"aria2c timed out downloading {fileName}.");
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                            // process already exited
                        }
                        catch (System.ComponentModel.Win32Exception)
                        {
                            // ignore kill errors
                        }
                        return false;
                    }

                    if (process.ExitCode != 0)
                    {
                        Logger.Warn($"aria2c download failed with code {process.ExitCode} for {fileName}.");
                        return false;
                    }

                    var downloadedFile = new FileInfo(outputPath);
                    return downloadedFile.Exists && downloadedFile.Length > 0;
                }
                finally
                {
                    process?.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Debug($"aria2c unavailable: {ex.Message}");
                return false;
            }
            finally
            {
                // Clean up aria2 config file
                if (!string.IsNullOrEmpty(configPath) && System.IO.File.Exists(configPath))
                {
                    try
                    {
                        System.IO.File.Delete(configPath);
                    }
                    catch (IOException)
                    {
                        // ignore cleanup failures
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // ignore cleanup failures
                    }
                }
            }
        }

        private static int XMLFUSRequest(string URL, string xml, out string xmlresponse)
        {
            xmlresponse = null;
            HttpWebRequest wr = FUSRequest.Create(URL);
            wr.CookieContainer = Cookies;
            wr.Method = "POST";
            byte[] bytes = Encoding.ASCII.GetBytes(Regex.Replace(xml, @"\r\n?|\n|\t", string.Empty));
            wr.ContentLength = bytes.Length;
            using (Stream stream = wr.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            using (HttpWebResponse response = (HttpWebResponse)wr.GetFUSResponse())
            {
                if (response == null)
                {
                    return 0x385;
                }
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    try
                    {
                        xmlresponse = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    }
                    catch (Exception)
                    {
                        return 900;
                    }
                }
                return (int)response.StatusCode;
            }
        }

        public static WebResponse GetFUSResponse(this WebRequest wr)
        {
            try
            {
                WebResponse response = wr.GetResponse();
                if (response.Headers.AllKeys.Contains("NONCE"))
                {
                    Nonce = response.Headers["NONCE"];
                    NonceDecrypted = Auth.DecryptNonce(Nonce);
                }
                return response;
            }
            catch (WebException exception)
            {
                Logger.Error($"GetResponseFUS() -> {exception.Message}");
                if (exception.Status == WebExceptionStatus.NameResolutionFailure)
                {
                    SetReconnect();
                }
                return exception.Response;
            }
        }
    }
}
