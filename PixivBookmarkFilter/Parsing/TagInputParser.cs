using System;
using System.Collections.Generic;
using System.Linq;

namespace PixivBookmarkFilter
{
    internal sealed class TagInputParseResult
    {
        public bool IsValid { get; set; }
        public bool CancelBookmark { get; set; }
        public bool UseOpenAi { get; set; }
        public string Error { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    internal static class TagInputParser
    {
        public static TagInputParseResult Parse(string input, IReadOnlyList<TagSuggestion> suggestions, bool allowOpenAi)
        {
            string value = input?.Trim() ?? "";
            if (value == "-") return new TagInputParseResult { IsValid = true, CancelBookmark = true };
            if (value == "") return new TagInputParseResult { Error = "請輸入標籤或操作" };

            string[] values = value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            bool containsOpenAi = values.Any((item) => string.Equals(item, "#AI", StringComparison.OrdinalIgnoreCase));
            if (containsOpenAi)
            {
                if (values.Length != 1) return new TagInputParseResult { Error = "#AI 必須單獨輸入" };
                if (!allowOpenAi) return new TagInputParseResult { Error = "目前沒有可用的 OpenAI 降級選項" };
                return new TagInputParseResult { IsValid = true, UseOpenAi = true };
            }

            List<string> tags = new List<string>();
            foreach (string item in values)
            {
                if (item.StartsWith("#", StringComparison.Ordinal) && int.TryParse(item.Substring(1), out int suggestionIndex))
                {
                    if (suggestionIndex < 1 || suggestionIndex > suggestions.Count)
                        return new TagInputParseResult { Error = $"無效的建議編號: {item}" };
                    tags.Add(suggestions[suggestionIndex - 1].Tag);
                }
                else
                {
                    tags.Add(item);
                }
            }

            tags = tags
                .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return tags.Count == 0
                ? new TagInputParseResult { Error = "請輸入至少一個標籤" }
                : new TagInputParseResult { IsValid = true, Tags = tags };
        }
    }
}
