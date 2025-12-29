using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public static Dictionary<string, string> DecryptMD5Versions(string xmlContent, string model, string region)
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

                Console.WriteLine($"Found {md5Values.Count} MD5 values to decrypt...");

                // Get the latest production version to determine base codes
                string latestVer = doc.XPathSelectElement("./versioninfo/firmware/version/latest")?.Value ?? "";
                
                if (string.IsNullOrEmpty(latestVer))
                {
                    Console.WriteLine("No production version found, using default parameters");
                    return decryptedVersions;
                }

                var verParts = latestVer.Split('/');
                if (verParts.Length < 3)
                {
                    Console.WriteLine("Invalid version format");
                    return decryptedVersions;
                }

                // Extract base codes from production version
                string firstCode = verParts[0].Substring(0, verParts[0].Length - 6);  // e.g., S9280ZC
                string secondCode = verParts[1].Substring(0, verParts[1].Length - 5); // e.g., S9280CHC
                string thirdCode = verParts[2].Length > 6 ? verParts[2].Substring(0, verParts[2].Length - 6) : ""; // e.g., S9280ZC

                // Get current year and start parameters
                int currentYear = DateTime.Now.Year;
                char startYear = (char)('A' + (currentYear - 2001 - 3)); // Start from 3 years ago
                char endYear = (char)('A' + (currentYear - 2001 + 1));   // Up to next year
                
                Console.WriteLine($"Decrypting versions for {model}/{region}...");
                Console.WriteLine($"Base codes: {firstCode}, {secondCode}, {thirdCode}");
                
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
                                        
                                        string versionSuffix = $"{updateType}{blVersion}{majorVer}{year}{month}{build}";
                                        string thirdPart = string.IsNullOrEmpty(thirdCode) ? "" : thirdCode + versionSuffix;
                                        string version = $"{firstCode}{versionSuffix}/{secondCode}{versionSuffix}/{thirdPart}";

                                        // Calculate MD5 and check if it matches
                                        string md5Hash = CalculateMD5(version);
                                        if (md5Values.Contains(md5Hash))
                                        {
                                            decryptedVersions[md5Hash] = version;
                                            decryptCount++;
                                            Console.WriteLine($"Decrypted: {version} (MD5: {md5Hash})");
                                            
                                            // Remove from set to speed up future checks
                                            md5Values.Remove(md5Hash);
                                            
                                            if (md5Values.Count == 0)
                                            {
                                                Console.WriteLine($"All MD5 values decrypted! ({decryptCount} versions)");
                                                return decryptedVersions;
                                            }
                                        }
                                        
                                        // Show progress every 100k attempts
                                        if (totalAttempts % 100000 == 0)
                                        {
                                            Console.Write($"\rTried {totalAttempts:N0} combinations, decrypted {decryptCount}/{decryptCount + md5Values.Count}...");
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"\nDecryption complete: {decryptCount} out of {decryptCount + md5Values.Count} versions decrypted");
                Console.WriteLine($"Total attempts: {totalAttempts:N0}");
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
