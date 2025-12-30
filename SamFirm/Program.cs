using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;
using CommandLine;
using SamFirm.Utils;

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

            [Option("ap", Required = false, HelpText = "Download only AP (Application Processor) file")]
            public bool DownloadAP { get; set; }

            [Option("bl", Required = false, HelpText = "Download only BL (Bootloader) file")]
            public bool DownloadBL { get; set; }

            [Option("cp", Required = false, HelpText = "Download only CP (Modem/Radio) file")]
            public bool DownloadCP { get; set; }

            [Option("csc", Required = false, HelpText = "Download only CSC (Consumer Software Customization) file")]
            public bool DownloadCSC { get; set; }

            [Option("home-csc", Required = false, HelpText = "Download only HOME_CSC file")]
            public bool DownloadHomeCSC { get; set; }
        }

        private static readonly HttpClient _httpClient = new HttpClient();

        private static async Task<string> GetLatestVersion(string region, string model)
        {

            string url = $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.xml";
            string xmlString = await _httpClient.GetStringAsync(url);
            return XDocument.Parse(xmlString).XPathSelectElement("./versioninfo/firmware/version/latest").Value;
        }

        static async Task Main(string[] args)
        {
            string model = "";
            string region = "";
            string imei = "";
            string[] components = null;
            Parser.Default.ParseArguments<Options>(args)
            .WithParsed(o =>
            {
                model = o.Model;
                region = o.Region;
                imei = o.imei;

                // Build component filter based on options
                var selectedComponents = new System.Collections.Generic.List<string>();
                if (o.DownloadAP) selectedComponents.Add("AP");
                if (o.DownloadBL) selectedComponents.Add("BL");
                if (o.DownloadCP) selectedComponents.Add("CP");
                if (o.DownloadCSC) selectedComponents.Add("CSC");
                if (o.DownloadHomeCSC) selectedComponents.Add("HOME_CSC");

                // If no specific components selected, download all
                if (selectedComponents.Count > 0)
                {
                    components = selectedComponents.ToArray();
                }
            });

            if (string.IsNullOrEmpty(model) || string.IsNullOrEmpty(region) || string.IsNullOrEmpty(imei)) return;

            Console.OutputEncoding = Encoding.UTF8;
            Logger.Raw($"\n  Model: {model}\n  Region: {region}");

            string latestVersionStr = await GetLatestVersion(region, model);
            string[] versions = latestVersionStr.Split('/');
            string versionPDA = versions[0];
            string versionCSC = versions[1];
            string versionMODEM = versions[2];
            string version = $"{versionPDA}/{versionCSC}/{(versionMODEM.Length > 0 ? versionMODEM : versionPDA)}/{versionPDA}";

            Logger.Raw($"  Latest version:\n    PDA: {versionPDA}\n    CSC: {versionCSC}\n    MODEM: {(versionMODEM.Length > 0 ? versionMODEM : "N/A")}");

            Logger.Info("Fetching firmware information...");
            FUSClient.GenerateNonce();

            string binaryInfoXMLString;
            FUSClient.DownloadBinaryInform(
                Msg.GetBinaryInformMsg(version, region, model, imei, FUSClient.NonceDecrypted), out binaryInfoXMLString);

            XDocument binaryInfo = XDocument.Parse(binaryInfoXMLString);
            long binaryByteSize = long.Parse(binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/BINARY_BYTE_SIZE/Data").Value);
            string binaryFilename = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/BINARY_NAME/Data").Value;
            string binaryLogicValue = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/LOGIC_VALUE_FACTORY/Data").Value;
            string binaryModelPath = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Put/MODEL_PATH/Data").Value;
            string binaryVersion = binaryInfo.XPathSelectElement("./FUSMsg/FUSBody/Results/LATEST_FW_VERSION/Data").Value;

            Logger.Raw($"  Firmware file: {binaryFilename}");
            Logger.Raw($"  Firmware size: {binaryByteSize / (1024.0 * 1024.0 * 1024.0):F2} GB");

            FUSClient.DownloadBinaryInit(Msg.GetBinaryInitMsg(binaryFilename, FUSClient.NonceDecrypted), out _);

            Utils.File.FileSize = binaryByteSize;
            Utils.File.SetDecryptKey(binaryVersion, binaryLogicValue);

            string savePath = Path.GetFullPath($"./{model}_{region}");
            Logger.Raw($"  Save path: {savePath}");

            if (components != null && components.Length > 0)
            {
                Logger.Info($"Selected components: {string.Join(", ", components)}");
            }
            else
            {
                Logger.Info("Downloading all components");
            }

            try
            {
                await FUSClient.DownloadBinary(binaryModelPath, binaryFilename, savePath, components);
            }
            catch (IOException ex) when (ex.Message.Contains("No space left on device"))
            {
                Logger.ErrorExit($"Not enough disk space to download firmware: {ex.Message}", 1);
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Logger.ErrorExit($"Failed to download firmware: {ex.Message}", 1);
                Environment.Exit(1);
            }
        }
    }
}
