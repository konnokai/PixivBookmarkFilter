using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace PixivBookmarkFilter
{
    public class BookmarkData
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("private")]
        public bool Private { get; set; }
    }

    public class Work
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("illustType")]
        public int IllustType { get; set; }

        [JsonProperty("xRestrict")]
        public int XRestrict { get; set; }

        [JsonProperty("restrict")]
        public int Restrict { get; set; }

        [JsonProperty("sl")]
        public int Sl { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("userId")]
        public string UserId { get; set; }

        [JsonProperty("userName")]
        public string UserName { get; set; }

        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }

        [JsonProperty("pageCount")]
        public int PageCount { get; set; }

        [JsonProperty("isBookmarkable")]
        public bool IsBookmarkable { get; set; }

        [JsonProperty("bookmarkData")]
        public BookmarkData BookmarkData { get; set; }

        [JsonProperty("alt")]
        public string Alt { get; set; }

        [JsonProperty("createDate")]
        public DateTime CreateDate { get; set; }

        [JsonProperty("updateDate")]
        public DateTime UpdateDate { get; set; }

        [JsonProperty("isUnlisted")]
        public bool IsUnlisted { get; set; }

        [JsonProperty("isMasked")]
        public bool IsMasked { get; set; }

        [JsonProperty("profileImageUrl")]
        public string ProfileImageUrl { get; set; }
    }

    public class BookmarksMetadata
    {
        [JsonProperty("bookmarkTags")]
        public JToken BookmarkTags { get; set; }

        [JsonProperty("works")]
        public List<Work> Works { get; set; }
    }

}
