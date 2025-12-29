#pragma warning disable SYSLIB0014
using System;
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

        public static HttpWebRequest CreateForOsp(string requestUriString, string model, string region)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(requestUriString);
            request.Headers["Cache-Control"] = "no-cache";
            request.UserAgent = "Kies2.0_FUS";
            
            // Use OAuth if enabled, otherwise use FUS auth
            if (FUSClient.UseOAuth)
            {
                // Generate OAuth 1.0 Authorization header
                var uri = new Uri(requestUriString);
                var oauthHeader = OAuthHelper.GenerateOAuthHeader("POST", requestUriString, uri.Query);
                request.Headers.Add("Authorization", oauthHeader);
                Console.WriteLine($"  Using OAuth 1.0 authentication");
            }
            else
            {
                // Use traditional FUS authentication
                request.Headers.Add("Authorization",
                    $"FUS nonce=\"{FUSClient.Nonce}\", signature=\"{(FUSClient.NonceDecrypted.Length > 0 ? Auth.GetAuthorization(FUSClient.NonceDecrypted) : "")}\", nc=\"\", type=\"\", realm=\"\", newauth=\"1\"");
            }
            
            // Add OSP-specific headers based on FOTA agent analysis
            request.Headers.Add("X-Sec-Dm-DeviceModel", model);
            request.Headers.Add("X-Sec-Dm-CustomerCode", region);
            request.Headers.Add("x-osp-version", "v1");
            request.Headers.Add("Accept-Encoding", "identity");
            request.ContentType = "text/xml";
            request.CookieContainer = FUSClient.Cookies;
            return request;
        }
    }

    internal static class FUSClient
    {
        public static CookieContainer Cookies = new CookieContainer();
        public static string Nonce { get; set; } = string.Empty;
        public static string NonceDecrypted { get; set; } = string.Empty;
        public static string Model { get; set; } = string.Empty;
        public static string Region { get; set; } = string.Empty;
        public static bool UseOAuth { get; set; } = false;


        private static readonly HttpClient _httpClient = new HttpClient();


        public static async Task DownloadBinary(string path, string file, string saveTo)
        {
            string url = "http://cloud-neofussvr.samsungmobile.com/NF_DownloadBinaryForMass.do?file=" + path + file;

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

        private static int XMLFUSRequest(string URL, string xml, out string xmlresponse)
        {
            xmlresponse = null;
            HttpWebRequest wr;
            
            // Use OSP-specific headers if model and region are set
            if (!string.IsNullOrEmpty(Model) && !string.IsNullOrEmpty(Region))
            {
                wr = FUSRequest.CreateForOsp(URL, Model, Region);
            }
            else
            {
                wr = FUSRequest.Create(URL);
            }
            
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
