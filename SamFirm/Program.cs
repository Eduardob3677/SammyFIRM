using System;
using System.IO;
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
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        private static async Task<string> GetLatestVersion(string region, string model, bool useTestServer = false)
        {
            string versionFile = useTestServer ? "version.test.xml" : "version.xml";
            string url = $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/{versionFile}";
            string xmlString = await _httpClient.GetStringAsync(url);
            return XDocument.Parse(xmlString).XPathSelectElement("./versioninfo/firmware/version/latest").Value;
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
            Console.WriteLine($"\n  Model: {model}\n  Region: {region}");
            if (useTestServer)
            {
                Console.WriteLine("  Mode: Test Server (version.test.xml)");
            }

            string latestVersionStr = await GetLatestVersion(region, model, useTestServer);
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
    }
}
