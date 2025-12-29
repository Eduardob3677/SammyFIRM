using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Xml.XPath;

namespace SamFirm.Utils
{
    internal static class MD5Decrypt
    {
        /// <summary>
        /// Decrypt MD5-encoded firmware versions from version.test.xml
        /// Based on the brute-force approach from SamsungTestFirmwareVersionDecrypt
        /// </summary>
        public static async Task<Dictionary<string, string>> DecryptMD5VersionsAsync(string xmlContent, string model, string region)
        {
            var decryptedVersions = new Dictionary<string, string>();
            
            try
            {
                // Parse XML and get MD5 values
                XDocument doc = XDocument.Parse(xmlContent);
                var md5Values = doc.XPathSelectElements("//upgrade/value")
                    .Select(e => e.Value)
                    .ToHashSet();

                if (md5Values.Count == 0)
                {
                    Console.WriteLine("No MD5 values found in version.test.xml");
                    return decryptedVersions;
                }

                int originalMD5Count = md5Values.Count;
                Console.WriteLine($"Found {originalMD5Count} MD5 values to decrypt...");

                // Get the latest production version to determine base codes
                string latestVer = doc.XPathSelectElement("./versioninfo/firmware/version/latest")?.Value ?? "";
                
                // If no version in test XML, try to get it from production XML
                if (string.IsNullOrEmpty(latestVer))
                {
                    Console.WriteLine("No production version in test XML, fetching from production server...");
                    try
                    {
                        string prodUrl = $"http://fota-cloud-dn.ospserver.net/firmware/{region}/{model}/version.xml";
                        string prodXmlString = await new HttpClient().GetStringAsync(prodUrl);
                        XDocument prodDoc = XDocument.Parse(prodXmlString);
                        latestVer = prodDoc.XPathSelectElement("./versioninfo/firmware/version/latest")?.Value ?? "";
                        
                        if (!string.IsNullOrEmpty(latestVer))
                        {
                            Console.WriteLine($"Found production version: {latestVer}");
                        }
                    }
                    catch
                    {
                        Console.WriteLine("Could not fetch production version, will construct from model name");
                    }
                }
                
                string firstCode, secondCode, thirdCode;
                
                if (!string.IsNullOrEmpty(latestVer))
                {
                    var verParts = latestVer.Split('/');
                    if (verParts.Length >= 3)
                    {
                        // Extract base codes from production version
                        // Python uses [:-6] which removes last 6 characters
                        firstCode = verParts[0].Substring(0, verParts[0].Length - 6);  // e.g., S916BXX (without final S/U)
                        secondCode = verParts[1].Substring(0, verParts[1].Length - 5); // e.g., S916BOXM
                        thirdCode = verParts[2].Length > 6 ? verParts[2].Substring(0, verParts[2].Length - 6) : ""; // e.g., S916BXX
                        
                        Console.WriteLine($"Using production version as reference: {latestVer}");
                        Console.WriteLine($"Base codes: {firstCode}, {secondCode}, {thirdCode}");
                    }
                    else
                    {
                        Console.WriteLine("Invalid production version format, using model-based codes");
                        firstCode = model.Replace("SM-", "").Replace("-", "") + "XX"; // Generic
                        secondCode = model.Replace("SM-", "").Replace("-", "") + "OXM";
                        thirdCode = model.Replace("SM-", "").Replace("-", "") + "XX";
                    }
                }
                else
                {
                    // No production version available, construct from model
                    Console.WriteLine("No production version found, constructing from model name");
                    string modelCode = model.Replace("SM-", "").Replace("-", "");
                    
                    // Try to guess region codes - try multiple possibilities
                    string[] regionPrefixes;
                    
                    if (region == "CHC" || region == "CHN")
                    {
                        regionPrefixes = new[] { "ZCS", "ZCU", "ZHU" }; // China variants
                    }
                    else if (region == "EUX" || region.StartsWith("E"))
                    {
                        regionPrefixes = new[] { "XXU", "DBT", "OXM" }; // Europe variants
                    }
                    else if (region == "KOO")
                    {
                        regionPrefixes = new[] { "KSU", "SKC", "KTC" }; // Korea variants
                    }
                    else if (region == "XAA")
                    {
                        regionPrefixes = new[] { "UEU", "TMB", "ATT" }; // USA variants
                    }
                    else
                    {
                        regionPrefixes = new[] { "XXU", "OXM" }; // Generic global
                    }
                    
                    // Try first prefix
                    firstCode = modelCode + regionPrefixes[0];
                    secondCode = modelCode + (region.Length == 3 ? region : "OXM"); // Use actual region or OXM
                    thirdCode = modelCode + regionPrefixes[0];
                    
                    Console.WriteLine($"Constructed base codes: {firstCode}, {secondCode}, {thirdCode}");
                    Console.WriteLine($"Will also try alternate prefixes: {string.Join(", ", regionPrefixes.Skip(1))}");
                }

                // Get current year and start parameters  
                int currentYear = DateTime.Now.Year;
                // Expand range: from 5 years ago to 2 years in future
                char startYear = (char)('A' + Math.Max(0, currentYear - 2001 - 5));
                char endYear = (char)('A' + Math.Min(25, currentYear - 2001 + 2));
                
                Console.WriteLine($"Decrypting versions for {model}/{region}...");
                Console.WriteLine($"Year range: {startYear} ({2001 + (startYear - 'A')}) to {endYear} ({2001 + (endYear - 'A')})");
                Console.WriteLine($"This may take several minutes depending on the number of versions...\n");
                
                int decryptCount = 0;
                int totalAttempts = 0;

                // Brute force through possible version combinations
                foreach (char updateType in new[] { 'U', 'S' }) // U=major update, S=security patch
                {
                    for (char blVersion = '0'; blVersion <= '9'; blVersion++) // Bootloader version
                    {
                        foreach (char majorVer in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") // Major version
                        {
                            for (char year = startYear; year <= endYear; year++) // Year
                            {
                                foreach (char month in "ABCDEFGHIJKL") // Month (A=Jan, L=Dec)
                                {
                                    // Try different build identifiers
                                    foreach (char build in "123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
                                    {
                                        totalAttempts++;
                                        
                                        string versionSuffix = $"{blVersion}{majorVer}{year}{month}{build}";
                                        string thirdPart = string.IsNullOrEmpty(thirdCode) ? "" : thirdCode + updateType + versionSuffix;
                                        // NOTE: SecondCode does NOT get the updateType prefix, only randomVersion
                                        string version = $"{firstCode}{updateType}{versionSuffix}/{secondCode}{versionSuffix}/{thirdPart}";

                                        // Calculate MD5 and check if it matches
                                        string md5Hash = CalculateMD5(version);
                                        if (md5Values.Contains(md5Hash))
                                        {
                                            decryptedVersions[md5Hash] = version;
                                            decryptCount++;
                                            Console.WriteLine($"✓ Decrypted [{decryptCount}/{originalMD5Count}]: {version}");
                                            
                                            // Remove from set to speed up future checks
                                            md5Values.Remove(md5Hash);
                                            
                                            if (md5Values.Count == 0)
                                            {
                                                Console.WriteLine($"\n✅ All {decryptCount} MD5 values successfully decrypted!");
                                                return decryptedVersions;
                                            }
                                        }
                                        
                                        // Show progress every 100k attempts
                                        if (totalAttempts % 100000 == 0)
                                        {
                                            Console.Write($"\r⏳ Progress: {totalAttempts:N0} attempts, {decryptCount}/{originalMD5Count} decrypted ({(decryptCount * 100.0 / originalMD5Count):F1}%)...");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"\n\n📊 Decryption Summary:");
                Console.WriteLine($"   Total attempts: {totalAttempts:N0}");
                Console.WriteLine($"   Decrypted: {decryptCount} out of {originalMD5Count} versions ({(decryptCount * 100.0 / originalMD5Count):F1}%)");
                Console.WriteLine($"   Remaining: {md5Values.Count} versions could not be decrypted");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during MD5 decryption: {ex.Message}");
            }

            return decryptedVersions;
        }

        /// <summary>
        /// Calculate MD5 hash of a string
        /// </summary>
        private static string CalculateMD5(string input)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(input);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                {
                    sb.Append(hashBytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
