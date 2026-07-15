using Newtonsoft.Json;
using System.Collections.Generic;

namespace PixivBookmarkFilter
{
    public class Public
    {
        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("cnt")]
        public int Cnt { get; set; }
    }

    public class BookmarkTagsMetadata
    {
        [JsonProperty("public")]
        public List<Public> Public { get; set; }

        [JsonProperty("private")]
        public List<object> Private { get; set; }

        [JsonProperty("tooManyBookmark")]
        public bool TooManyBookmark { get; set; }

        [JsonProperty("tooManyBookmarkTags")]
        public bool TooManyBookmarkTags { get; set; }
    }
}
