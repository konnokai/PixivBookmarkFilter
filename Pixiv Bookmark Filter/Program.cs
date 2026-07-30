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
    class Program
    {
        private class TagSuggestion
        {
            public string Tag { get; set; }
            public string SourceTag { get; set; }
            public double Score { get; set; }
            public bool IsExactMatch { get; set; }
        }

        static string userId = "";
        static UserData userData = null;

        static async Task Main(string[] args)
        {
            Dictionary<string, string> tagConvertList = GetTagConvertList();
            List<string> ignoreDownloadList = GetIgnoreDownloadTagList();
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.Unicode;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13;

            try
            {
                if (File.Exists("UserData.json"))
                    userData = JsonConvert.DeserializeObject<UserData>(File.ReadAllText("UserData.json"));
            }
            catch
            {
                FormatColorWrite("使用者資料錯誤，請重新登入", ConsoleColor.Red);
                if (File.Exists("UserData.json")) File.Delete("UserData.json");
            }

            if (!await LoginPixivAsync()) return;

            if (!string.IsNullOrWhiteSpace(userData.PHPSESSID))
            {
                userId = userData.PHPSESSID.Split(new char[] { '_' })[0];
                FormatColorWrite("取得使用者收藏標籤列表", ConsoleColor.DarkYellow);
                List<string> tagList = await GetUserTagListAsync();
                if (tagList == null) return;

                int offset = 0;
                while (true)
                {
                    var bookmarksMetadata = await GetServerDataAsync<BookmarksMetadata>($"user/{userId}/illusts/bookmarks?tag=&offset={offset}&limit=50&rest=show");
                    if (bookmarksMetadata?.Works == null)
                    {
                        FormatColorWrite("取得收藏資料失敗，已停止處理", ConsoleColor.Red);
                        return;
                    }

                    List<Work> bookmarkWorks;
                    if (bookmarksMetadata.BookmarkTags is JObject bookmarkTags)
                    {
                        bookmarkWorks = bookmarksMetadata.Works
                            .Where((x) => bookmarkTags[x.BookmarkData.Id] == null || bookmarkTags[x.BookmarkData.Id].Count() == 0)
                            .ToList();
                    }
                    else
                    {
                        bookmarkWorks = bookmarksMetadata.Works;
                    }

                    if (bookmarkWorks.Count == 0)
                    {
                        FormatColorWrite("已全部分類完成!", ConsoleColor.Green);
                        break;
                    }

                    foreach (var item in bookmarkWorks)
                    {
                        bool isNeedRefreshUserTagList = false;
                        List<string> needAddTag = item.Tags.Where((x) => tagList.Contains(x)).ToList();

                        if (needAddTag.Count == 0)
                        {
                            //tagConvertList = GetTagConvertList();
                            List<TagSuggestion> suggestions = GetTagSuggestions(item.Tags, tagList);

                            FormatColorWrite($"{item.Id} - {item.Title} 未包含已儲存的標籤!", ConsoleColor.Red);
                            if (suggestions.Count > 0)
                            {
                                FormatColorWrite("建議標籤:", ConsoleColor.Cyan);
                                for (int i = 0; i < suggestions.Count; i++)
                                {
                                    TagSuggestion suggestion = suggestions[i];
                                    FormatColorWrite($"  {i + 1}. {suggestion.Tag} (依據: {suggestion.SourceTag} | {suggestion.Score * 100:F2}%)", ConsoleColor.DarkCyan);
                                }
                            }

                            string suggestionHint = suggestions.Count > 0 ? "建議編號（如 #1）、" : "";
                            FormatColorWrite($"[{string.Join(" ", item.Tags)}](輸入{suggestionHint}標籤，\"-\" 取消收藏): ", newLine: false);
                            string addTag = "";
                            List<string> inputTags = null;

                            do
                            {
                                addTag = Console.ReadLine().Trim();
                                if (addTag == "-") break;
                                if (addTag != "" && TryParseSuggestedTags(addTag, suggestions, out inputTags)) break;
                            } while (true);

                            if (addTag == "-")
                            {
                                if (!await PostServerDataAsync("illusts/bookmarks/delete", $"bookmark_id={item.BookmarkData.Id}"))
                                {
                                    FormatColorWrite("取消收藏失敗，已停止處理", ConsoleColor.Red);
                                    return;
                                }
                                continue;
                            }
                            else
                            {
                                needAddTag.AddRange(inputTags);
                                isNeedRefreshUserTagList = needAddTag.Any((x) => !tagList.Contains(x));
                            }
                        }

                        string tag = GetDownloadFolder(needAddTag, tagConvertList);

                        if (ignoreDownloadList.Any((x) => needAddTag.Contains(x)))
                        {
                            FormatColorWrite("(已忽略下載) ", ConsoleColor.DarkYellow, false);
                        }
                        else
                        {
                            string savePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}\\Pixiv收藏分類儲存\\{tag}\\";
                            if (!await DownloadPictureAsync(item.Id, savePath, needAddTag.Any((x) => x.ToLower().Contains("r-18"))))
                            {
                                FormatColorWrite("圖片下載不完整，未新增收藏標籤並停止處理", ConsoleColor.Red);
                                return;
                            }
                        }

                        if (!await PostServerDataAsync("illusts/bookmarks/add_tags", JsonConvert.SerializeObject(new { tags = needAddTag, bookmarkIds = new List<string>() { item.BookmarkData.Id } })))
                        {
                            FormatColorWrite("新增收藏標籤失敗，已停止處理", ConsoleColor.Red);
                            return;
                        }

                        FormatColorWrite($"{item.Id} - {item.Title} ({string.Join(" ", needAddTag)}) {tag}", ConsoleColor.Green);

                        if (isNeedRefreshUserTagList)
                        {
                            FormatColorWrite("刷新使用者標籤清單", ConsoleColor.DarkYellow);
                            tagList = await GetUserTagListAsync();
                            if (tagList == null) return;
                        }
                    }
                }
            }

            Console.WriteLine("請按任意鍵繼續...");
            Console.ReadKey();
        }

        static HttpClient downloadClient;
        private static async Task<bool> DownloadPictureAsync(string id, string savePath, bool isR18 = false)
        {
            if (downloadClient == null)
            {
                downloadClient = new();
                downloadClient.DefaultRequestHeaders.Add("Referer", "https://pixiv.net");
            }

            IllustMetadata illust = await GetServerDataAsync<IllustMetadata>($"illust/{id}");
            if (illust == null) return false;

            if (illust.IllustType < 2)
            {
                if (illust.PageCount == 1)
                {
                    if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                    using var response = await downloadClient.GetAsync(illust.Urls.Original);
                    response.EnsureSuccessStatusCode();
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(Path.Combine(savePath, (isR18 ? $"{DateTime.Now:yyyyMMdd_HHmmss}_" : "") + Path.GetFileName(illust.Urls.Original)), imageBytes);
                    return true;
                }
                else
                {
                    string pageSavePath = Path.Combine(savePath, id);
                    if (!Directory.Exists(pageSavePath)) Directory.CreateDirectory(pageSavePath);
                    string imageFolder = illust.Urls.Original;
                    imageFolder = imageFolder.Replace(imageFolder.Split(new char[] { '/' }).Last(), "");
                    string extension = Path.GetExtension(illust.Urls.Original);
                    bool allDownloaded = true;

                    Console.WriteLine($"下載圖檔: {illust.PageCount}");
                    using (var progressBar = new ProgressBar())
                    {
                        for (int i = 0; i < illust.PageCount; i++)
                        {
                            progressBar.Report((i + 1) / (double)illust.PageCount);

                            try
                            {
                                using var response = await downloadClient.GetAsync(imageFolder + id + "_p" + i.ToString() + extension);
                                response.EnsureSuccessStatusCode();
                                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(Path.Combine(pageSavePath, (isR18 ? $"{DateTime.Now:yyyyMMdd_HHmmss}_" : "") + id + "_p" + i.ToString() + extension), imageBytes);
                            }
                            catch (Exception ex)
                            {
                                allDownloaded = false;
                                Console.WriteLine($"{id}_p{i}{extension} 下載失敗: {ex.Message}");
                            }
                        }
                    }

                    return allDownloaded;
                }
            }
            else if (illust.IllustType == 2)
            {
                FormatColorWrite("動圖不支援下載!!", ConsoleColor.Cyan);
                return true;
            }

            FormatColorWrite("錯誤，該ID可能已被刪除", ConsoleColor.Red);
            return false;
        }

        private static async Task<bool> LoginPixivAsync()
        {
            if (userData != null)
            {
                FormatColorWrite("已載入儲存的使用者資料", ConsoleColor.DarkYellow);
                return true;
            }

            while (userData == null)
            {
                try
                {
                    userData = new UserData();

                    FormatColorWrite("請輸入PHPSESSID: ", ConsoleColor.DarkYellow, false);
                    userData.PHPSESSID = Console.ReadLine();

                    FormatColorWrite("請輸入X_CSRF_TOKEN: ", ConsoleColor.DarkYellow, false);
                    userData.X_CSRF_TOKEN = Console.ReadLine();

                    if (await GetServerDataAsync<ExtraMetaData>($"user/extra", false, false) != null)
                    {
                        FormatColorWrite("驗證成功", ConsoleColor.Green);
                        File.WriteAllText("UserData.json", JsonConvert.SerializeObject(userData));
                        return true;
                    }

                    FormatColorWrite("驗證失敗", ConsoleColor.Red);
                    userData = null;
                }
                catch (Exception ex)
                {
                    FormatColorWrite(ex.Message, ConsoleColor.Red);
                    userData = null;
                }
            }

            return false;
        }

        private static HttpClient postClient;
        private static async Task<bool> PostServerDataAsync(string url, string data)
        {
            try
            {
                if (postClient == null)
                {
                    postClient = new();
                    postClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36");
                }

                using var request = new HttpRequestMessage(HttpMethod.Post, $"https://www.pixiv.net/ajax/{url}");
                request.Headers.TryAddWithoutValidation("Origin", "https://www.pixiv.net");
                request.Headers.TryAddWithoutValidation("X-Csrf-Token", userData.X_CSRF_TOKEN);
                request.Headers.TryAddWithoutValidation("Cookie", $"PHPSESSID={userData.PHPSESSID}");
                request.Content = new StringContent(data, Encoding.UTF8, data.StartsWith("{") ? "application/json" : "application/x-www-form-urlencoded");

                using var response = await postClient.SendAsync(request);
                string result = await response.Content.ReadAsStringAsync();
                response.EnsureSuccessStatusCode();

                if (!string.IsNullOrWhiteSpace(result))
                {
                    var resultBody = JObject.Parse(result);
                    if (resultBody.Value<bool?>("error") == true)
                    {
                        FormatColorWrite(resultBody.Value<string>("message") ?? "Pixiv API 回傳錯誤", ConsoleColor.Red);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                FormatColorWrite($"{ex}", ConsoleColor.Red);
                return false;
            }
        }

        private static HttpClient getClient;
        private static async Task<T> GetServerDataAsync<T>(string url, bool showError = true, bool allowRelogin = true)
        {
            int maxAttempts = allowRelogin ? 2 : 1;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (getClient == null)
                    {
                        getClient = new();
                        getClient.DefaultRequestHeaders.Add("Referer", "https://pixiv.net");
                        getClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36");
                    }

                    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.pixiv.net/ajax/{url}");
                    request.Headers.TryAddWithoutValidation("Cookie", $"PHPSESSID={userData.PHPSESSID}");
                    using var response = await getClient.SendAsync(request);

                    if ((response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                        && allowRelogin && attempt == 0)
                    {
                        FormatColorWrite("需要重新登入", ConsoleColor.DarkRed);
                        userData = null;
                        if (!await LoginPixivAsync()) return default;
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadAsStringAsync();

                    var resultBody = JsonConvert.DeserializeObject<ResultBody<T>>(result);
                    if (resultBody == null) throw new JsonException("Pixiv API 回傳格式錯誤");

                    if (resultBody.Error)
                    {
                        if (allowRelogin && attempt == 0)
                        {
                            FormatColorWrite("需要重新登入", ConsoleColor.DarkRed);
                            userData = null;
                            if (!await LoginPixivAsync()) return default;
                            continue;
                        }

                        if (showError) FormatColorWrite(resultBody.Message ?? "Pixiv API 回傳錯誤", ConsoleColor.Red);
                        return default;
                    }

                    return resultBody.Body;
                }
                catch (Exception ex)
                {
                    if (showError) FormatColorWrite(ex.Message, ConsoleColor.Red);
                    return default;
                }
            }

            return default;
        }

        private static List<TagSuggestion> GetTagSuggestions(IEnumerable<string> sourceTags, List<string> userTagList, int maxSuggestions = 5)
        {
            if (sourceTags == null || userTagList == null || maxSuggestions <= 0) return new List<TagSuggestion>();

            List<string> tags = sourceTags.Where((x) => !string.IsNullOrWhiteSpace(x)).ToList();
            return userTagList
                .Where((x) => !string.IsNullOrWhiteSpace(x))
                .Select((x) =>
                {
                    string matchedTag = null;
                    double bestScore = 0;

                    foreach (string sourceTag in tags)
                    {
                        double score = GetTagSimilarity(sourceTag, x);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            matchedTag = sourceTag;
                        }
                    }

                    return new TagSuggestion
                    {
                        Tag = x,
                        SourceTag = matchedTag,
                        Score = bestScore,
                        IsExactMatch = tags.Any((sourceTag) => string.Equals(
                            sourceTag.Normalize(NormalizationForm.FormKC),
                            x.Normalize(NormalizationForm.FormKC),
                            StringComparison.OrdinalIgnoreCase))
                    };
                })
                .Where((x) => x.Score > 0.8)
                .GroupBy((x) => NormalizeTag(x.Tag))
                .Select((x) => x
                    .OrderByDescending((suggestion) => suggestion.IsExactMatch)
                    .ThenByDescending((suggestion) => suggestion.Score)
                    .First())
                .OrderByDescending((x) => x.Score)
                .ThenByDescending((x) => NormalizeTag(x.Tag).Length)
                .ThenBy((x) => x.Tag, StringComparer.OrdinalIgnoreCase)
                .Take(maxSuggestions)
                .ToList();
        }

        private static bool TryParseSuggestedTags(string input, List<TagSuggestion> suggestions, out List<string> tags)
        {
            tags = new List<string>();
            foreach (string value in input.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (value.StartsWith("#") && int.TryParse(value.Substring(1), out int suggestionIndex))
                {
                    if (suggestionIndex < 1 || suggestionIndex > suggestions.Count)
                    {
                        FormatColorWrite($"無效的建議編號: {value}", ConsoleColor.Red);
                        tags = null;
                        return false;
                    }

                    tags.Add(suggestions[suggestionIndex - 1].Tag);
                }
                else
                {
                    tags.Add(value);
                }
            }

            tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return tags.Count > 0;
        }

        private static double GetTagSimilarity(string sourceTag, string candidateTag)
        {
            string source = NormalizeTag(sourceTag);
            string candidate = NormalizeTag(candidateTag);
            if (source.Length == 0 || candidate.Length == 0) return 0;
            if (source == candidate) return 1;

            Rune[] sourceCharacters = source.EnumerateRunes().ToArray();
            Rune[] candidateCharacters = candidate.EnumerateRunes().ToArray();

            bool hasNonAsciiCharacter = candidateCharacters.Any((x) => x.Value > 127);
            bool isLongAsciiPrefix = candidateCharacters.Length >= 4 && source.StartsWith(candidate, StringComparison.Ordinal);
            bool isShortAcronymPrefix = IsShortAcronymPrefix(sourceTag, candidateTag);

            if ((hasNonAsciiCharacter && source.Contains(candidate)) || isLongAsciiPrefix || isShortAcronymPrefix)
            {
                double lengthRatio = Math.Min(sourceCharacters.Length, candidateCharacters.Length) / (double)Math.Max(sourceCharacters.Length, candidateCharacters.Length);
                return 0.85 + (0.15 * lengthRatio);
            }

            if (!IsMeaningfulTagLength(sourceCharacters) || !IsMeaningfulTagLength(candidateCharacters)) return 0;

            int distance = GetLevenshteinDistance(sourceCharacters, candidateCharacters);
            return 1 - (distance / (double)Math.Max(sourceCharacters.Length, candidateCharacters.Length));
        }

        private static string NormalizeTag(string value, bool lowerCase = true)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            StringBuilder normalized = new StringBuilder();
            string normalizedValue = value.Normalize(NormalizationForm.FormKC);
            if (lowerCase) normalizedValue = normalizedValue.ToLowerInvariant();

            foreach (Rune character in normalizedValue.EnumerateRunes())
            {
                if (Rune.IsLetterOrDigit(character) || character.Value == '+' || character.Value == '#')
                {
                    normalized.Append(character.ToString());
                }
            }

            return normalized.ToString();
        }

        private static bool IsShortAcronymPrefix(string sourceTag, string candidateTag)
        {
            Rune[] source = NormalizeTag(sourceTag, false).EnumerateRunes().ToArray();
            Rune[] candidate = NormalizeTag(candidateTag, false).EnumerateRunes().ToArray();
            if (candidate.Length != 2 || source.Length <= candidate.Length) return false;
            if (!candidate.Any(Rune.IsLetter) || !candidate.Where(Rune.IsLetter).All(Rune.IsUpper)) return false;
            if (source[0].Value != candidate[0].Value || source[1].Value != candidate[1].Value) return false;

            Rune nextCharacter = source[candidate.Length];
            return nextCharacter.Value > 127 || Rune.IsUpper(nextCharacter) || Rune.IsDigit(nextCharacter);
        }

        private static bool IsMeaningfulTagLength(Rune[] value)
        {
            if (value.Length >= 3) return true;
            return value.Length >= 2 && value.Any((x) => x.Value > 127);
        }

        private static int GetLevenshteinDistance(Rune[] source, Rune[] target)
        {
            int[] distances = Enumerable.Range(0, target.Length + 1).ToArray();
            for (int sourceIndex = 1; sourceIndex <= source.Length; sourceIndex++)
            {
                int previousDiagonal = distances[0];
                distances[0] = sourceIndex;

                for (int targetIndex = 1; targetIndex <= target.Length; targetIndex++)
                {
                    int previousAbove = distances[targetIndex];
                    int substitutionCost = source[sourceIndex - 1].Value == target[targetIndex - 1].Value ? 0 : 1;
                    distances[targetIndex] = Math.Min(
                        Math.Min(distances[targetIndex] + 1, distances[targetIndex - 1] + 1),
                        previousDiagonal + substitutionCost);
                    previousDiagonal = previousAbove;
                }
            }

            return distances[target.Length];
        }

        private static string GetDownloadFolder(List<string> tags, Dictionary<string, string> tagConvertList)
        {
            foreach (string tag in tags)
            {
                var exactMatch = tagConvertList.FirstOrDefault((x) => string.Equals(x.Key, tag, StringComparison.OrdinalIgnoreCase));
                if (exactMatch.Key != null) return exactMatch.Value;
            }

            var partialMatch = tagConvertList.FirstOrDefault((x) => tags.Any((tag) => tag.Contains(x.Key, StringComparison.OrdinalIgnoreCase)));
            return partialMatch.Key == null ? tags.First() : partialMatch.Value;
        }

        private static Dictionary<string, string> GetTagConvertList()
        {
            try
            {
                string listSavePath = AppDomain.CurrentDomain.BaseDirectory + "TagConvertList.json";
                if (File.Exists(listSavePath)) return JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(listSavePath));
                else return new Dictionary<string, string>();
            }
            catch (Exception ex) { FormatColorWrite(ex.Message, ConsoleColor.DarkRed); throw; }
        }

        private static List<string> GetIgnoreDownloadTagList()
        {
            try
            {
                string listSavePath = AppDomain.CurrentDomain.BaseDirectory + "IgnoreDownloadTag.json";
                if (File.Exists(listSavePath)) return JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(listSavePath));
                else return new List<string>();
            }
            catch (Exception ex) { FormatColorWrite(ex.Message, ConsoleColor.DarkRed); throw; }
        }

        private static async Task<List<string>> GetUserTagListAsync()
        {
            List<string> tagList = new List<string>();
            var bookmarkTagsResult = await GetServerDataAsync<BookmarkTagsMetadata>($"user/{userId}/illusts/bookmark/tags?lang=zh_tw");
            if (bookmarkTagsResult?.Public == null)
            {
                FormatColorWrite("取得使用者標籤清單失敗", ConsoleColor.Red);
                return null;
            }

            foreach (var item in bookmarkTagsResult.Public) tagList.Add(item.Tag);
            return tagList;
        }

        public static void FormatColorWrite(string text, ConsoleColor consoleColor = ConsoleColor.Gray, bool newLine = true)
        {
            Console.ForegroundColor = consoleColor;
            if (newLine) Console.WriteLine(text);
            else Console.Write(text);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }
}
