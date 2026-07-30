using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal sealed class RagTagSuggestionService
    {
        private readonly RagSettings settings;
        private readonly IEmbeddingClient embeddingClient;
        private readonly IOpenAiTagSuggestionClient openAiClient;
        private readonly BookmarkTagIndex index;

        public string LastRagError { get; private set; }
        public string LastOpenAiError { get; private set; }
        public int IndexedDocumentCount => index?.Count ?? 0;
        public bool CanUseOpenAi => openAiClient.IsConfigured;

        public RagTagSuggestionService(
            RagSettings settings,
            IEmbeddingClient embeddingClient,
            IOpenAiTagSuggestionClient openAiClient,
            BookmarkTagIndex index)
        {
            this.settings = settings;
            this.embeddingClient = embeddingClient;
            this.openAiClient = openAiClient;
            this.index = index;
        }

        public async Task<List<TagSuggestion>> GetRagSuggestionsAsync(
            Work work,
            IReadOnlyList<string> userTagList,
            CancellationToken cancellationToken = default)
        {
            LastRagError = null;
            if (work == null || index == null || index.Count == 0) return new List<TagSuggestion>();

            try
            {
                string embeddingText = BookmarkTagIndex.CreateEmbeddingText(work.Title, work.Tags);
                float[] queryEmbedding = (await embeddingClient.GenerateEmbeddingsAsync(
                    new[] { embeddingText }, cancellationToken))[0];
                List<RetrievedBookmark> retrieved = index.Search(
                    queryEmbedding,
                    work.Id,
                    settings.RetrievalTopK,
                    settings.MinimumSimilarity);
                return RankTags(retrieved, userTagList, settings.MaxSuggestions);
            }
            catch (Exception ex)
            {
                LastRagError = ex.Message;
                return new List<TagSuggestion>();
            }
        }

        public async Task<List<TagSuggestion>> GetOpenAiSuggestionsAsync(
            Work work,
            IReadOnlyList<string> userTagList,
            CancellationToken cancellationToken = default)
        {
            LastOpenAiError = null;
            if (!openAiClient.IsConfigured) return new List<TagSuggestion>();

            try
            {
                return await openAiClient.GetSuggestionsAsync(work, userTagList, cancellationToken);
            }
            catch (Exception ex)
            {
                LastOpenAiError = ex.Message;
                return new List<TagSuggestion>();
            }
        }

        public async Task UpdateIndexAsync(
            Work work,
            IEnumerable<string> assignedTags,
            CancellationToken cancellationToken = default)
        {
            if (work == null || index == null) return;

            await index.UpsertAsync(new BookmarkTagDocumentInput
            {
                WorkId = work.Id,
                Title = work.Title,
                SourceTags = work.Tags ?? new List<string>(),
                AssignedTags = assignedTags?.ToList() ?? new List<string>()
            }, embeddingClient, cancellationToken);
        }

        internal static List<TagSuggestion> RankTags(
            IReadOnlyList<RetrievedBookmark> retrieved,
            IReadOnlyList<string> userTagList,
            int maxSuggestions)
        {
            if (retrieved == null || retrieved.Count == 0 || userTagList == null)
                return new List<TagSuggestion>();

            Dictionary<string, string> validTags = userTagList
                .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                .GroupBy((tag) => tag, StringComparer.OrdinalIgnoreCase)
                .ToDictionary((group) => group.Key, (group) => group.First(), StringComparer.OrdinalIgnoreCase);
            Dictionary<string, TagVote> votes = new Dictionary<string, TagVote>(StringComparer.OrdinalIgnoreCase);
            double totalSimilarity = retrieved.Sum((result) => Math.Max(result.Similarity, 0));
            if (totalSimilarity <= 0) return new List<TagSuggestion>();

            foreach (RetrievedBookmark result in retrieved)
            {
                foreach (string assignedTag in result.Document.AssignedTags.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!validTags.TryGetValue(assignedTag, out string validTag)) continue;
                    if (!votes.TryGetValue(validTag, out TagVote vote))
                    {
                        vote = new TagVote { Tag = validTag };
                        votes[validTag] = vote;
                    }

                    vote.WeightedScore += result.Similarity;
                    vote.SupportCount++;
                    if (vote.BestEvidence == null || result.Similarity > vote.BestEvidence.Similarity)
                        vote.BestEvidence = result;
                }
            }

            return votes.Values
                .OrderByDescending((vote) => vote.WeightedScore)
                .ThenByDescending((vote) => vote.SupportCount)
                .ThenBy((vote) => vote.Tag, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Min(maxSuggestions, 5))
                .Select((vote) => new TagSuggestion
                {
                    Tag = vote.Tag,
                    Score = Math.Clamp(vote.WeightedScore / totalSimilarity, 0, 1),
                    Evidence = vote.BestEvidence == null
                        ? ""
                        : $"{vote.BestEvidence.Document.Title} ({vote.BestEvidence.Similarity:P0})",
                    Reason = $"{vote.SupportCount} 筆相似收藏使用此標籤",
                    Source = TagSuggestionSource.Rag
                })
                .ToList();
        }

        private sealed class TagVote
        {
            public string Tag { get; set; }
            public double WeightedScore { get; set; }
            public int SupportCount { get; set; }
            public RetrievedBookmark BestEvidence { get; set; }
        }
    }
}
