using Newtonsoft.Json;

namespace PixivBookmarkFilter
{
    public class ExtraMetaData
    {
        [JsonProperty("following")]
        public int Following { get; set; }

        [JsonProperty("followers")]
        public int Followers { get; set; }

        [JsonProperty("mypixivCount")]
        public int MypixivCount { get; set; }

        [JsonProperty("background")]
        public object Background { get; set; }
    }
}
