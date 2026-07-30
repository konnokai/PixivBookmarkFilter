using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal sealed class ImageDownloadService : IDisposable
    {
        private readonly PixivApiClient pixivApiClient;
        private readonly HttpClient downloadClient;

        public ImageDownloadService(PixivApiClient pixivApiClient)
        {
            this.pixivApiClient = pixivApiClient;
            downloadClient = new HttpClient();
            downloadClient.DefaultRequestHeaders.Add("Referer", "https://pixiv.net");
        }

        public async Task<bool> DownloadAsync(string id, string savePath, bool isR18 = false)
        {
            IllustMetadata illust = await pixivApiClient.GetIllustAsync(id);
            if (illust == null) return false;

            if (illust.IllustType < 2)
            {
                if (illust.PageCount == 1)
                    return await DownloadSinglePageAsync(illust, savePath, isR18);

                return await DownloadMultiplePagesAsync(illust, id, savePath, isR18);
            }

            if (illust.IllustType == 2)
            {
                ConsoleOutput.Write("動圖不支援下載!!", ConsoleColor.Cyan);
                return true;
            }

            ConsoleOutput.Write("錯誤，該ID可能已被刪除", ConsoleColor.Red);
            return false;
        }

        public void Dispose()
        {
            downloadClient.Dispose();
        }

        private async Task<bool> DownloadSinglePageAsync(IllustMetadata illust, string savePath, bool isR18)
        {
            Directory.CreateDirectory(savePath);
            using HttpResponseMessage response = await downloadClient.GetAsync(illust.Urls.Original);
            response.EnsureSuccessStatusCode();
            byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
            string fileName = CreateFileName(Path.GetFileName(illust.Urls.Original), isR18);
            await File.WriteAllBytesAsync(Path.Combine(savePath, fileName), imageBytes);
            return true;
        }

        private async Task<bool> DownloadMultiplePagesAsync(
            IllustMetadata illust,
            string id,
            string savePath,
            bool isR18)
        {
            string pageSavePath = Path.Combine(savePath, id);
            Directory.CreateDirectory(pageSavePath);
            string imageFolder = illust.Urls.Original;
            imageFolder = imageFolder.Replace(imageFolder.Split(new[] { '/' }).Last(), "");
            string extension = Path.GetExtension(illust.Urls.Original);
            bool allDownloaded = true;

            Console.WriteLine($"下載圖檔: {illust.PageCount}");
            using var progressBar = new ProgressBar();
            for (int page = 0; page < illust.PageCount; page++)
            {
                progressBar.Report((page + 1) / (double)illust.PageCount);
                string fileName = $"{id}_p{page}{extension}";

                try
                {
                    using HttpResponseMessage response = await downloadClient.GetAsync(imageFolder + fileName);
                    response.EnsureSuccessStatusCode();
                    byte[] imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(
                        Path.Combine(pageSavePath, CreateFileName(fileName, isR18)),
                        imageBytes);
                }
                catch (Exception ex)
                {
                    allDownloaded = false;
                    Console.WriteLine($"{fileName} 下載失敗: {ex.Message}");
                }
            }

            return allDownloaded;
        }

        private static string CreateFileName(string fileName, bool isR18)
        {
            return isR18 ? $"{DateTime.Now:yyyyMMdd_HHmmss}_{fileName}" : fileName;
        }
    }
}
