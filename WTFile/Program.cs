using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace WTFile
{
    internal class Program
    {
        // Upper limit on how many bytes of a file to read, even if a signature
        // asks for more. The signature list's deepest entry starts around byte 29000.
        const int MaxBytesToRead = 64 * 1024;

        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    Console.Error.WriteLine("Please provide a file path as a command-line argument.");
                    return 1;
                }

                string filePath = args[0];

                if (!File.Exists(filePath))
                {
                    Console.Error.WriteLine($"File not found: {filePath}");
                    return 1;
                }

                // The signature list ships next to the executable.
                string signaturesPath = Path.Combine(AppContext.BaseDirectory, "file_sigs.json");
                if (!File.Exists(signaturesPath))
                {
                    Console.Error.WriteLine($"Signature file not found: {signaturesPath}");
                    return 1;
                }

                string jsonContent = File.ReadAllText(signaturesPath);

                // Read only as much of the file as the deepest signature needs.
                int bytesToRead = Math.Min(LargestOffsetPlusSignature(jsonContent), MaxBytesToRead);
                string fileHex = ReadFileStartAsHex(filePath, bytesToRead);

                List<(string Description, string Extension, string Signature)> matches =
                    FindMatchingSignatures(jsonContent, fileHex, filePath);

                if (matches.Count == 0)
                {
                    Console.WriteLine("No matching file signature found.");
                    Console.WriteLine($"File signature: {fileHex}");
                    return 1;
                }

                foreach (var match in matches)
                {
                    Console.WriteLine(
                        $"The file signature {match.Signature} matches the file {filePath} " +
                        $"with description '{match.Description}' and extension '{match.Extension}'");
                }
                return 0;
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.Error.WriteLine($"Access to the path is denied: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"An error occurred: {ex.Message}");
                return 1;
            }
        }

        // Reads the start of the file and returns it as a lowercase hex string
        // (two characters per byte). Files shorter than bytesToRead are fine.
        static string ReadFileStartAsHex(string filePath, int bytesToRead)
        {
            byte[] buffer = new byte[bytesToRead];
            using FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            StringBuilder hex = new StringBuilder(bytesRead * 2);
            for (int i = 0; i < bytesRead; i++)
            {
                hex.AppendFormat("{0:x2}", buffer[i]);
            }
            return hex.ToString();
        }

        // Returns how many bytes from the start of a file the deepest valid
        // signature could possibly need.
        static int LargestOffsetPlusSignature(string jsonContent)
        {
            int largest = 64;
            using JsonDocument document = JsonDocument.Parse(jsonContent);
            foreach (JsonElement fileSig in document.RootElement.GetProperty("filesigs").EnumerateArray())
            {
                if (!int.TryParse(fileSig.GetProperty("Header offset").GetString(), out int offset))
                {
                    continue;
                }
                string signature = fileSig.GetProperty("Header (hex)").GetString()?.Replace(" ", "") ?? "";
                largest = Math.Max(largest, offset + signature.Length / 2);
            }
            return largest;
        }

        // Returns every signature in the JSON that matches the file's hex string.
        // For ZIP-based files, a deeper look inside the archive refines the answer.
        static List<(string Description, string Extension, string Signature)> FindMatchingSignatures(
            string jsonContent, string fileHex, string filePath)
        {
            List<(string Description, string Extension, string Signature)> matches = new();

            using JsonDocument document = JsonDocument.Parse(jsonContent);
            foreach (JsonElement fileSig in document.RootElement.GetProperty("filesigs").EnumerateArray())
            {
                if (!fileSig.TryGetProperty("Header (hex)", out JsonElement headerHexElement) ||
                    !fileSig.TryGetProperty("Header offset", out JsonElement headerOffsetElement) ||
                    !fileSig.TryGetProperty("File description", out JsonElement descriptionElement) ||
                    !fileSig.TryGetProperty("File extension", out JsonElement extensionElement))
                {
                    continue;
                }

                string signature = headerHexElement.GetString()?.Replace(" ", "").ToLower() ?? string.Empty;
                string description = descriptionElement.GetString() ?? "Unknown";
                string extension = extensionElement.GetString() ?? "Unknown";

                // Signatures with a non-numeric offset are skipped rather than
                // silently matched at offset 0.
                if (!int.TryParse(headerOffsetElement.GetString(), out int offset))
                {
                    continue;
                }

                int start = offset * 2;
                if (string.IsNullOrEmpty(signature) || start + signature.Length > fileHex.Length)
                {
                    continue;
                }

                if (fileHex.Substring(start, signature.Length) == signature)
                {
                    matches.Add((description, extension, signature));
                }
            }

            // Several formats share the ZIP signature (DOCX, XLSX, PPTX, APK, JAR...).
            // Opening the archive tells us which one this really is, so we can
            // report a single clear answer instead of every ZIP-based format.
            if (matches.Any(m => m.Extension.Contains("ZIP")) && IsZipFile(filePath))
            {
                matches.Clear();
                matches.Add(DescribeZipContent(filePath));
            }

            return matches;
        }

        // Returns true if the file opens as a valid ZIP archive.
        static bool IsZipFile(string filePath)
        {
            try
            {
                using ZipArchive archive = ZipFile.OpenRead(filePath);
                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        // Looks inside a ZIP-based file and describes its actual content.
        static (string Description, string Extension, string Signature) DescribeZipContent(string filePath)
        {
            using ZipArchive archive = ZipFile.OpenRead(filePath);

            bool hasWordFolder = false;
            bool hasExcelFolder = false;
            bool hasPowerPointFolder = false;
            bool hasAndroidManifest = false;

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.FullName.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                {
                    hasWordFolder = true;
                }
                else if (entry.FullName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                {
                    hasExcelFolder = true;
                }
                else if (entry.FullName.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                {
                    hasPowerPointFolder = true;
                }
                else if (entry.FullName.Equals("AndroidManifest.xml", StringComparison.OrdinalIgnoreCase))
                {
                    hasAndroidManifest = true;
                }
            }

            if (hasWordFolder)
            {
                return ("Microsoft Office Open XML Format document (Word)", "DOCX", "504b0304 + word/");
            }
            if (hasExcelFolder)
            {
                return ("Microsoft Office Open XML Format spreadsheet", "XLSX", "504b0304 + xl/");
            }
            if (hasPowerPointFolder)
            {
                return ("Microsoft Office Open XML Format presentation", "PPTX", "504b0304 + ppt/");
            }
            if (hasAndroidManifest)
            {
                return ("Android package file", "APK", "504b0304 + AndroidManifest.xml");
            }

            return ("ZIP compressed archive file", "ZIP", "504b0304");
        }
    }
}
