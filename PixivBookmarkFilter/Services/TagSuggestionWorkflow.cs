using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal sealed class TagSuggestionWorkflow : IDisposable
    {
        private RagSettings settings;
        private EmbeddingModelClient embeddingClient;
        private OpenAiTagSuggestionClient openAiClient;
        private RagTagSuggestionService suggestionService;

        public async Task InitializeAsync(PixivApiClient pixivApiClient, List<string> userTagList)
        {
            try
            {
                settings = RagSettings.Load();
                embeddingClient = new EmbeddingModelClient(settings);
                openAiClient = new OpenAiTagSuggestionClient(settings);
                BookmarkTagIndex index = null;

                try
                {
                    index = new BookmarkTagIndex(settings.GetIndexPath(), settings.EmbeddingModel);
                    await index.InitializeAsync();
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Write($"初始化 RAG 索引失敗: {ex.Message}", ConsoleColor.DarkYellow);
                }

                if (index != null)
                {
                    try
                    {
                        ConsoleOutput.Write("確認 Embedding 模型端點", ConsoleColor.DarkYellow);
                        int embeddingDimension = await embeddingClient.VerifyAvailabilityAsync();
                        ConsoleOutput.Write(
                            $"Embedding 模型端點可用，向量維度 {embeddingDimension}",
                            ConsoleColor.Green);
                    }
                    catch (Exception ex)
                    {
                        ConsoleOutput.Write(
                            $"Embedding 模型端點無法使用，已略過歷史收藏查詢: {ex.Message}",
                            ConsoleColor.DarkYellow);
                        index = null;
                    }
                }

                suggestionService = new RagTagSuggestionService(
                    settings,
                    embeddingClient,
                    openAiClient,
                    index);
                if (index == null) return;

                try
                {
                    // 有錨點時只需往回讀到錨點；沒有錨點就每個標籤各抽最近幾筆，不掃整個收藏歷史
                    bool canSyncIncrementally = index.Count > 0 && !string.IsNullOrWhiteSpace(index.SyncAnchorWorkId);
                    BookmarkHistorySyncResult history;
                    if (canSyncIncrementally)
                    {
                        ConsoleOutput.Write("增量同步歷史收藏 RAG 索引", ConsoleColor.DarkYellow);
                        history = await pixivApiClient.GetTaggedBookmarkHistoryAsync(
                            index.SyncAnchorWorkId,
                            index.WorkIds,
                            (fetched, total) => ConsoleOutput.Write(
                                total.HasValue ? $"讀取收藏: {fetched}/{total}" : $"讀取收藏: {fetched}",
                                ConsoleColor.DarkYellow));
                    }
                    else
                    {
                        ConsoleOutput.Write(
                            $"建立歷史收藏 RAG 索引，每個標籤取最近 {settings.IndexSamplesPerTag} 筆",
                            ConsoleColor.DarkYellow);
                        history = await pixivApiClient.GetTagSampledBookmarkHistoryAsync(
                            userTagList,
                            settings.IndexSamplesPerTag,
                            (completed, total) =>
                            {
                                if (completed % 10 == 0 || completed == total)
                                    ConsoleOutput.Write($"讀取標籤: {completed}/{total}", ConsoleColor.DarkYellow);
                            });
                    }

                    if (history == null) return;

                    Action<int, int> reportProgress = (completed, total) =>
                    {
                        if (total > 0)
                            ConsoleOutput.Write($"產生 Embedding: {completed}/{total}", ConsoleColor.DarkYellow);
                    };

                    if (history.IsCompleteSnapshot)
                        await index.SynchronizeAsync(history.Documents, embeddingClient, reportProgress);
                    else
                        await index.SynchronizeIncrementalAsync(history.Documents, embeddingClient, reportProgress);

                    await index.SetSyncAnchorAsync(history.LatestWorkId);
                    string syncMode = history.IsCompleteSnapshot ? "完整" : "增量";
                    ConsoleOutput.Write(
                        $"RAG 索引{syncMode}同步完成，查詢 {history.FetchedWorkCount} 筆、新增區段 {history.Documents.Count} 筆，索引共 {index.Count} 筆",
                        ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    ConsoleOutput.Write(
                        $"同步 RAG 索引失敗，將自動使用 OpenAI 或字串比對: {ex.Message}",
                        ConsoleColor.DarkYellow);
                }
            }
            catch (Exception ex)
            {
                ConsoleOutput.Write(
                    $"載入 RAG 設定失敗，將使用字串比對: {ex.Message}",
                    ConsoleColor.DarkYellow);
            }
        }

        public async Task<TagInputParseResult> SelectTagsAsync(Work work, List<string> userTagList)
        {
            List<TagSuggestion> ragSuggestions = suggestionService == null
                ? new List<TagSuggestion>()
                : await suggestionService.GetRagSuggestionsAsync(work, userTagList);

            if (!string.IsNullOrWhiteSpace(suggestionService?.LastRagError))
                ConsoleOutput.Write($"RAG 搜尋失敗: {suggestionService.LastRagError}", ConsoleColor.DarkYellow);

            if (ragSuggestions.Count > 0)
            {
                bool allowOpenAi = suggestionService.CanUseOpenAi;
                DisplaySuggestions("RAG 建議標籤", ragSuggestions, allowOpenAi);
                TagInputParseResult ragSelection = PromptForTags(work, ragSuggestions, allowOpenAi);
                if (!ragSelection.UseOpenAi) return ragSelection;
            }

            if (suggestionService?.CanUseOpenAi == true)
            {
                ConsoleOutput.Write("使用 OpenAI 判斷標籤", ConsoleColor.DarkYellow);
                List<TagSuggestion> openAiSuggestions = await suggestionService.GetOpenAiSuggestionsAsync(
                    work,
                    userTagList);
                if (!string.IsNullOrWhiteSpace(suggestionService.LastOpenAiError))
                {
                    ConsoleOutput.Write(
                        $"OpenAI 判斷失敗: {suggestionService.LastOpenAiError}",
                        ConsoleColor.DarkYellow);
                }

                if (openAiSuggestions.Count > 0)
                {
                    DisplaySuggestions("OpenAI 建議標籤", openAiSuggestions, false);
                    return PromptForTags(work, openAiSuggestions, false);
                }
            }

            List<TagSuggestion> lexicalSuggestions = GetLexicalSuggestions(
                work.Tags,
                userTagList,
                settings?.MaxSuggestions ?? 5);
            if (lexicalSuggestions.Count > 0)
                DisplaySuggestions("字串比對建議標籤", lexicalSuggestions, false);
            return PromptForTags(work, lexicalSuggestions, false);
        }

        public Task UpdateIndexAsync(Work work, IEnumerable<string> assignedTags)
        {
            return suggestionService == null
                ? Task.CompletedTask
                : suggestionService.UpdateIndexAsync(work, assignedTags);
        }

        public void Dispose()
        {
            openAiClient?.Dispose();
            embeddingClient?.Dispose();
        }

        private static void DisplaySuggestions(
            string heading,
            List<TagSuggestion> suggestions,
            bool allowOpenAi)
        {
            ConsoleOutput.Write($"{heading}:", ConsoleColor.Cyan);
            for (int index = 0; index < suggestions.Count; index++)
            {
                TagSuggestion suggestion = suggestions[index];
                string details = string.Join(" | ", new[]
                {
                    suggestion.Evidence,
                    suggestion.Reason,
                    $"{suggestion.Score:P0}"
                }.Where((value) => !string.IsNullOrWhiteSpace(value)));
                ConsoleOutput.Write($"  #{index + 1} {suggestion.Tag} ({details})", ConsoleColor.DarkCyan);
            }

            if (allowOpenAi)
                ConsoleOutput.Write("  #AI 改用 OpenAI 判斷", ConsoleColor.DarkCyan);
        }

        private static TagInputParseResult PromptForTags(
            Work work,
            List<TagSuggestion> suggestions,
            bool allowOpenAi)
        {
            string suggestionHint = suggestions.Count > 0 ? "建議編號（如 #1）、" : "";
            string openAiHint = allowOpenAi ? "#AI、" : "";

            while (true)
            {
                ConsoleOutput.Write(
                    $"[{string.Join(" ", work.Tags ?? new List<string>())}](輸入{suggestionHint}{openAiHint}標籤，\"-\" 取消收藏): ",
                    newLine: false);
                string input = Console.ReadLine()?.Trim() ?? "";
                TagInputParseResult result = TagInputParser.Parse(input, suggestions, allowOpenAi);
                if (result.IsValid) return result;
                ConsoleOutput.Write(result.Error, ConsoleColor.Red);
            }
        }

        private static List<TagSuggestion> GetLexicalSuggestions(
            IEnumerable<string> sourceTags,
            List<string> userTagList,
            int maxSuggestions = 5)
        {
            if (sourceTags == null || userTagList == null || maxSuggestions <= 0)
                return new List<TagSuggestion>();

            List<string> tags = sourceTags.Where((tag) => !string.IsNullOrWhiteSpace(tag)).ToList();
            return userTagList
                .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                .Select((candidateTag) => CreateLexicalSuggestion(tags, candidateTag))
                .Where((suggestion) => suggestion.Score > 0.8)
                .GroupBy((suggestion) => NormalizeTag(suggestion.Tag))
                .Select((group) => group
                    .OrderByDescending((suggestion) => suggestion.IsExactMatch)
                    .ThenByDescending((suggestion) => suggestion.Score)
                    .First())
                .OrderByDescending((suggestion) => suggestion.Score)
                .ThenByDescending((suggestion) => NormalizeTag(suggestion.Tag).Length)
                .ThenBy((suggestion) => suggestion.Tag, StringComparer.OrdinalIgnoreCase)
                .Take(maxSuggestions)
                .ToList();
        }

        private static TagSuggestion CreateLexicalSuggestion(List<string> sourceTags, string candidateTag)
        {
            string matchedTag = null;
            double bestScore = 0;

            foreach (string sourceTag in sourceTags)
            {
                double score = GetTagSimilarity(sourceTag, candidateTag);
                if (score > bestScore)
                {
                    bestScore = score;
                    matchedTag = sourceTag;
                }
            }

            return new TagSuggestion
            {
                Tag = candidateTag,
                Evidence = matchedTag,
                Reason = "字串相似度比對",
                Score = bestScore,
                Source = TagSuggestionSource.Lexical,
                IsExactMatch = sourceTags.Any((sourceTag) => string.Equals(
                    sourceTag.Normalize(NormalizationForm.FormKC),
                    candidateTag.Normalize(NormalizationForm.FormKC),
                    StringComparison.OrdinalIgnoreCase))
            };
        }

        private static double GetTagSimilarity(string sourceTag, string candidateTag)
        {
            string source = NormalizeTag(sourceTag);
            string candidate = NormalizeTag(candidateTag);
            if (source.Length == 0 || candidate.Length == 0) return 0;
            if (source == candidate) return 1;

            Rune[] sourceCharacters = source.EnumerateRunes().ToArray();
            Rune[] candidateCharacters = candidate.EnumerateRunes().ToArray();

            bool hasNonAsciiCharacter = candidateCharacters.Any((character) => character.Value > 127);
            bool isLongAsciiPrefix = candidateCharacters.Length >= 4
                && source.StartsWith(candidate, StringComparison.Ordinal);
            bool isShortAcronymPrefix = IsShortAcronymPrefix(sourceTag, candidateTag);

            if ((hasNonAsciiCharacter && source.Contains(candidate)) || isLongAsciiPrefix || isShortAcronymPrefix)
            {
                double lengthRatio = Math.Min(sourceCharacters.Length, candidateCharacters.Length)
                    / (double)Math.Max(sourceCharacters.Length, candidateCharacters.Length);
                return 0.85 + (0.15 * lengthRatio);
            }

            if (!IsMeaningfulTagLength(sourceCharacters) || !IsMeaningfulTagLength(candidateCharacters))
                return 0;

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
                    normalized.Append(character.ToString());
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
            return value.Length >= 2 && value.Any((character) => character.Value > 127);
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
    }
}
