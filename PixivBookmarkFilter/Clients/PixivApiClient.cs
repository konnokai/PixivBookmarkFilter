using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal sealed class PixivApiClient : IDisposable
    {
        private const string UserDataFileName = "UserData.json";
        private const string AjaxBaseUrl = "https://www.pixiv.net/ajax/";

        private readonly HttpClient getClient;
        private readonly HttpClient postClient;
        private UserData userData;
        private bool savedUserDataLoaded;

        public string UserId => userData?.PHPSESSID?.Split(new[] { '_' })[0] ?? "";

        public PixivApiClient()
        {
            getClient = new HttpClient();
            getClient.DefaultRequestHeaders.Add("Referer", "https://pixiv.net");
            getClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36");

            postClient = new HttpClient();
            postClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36");
        }

        public async Task<bool> LoginAsync()
        {
            LoadSavedUserData();
            if (userData != null)
            {
                ConsoleOutput.Write("已載入儲存的使用者資料", ConsoleColor.DarkYellow);
                return true;
            }

            while (userData == null)
            {
                try
                {
                    userData = new UserData();

                    ConsoleOutput.Write("請輸入PHPSESSID: ", ConsoleColor.DarkYellow, false);
                    userData.PHPSESSID = Console.ReadLine();

                    ConsoleOutput.Write("請輸入X_CSRF_TOKEN: ", ConsoleColor.DarkYellow, false);
                    userData.X_CSRF_TOKEN = Console.ReadLine();

                    if (await GetAsync<ExtraMetaData>("user/extra", false, false) != null)
                    {
                        ConsoleOutput.Write("驗證成功", ConsoleColor.Green);
                        File.WriteAllText(UserDataFileName, JsonConvert.SerializeObject(userData));
                        return true;
                    }

                    ConsoleOutput.Write("驗證失敗", ConsoleColor.Red);
                    userData = null;
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Write(ex.Message, ConsoleColor.Red);
                    userData = null;
                }
            }

            return false;
        }

        public Task<BookmarksMetadata> GetBookmarksAsync(int offset, int limit = 50)
        {
            return GetAsync<BookmarksMetadata>(
                $"user/{UserId}/illusts/bookmarks?tag=&offset={offset}&limit={limit}&rest=show");
        }

        public Task<IllustMetadata> GetIllustAsync(string id)
        {
            return GetAsync<IllustMetadata>($"illust/{id}");
        }

        public async Task<List<string>> GetUserTagListAsync()
        {
            BookmarkTagsMetadata result = await GetAsync<BookmarkTagsMetadata>(
                $"user/{UserId}/illusts/bookmark/tags?lang=zh_tw");
            if (result?.Public == null)
            {
                ConsoleOutput.Write("取得使用者標籤清單失敗", ConsoleColor.Red);
                return null;
            }

            return result.Public.Select((item) => item.Tag).ToList();
        }

        public async Task<BookmarkHistorySyncResult> GetTaggedBookmarkHistoryAsync(
            string syncAnchorWorkId,
            IEnumerable<string> knownWorkIds)
        {
            const int pageSize = 50;
            int offset = 0;
            HashSet<string> seenWorkIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> knownIds = new HashSet<string>(
                knownWorkIds ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            List<BookmarkTagDocumentInput> history = new List<BookmarkTagDocumentInput>();
            string latestWorkId = null;
            bool reachedSyncBoundary = false;
            int fetchedWorkCount = 0;

            while (true)
            {
                BookmarksMetadata metadata = await GetBookmarksAsync(offset, pageSize);
                if (metadata?.Works == null) return null;
                if (metadata.Works.Count == 0) break;
                fetchedWorkCount += metadata.Works.Count;

                int newWorkCount = 0;
                foreach (Work work in metadata.Works)
                {
                    if (work == null || string.IsNullOrWhiteSpace(work.Id) || !seenWorkIds.Add(work.Id)) continue;
                    newWorkCount++;
                    latestWorkId ??= work.Id;

                    if (string.Equals(work.Id, syncAnchorWorkId, StringComparison.Ordinal)
                        || knownIds.Contains(work.Id))
                    {
                        reachedSyncBoundary = true;
                        break;
                    }

                    List<string> assignedTags = GetBookmarkTags(metadata.BookmarkTags, work.BookmarkData?.Id);
                    if (assignedTags.Count == 0) continue;

                    history.Add(new BookmarkTagDocumentInput
                    {
                        WorkId = work.Id,
                        Title = work.Title,
                        SourceTags = work.Tags ?? new List<string>(),
                        AssignedTags = assignedTags
                    });
                }

                if (reachedSyncBoundary) break;
                if (newWorkCount == 0) return null;
                offset += metadata.Works.Count;
                if (metadata.Works.Count < pageSize) break;
            }

            return new BookmarkHistorySyncResult
            {
                Documents = history,
                LatestWorkId = latestWorkId,
                IsCompleteSnapshot = !reachedSyncBoundary,
                FetchedWorkCount = fetchedWorkCount
            };
        }

        public Task<bool> DeleteBookmarkAsync(string bookmarkId)
        {
            return PostAsync("illusts/bookmarks/delete", $"bookmark_id={bookmarkId}");
        }

        public Task<bool> AddBookmarkTagsAsync(string bookmarkId, List<string> tags)
        {
            return PostAsync(
                "illusts/bookmarks/add_tags",
                JsonConvert.SerializeObject(new { tags, bookmarkIds = new List<string> { bookmarkId } }));
        }

        public static List<string> GetBookmarkTags(JToken bookmarkTags, string bookmarkId)
        {
            if (!(bookmarkTags is JObject bookmarkTagsObject) || string.IsNullOrWhiteSpace(bookmarkId))
                return new List<string>();

            JToken token = bookmarkTagsObject[bookmarkId];
            if (token == null || token.Type == JTokenType.Null) return new List<string>();

            return token.Type == JTokenType.Array
                ? token.Values<string>()
                    .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
                : new List<string>();
        }

        public void Dispose()
        {
            getClient.Dispose();
            postClient.Dispose();
        }

        private void LoadSavedUserData()
        {
            if (savedUserDataLoaded) return;
            savedUserDataLoaded = true;

            try
            {
                if (File.Exists(UserDataFileName))
                    userData = JsonConvert.DeserializeObject<UserData>(File.ReadAllText(UserDataFileName));
            }
            catch
            {
                ConsoleOutput.Write("使用者資料錯誤，請重新登入", ConsoleColor.Red);
                if (File.Exists(UserDataFileName)) File.Delete(UserDataFileName);
            }
        }

        private async Task<bool> PostAsync(string url, string data)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, AjaxBaseUrl + url);
                request.Headers.TryAddWithoutValidation("Origin", "https://www.pixiv.net");
                request.Headers.TryAddWithoutValidation("X-Csrf-Token", userData.X_CSRF_TOKEN);
                request.Headers.TryAddWithoutValidation("Cookie", $"PHPSESSID={userData.PHPSESSID}");
                request.Content = new StringContent(
                    data,
                    Encoding.UTF8,
                    data.StartsWith("{") ? "application/json" : "application/x-www-form-urlencoded");

                using HttpResponseMessage response = await postClient.SendAsync(request);
                string result = await response.Content.ReadAsStringAsync();
                response.EnsureSuccessStatusCode();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    JObject resultBody = JObject.Parse(result);
                    if (resultBody.Value<bool?>("error") == true)
                    {
                        ConsoleOutput.Write(
                            resultBody.Value<string>("message") ?? "Pixiv API 回傳錯誤",
                            ConsoleColor.Red);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ConsoleOutput.Write($"{ex}", ConsoleColor.Red);
                return false;
            }
        }

        private async Task<T> GetAsync<T>(string url, bool showError = true, bool allowRelogin = true)
        {
            int maxAttempts = allowRelogin ? 2 : 1;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, AjaxBaseUrl + url);
                    request.Headers.TryAddWithoutValidation("Cookie", $"PHPSESSID={userData.PHPSESSID}");
                    using HttpResponseMessage response = await getClient.SendAsync(request);

                    if ((response.StatusCode == HttpStatusCode.Unauthorized
                            || response.StatusCode == HttpStatusCode.Forbidden)
                        && allowRelogin
                        && attempt == 0)
                    {
                        ConsoleOutput.Write("需要重新登入", ConsoleColor.DarkRed);
                        userData = null;
                        if (!await LoginAsync()) return default;
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    string result = await response.Content.ReadAsStringAsync();
                    ResultBody<T> resultBody = JsonConvert.DeserializeObject<ResultBody<T>>(result);
                    if (resultBody == null) throw new JsonException("Pixiv API 回傳格式錯誤");

                    if (resultBody.Error)
                    {
                        if (allowRelogin && attempt == 0)
                        {
                            ConsoleOutput.Write("需要重新登入", ConsoleColor.DarkRed);
                            userData = null;
                            if (!await LoginAsync()) return default;
                            continue;
                        }

                        if (showError)
                            ConsoleOutput.Write(resultBody.Message ?? "Pixiv API 回傳錯誤", ConsoleColor.Red);
                        return default;
                    }

                    return resultBody.Body;
                }
                catch (Exception ex)
                {
                    if (showError) ConsoleOutput.Write(ex.Message, ConsoleColor.Red);
                    return default;
                }
            }

            return default;
        }
    }
}
