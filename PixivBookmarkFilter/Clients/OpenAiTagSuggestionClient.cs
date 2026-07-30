using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal interface IOpenAiTagSuggestionClient
    {
        bool IsConfigured { get; }
        Task<List<TagSuggestion>> GetSuggestionsAsync(Work work, IReadOnlyList<string> userTagList, CancellationToken cancellationToken = default);
    }

    internal sealed class OpenAiTagSuggestionClient : IOpenAiTagSuggestionClient, IDisposable
    {
        private const int MaxAttempts = 3;

        private readonly RagSettings settings;
        private readonly HttpClient httpClient;

        public bool IsConfigured { get; }

        public OpenAiTagSuggestionClient(RagSettings settings, HttpMessageHandler handler = null)
        {
            this.settings = settings;
            IsConfigured = settings.OpenAiEnabled && !string.IsNullOrWhiteSpace(settings.OpenAiApiKey);
            if (!IsConfigured) return;

            httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
            httpClient.BaseAddress = new Uri(settings.OpenAiBaseUrl.TrimEnd('/') + "/");
            httpClient.Timeout = TimeSpan.FromMinutes(2);
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.OpenAiApiKey);
        }

        public async Task<List<TagSuggestion>> GetSuggestionsAsync(Work work, IReadOnlyList<string> userTagList, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured || work == null || userTagList == null || userTagList.Count == 0)
                return new List<TagSuggestion>();

            string requestJson = CreateRequestJson(work, userTagList);
            string responseJson = await SendRequestAsync(requestJson, cancellationToken);
            string outputText = ExtractOutputText(responseJson);
            if (string.IsNullOrWhiteSpace(outputText)) return new List<TagSuggestion>();

            OpenAiSuggestionResponse response = JsonConvert.DeserializeObject<OpenAiSuggestionResponse>(outputText);
            return ValidateSuggestions(response?.Suggestions, userTagList, settings.MaxSuggestions);
        }

        internal static List<TagSuggestion> ValidateSuggestions(
            IEnumerable<OpenAiSuggestion> suggestions,
            IReadOnlyList<string> userTagList,
            int maxSuggestions)
        {
            if (suggestions == null || userTagList == null) return new List<TagSuggestion>();

            Dictionary<string, string> validTags = userTagList
                .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                .GroupBy((tag) => tag, StringComparer.OrdinalIgnoreCase)
                .ToDictionary((group) => group.Key, (group) => group.First(), StringComparer.OrdinalIgnoreCase);

            List<TagSuggestion> results = new List<TagSuggestion>();
            HashSet<string> usedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenAiSuggestion suggestion in suggestions)
            {
                if (suggestion == null || !validTags.TryGetValue(suggestion.Tag ?? "", out string validTag)) continue;
                if (!usedTags.Add(validTag)) continue;
                if (double.IsNaN(suggestion.Confidence) || double.IsInfinity(suggestion.Confidence)) continue;

                results.Add(new TagSuggestion
                {
                    Tag = validTag,
                    Score = Math.Clamp(suggestion.Confidence, 0, 1),
                    Reason = suggestion.Reason?.Trim() ?? "",
                    Evidence = suggestion.Evidence?.Trim() ?? "",
                    Source = TagSuggestionSource.OpenAi
                });

                if (results.Count >= Math.Min(maxSuggestions, 5)) break;
            }

            return results;
        }

        internal static string ExtractOutputText(string responseJson)
        {
            JObject response = JObject.Parse(responseJson);
            JToken content = response["choices"]?.FirstOrDefault()?["message"]?["content"];
            if (content?.Type == JTokenType.String) return content.Value<string>();

            return content?
                .Children<JObject>()
                .Where((part) => string.Equals(part.Value<string>("type"), "text", StringComparison.Ordinal)
                    || string.Equals(part.Value<string>("type"), "output_text", StringComparison.Ordinal))
                .Select((part) => part.Value<string>("text"))
                .FirstOrDefault((text) => !string.IsNullOrWhiteSpace(text));
        }

        public void Dispose()
        {
            httpClient?.Dispose();
        }

        private async Task<string> SendRequestAsync(string requestJson, CancellationToken cancellationToken)
        {
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using HttpResponseMessage response = await httpClient.PostAsync("chat/completions", content, cancellationToken);
                string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

                if (response.IsSuccessStatusCode) return responseJson;
                if (attempt == MaxAttempts || !ShouldRetry(response.StatusCode))
                {
                    string errorMessage = TryGetErrorMessage(responseJson);
                    throw new HttpRequestException(
                        $"OpenAI API {(int)response.StatusCode}: {errorMessage ?? response.ReasonPhrase}",
                        null,
                        response.StatusCode);
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), cancellationToken);
            }

            throw new InvalidOperationException("OpenAI API 請求失敗");
        }

        private string CreateRequestJson(Work work, IReadOnlyList<string> userTagList)
        {
            JObject schema = JObject.Parse(CreateResponseSchema(settings.MaxSuggestions));
            return JsonConvert.SerializeObject(new
            {
                model = settings.OpenAiModel,
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content =
                            "你是 Pixiv 收藏標籤分類器。請根據作品標題與 Pixiv 原始標籤，理解人物、作品系列、別名、縮寫與翻譯關係。" +
                            "只能從可選標籤中選擇，不可建立新標籤。最多回傳指定數量，沒有合理結果時回傳空陣列。理由與依據請使用繁體中文。"
                    },
                    new
                    {
                        role = "user",
                        content = CreatePrompt(work, userTagList, settings.MaxSuggestions)
                    }
                },
                store = false,
                max_completion_tokens = 800,
                reasoning_effort = settings.OpenAiReasoningEffort,
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "pixiv_tag_suggestions",
                        description = "Select existing Pixiv bookmark tags for the current work.",
                        strict = true,
                        schema
                    }
                }
            });
        }

        private static string CreatePrompt(Work work, IReadOnlyList<string> userTagList, int maxSuggestions)
        {
            StringBuilder prompt = new StringBuilder();
            prompt.AppendLine($"最多選擇 {Math.Min(maxSuggestions, 5)} 個標籤。");
            prompt.AppendLine($"作品標題：{work.Title}");
            prompt.AppendLine($"Pixiv 原始標籤：{string.Join("、", work.Tags ?? new List<string>())}");
            prompt.AppendLine("可選標籤：");
            foreach (string tag in userTagList.Where((tag) => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase))
                prompt.AppendLine($"- {tag}");
            return prompt.ToString();
        }

        private static string CreateResponseSchema(int maxSuggestions)
        {
            return JsonConvert.SerializeObject(new
            {
                type = "object",
                properties = new
                {
                    suggestions = new
                    {
                        type = "array",
                        maxItems = Math.Min(maxSuggestions, 5),
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                tag = new { type = "string" },
                                confidence = new { type = "number", minimum = 0, maximum = 1 },
                                reason = new { type = "string" },
                                evidence = new { type = "string" }
                            },
                            required = new[] { "tag", "confidence", "reason", "evidence" },
                            additionalProperties = false
                        }
                    }
                },
                required = new[] { "suggestions" },
                additionalProperties = false
            });
        }

        private static bool ShouldRetry(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout
                || statusCode == HttpStatusCode.TooManyRequests
                || (int)statusCode >= 500;
        }

        private static string TryGetErrorMessage(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson)) return null;
            try
            {
                return JObject.Parse(responseJson)["error"]?.Value<string>("message");
            }
            catch (JsonException)
            {
                return responseJson.Length <= 200 ? responseJson : responseJson.Substring(0, 200);
            }
        }

        internal sealed class OpenAiSuggestionResponse
        {
            [JsonProperty("suggestions")]
            public List<OpenAiSuggestion> Suggestions { get; set; }
        }

        internal sealed class OpenAiSuggestion
        {
            [JsonProperty("tag")]
            public string Tag { get; set; }

            [JsonProperty("confidence")]
            public double Confidence { get; set; }

            [JsonProperty("reason")]
            public string Reason { get; set; }

            [JsonProperty("evidence")]
            public string Evidence { get; set; }
        }
    }
}
