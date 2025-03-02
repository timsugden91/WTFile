using System.IO.Compression;
using System.Text;
using System.Text.Json;
namespace WTFile
{
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                // Check if a file path is provided as a command-line argument
                if (args.Length == 0)
                {
                    Console.WriteLine("Please provide a file path as a command-line argument.");
                    return;
                }

                // Get the file path from the command-line arguments
                string filePath = args[0];
                // Path to the JSON file containing file signatures
                string magicNumbers = "C:\\Users\\Tim\\source\\repos\\WTFile\\WTFile\\file_sigs.json";
                // Read the JSON content from the file
                string jsonContent = File.ReadAllText(magicNumbers);

                // Read the first 64 bytes of the specified file (to ensure we have enough bytes for comparison)
                byte[] fileBytes = new byte[64];
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    fs.ReadExactly(fileBytes);
                }

                // Convert the bytes to a hex string
                StringBuilder hex = new StringBuilder(fileBytes.Length * 2);
                foreach (byte b in fileBytes)
                {
                    hex.AppendFormat("{0:x2}", b);
                }
                string fileHex = hex.ToString();

                // Parse the JSON content
                using (JsonDocument document = JsonDocument.Parse(jsonContent))
                {
                    JsonElement root = document.RootElement;
                    JsonElement fileSigs = root.GetProperty("filesigs");
                    bool matchFound = false;

                    // Iterate through each file signature in the JSON array
                    foreach (JsonElement fileSig in fileSigs.EnumerateArray())
                    {
                        if (fileSig.TryGetProperty("Header (hex)", out JsonElement headerHexElement) &&
                            fileSig.TryGetProperty("Header offset", out JsonElement headerOffsetElement) &&
                            fileSig.TryGetProperty("File description", out JsonElement descriptionElement) &&
                            fileSig.TryGetProperty("File extension", out JsonElement extensionElement))
                        {
                            string sig = headerHexElement.GetString()?.Replace(" ", "").ToLower() ?? string.Empty;
                            int offset = int.TryParse(headerOffsetElement.GetString(), out int parsedOffset) ? parsedOffset : 0;

                            // Ensure we have enough bytes to compare
                            if (fileHex.Length >= (offset * 2 + sig.Length))
                            {
                                // Compare the file's hex string with the signature at the specified offset
                                if (fileHex.Substring(offset * 2, sig.Length) == sig)
                                {
                                    // If a match is found, retrieve and print the file description and extension
                                    string description = descriptionElement.GetString() ?? "Unknown";
                                    string extension = extensionElement.GetString() ?? "Unknown";

                                    // Additional check for DOCX files
                                    if (extension.Contains("ZIP") && IsDocxFile(filePath))
                                    {
                                        description = "Microsoft Office Open XML Format Document";
                                        extension = "DOCX";
                                    }

                                    Console.WriteLine($"The file signature {sig} matches the file {filePath} with description '{description}' and extension '{extension}'");
                                    matchFound = true;
                                    break;
                                }
                            }
                        }
                    }

                    // If no match is found, print a message and the file's hex signature
                    if (!matchFound)
                    {
                        Console.WriteLine("No matching file signature found.");
                        Console.WriteLine($"File signature: {fileHex}");
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access to the path is denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        static bool IsDocxFile(string filePath)
        {
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(filePath))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Handle exceptions if the file is not a valid ZIP archive
            }
            return false;
        }
    }
}
