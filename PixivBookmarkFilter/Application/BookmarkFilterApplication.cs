using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal sealed class BookmarkFilterApplication : IDisposable
    {
        private readonly PixivApiClient pixivApiClient;
        private readonly ImageDownloadService imageDownloadService;
        private readonly TagSuggestionWorkflow tagSuggestionWorkflow;

        public BookmarkFilterApplication()
        {
            pixivApiClient = new PixivApiClient();
            imageDownloadService = new ImageDownloadService(pixivApiClient);
            tagSuggestionWorkflow = new TagSuggestionWorkflow();
        }

        public async Task RunAsync()
        {
            Dictionary<string, string> tagConvertList = LocalSettings.LoadTagConvertList();
            List<string> ignoreDownloadList = LocalSettings.LoadIgnoreDownloadTagList();

            if (!await pixivApiClient.LoginAsync()) return;

            if (!string.IsNullOrWhiteSpace(pixivApiClient.UserId))
                await ProcessBookmarksAsync(tagConvertList, ignoreDownloadList);

            Console.WriteLine("請按任意鍵繼續...");
            Console.ReadKey();
        }

        public void Dispose()
        {
            tagSuggestionWorkflow.Dispose();
            imageDownloadService.Dispose();
            pixivApiClient.Dispose();
        }

        private async Task ProcessBookmarksAsync(
            Dictionary<string, string> tagConvertList,
            List<string> ignoreDownloadList)
        {
            ConsoleOutput.Write("取得使用者收藏標籤列表", ConsoleColor.DarkYellow);
            List<string> tagList = await pixivApiClient.GetUserTagListAsync();
            if (tagList == null) return;

            await tagSuggestionWorkflow.InitializeAsync(pixivApiClient, tagList);

            const int offset = 0;
            while (true)
            {
                BookmarksMetadata bookmarksMetadata = await pixivApiClient.GetBookmarksAsync(offset);
                if (bookmarksMetadata?.Works == null)
                {
                    ConsoleOutput.Write("取得收藏資料失敗，已停止處理", ConsoleColor.Red);
                    return;
                }

                List<Work> bookmarkWorks = GetUnclassifiedWorks(bookmarksMetadata);
                if (bookmarkWorks.Count == 0)
                {
                    ConsoleOutput.Write("已全部分類完成!", ConsoleColor.Green);
                    break;
                }

                foreach (Work work in bookmarkWorks)
                {
                    bool refreshUserTagList = false;
                    List<string> tagsToAdd = (work.Tags ?? new List<string>())
                        .Where((tag) => tagList.Contains(tag))
                        .ToList();

                    if (tagsToAdd.Count == 0)
                    {
                        ConsoleOutput.Write(
                            $"{work.Id} - {work.Title} 未包含已儲存的標籤!",
                            ConsoleColor.Red);
                        TagInputParseResult selection = await tagSuggestionWorkflow.SelectTagsAsync(work, tagList);
                        if (selection.CancelBookmark)
                        {
                            if (!await pixivApiClient.DeleteBookmarkAsync(work.BookmarkData.Id))
                            {
                                ConsoleOutput.Write("取消收藏失敗，已停止處理", ConsoleColor.Red);
                                return;
                            }

                            continue;
                        }

                        tagsToAdd.AddRange(selection.Tags);
                        refreshUserTagList = tagsToAdd.Any((tag) => !tagList.Contains(tag));
                    }

                    string downloadFolder = GetDownloadFolder(tagsToAdd, tagConvertList);
                    if (ignoreDownloadList.Any((tag) => tagsToAdd.Contains(tag)))
                    {
                        ConsoleOutput.Write("(已忽略下載) ", ConsoleColor.DarkYellow, false);
                    }
                    else
                    {
                        string savePath = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                            "Pixiv收藏分類儲存",
                            downloadFolder);
                        bool isR18 = tagsToAdd.Any((tag) => tag.ToLower().Contains("r-18"));
                        if (!await imageDownloadService.DownloadAsync(work.Id, savePath, isR18))
                        {
                            ConsoleOutput.Write("圖片下載不完整，未新增收藏標籤並停止處理", ConsoleColor.Red);
                            return;
                        }
                    }

                    if (!await pixivApiClient.AddBookmarkTagsAsync(work.BookmarkData.Id, tagsToAdd))
                    {
                        ConsoleOutput.Write("新增收藏標籤失敗，已停止處理", ConsoleColor.Red);
                        return;
                    }

                    ConsoleOutput.Write(
                        $"{work.Id} - {work.Title} ({string.Join(" ", tagsToAdd)}) {downloadFolder}",
                        ConsoleColor.Green);

                    try
                    {
                        await tagSuggestionWorkflow.UpdateIndexAsync(work, tagsToAdd);
                    }
                    catch (Exception ex)
                    {
                        ConsoleOutput.Write($"更新 RAG 索引失敗: {ex.Message}", ConsoleColor.DarkYellow);
                    }

                    if (refreshUserTagList)
                    {
                        ConsoleOutput.Write("刷新使用者標籤清單", ConsoleColor.DarkYellow);
                        tagList = await pixivApiClient.GetUserTagListAsync();
                        if (tagList == null) return;
                    }
                }
            }
        }

        private static List<Work> GetUnclassifiedWorks(BookmarksMetadata bookmarksMetadata)
        {
            if (!(bookmarksMetadata.BookmarkTags is JObject)) return bookmarksMetadata.Works;

            return bookmarksMetadata.Works
                .Where((work) => PixivApiClient.GetBookmarkTags(
                    bookmarksMetadata.BookmarkTags,
                    work.BookmarkData?.Id).Count == 0)
                .ToList();
        }

        private static string GetDownloadFolder(
            List<string> tags,
            Dictionary<string, string> tagConvertList)
        {
            foreach (string tag in tags)
            {
                KeyValuePair<string, string> exactMatch = tagConvertList.FirstOrDefault(
                    (item) => string.Equals(item.Key, tag, StringComparison.OrdinalIgnoreCase));
                if (exactMatch.Key != null) return exactMatch.Value;
            }

            KeyValuePair<string, string> partialMatch = tagConvertList.FirstOrDefault(
                (item) => tags.Any((tag) => tag.Contains(item.Key, StringComparison.OrdinalIgnoreCase)));
            return partialMatch.Key == null ? tags.First() : partialMatch.Value;
        }
    }
}
