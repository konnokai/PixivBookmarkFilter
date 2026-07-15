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

            LoginPixiv();

            if (!string.IsNullOrWhiteSpace(userData.PHPSESSID))
            {
                userId = userData.PHPSESSID.Split(new char[] { '_' })[0];
                FormatColorWrite("取得使用者收藏標籤列表", ConsoleColor.DarkYellow);
                List<string> tagList = GetUserTagList();

                int offset = 0;
                while (true)
                {
                    bool isNoImageConvert = true;
                    var bookmarksMetadata = await GetServerDataAsync<BookmarksMetadata>($"user/{userId}/illusts/bookmarks?tag=&offset={offset}&limit=50&rest=show");

                    IEnumerable<Work> bookmarkWorks;
                    if (bookmarksMetadata.BookmarkTags is JArray)
                    {
                        bookmarkWorks = bookmarksMetadata.Works;
                    }
                    else
                    {
                        var bookmarkTags = JObject.Parse(bookmarksMetadata.BookmarkTags.ToString());
                        bookmarkWorks = bookmarksMetadata.Works.Where((x) => bookmarkTags[x.BookmarkData.Id] == null || bookmarkTags[x.BookmarkData.Id].Count() == 0);
                    }

                    offset += 50;

                    if (bookmarkWorks.Count() == 0) { FormatColorWrite("已全部分類完成!", ConsoleColor.Green); break; }

                    foreach (var item in bookmarkWorks)
                    {
                        bool isNeedRefreshUserTagList = false;
                        List<string> needAddTag = item.Tags.Where((x) => tagList.Contains(x)).ToList();

                        if (needAddTag.Count == 0)
                        {
                            FormatColorWrite($"{item.Id} - {item.Title} 未包含已儲存的標籤!", ConsoleColor.Red);
                            FormatColorWrite($"[{string.Join(" ", item.Tags)}](輸入 \"-\" 取消收藏): ", newLine: false);
                            string addTag = "";

                            do
                            {
                                addTag = Console.ReadLine().Trim();
                                if (addTag != "") break;
                            } while (true);

                            if (addTag == "-")
                            {
                                await PostServerDataAsync("illusts/bookmarks/delete", $"bookmark_id={item.BookmarkData.Id}");
                                continue;
                            }
                            else
                            {
                                needAddTag.AddRange(addTag.Split(new char[] { ' ' })); tagConvertList = GetTagConvertList();
                                try { isNeedRefreshUserTagList = !tagConvertList.Any((x) => needAddTag.Any((x2) => x2.Contains(x.Key))); }
                                catch { isNeedRefreshUserTagList = true; }
                            }
                        }

                        string tag;
                        try
                        {
                            tag = tagConvertList.First((x) => needAddTag.Any((x2) => x2.Contains(x.Key))).Value;
                        }
                        catch (Exception)
                        {
                            tag = needAddTag.First();
                        }

                        if (ignoreDownloadList.Any((x) => needAddTag.Contains(x)))
                        {
                            FormatColorWrite("(已忽略下載) ", ConsoleColor.DarkYellow, false);
                        }
                        else
                        {
                            string savePath = $"{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}\\Pixiv收藏分類儲存\\{tag}\\";
                            await DownloadPictureAsync(item.Id, savePath, needAddTag.Any((x) => x.ToLower().Contains("r-18")));
                        }

                        await PostServerDataAsync("illusts/bookmarks/add_tags", JsonConvert.SerializeObject(new { tags = needAddTag, bookmarkIds = new List<string>() { item.BookmarkData.Id } }));

                        isNoImageConvert = false;

                        FormatColorWrite($"{item.Id} - {item.Title} ({string.Join(" ", needAddTag)}) {tag}", ConsoleColor.Green);

                        if (isNeedRefreshUserTagList)
                        {
                            FormatColorWrite("刷新使用者標籤清單", ConsoleColor.DarkYellow);
                            tagList = GetUserTagList();
                        }
                    }

                    if (isNoImageConvert)
                    {
                        FormatColorWrite("此頁無圖片符合標籤!", ConsoleColor.Red);
                        break;
                    }
                }
            }

            Console.WriteLine("請按任意鍵繼續...");
            Console.ReadKey();
        }

        static HttpClient downloadClient;
        private static async Task DownloadPictureAsync(string id, string savePath, bool isR18 = false)
        {
            if (downloadClient == null)
            {
                downloadClient = new();
                downloadClient.DefaultRequestHeaders.Add("Referer", "https://pixiv.net");
            }

            IllustMetadata illust = await GetServerDataAsync<IllustMetadata>($"illust/{id}");

            if (illust.IllustType < 2)
            {
                if (illust.PageCount == 1)
                {
                    if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                    var response = await downloadClient.GetAsync(illust.Urls.Original);
                    response.EnsureSuccessStatusCode();
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    await File.WriteAllBytesAsync(savePath + (isR18 ? $"{DateTime.Now:yyyyMMdd_HHmmss}_" : "") + Path.GetFileName(illust.Urls.Original), imageBytes);
                }
                else
                {
                    string SavePath = savePath + id;
                    if (!Directory.Exists(SavePath)) Directory.CreateDirectory(SavePath);
                    string imageFolder = illust.Urls.Original;
                    imageFolder = imageFolder.Replace(imageFolder.Split(new char[] { '/' }).Last(), "");
                    string extension = Path.GetExtension(illust.Urls.Original);

                    Console.WriteLine($"下載圖檔: {illust.PageCount}");
                    using (var progressBar = new ProgressBar())
                    {
                        for (int i = 0; i < illust.PageCount; i++)
                        {
                            progressBar.Report((i + 1) / (double)illust.PageCount);

                            try
                            {
                                var response = await downloadClient.GetAsync(imageFolder + id + "_p" + i.ToString() + extension);
                                response.EnsureSuccessStatusCode();
                                var imageBytes = await response.Content.ReadAsByteArrayAsync();
                                await File.WriteAllBytesAsync(SavePath + "\\" + (isR18 ? $"{DateTime.Now:yyyyMMdd_HHmmss}_" : "") + id + "_p" + i.ToString() + extension, imageBytes);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"{id}_p{i}.{extension} 下載失敗: {ex.InnerException.Message}");
                            }
                        }
                    }
                }
            }
            else if (illust.IllustType == 2) FormatColorWrite("動圖不支援下載!!", ConsoleColor.Cyan);
            else FormatColorWrite("錯誤，該ID可能已被刪除", ConsoleColor.Red);
        }

        private static void LoginPixiv()
        {
            if (userData == null)
            {
                try
                {
                    userData = new UserData();

                    FormatColorWrite("請輸入PHPSESSID: ", ConsoleColor.DarkYellow, false);
                    userData.PHPSESSID = Console.ReadLine();

                    FormatColorWrite("請輸入X_CSRF_TOKEN: ", ConsoleColor.DarkYellow, false);
                    userData.X_CSRF_TOKEN = Console.ReadLine();

                    if (GetServerDataAsync<ExtraMetaData>($"user/extra").Result != null)
                    {
                        FormatColorWrite("驗證成功", ConsoleColor.Green);
                        File.WriteAllText("UserData.json", JsonConvert.SerializeObject(userData));
                    }
                    else { FormatColorWrite("驗證失敗", ConsoleColor.Red); userData = null; LoginPixiv(); }
                }
                catch (Exception ex) { FormatColorWrite(ex.Message, ConsoleColor.Red); }
            }
            else FormatColorWrite("已載入儲存的使用者資料", ConsoleColor.DarkYellow);
        }

        private static HttpClient postClient;
        private static async Task PostServerDataAsync(string url, string data)
        {
            try
            {
                if (postClient == null)
                {
                    postClient = new();
                    postClient.DefaultRequestHeaders.Add("Origin", "https://www.pixiv.net");
                    postClient.DefaultRequestHeaders.Add("X-Csrf-Token", userData.X_CSRF_TOKEN);
                    postClient.DefaultRequestHeaders.Add("Cookie", $"PHPSESSID={userData.PHPSESSID}");
                    postClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36");
                }

                var content = new StringContent(data, Encoding.UTF8, data.StartsWith("{") ? "application/json" : "application/x-www-form-urlencoded");
                var response = await postClient.PostAsync($"https://www.pixiv.net/ajax/{url}", content);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) { FormatColorWrite($"{ex}", ConsoleColor.Red); }
        }

        private static HttpClient getClient;
        private static async Task<T> GetServerDataAsync<T>(string Url, bool ShowError = true)
        {
            try
            {
                if (getClient == null)
                {
                    getClient = new();
                    getClient.DefaultRequestHeaders.Add("Referer", "https://pixiv.net");
                    getClient.DefaultRequestHeaders.Add("Cookie", $"PHPSESSID={userData.PHPSESSID}");
                    getClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/92.0.4515.107 Safari/537.36");
                }

                var response = await getClient.GetAsync($"https://www.pixiv.net/ajax/{Url}");
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadAsStringAsync();

                var resultBody = JsonConvert.DeserializeObject<ResultBody<T>>(result);

                if (resultBody.Error)
                {
                    FormatColorWrite("需要重新登入", ConsoleColor.DarkRed);
                    userData = null;
                    LoginPixiv();
                    return default;
                }

                return resultBody.Body;
            }
            catch (Exception ex) { if (ShowError) FormatColorWrite(ex.Message, ConsoleColor.Red); return default; }
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

        private static List<string> GetUserTagList()
        {
            List<string> tagList = new List<string>();
            var bookmarkTagsMetadata = GetServerDataAsync<BookmarkTagsMetadata>($"user/{userId}/illusts/bookmark/tags?lang=zh_tw").Result.Public;

            foreach (var item in bookmarkTagsMetadata) tagList.Add(item.Tag);
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
