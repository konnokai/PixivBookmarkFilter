using System.Collections.Generic;

namespace PixivBookmarkFilter
{
    internal enum TagSuggestionSource
    {
        Rag,
        OpenAi,
        Lexical
    }

    internal sealed class TagSuggestion
    {
        public string Tag { get; set; }
        public double Score { get; set; }
        public string Evidence { get; set; }
        public string Reason { get; set; }
        public TagSuggestionSource Source { get; set; }
        public bool IsExactMatch { get; set; }
    }

    internal sealed class BookmarkTagDocumentInput
    {
        public string WorkId { get; set; }
        public string Title { get; set; }
        public List<string> SourceTags { get; set; } = new List<string>();
        public List<string> AssignedTags { get; set; } = new List<string>();
    }

    internal sealed class BookmarkHistorySyncResult
    {
        public List<BookmarkTagDocumentInput> Documents { get; set; } = new List<BookmarkTagDocumentInput>();
        public string LatestWorkId { get; set; }
        public bool IsCompleteSnapshot { get; set; }
        public int FetchedWorkCount { get; set; }
    }
}
