using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using GameMainConfigEncryption;
using MemoryPack;

namespace MediaCatalogDownloader
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: BlueArchiveAssetsDownloader [apkPath] [-o outputDirectory] [-t Audio|Video|Texture]");
                Console.WriteLine("可以前往 https://d.apkpure.com/b/XAPK/com.YostarJP.BlueArchive?version=latest 下载最新版APK查找");

                Console.WriteLine("按下任意键退出...");
                Console.ReadKey();
                return;
            }

            string apkFilePath = args[0];

            if (!File.Exists(apkFilePath))
            {
                Console.WriteLine("APK file not found: " + apkFilePath);
                return;
            }

            string serverInfoDataUrl = "";
            string version = "";
            try
            {
                byte[] apkData = File.ReadAllBytes(apkFilePath);
                string targetString = "GameMainConfig";
                byte[] targetBytes = Encoding.UTF8.GetBytes(targetString);

                // Search for the target string in the APK data
                int offset = FindBytes(apkData, targetBytes);

                if (offset == -1)
                {
                    Console.WriteLine("Target string 'GameMainConfig' not found in APK.");
                    return;
                }

                // Align to the next 4-byte boundary for size
                int sizeOffset = offset + targetBytes.Length;
                sizeOffset = (sizeOffset + 3) & ~3; // Align to 4-byte boundary

                if (sizeOffset + 4 > apkData.Length)
                {
                    Console.WriteLine("Invalid size offset.");
                    return;
                }

                uint size = BitConverter.ToUInt32(apkData, sizeOffset);

                // Read the data block
                int dataOffset = sizeOffset + 4;
                if (dataOffset + size > apkData.Length)
                {
                    Console.WriteLine("Invalid data size or offset.");
                    return;
                }

                byte[] dataBlock = new byte[size];
                Array.Copy(apkData, dataOffset, dataBlock, 0, size);

                // Base64 encode the data block
                string base64Data = Convert.ToBase64String(dataBlock);

                // Decrypt using GameMainConfig key
                byte[] configKey = EncryptionUtils.CreateKey("GameMainConfig");
                string decryptedJson = EncryptionUtils.ConvertString(base64Data, configKey);

                var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(decryptedJson);

                // Process ServerInfoDataUrl key
                byte[] siduKey = EncryptionUtils.CreateKey("ServerInfoDataUrl");
                string cryptedKey = EncryptionUtils.NewEncryptString("ServerInfoDataUrl", siduKey);

                if (!dict.TryGetValue(cryptedKey, out var cryptedValueObj))
                {
                    Console.WriteLine("Cannot find crypted key in JSON data!");
                    return;
                }

                // Decrypt the crypted value
                string cryptedValue = cryptedValueObj.ToString() ?? "";
                byte[] siduKey2 = EncryptionUtils.CreateKey("ServerInfoDataUrl");
                serverInfoDataUrl = EncryptionUtils.ConvertString(cryptedValue, siduKey2);

                // Fetch and parse JSON data from serverInfoDataUrl
                using HttpClient client = new HttpClient();
                string jsonData = await client.GetStringAsync(serverInfoDataUrl);
                var serverInfo = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonData);

                if (serverInfo.ContainsKey("ConnectionGroups"))
                {
                    var connectionGroups = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(serverInfo["ConnectionGroups"].ToString()!);
                    if (connectionGroups != null && connectionGroups.Count > 0)
                    {
                        var overrideGroups = connectionGroups[0]["OverrideConnectionGroups"] as JsonElement?;
                        if (overrideGroups.HasValue)
                        {
                            var overrideList = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(overrideGroups.Value.GetRawText());
                            if (overrideList != null && overrideList.Count > 0)
                            {
                                version = overrideList[^1]["AddressablesCatalogUrlRoot"].Split('/').Last();
                            }
                        }
                    }
                }

                // Print the final result
                Console.WriteLine("Version => " + version);
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: " + ex.Message);
            }

            string baseUrl = $"https://prod-clientpatch.bluearchiveyostar.com/{version}/MediaResources/";
            string catalogUrl = $"{baseUrl}MediaCatalog.bytes";
            string downloadDirectory = "BADownloadAsset"; // Default download directory
            MediaType fileTypeToDownload = MediaType.None; // Default to None
            byte[] catalogData = null;

            try
            {
                if (args.Length > 1)
                {
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] == "-o")
                        {
                            downloadDirectory = GetOutputDirectory(args, ref i);
                            if (string.IsNullOrEmpty(downloadDirectory))
                            {
                                return;
                            }
                        }
                        else if (args[i] == "-t")
                        {
                            fileTypeToDownload = GetFileType(args, ref i);
                        }
                    }
                }

                if (catalogData == null)
                {
                    Console.WriteLine($"Downloading catalog from: {catalogUrl}");
                    catalogData = await DownloadBytesAsync(catalogUrl);
                }

                await ProcessCatalogData(catalogData, baseUrl, downloadDirectory, fileTypeToDownload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        static int FindBytes(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        static string GetOutputDirectory(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                Console.WriteLine("Error: Please provide the output directory when using -o option.");
                return null;
            }

            string outputDirectory = args[++index];
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            return outputDirectory;
        }

        static MediaType GetFileType(string[] args, ref int index)
        {
            if (index + 1 >= args.Length)
            {
                Console.WriteLine("Error: Please provide the file type when using -t option.");
                return MediaType.None;
            }

            string fileType = args[++index].ToLower();
            return fileType switch
            {
                "audio" => MediaType.Audio,
                "video" => MediaType.Video,
                "texture" => MediaType.Texture,
                _ => MediaType.None,
            };
        }

        static async Task<byte[]> DownloadBytesAsync(string url)
        {
            using (HttpClient client = new HttpClient())
            {
                return await client.GetByteArrayAsync(url);
            }
        }

        static async Task ProcessCatalogData(byte[] catalogData, string baseUrl, string downloadDirectory, MediaType fileTypeToDownload)
        {
            var catalog = MemoryPackSerializer.Deserialize<MediaCatalog>(catalogData);

            if (catalog != null)
            {
                var downloadTasks = new List<Task>();
                var fileQueue = new ConcurrentQueue<KeyValuePair<string, string>>(catalog.Table
                    .Where(pair => fileTypeToDownload == MediaType.None || pair.Value.MediaType == fileTypeToDownload)
                    .Select(pair => new KeyValuePair<string, string>(pair.Key, pair.Value.Path)));

                for (int i = 0; i < 8; i++)
                {
                    downloadTasks.Add(Task.Run(() => DownloadFiles(fileQueue, baseUrl, downloadDirectory)));
                }

                await Task.WhenAll(downloadTasks);
            }
            else
            {
                Console.WriteLine("Failed to deserialize the MediaCatalog.");
            }
        }

        static async Task DownloadFiles(ConcurrentQueue<KeyValuePair<string, string>> fileQueue, string baseUrl, string downloadDirectory)
        {
            while (fileQueue.TryDequeue(out var file))
            {
                string resourceUrl = baseUrl + file.Value;
                string localFilePath = Path.Combine(downloadDirectory, file.Value);

                if (File.Exists(localFilePath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"Skipping already downloaded file: {localFilePath}");
                    Console.ResetColor();
                    continue;
                }

                bool success = await DownloadFileWithRetry(resourceUrl, localFilePath);

                if (!success)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Failed to download file: {file.Value}");
                    Console.ResetColor();
                }
            }
        }

        static async Task<bool> DownloadFileWithRetry(string url, string localFilePath)
        {
            const int maxRetries = 2;
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"Downloading {localFilePath} from {url} (Attempt {attempt})");
                    byte[] resourceData = await DownloadBytesAsync(url);
                    SaveFile(localFilePath, resourceData);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Error downloading {localFilePath} from {url}: {ex.Message}");
                    Console.ResetColor();
                    if (attempt < maxRetries)
                    {
                        Console.WriteLine($"Retrying in 1 second...");
                        await Task.Delay(1000);
                    }
                }
            }

            return false;
        }

        static void SaveFile(string path, byte[] data)
        {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, data);
        }
    }
}
