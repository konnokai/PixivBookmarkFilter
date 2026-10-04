using PixivBookmarkFilter;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PixivBookmarkFilter.Tests
{
    public class PixivApiClientTests
    {
        [Fact]
        public async Task TagSampledHistory_CapsEachTagSkipsUnclassifiedAndDeduplicates()
        {
            FakeBookmarkHandler handler = new FakeBookmarkHandler();
            for (int index = 0; index < 120; index++)
                handler.AddBookmark($"a{index}", $"A {index}", "TagA");
            handler.AddBookmark("shared", "Shared", "TagA/B", "TagC");
            handler.AddBookmark("c1", "C only", "TagC");
            handler.AddBookmark("900", "Latest");

            using PixivApiClient client = new PixivApiClient(handler, new UserData { PHPSESSID = "123_abc" });
            List<(int Completed, int Total)> progress = new List<(int Completed, int Total)>();
            BookmarkHistorySyncResult result = await client.GetTagSampledBookmarkHistoryAsync(
                new[] { "TagA", "未分類", "TagA/B", "TagC", "TagA" },
                70,
                (completed, total) => progress.Add((completed, total)));

            Assert.NotNull(result);
            Assert.True(result.IsCompleteSnapshot);
            Assert.Equal("900", result.LatestWorkId);
            Assert.Equal(70, result.Documents.Count((document) => document.WorkId.StartsWith("a")));
            Assert.Equal(new[] { "TagA/B", "TagC" }, result.Documents.Single((document) => document.WorkId == "shared").AssignedTags);
            Assert.Single(result.Documents, (document) => document.WorkId == "c1");
            Assert.DoesNotContain(result.Documents, (document) => document.WorkId == "900");
            Assert.Equal(new[] { (1, 3), (2, 3), (3, 3) }, progress);

            Assert.DoesNotContain(handler.RequestedTags, (tag) => tag == "未分類");
            Assert.Contains("tag=TagA%2FB&", handler.RequestUris.Select((uri) => uri.Query).Single((query) => query.Contains("TagA%2FB")));
            Assert.Equal(new[] { "0:50", "50:20" }, handler.GetPages("TagA"));
            Assert.Equal(new[] { "0:1" }, handler.GetPages(""));
        }

        [Fact]
        public async Task TagSampledHistory_ReturnsNullWhenTagRequestFails()
        {
            FakeBookmarkHandler handler = new FakeBookmarkHandler { FailingTag = "TagB" };
            handler.AddBookmark("1", "A", "TagA");

            using PixivApiClient client = new PixivApiClient(handler, new UserData { PHPSESSID = "123_abc" });
            BookmarkHistorySyncResult result = await client.GetTagSampledBookmarkHistoryAsync(
                new[] { "TagA", "TagB" },
                100);

            Assert.Null(result);
        }

        [Fact]
        public async Task UserTagList_ExcludesUnclassified()
        {
            StaticResponseHandler handler = new StaticResponseHandler(JsonConvert.SerializeObject(new
            {
                error = false,
                message = "",
                body = new
                {
                    @public = new[]
                    {
                        new { tag = "未分類", cnt = 4 },
                        new { tag = "Fate", cnt = 10 },
                        new { tag = "VOCALOID", cnt = 3 }
                    }
                }
            }));

            using PixivApiClient client = new PixivApiClient(handler, new UserData { PHPSESSID = "123_abc" });
            List<string> tags = await client.GetUserTagListAsync();

            Assert.Equal(new[] { "Fate", "VOCALOID" }, tags);
        }

        private sealed class StaticResponseHandler : HttpMessageHandler
        {
            private readonly string responseBody;

            public StaticResponseHandler(string responseBody)
            {
                this.responseBody = responseBody;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                });
            }
        }

        private sealed class FakeBookmarkHandler : HttpMessageHandler
        {
            private readonly List<(string Id, string Title, string[] Tags)> bookmarks = new List<(string Id, string Title, string[] Tags)>();

            public string FailingTag { get; set; }
            public List<Uri> RequestUris { get; } = new List<Uri>();
            public List<string> RequestedTags { get; } = new List<string>();
            private List<(string Tag, int Offset, int Limit)> Pages { get; } = new List<(string Tag, int Offset, int Limit)>();

            public void AddBookmark(string id, string title, params string[] tags)
            {
                // 越晚加入的越新，跟 Pixiv 一樣由新到舊回傳
                bookmarks.Insert(0, (id, title, tags));
            }

            public string[] GetPages(string tag)
            {
                return Pages
                    .Where((page) => page.Tag == tag)
                    .Select((page) => $"{page.Offset}:{page.Limit}")
                    .ToArray();
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestUris.Add(request.RequestUri);
                Dictionary<string, string> query = request.RequestUri.Query.TrimStart('?')
                    .Split('&')
                    .Select((part) => part.Split('='))
                    .ToDictionary((part) => part[0], (part) => Uri.UnescapeDataString(part[1]));
                string tag = query["tag"];
                int offset = int.Parse(query["offset"]);
                int limit = int.Parse(query["limit"]);
                RequestedTags.Add(tag);
                Pages.Add((tag, offset, limit));

                // 用 500 而不是 error=true，後者會走到需要主控台輸入的重新登入流程
                if (tag == FailingTag)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

                List<(string Id, string Title, string[] Tags)> page = bookmarks
                    .Where((bookmark) => tag.Length == 0 || bookmark.Tags.Contains(tag))
                    .Skip(offset)
                    .Take(limit)
                    .ToList();
                var body = new
                {
                    works = page.Select((bookmark) => new
                    {
                        id = bookmark.Id,
                        title = bookmark.Title,
                        tags = new[] { bookmark.Title },
                        bookmarkData = new { id = $"bm{bookmark.Id}" }
                    }),
                    bookmarkTags = page
                        .Where((bookmark) => bookmark.Tags.Length > 0)
                        .ToDictionary((bookmark) => $"bm{bookmark.Id}", (bookmark) => bookmark.Tags)
                };
                return Task.FromResult(CreateResponse(new { error = false, message = "", body }));
            }

            private static HttpResponseMessage CreateResponse(object value)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
