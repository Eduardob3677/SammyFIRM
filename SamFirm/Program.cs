using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
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

            [Option('t', "test", Required = false, HelpText = "Use test server (version.test.xml)")]
            public bool UseTestServer { get; set; }

            [Option('d', "decrypt", Required = false, HelpText = "Decrypt MD5-encoded test firmware versions")]
            public bool DecryptMD5 { get; set; }

            [Option('v', "version", Required = false, HelpText = "Specific firmware version to download (e.g., S916BXXU8FYLJ/S916BOXM8FYLJ/S916BXXU8FYLJ)")]
            public string SpecificVersion { get; set; }
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        private static async Task<string> GetLatestVersion(string region, string model, bool useTestServer = false)
        {
            string versionFile = useTestServer ? "version.test.xml" : "version.xml";
            string url = $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/{versionFile}";
            string xmlString = await _httpClient.GetStringAsync(url);
            var latestElement = XDocument.Parse(xmlString).XPathSelectElement("./versioninfo/firmware/version/latest");
            
            if (latestElement == null || string.IsNullOrEmpty(latestElement.Value))
            {
                // Return null to signal that we need to decrypt
                return null;
            }
            
            return latestElement.Value;
        }

        static async Task Main(string[] args)
        {
            string model = "";
            string region = "";
            string imei = "";
            bool useTestServer = false;
            bool decryptMD5 = false;
            string specificVersion = "";
            Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                model = o.Model;
                region = o.Region;
                imei = o.imei;
                useTestServer = o.UseTestServer;
                decryptMD5 = o.DecryptMD5;
                specificVersion = o.SpecificVersion ?? "";
            });

            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(region) || string.IsNullOrEmpty(imei)) return;

            try
            {
                Console.OutputEncoding = Encoding.UTF8;
                Console.WriteLine($"\n  Model: {model}\n  Region: {region}");
                if (useTestServer)
                {
                    Console.WriteLine("  Mode: Test Server (version.test.xml)");
                }

                // If decrypt flag is set and using test server, decrypt MD5 values
                if (decryptMD5 && useTestServer)
                {
                    Console.WriteLine("\n=== MD5 Decryption Mode ===");
                    string versionFile = "version.test.xml";
                    string url = $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/{versionFile}";
                    string xmlString = await _httpClient.GetStringAsync(url);
                    
                    var decryptedVersions = await Utils.MD5Decrypt.DecryptMD5VersionsAsync(xmlString, model, region);
                    
                    if (decryptedVersions.Count > 0)
                    {
                        Console.WriteLine($"\n╔════════════════════════════════════════════════════════════════════╗");
                        Console.WriteLine($"║  Decrypted {decryptedVersions.Count} Test Firmware Versions");
                        Console.WriteLine($"╚════════════════════════════════════════════════════════════════════╝\n");
                        
                        var sortedVersions = decryptedVersions.OrderByDescending(x => x.Value).ToList();
                        int count = 1;
                        foreach (var kvp in sortedVersions)
                        {
                            Console.WriteLine($"  [{count}] {kvp.Value}");
                            count++;
                        }
                        
                        Console.WriteLine($"\n╔════════════════════════════════════════════════════════════════════╗");
                        Console.WriteLine($"║  Latest Test Firmware: {sortedVersions[0].Value}");
                        Console.WriteLine($"╚════════════════════════════════════════════════════════════════════╝");
                        
                        Console.WriteLine("\n💡 To download a specific version, run without --decrypt flag and add --version:");
                        Console.WriteLine($"   Example: ./SamFirm -m {model} -r {region} -i {imei} --test --version \"{sortedVersions[0].Value}\"");
                        Console.WriteLine("\n💡 Or simply use --test without --decrypt to auto-download the latest:");
                        Console.WriteLine($"   Example: ./SamFirm -m {model} -r {region} -i {imei} --test");
                    }
                    else
                    {
                        Console.WriteLine("\n❌ No versions could be decrypted. This may happen if:");
                        Console.WriteLine("   - The model/region combination is incorrect");
                        Console.WriteLine("   - The firmware uses a different naming scheme");
                        Console.WriteLine("   - More time is needed for brute-force decryption");
                    }
                    
                    return;
                }

                string latestVersionStr = await GetLatestVersion(region, model, useTestServer);
                
                // If using test server and no readable version, auto-decrypt and get latest
                if (useTestServer && latestVersionStr == null)
                {
                    if (!string.IsNullOrEmpty(specificVersion))
                    {
                        // User provided a specific version to download
                        latestVersionStr = specificVersion;
                        Console.WriteLine($"\n  Using specified version: {latestVersionStr}");
                    }
                    else
                    {
                        // Auto-decrypt to find the latest version
                        Console.WriteLine("\n=== Auto-decrypting test firmware versions ===");
                        string versionFile = "version.test.xml";
                        string url = $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/{versionFile}";
                        string xmlString = await _httpClient.GetStringAsync(url);
                        
                        var decryptedVersions = await Utils.MD5Decrypt.DecryptMD5VersionsAsync(xmlString, model, region);
                        
                        if (decryptedVersions.Count > 0)
                        {
                            var sortedVersions = decryptedVersions.OrderByDescending(x => x.Value).ToList();
                            latestVersionStr = sortedVersions[0].Value;
                            
                            Console.WriteLine($"\n✅ Found latest test firmware: {latestVersionStr}");
                            Console.WriteLine($"   Total versions available: {decryptedVersions.Count}");
                            Console.WriteLine("\n🔽 Starting download...\n");
                        }
                        else
                        {
                            throw new Exception("Could not decrypt any test firmware versions. Try with --decrypt flag to see details.");
                        }
                    }
                }
                string[] versions = latestVersionStr.Split('/');
                string versionPDA = versions[0];
                string versionCSC = versions[1];
                string versionMODEM = versions[2];
                string version = $"{versionPDA}/{versionCSC}/{(versionMODEM.Length > 0 ? versionMODEM : versionPDA)}/{versionPDA}";

                Console.WriteLine($"\n  Latest version:\n    PDA: {versionPDA}\n    CSC: {versionCSC}\n    MODEM: {(versionMODEM.Length > 0 ? versionMODEM : "N/A")}");

                Utils.FUSClient.GenerateNonce();

                string binaryInfoXMLString;
                Utils.FUSClient.DownloadBinaryInform(
                    Utils.Msg.GetBinaryInformMsg(version, region, model, imei, Utils.FUSClient.NonceDecrypted), out binaryInfoXMLString);

                XDocument binaryInfo = XDocument.Parse(binaryInfoXMLString);
                long binaryByteSize = long.Parse(binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/BINARY_BYTE_SIZE/Data").Value);
                string binaryFilename = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/BINARY_NAME/Data").Value;
                string binaryLogicValue = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/LOGIC_VALUE_FACTORY/Data").Value;
                string binaryModelPath = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/MODEL_PATH/Data").Value;
                string binaryVersion = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Results/LATEST_FW_VERSION/Data").Value;

                Utils.FUSClient.DownloadBinaryInit(Utils.Msg.GetBinaryInitMsg(binaryFilename, Utils.FUSClient.NonceDecrypted), out _);

                Utils.File.FileSize = binaryByteSize;
                Utils.File.SetDecryptKey(binaryVersion, binaryLogicValue);

                string savePath = Path.GetFullPath($"./{model}_{region}");
                Console.WriteLine($"\nSaving to: {savePath}");


                await Utils.FUSClient.DownloadBinary(binaryModelPath, binaryFilename, savePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
                if (useTestServer && !decryptMD5)
                {
                    Console.WriteLine("\nHint: Test server firmware versions are MD5-encoded.");
                    Console.WriteLine("Use --decrypt flag to decrypt them first:");
                    Console.WriteLine($"  ./SamFirm -m {model} -r {region} -i {imei} --test --decrypt");
                }
                Environment.Exit(1);
            }
        }
    }
}
