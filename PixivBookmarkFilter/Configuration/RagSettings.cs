using Newtonsoft.Json;
using System;
using System.IO;

namespace PixivBookmarkFilter
{
    internal sealed class RagSettings
    {
        public string LmStudioBaseUrl { get; set; } = "http://localhost:1234/v1";
        public string EmbeddingModel { get; set; } = "text-embedding-bge-m3";
        public int RetrievalTopK { get; set; } = 20;
        public double MinimumSimilarity { get; set; } = 0.6;
        public bool OpenAiEnabled { get; set; } = true;
        public string OpenAiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string OpenAiApiKey { get; set; } = "";
        public string OpenAiModel { get; set; } = "gpt-5.6-luna";
        public string OpenAiReasoningEffort { get; set; } = "low";
        public int MaxSuggestions { get; set; } = 5;

        public static RagSettings Load()
        {
            string settingsPath = FindSettingsPath();
            if (settingsPath == null) return new RagSettings();

            RagSettings settings = JsonConvert.DeserializeObject<RagSettings>(File.ReadAllText(settingsPath)) ?? new RagSettings();
            settings.Validate();
            return settings;
        }

        public string GetIndexPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TagSuggestionIndex.db");
        }

        private static string FindSettingsPath()
        {
            string baseDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RagSettings.json");
            if (File.Exists(baseDirectoryPath)) return baseDirectoryPath;

            string currentDirectoryPath = Path.Combine(Directory.GetCurrentDirectory(), "RagSettings.json");
            return File.Exists(currentDirectoryPath) ? currentDirectoryPath : null;
        }

        private void Validate()
        {
            if (!Uri.TryCreate(LmStudioBaseUrl, UriKind.Absolute, out _))
                throw new InvalidDataException("RagSettings.json 的 lmStudioBaseUrl 格式錯誤");
            if (string.IsNullOrWhiteSpace(EmbeddingModel))
                throw new InvalidDataException("RagSettings.json 的 embeddingModel 不可為空");
            if (RetrievalTopK <= 0) RetrievalTopK = 20;
            if (MinimumSimilarity < -1 || MinimumSimilarity > 1)
                throw new InvalidDataException("RagSettings.json 的 minimumSimilarity 必須介於 -1 到 1");
            if (!Uri.TryCreate(OpenAiBaseUrl, UriKind.Absolute, out _))
                throw new InvalidDataException("RagSettings.json 的 openAiBaseUrl 格式錯誤");
            if (MaxSuggestions <= 0) MaxSuggestions = 5;
            MaxSuggestions = Math.Min(MaxSuggestions, 5);
            if (string.IsNullOrWhiteSpace(OpenAiModel)) OpenAiModel = "gpt-5.6-luna";
        }
    }
}
