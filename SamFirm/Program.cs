using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using CommandLine;

namespace SamFirm
{
    class Program
    {
        public class Options
        {
            [Option('m', "model", Required = true)]
            public string Model { get; set; }

            [Option('r', "region", Required = true)]
            public string Region { get; set; }

            [Option('i', "imei", Required = true)]
            public string imei { get; set; }

            [Option('t', "test", Required = false, HelpText = "Use test firmware server (version.test.xml)")]
            public bool UseTestServer { get; set; }
        }

        private static readonly HttpClient _httpClient = new HttpClient();
        private const string TestVersionUrlTemplate = "https://fota-cloud-dn.ospserver.net/firmware/{0}/{1}/version.test.xml";
        private const string RegularVersionUrlTemplate = "http://fota-cloud-dn.ospserver.net/firmware/{0}/{1}/version.xml";
        private const int PdaSuffixLength = 6;
        private const int CscSuffixLength = 5;
        private const string MonthChars = "ABCDEFGHIJKL";
        private const string SerialAlphabet = "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        private static char NextChar(char c, string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            int idx = alphabet.IndexOf(c);
            if (idx < 0 || idx + 1 >= alphabet.Length) return c;
            return alphabet[idx + 1];
        }

        private static string ComputeMd5(string input)
        {
            using MD5 md5 = MD5.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = md5.ComputeHash(bytes);
            StringBuilder sb = new StringBuilder();
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static string TryDecryptTestVersion(
            HashSet<string> md5Targets,
            string[] latestParts)
        {
            if (latestParts.Length < 2 || md5Targets == null || md5Targets.Count == 0) return null;
            string latestPda = latestParts[0] ?? string.Empty;
            string latestCsc = latestParts[1] ?? string.Empty;
            string latestCp = latestParts.Length > 2 ? latestParts[2] : string.Empty;

            if (string.IsNullOrEmpty(latestPda) || string.IsNullOrEmpty(latestCsc)) return null;

            string firstCode = latestPda.Length > PdaSuffixLength ? latestPda[..^PdaSuffixLength] : latestPda;   // e.g. S9280ZC
            string secondCode = latestCsc.Length > CscSuffixLength ? latestCsc[..^CscSuffixLength] : latestCsc;  // e.g. S9280CH
            string thirdCode = latestCp.Length > PdaSuffixLength ? latestCp[..^PdaSuffixLength] : string.Empty;

            char startBl = latestPda.Length >= 5 ? latestPda[^5] : '0';
            char updateChar = latestPda.Length >= 4 ? latestPda[^4] : 'A';
            char yearChar = latestPda.Length >= 3 ? latestPda[^3] : 'A';

            ReadOnlySpan<char> months = MonthChars;
            ReadOnlySpan<char> serials = SerialAlphabet;

            Span<char> bootloaders = stackalloc char[] { startBl, NextChar(startBl) };
            Span<char> updates = stackalloc char[] { updateChar, NextChar(updateChar), 'Z' };
            Span<char> years = stackalloc char[] { yearChar, NextChar(yearChar) };

            Span<char> prefixes = stackalloc char[] { 'U', 'S' };
            foreach (char i1 in prefixes)
            {
                foreach (char bl in bootloaders)
                {
                    foreach (char upd in updates)
                    {
                        foreach (char year in years)
                        {
                            foreach (char month in months)
                            {
                                foreach (char serial in serials)
                                {
                                    string randomVersion = $"{bl}{upd}{year}{month}{serial}";
                                    string cpPart = string.IsNullOrEmpty(thirdCode) ? string.Empty : $"{thirdCode}{i1}{randomVersion}";
                                    
                                    // Try with CP part if available, otherwise try without it
                                    string candidate = string.IsNullOrEmpty(cpPart) 
                                        ? $"{firstCode}{i1}{randomVersion}/{secondCode}{randomVersion}"
                                        : $"{firstCode}{i1}{randomVersion}/{secondCode}{randomVersion}/{cpPart}";
                                    
                                    string hash = ComputeMd5(candidate);
                                    if (md5Targets.Contains(hash))
                                    {
                                        return candidate;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static async Task<string> GetLatestVersion(string region, string model, bool useTestServer)
        {
            string url = useTestServer
                ? string.Format(TestVersionUrlTemplate, region, model)
                : string.Format(RegularVersionUrlTemplate, region, model);

            string xmlString;
            try
            {
                xmlString = await _httpClient.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to fetch version XML from {url}", ex);
            }
            XDocument document = XDocument.Parse(xmlString);

            if (!useTestServer)
            {
                XElement latestEl = document.XPathSelectElement("./versioninfo/firmware/version/latest");
                if (latestEl == null) throw new InvalidOperationException("version.xml missing <latest> element");
                return latestEl.Value;
            }

            HashSet<string> md5Values = document.XPathSelectElements("//version/upgrade/value").Select(e => e.Value).ToHashSet();
            // get regular latest version info as seed
            string[] latestParts = Array.Empty<string>();
            try
            {
                string regularXml = await _httpClient.GetStringAsync(string.Format(RegularVersionUrlTemplate, region, model));
                XDocument regularDoc = XDocument.Parse(regularXml);
                string regularLatest = regularDoc.XPathSelectElement("./versioninfo/firmware/version/latest")?.Value;
                latestParts = regularLatest?.Split('/') ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                // fallback to empty latestParts; will return null if we cannot resolve
            }

            string resolved = TryDecryptTestVersion(md5Values, latestParts);
            if (resolved != null) return resolved;

            throw new InvalidOperationException("Unable to resolve test firmware version from version.test.xml");
        }

        static async Task Main(string[] args)
        {
            string model = "";
            string region = "";
            string imei = "";
            bool useTestServer = false;
            Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                model = o.Model;
                region = o.Region;
                imei = o.imei;
                useTestServer = o.UseTestServer;
            });

            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(region) || string.IsNullOrEmpty(imei)) return;

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"\n  Model: {model}\n  Region: {region}" + (useTestServer ? "\n  Using test firmware server" : string.Empty));

            string latestVersionStr;
            try
            {
                latestVersionStr = await GetLatestVersion(region, model, useTestServer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to resolve latest firmware version: {ex.Message}");
                return;
            }
            string[] versions = latestVersionStr.Split('/');
            string versionPDA = versions.Length > 0 ? versions[0] : string.Empty;
            string versionCSC = versions.Length > 1 ? versions[1] : string.Empty;
            string versionMODEM = versions.Length > 2 ? versions[2] : string.Empty;
            string version = $"{versionPDA}/{versionCSC}/{(versionMODEM.Length > 0 ? versionMODEM : versionPDA)}/{versionPDA}";

            Console.WriteLine($"\n  Latest version:\n    PDA: {versionPDA}\n    CSC: {versionCSC}\n    MODEM: {(versionMODEM.Length > 0 ? versionMODEM : "N/A")}");

            Utils.FUSClient.GenerateNonce();

            string binaryInfoXMLString;
            int informStatus = Utils.FUSClient.DownloadBinaryInform(
                Utils.Msg.GetBinaryInformMsg(version, region, model, imei, Utils.FUSClient.NonceDecrypted), out binaryInfoXMLString);
            if (informStatus != 200 || string.IsNullOrEmpty(binaryInfoXMLString))
            {
                Console.WriteLine($"Failed to fetch binary info (status {informStatus}).");
                return;
            }

            XDocument binaryInfo = XDocument.Parse(binaryInfoXMLString);
            XElement sizeEl = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/BINARY_BYTE_SIZE/Data");
            XElement nameEl = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/BINARY_NAME/Data");
            XElement logicEl = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/LOGIC_VALUE_FACTORY/Data");
            XElement pathEl = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/MODEL_PATH/Data");
            XElement versionEl = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Results/LATEST_FW_VERSION/Data");
            if (sizeEl == null || nameEl == null || logicEl == null || pathEl == null || versionEl == null)
            {
                Console.WriteLine("Binary info response missing required fields.");
                return;
            }
            long binaryByteSize = long.Parse(sizeEl.Value);
            string binaryFilename = nameEl.Value;
            string binaryLogicValue = logicEl.Value;
            string binaryModelPath = pathEl.Value;
            string binaryVersion = versionEl.Value;

            Utils.FUSClient.DownloadBinaryInit(Utils.Msg.GetBinaryInitMsg(binaryFilename, Utils.FUSClient.NonceDecrypted), out _);

            Utils.File.FileSize = binaryByteSize;
            Utils.File.SetDecryptKey(binaryVersion, binaryLogicValue);

            string savePath = Path.GetFullPath($"./{model}_{region}");
            Console.WriteLine($"\nSaving to: {savePath}");


            await Utils.FUSClient.DownloadBinary(binaryModelPath, binaryFilename, savePath);
        }
    }
}
