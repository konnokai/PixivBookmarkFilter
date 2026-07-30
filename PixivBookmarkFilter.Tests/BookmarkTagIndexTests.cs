using PixivBookmarkFilter;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PixivBookmarkFilter.Tests
{
    public class BookmarkTagIndexTests
    {
        [Fact]
        public async Task Synchronize_PersistsRemovesStaleAndInvalidatesModel()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PixivBookmarkFilterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "index.db");
            FakeEmbeddingClient embeddingClient = new FakeEmbeddingClient();

            try
            {
                BookmarkTagIndex index = new BookmarkTagIndex(databasePath, "model-a");
                await index.InitializeAsync();
                await index.SynchronizeAsync(new[]
                {
                    CreateInput("1", "Fate work", "Fate"),
                    CreateInput("2", "Miku work", "VOCALOID")
                }, embeddingClient);
                Assert.Equal(2, index.Count);

                BookmarkTagIndex reloaded = new BookmarkTagIndex(databasePath, "model-a");
                await reloaded.InitializeAsync();
                Assert.Equal(2, reloaded.Count);

                await reloaded.SynchronizeAsync(new[] { CreateInput("1", "Fate work", "FGO") }, embeddingClient);
                Assert.Equal(1, reloaded.Count);
                Assert.Equal("FGO", Assert.Single(reloaded.Search(new[] { 1f, 0f }, null, 10, -1)).Document.AssignedTags.Single());
                await reloaded.SetSyncAnchorAsync("1");

                BookmarkTagIndex invalidated = new BookmarkTagIndex(databasePath, "model-b");
                await invalidated.InitializeAsync();
                Assert.Equal(0, invalidated.Count);
                Assert.Null(invalidated.SyncAnchorWorkId);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public async Task IncrementalSynchronize_PreservesExistingDocumentsAndPersistsAnchor()
        {
            string directory = Path.Combine(Path.GetTempPath(), "PixivBookmarkFilterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "index.db");
            FakeEmbeddingClient embeddingClient = new FakeEmbeddingClient();

            try
            {
                BookmarkTagIndex index = new BookmarkTagIndex(databasePath, "model-a");
                await index.InitializeAsync();
                await index.SynchronizeAsync(new[]
                {
                    CreateInput("1", "Fate work", "Fate"),
                    CreateInput("2", "Miku work", "VOCALOID")
                }, embeddingClient);
                Assert.Equal(2, embeddingClient.EmbeddedInputCount);

                await index.SynchronizeIncrementalAsync(new[]
                {
                    CreateInput("1", "Fate work", "FGO"),
                    CreateInput("3", "New work", "Original")
                }, embeddingClient);
                await index.SetSyncAnchorAsync("3");

                Assert.Equal(3, index.Count);
                Assert.Equal(3, embeddingClient.EmbeddedInputCount);
                Assert.Equal("FGO", index.Search(new[] { 1f, 0f }, null, 10, -1)
                    .Single((item) => item.Document.WorkId == "1")
                    .Document.AssignedTags.Single());

                BookmarkTagIndex reloaded = new BookmarkTagIndex(databasePath, "model-a");
                await reloaded.InitializeAsync();
                Assert.Equal(3, reloaded.Count);
                Assert.Equal("3", reloaded.SyncAnchorWorkId);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(directory, true);
            }
        }

        private static BookmarkTagDocumentInput CreateInput(string id, string title, string assignedTag)
        {
            return new BookmarkTagDocumentInput
            {
                WorkId = id,
                Title = title,
                SourceTags = new List<string> { title },
                AssignedTags = new List<string> { assignedTag }
            };
        }

        private sealed class FakeEmbeddingClient : IEmbeddingClient
        {
            public int EmbeddedInputCount { get; private set; }

            public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
                IReadOnlyList<string> inputs,
                CancellationToken cancellationToken = default)
            {
                EmbeddedInputCount += inputs.Count;
                IReadOnlyList<float[]> results = inputs
                    .Select((input) => input.Contains("Fate", StringComparison.OrdinalIgnoreCase)
                        ? new[] { 1f, 0f }
                        : new[] { 0f, 1f })
                    .ToList();
                return Task.FromResult(results);
            }
        }
    }
}
