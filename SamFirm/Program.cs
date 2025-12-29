using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        private const string TestFirmwareMapUrl = "https://raw.githubusercontent.com/Mai19930513/SamsungTestFirmwareVersionDecrypt/master/firmware.json";

        private static async Task<Dictionary<string, string>> GetTestFirmwareMap(string region, string model)
        {
            string mappingJson = await _httpClient.GetStringAsync(TestFirmwareMapUrl);
            using JsonDocument document = JsonDocument.Parse(mappingJson);
            if (!document.RootElement.TryGetProperty(model, out JsonElement modelElement)) return null;
            if (!modelElement.TryGetProperty(region, out JsonElement regionElement)) return null;
            if (!regionElement.TryGetProperty("版本号", out JsonElement versionsElement)) return null;

            Dictionary<string, string> map = new Dictionary<string, string>();
            foreach (JsonProperty property in versionsElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    map[property.Name] = property.Value.GetString();
                }
            }
            return map;
        }

        private static async Task<string> GetLatestVersion(string region, string model, bool useTestServer)
        {
            string url = useTestServer
                ? $"https://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.test.xml"
                : $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.xml";

            string xmlString = await _httpClient.GetStringAsync(url);
            XDocument document = XDocument.Parse(xmlString);

            if (!useTestServer)
            {
                return document.XPathSelectElement("./versioninfo/firmware/version/latest").Value;
            }

            IEnumerable<string> md5Values = document.XPathSelectElements("//version/upgrade/value").Select(e => e.Value);
            Dictionary<string, string> versionMap = await GetTestFirmwareMap(region, model);
            if (versionMap != null)
            {
                foreach (string md5 in md5Values)
                {
                    if (versionMap.TryGetValue(md5, out string mappedVersion))
                    {
                        return mappedVersion;
                    }
                }
            }

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
    }
}
