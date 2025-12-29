using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace SamFirm.Utils
{
    /// <summary>
    /// OAuth 1.0 implementation for Samsung OSP server authentication
    /// Based on FotaAgent RequestPropertiesForOsp$WithAuth analysis
    /// </summary>
    internal static class OAuthHelper
    {
        // Service IDs from Flavor.smali
        private const string SERVICE_ID_X6G = "x6g1q14r75";
        private const string SERVICE_ID_MFORM = "mformtest2020";
        
        // OAuth keys extracted from libdprw.so and profile analysis
        // Found near getTimeKey/getTimeValue methods
        private const string OAUTH_CONSUMER_KEY_PRIMARY = "j5p7ll8g33";
        private const string OAUTH_CONSUMER_SECRET_PRIMARY = "2cbmvps5z4";
        
        // From x6g1q14r75 profile credentials (SyncML DM auth)
        // Server: ns.ospserver.net/v1/device/magicsync/mdm
        // Server Password (base64): T1NQIERNIFNIcnZIcg== -> "OSP DM SHrvHr"
        private const string OAUTH_CONSUMER_KEY_X6G = "x6g1q14r75";
        private const string OAUTH_CONSUMER_SECRET_X6G = "OSP DM SHrvHr";
        
        // From mformtest2020 profile credentials (SyncML DM auth)
        // Server: https://iotnucleon.iot.nokia.com/oma/iop
        // Server Password: mform, Client Password: mform
        private const string OAUTH_CONSUMER_KEY_MFORM = "mformtest2020";
        private const string OAUTH_CONSUMER_SECRET_MFORM = "mform";
        
        // Client credentials from x6g1q14r75 profile
        private const string CLIENT_PASSWORD_X6G = "74V1gEt664mAKin01";
        
        // Hex keys from libdprw.so
        private const string OAUTH_CONSUMER_KEY_HEX = "5763D0052DC1462E13751F753384E9A9";
        private const string OAUTH_CONSUMER_SECRET_HEX = "AF87056C54E8BFD81142D235F4F8E552";

        // Current active keys (default to mformtest2020 for Nokia test server)
        private static string ActiveConsumerKey = OAUTH_CONSUMER_KEY_MFORM;
        private static string ActiveConsumerSecret = OAUTH_CONSUMER_SECRET_MFORM;

        /// <summary>
        /// Generate OAuth 1.0 Authorization header
        /// </summary>
        public static string GenerateOAuthHeader(string httpMethod, string url, string queryString = null)
        {
            // Generate OAuth parameters
            var timestamp = GetTimestamp();
            var nonce = GenerateNonce();
            
            var oauthParams = new Dictionary<string, string>
            {
                { "oauth_consumer_key", ActiveConsumerKey },
                { "oauth_signature_method", "HMAC-SHA1" },
                { "oauth_timestamp", timestamp },
                { "oauth_nonce", nonce },
                { "oauth_version", "1.0" }
            };

            // Generate signature
            var signature = GenerateSignature(httpMethod, url, queryString, oauthParams, ActiveConsumerSecret);
            oauthParams.Add("oauth_signature", signature);

            // Build OAuth header
            var headerValue = "OAuth " + string.Join(", ",
                oauthParams.Select(kvp => $"{kvp.Key}=\"{Uri.EscapeDataString(kvp.Value)}\""));

            Console.WriteLine($"  OAuth Consumer Key: {ActiveConsumerKey}");
            Console.WriteLine($"  OAuth Timestamp: {timestamp}");
            Console.WriteLine($"  OAuth Nonce: {nonce}");

            return headerValue;
        }

        /// <summary>
        /// Try different key combinations
        /// </summary>
        public static void SetKeySet(int keySetIndex)
        {
            switch (keySetIndex)
            {
                case 1: // mformtest2020 profile (Nokia test server)
                    ActiveConsumerKey = OAUTH_CONSUMER_KEY_MFORM;
                    ActiveConsumerSecret = OAUTH_CONSUMER_SECRET_MFORM;
                    Console.WriteLine($"  Using mformtest2020 keys: {OAUTH_CONSUMER_KEY_MFORM}");
                    break;
                case 2: // x6g1q14r75 profile (OSP server)
                    ActiveConsumerKey = OAUTH_CONSUMER_KEY_X6G;
                    ActiveConsumerSecret = OAUTH_CONSUMER_SECRET_X6G;
                    Console.WriteLine($"  Using x6g1q14r75 keys: {SERVICE_ID_X6G}");
                    break;
                case 3: // Library extracted keys
                    ActiveConsumerKey = OAUTH_CONSUMER_KEY_PRIMARY;
                    ActiveConsumerSecret = OAUTH_CONSUMER_SECRET_PRIMARY;
                    Console.WriteLine($"  Using Primary keys from libdprw.so");
                    break;
                case 4: // Hex keys
                    ActiveConsumerKey = OAUTH_CONSUMER_KEY_HEX;
                    ActiveConsumerSecret = OAUTH_CONSUMER_SECRET_HEX;
                    Console.WriteLine($"  Using Hex keys from libdprw.so");
                    break;
                case 5: // x6g1q14r75 with client password
                    ActiveConsumerKey = OAUTH_CONSUMER_KEY_X6G;
                    ActiveConsumerSecret = CLIENT_PASSWORD_X6G;
                    Console.WriteLine($"  Using x6g1q14r75 key with Client password");
                    break;
                default:
                    // Keep current
                    break;
            }
        }

        /// <summary>
        /// Generate OAuth signature using HMAC-SHA1
        /// Based on FotaAgent generateSignature and computeSignature methods
        /// </summary>
        private static string GenerateSignature(string httpMethod, string url, string queryString, 
            Dictionary<string, string> oauthParams, string consumerSecret)
        {
            // 1. Create signature base string
            var signatureBase = GenerateSignatureBaseString(httpMethod, url, queryString, oauthParams);

            // 2. Create signing key: consumer_secret&
            var signingKey = $"{Uri.EscapeDataString(consumerSecret)}&";

            // 3. Compute HMAC-SHA1
            using (var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase));
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Generate signature base string according to OAuth 1.0 spec
        /// Format: HTTP_METHOD&URL_ENCODED&PARAMS_ENCODED
        /// Based on FotaAgent generateSignatureSource method
        /// </summary>
        private static string GenerateSignatureBaseString(string httpMethod, string url, 
            string queryString, Dictionary<string, string> oauthParams)
        {
            // 1. Normalize URL (remove query string, fragment, default ports)
            var normalizedUrl = NormalizeUrl(url);

            // 2. Collect and sort all parameters
            var allParams = new SortedDictionary<string, string>(oauthParams);
            
            // Add query string parameters if present
            if (!string.IsNullOrEmpty(queryString))
            {
                var queryParams = HttpUtility.ParseQueryString(queryString);
                foreach (string key in queryParams)
                {
                    if (!string.IsNullOrEmpty(key))
                    {
                        allParams[key] = queryParams[key];
                    }
                }
            }

            // 3. Create parameter string (sorted, URL encoded)
            var paramString = string.Join("&",
                allParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            // 4. Build signature base string
            var signatureBase = $"{httpMethod.ToUpperInvariant()}&" +
                              $"{Uri.EscapeDataString(normalizedUrl)}&" +
                              $"{Uri.EscapeDataString(paramString)}";

            return signatureBase;
        }

        /// <summary>
        /// Normalize URL for OAuth signature
        /// Based on FotaAgent normalizeUrlWithOAuthSpec method
        /// </summary>
        private static string NormalizeUrl(string url)
        {
            var uri = new Uri(url);
            
            // Build normalized URL: scheme://host:port/path
            var normalized = $"{uri.Scheme.ToLower()}://{uri.Host.ToLower()}";
            
            // Add port if not default
            if ((uri.Scheme == "http" && uri.Port != 80) || 
                (uri.Scheme == "https" && uri.Port != 443))
            {
                normalized += $":{uri.Port}";
            }
            
            normalized += uri.AbsolutePath;
            
            return normalized;
        }

        /// <summary>
        /// Generate Unix timestamp in seconds
        /// </summary>
        private static string GetTimestamp()
        {
            var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var timestamp = (long)(DateTime.UtcNow - unixEpoch).TotalSeconds;
            return timestamp.ToString();
        }

        /// <summary>
        /// Generate random nonce (10-character hexadecimal)
        /// Based on FotaAgent generateRandomToken method using SHA1PRNG
        /// </summary>
        private static string GenerateNonce()
        {
            // Generate 5 random bytes (will become 10 hex chars)
            var bytes = new byte[5];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            
            // Convert to hex string
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }
    }
}
