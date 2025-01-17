using System.CommandLine;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text;
using GameMainConfigEncryption;
using MemoryPack;

namespace MediaCatalogDownloader
{
    class Program
    {

        static async Task<int> Main(string[] args)
        {
            // 必须的位置参数
            var apkPathArgument = new Argument<string>("apkPath", "APK文件的路径");

            // 可选参数
            var outputDirectoryOption = new Option<string>(
                new[] { "--outputDirectory", "-o" },
                () => "BADownloadAsset",
                "保存下载文件的目录");
            var fileTypeOption = new Option<string>(
                new[] { "--fileType", "-t" },
                () => "None",
                "要下载的文件类型 (Audio, Video, Texture 或 None)");

            // 定义命令
            var rootCommand = new RootCommand("MediaCatalogDownloader")
            {
                apkPathArgument,
                outputDirectoryOption,
                fileTypeOption
            };

            // 设置命令处理逻辑
            rootCommand.SetHandler(
                HandleCommand,
                apkPathArgument,
                outputDirectoryOption,
                fileTypeOption);

            return await rootCommand.InvokeAsync(args);
        }

        // 命令逻辑处理
        static async Task HandleCommand(string apkPath, string outputDirectory, string fileType)
        {
            if (!File.Exists(apkPath))
            {
                Console.WriteLine($"错误：APK 文件未找到：{apkPath}");
                return;
            }

            string serverInfoDataUrl = "";
            string version = "";

            try
            {
                byte[] apkData = File.ReadAllBytes(apkPath);
                string targetString = "GameMainConfig";
                byte[] targetBytes = Encoding.UTF8.GetBytes(targetString);

                int offset = FindBytes(apkData, targetBytes);
                if (offset == -1)
                {
                    Console.WriteLine("未在 APK 中找到目标字符串 'GameMainConfig'。");
                    return;
                }

                int sizeOffset = (offset + targetBytes.Length + 3) & ~3;
                if (sizeOffset + 4 > apkData.Length)
                {
                    Console.WriteLine("无效的大小偏移量。");
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
                serverInfoDataUrl = EncryptionUtils.ConvertString(cryptedValue, siduKey);
                Console.WriteLine($"ServerInfoDataUrl: {serverInfoDataUrl}");

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
                Console.WriteLine("发生错误：" + ex.Message);
            }

            string baseUrl = $"https://prod-clientpatch.bluearchiveyostar.com/{version}/MediaResources/";
            string catalogUrl = $"{baseUrl}MediaCatalog.bytes";
            byte[] catalogData = null;
            MediaType fileTypeToDownload = Enum.TryParse(fileType, true, out MediaType parsedFileType) ? parsedFileType : MediaType.None;

            try
            {
                Console.WriteLine($"从以下地址下载目录：{catalogUrl}");
                catalogData = await DownloadBytesAsync(catalogUrl);
                await ProcessCatalogData(catalogData, baseUrl, outputDirectory, fileTypeToDownload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
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
