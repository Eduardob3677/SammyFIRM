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


        private static readonly HttpClient _httpClient = new HttpClient();


        public static async Task DownloadBinary(string path, string file, string saveTo)
        {
            string url = "http://cloud-neofussvr.samsungmobile.com/NF_DownloadBinaryForMass.do?file=" + path + file;

            Directory.CreateDirectory(saveTo);
            string encryptedPath = Path.Combine(saveTo, $"{file}.enc2");

            if (!await TryDownloadWithAria2c(url, encryptedPath))
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("User-Agent", "Kies2.0_FUS");
                request.Headers.Add("Authorization", $"FUS nonce=\"{Nonce}\", signature=\"{Auth.GetAuthorization(NonceDecrypted)}\"");


                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {

                        File.HandleEncryptedFile(stream, saveTo);
                    }
                }
                return;
            }

            try
            {
                using (var stream = System.IO.File.OpenRead(encryptedPath))
                {
                    File.HandleEncryptedFile(stream, saveTo);
                }
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
            try
            {
                string dir = Path.GetDirectoryName(outputPath) ?? string.Empty;
                if (string.IsNullOrEmpty(dir))
                {
                    dir = Directory.GetCurrentDirectory();
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "aria2c",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                psi.ArgumentList.Add("--continue=true");
                psi.ArgumentList.Add("--max-connection-per-server=16");
                psi.ArgumentList.Add("--split=16");
                psi.ArgumentList.Add("--min-split-size=1M");
                psi.ArgumentList.Add("--allow-overwrite=true");
                psi.ArgumentList.Add("--auto-file-renaming=false");
                psi.ArgumentList.Add("--disable-ipv6=true");
                psi.ArgumentList.Add("--header=User-Agent: Kies2.0_FUS");
                psi.ArgumentList.Add($"--header=Authorization: FUS nonce=\"{Nonce}\", signature=\"{Auth.GetAuthorization(NonceDecrypted)}\"");
                psi.ArgumentList.Add($"--out={Path.GetFileName(outputPath)}");
                psi.ArgumentList.Add($"--dir={dir}");
                psi.ArgumentList.Add(url);

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return false;
                }

                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"aria2c download failed with code {process.ExitCode} for {url}, falling back to builtin downloader.");
                    return false;
                }

                return System.IO.File.Exists(outputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"aria2c unavailable, falling back to builtin downloader: {ex.Message}");
                return false;
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
