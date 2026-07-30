using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal interface IEmbeddingClient
    {
        Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
    }

    internal sealed class EmbeddingModelClient : IEmbeddingClient, IDisposable
    {
        private readonly HttpClient httpClient;
        private readonly string model;

        public EmbeddingModelClient(RagSettings settings, HttpMessageHandler handler = null)
        {
            string baseUrl = settings.EmbeddingModelBaseUrl.TrimEnd('/') + "/";
            httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
            httpClient.BaseAddress = new Uri(baseUrl);
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            model = settings.EmbeddingModel;
        }

        public async Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken = default)
        {
            if (inputs == null || inputs.Count == 0) return Array.Empty<float[]>();

            string requestJson = JsonConvert.SerializeObject(new EmbeddingRequest
            {
                Model = model,
                Input = inputs
            });

            using var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await httpClient.PostAsync("embeddings", content, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            EmbeddingResponse result = JsonConvert.DeserializeObject<EmbeddingResponse>(responseJson);
            if (result?.Data == null || result.Data.Count != inputs.Count)
                throw new InvalidOperationException("Embedding 模型回傳數量不符");

            List<float[]> embeddings = result.Data
                .OrderBy((item) => item.Index)
                .Select((item) => item.Embedding)
                .ToList();

            if (embeddings.Any((embedding) => embedding == null || embedding.Length == 0))
                throw new InvalidOperationException("Embedding 模型回傳空白 Embedding");
            if (embeddings.Select((embedding) => embedding.Length).Distinct().Count() != 1)
                throw new InvalidOperationException("Embedding 模型回傳的 Embedding 維度不一致");

            return embeddings;
        }

        public async Task<int> VerifyAvailabilityAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<float[]> embeddings = await GenerateEmbeddingsAsync(
                new[] { "Embedding endpoint availability check" },
                cancellationToken);
            return embeddings[0].Length;
        }

        public void Dispose()
        {
            httpClient.Dispose();
        }

        private sealed class EmbeddingRequest
        {
            [JsonProperty("model")]
            public string Model { get; set; }

            [JsonProperty("input")]
            public IReadOnlyList<string> Input { get; set; }
        }

        private sealed class EmbeddingResponse
        {
            [JsonProperty("data")]
            public List<EmbeddingData> Data { get; set; }
        }

        private sealed class EmbeddingData
        {
            [JsonProperty("index")]
            public int Index { get; set; }

            [JsonProperty("embedding")]
            public float[] Embedding { get; set; }
        }
    }
}
