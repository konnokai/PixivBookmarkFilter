using PixivBookmarkFilter;
using Newtonsoft.Json.Linq;
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
    public class TagSuggestionTests
    {
        [Fact]
        public void CosineSimilarity_OrdersEquivalentVectorsFirst()
        {
            double equivalent = BookmarkTagIndex.CosineSimilarity(new[] { 1f, 0f }, new[] { 2f, 0f });
            double unrelated = BookmarkTagIndex.CosineSimilarity(new[] { 1f, 0f }, new[] { 0f, 1f });

            Assert.Equal(1, equivalent, 6);
            Assert.Equal(0, unrelated, 6);
        }

        [Fact]
        public void RankTags_UsesWeightedVotesFiltersTagsAndLimitsResults()
        {
            List<RetrievedBookmark> retrieved = new List<RetrievedBookmark>
            {
                CreateRetrieved("A", 0.9, "Fate", "Unknown", "FGO", "Tag3", "Tag4", "Tag5", "Tag6"),
                CreateRetrieved("B", 0.8, "Fate")
            };

            List<TagSuggestion> suggestions = RagTagSuggestionService.RankTags(
                retrieved,
                new[] { "Fate", "FGO", "Tag3", "Tag4", "Tag5", "Tag6" },
                5);

            Assert.Equal(5, suggestions.Count);
            Assert.Equal("Fate", suggestions[0].Tag);
            Assert.DoesNotContain(suggestions, (suggestion) => suggestion.Tag == "Unknown");
        }

        [Fact]
        public void InputParser_RequiresOpenAiActionToBeStandalone()
        {
            List<TagSuggestion> suggestions = new List<TagSuggestion>
            {
                new TagSuggestion { Tag = "Fate" }
            };

            TagInputParseResult invalid = TagInputParser.Parse("#1 #AI", suggestions, true);
            TagInputParseResult valid = TagInputParser.Parse("#AI", suggestions, true);

            Assert.False(invalid.IsValid);
            Assert.Equal("#AI 必須單獨輸入", invalid.Error);
            Assert.True(valid.IsValid);
            Assert.True(valid.UseOpenAi);
        }

        [Fact]
        public void OpenAiValidation_RejectsUnknownAndDuplicateTags()
        {
            List<OpenAiTagSuggestionClient.OpenAiSuggestion> response = new List<OpenAiTagSuggestionClient.OpenAiSuggestion>
            {
                new OpenAiTagSuggestionClient.OpenAiSuggestion { Tag = "fate", Confidence = 1.2, Reason = "r1", Evidence = "e1" },
                new OpenAiTagSuggestionClient.OpenAiSuggestion { Tag = "Fate", Confidence = 0.8, Reason = "r2", Evidence = "e2" },
                new OpenAiTagSuggestionClient.OpenAiSuggestion { Tag = "Unknown", Confidence = 0.9, Reason = "r3", Evidence = "e3" }
            };

            List<TagSuggestion> suggestions = OpenAiTagSuggestionClient.ValidateSuggestions(response, new[] { "Fate" }, 5);

            TagSuggestion suggestion = Assert.Single(suggestions);
            Assert.Equal("Fate", suggestion.Tag);
            Assert.Equal(1, suggestion.Score);
        }

        [Fact]
        public void OpenAiResponse_ExtractsStructuredOutputText()
        {
            string response = """
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"suggestions\":[]}"
                  }
                }
              ]
            }
            """;

            Assert.Equal("{\"suggestions\":[]}", OpenAiTagSuggestionClient.ExtractOutputText(response));
        }

        [Fact]
        public async Task OpenAiClient_UsesCustomChatCompletionsApiAndStructuredOutput()
        {
            RecordingHandler handler = new RecordingHandler("""
            {
              "choices": [
                {
                  "message": {
                    "content": "{\"suggestions\":[{\"tag\":\"Fate\",\"confidence\":0.9,\"reason\":\"人物關聯\",\"evidence\":\"セイバー\"}]}"
                  }
                }
              ]
            }
            """);
            RagSettings settings = new RagSettings
            {
                OpenAiEnabled = true,
                OpenAiBaseUrl = "https://example.test/openai/v1",
                OpenAiApiKey = "test-key",
                OpenAiModel = "gpt-5.6-luna"
            };

            using OpenAiTagSuggestionClient client = new OpenAiTagSuggestionClient(settings, handler);
            List<TagSuggestion> suggestions = await client.GetSuggestionsAsync(
                new Work { Title = "騎士王", Tags = new List<string> { "セイバー" } },
                new[] { "Fate" });

            Assert.Equal("https://example.test/openai/v1/chat/completions", handler.RequestUri.ToString());
            Assert.Equal("Bearer", handler.AuthorizationScheme);
            Assert.Equal("test-key", handler.AuthorizationParameter);
            JObject request = JObject.Parse(handler.RequestBody);
            Assert.Equal("gpt-5.6-luna", request.Value<string>("model"));
            Assert.False(request.Value<bool>("store"));
            Assert.Equal("system", request["messages"]?[0]?.Value<string>("role"));
            Assert.Equal("user", request["messages"]?[1]?.Value<string>("role"));
            Assert.Equal("json_schema", request["response_format"]?.Value<string>("type"));
            Assert.NotNull(request["response_format"]?["json_schema"]?["schema"]);
            Assert.Equal("Fate", Assert.Single(suggestions).Tag);
        }

        [Fact]
        public async Task EmbeddingAvailabilityCheck_RequestsEmbeddingAndReturnsDimension()
        {
            RecordingHandler handler = new RecordingHandler("""
            {
              "data": [
                {
                  "index": 0,
                  "embedding": [0.1, 0.2, 0.3]
                }
              ]
            }
            """);
            RagSettings settings = new RagSettings
            {
                EmbeddingModelBaseUrl = "http://localhost:1234/v1",
                EmbeddingModel = "text-embedding-bge-m3"
            };

            using EmbeddingModelClient client = new EmbeddingModelClient(settings, handler);
            int dimension = await client.VerifyAvailabilityAsync();

            Assert.Equal(3, dimension);
            Assert.Equal("http://localhost:1234/v1/embeddings", handler.RequestUri.ToString());
            JObject request = JObject.Parse(handler.RequestBody);
            Assert.Equal("text-embedding-bge-m3", request.Value<string>("model"));
            Assert.Single(request["input"]);
        }

        [Fact]
        public async Task EmbeddingClient_SendsBearerTokenOnlyWhenApiKeyIsSet()
        {
            const string responseBody = """{"data":[{"index":0,"embedding":[0.1]}]}""";

            RecordingHandler withKey = new RecordingHandler(responseBody);
            using (EmbeddingModelClient client = new EmbeddingModelClient(new RagSettings { EmbeddingModelApiKey = "omlx-key" }, withKey))
                await client.VerifyAvailabilityAsync();
            Assert.Equal("Bearer", withKey.AuthorizationScheme);
            Assert.Equal("omlx-key", withKey.AuthorizationParameter);

            RecordingHandler withoutKey = new RecordingHandler(responseBody);
            using (EmbeddingModelClient client = new EmbeddingModelClient(new RagSettings(), withoutKey))
                await client.VerifyAvailabilityAsync();
            Assert.Null(withoutKey.AuthorizationScheme);
        }

        private static RetrievedBookmark CreateRetrieved(string title, double similarity, params string[] tags)
        {
            return new RetrievedBookmark
            {
                Similarity = similarity,
                Document = new IndexedBookmark
                {
                    WorkId = Guid.NewGuid().ToString(),
                    Title = title,
                    AssignedTags = tags.ToList(),
                    SourceTags = new List<string>(),
                    Embedding = new[] { 1f }
                }
            };
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly string responseBody;

            public Uri RequestUri { get; private set; }
            public string RequestBody { get; private set; }
            public string AuthorizationScheme { get; private set; }
            public string AuthorizationParameter { get; private set; }

            public RecordingHandler(string responseBody)
            {
                this.responseBody = responseBody;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestUri = request.RequestUri;
                RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                AuthorizationScheme = request.Headers.Authorization?.Scheme;
                AuthorizationParameter = request.Headers.Authorization?.Parameter;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
