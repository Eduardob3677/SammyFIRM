#pragma warning disable SYSLIB0014
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http; // Required for HttpClient
using System.Threading.Tasks; // Required for Task
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
        private static readonly TimeSpan Aria2Timeout = TimeSpan.FromMinutes(30);


        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(30)
        };


        public static async Task DownloadBinary(string path, string file, string saveTo)
        {
            string url = "http://cloud-neofussvr.samsungmobile.com/NF_DownloadBinaryForMass.do?file=" + path + file;

            Directory.CreateDirectory(saveTo);
            string sanitizedFileName = Path.GetFileName(file);
            string encryptedPath = Path.Combine(saveTo, $"{sanitizedFileName}.enc2");

            Console.WriteLine($"\nDownloading firmware: {sanitizedFileName}");
            Console.WriteLine($"File size: {File.FileSize / (1024.0 * 1024.0):F2} MB");

            if (!await TryDownloadWithAria2c(url, encryptedPath))
            {
                Console.WriteLine("Using built-in downloader (streaming mode)...");
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Kies2.0_FUS");
                request.Headers.Add("Authorization", $"FUS nonce=\"{Nonce}\", signature=\"{Auth.GetAuthorization(NonceDecrypted)}\"");


                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        Console.WriteLine("Decrypting and extracting firmware...");
                        File.HandleEncryptedFile(stream, saveTo);
                    }
                }
                Console.WriteLine("\n✓ Download, decryption, and extraction complete!");
                return;
            }

            Console.WriteLine("✓ Download complete, now decrypting and extracting...");
            try
            {
                using (var stream = System.IO.File.OpenRead(encryptedPath))
                {
                    File.HandleEncryptedFile(stream, saveTo);
                }
                Console.WriteLine("\n✓ Decryption and extraction complete!");
            }
            finally
            {
                if (System.IO.File.Exists(encryptedPath))
                {
                    System.IO.File.Delete(encryptedPath);
                }
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
                    "max-tries=5",
                    "retry-wait=3",
                    "timeout=60",
                    "connect-timeout=30",
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
                        Console.WriteLine("aria2c failed to start. Ensure aria2c is installed and available in PATH.");
                        return false;
                    }

                    var waitTask = process.WaitForExitAsync();
                    var completedTask = await Task.WhenAny(waitTask, Task.Delay(Aria2Timeout));
                    if (completedTask != waitTask)
                    {
                        Console.WriteLine($"aria2c timed out downloading {fileName}, falling back to builtin downloader.");
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
                        Console.WriteLine($"aria2c download failed with code {process.ExitCode} for {fileName}, falling back to builtin downloader.");
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
                Console.WriteLine($"aria2c unavailable, falling back to builtin downloader: {ex.Message}");
                return false;
            }
            finally
            {
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
                Console.WriteLine("Error GetResponseFUS() -> " + exception.ToString());
                if (exception.Status == WebExceptionStatus.NameResolutionFailure)
                {
                    SetReconnect();
                }
                return exception.Response;
            }
        }
    }
}
