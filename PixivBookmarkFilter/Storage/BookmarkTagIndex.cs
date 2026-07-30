using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PixivBookmarkFilter
{
    internal sealed class BookmarkTagIndex
    {
        private const int EmbeddingBatchSize = 16;
        private const string SyncAnchorMetadataKey = "sync_anchor_work_id";

        private readonly string connectionString;
        private readonly string embeddingModel;
        private readonly Dictionary<string, IndexedBookmark> documents = new Dictionary<string, IndexedBookmark>(StringComparer.Ordinal);

        public int Count => documents.Count;
        public IReadOnlyCollection<string> WorkIds => documents.Keys.ToArray();
        public string SyncAnchorWorkId { get; private set; }

        public BookmarkTagIndex(string databasePath, string embeddingModel)
        {
            string directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            this.embeddingModel = embeddingModel;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
PRAGMA journal_mode = WAL;
CREATE TABLE IF NOT EXISTS metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS documents (
    work_id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    source_tags TEXT NOT NULL,
    assigned_tags TEXT NOT NULL,
    content_hash TEXT NOT NULL,
    embedding BLOB NOT NULL,
    dimension INTEGER NOT NULL
);";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            string storedModel = await GetMetadataAsync(connection, "embedding_model", cancellationToken);
            if (!string.Equals(storedModel, embeddingModel, StringComparison.Ordinal))
            {
                using SqliteTransaction transaction = connection.BeginTransaction();
                using (SqliteCommand clearCommand = connection.CreateCommand())
                {
                    clearCommand.Transaction = transaction;
                    clearCommand.CommandText = "DELETE FROM documents;";
                    await clearCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                using (SqliteCommand clearAnchorCommand = connection.CreateCommand())
                {
                    clearAnchorCommand.Transaction = transaction;
                    clearAnchorCommand.CommandText = "DELETE FROM metadata WHERE key = $key;";
                    clearAnchorCommand.Parameters.AddWithValue("$key", SyncAnchorMetadataKey);
                    await clearAnchorCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await SetMetadataAsync(connection, transaction, "embedding_model", embeddingModel, cancellationToken);
                transaction.Commit();
            }

            SyncAnchorWorkId = await GetMetadataAsync(connection, SyncAnchorMetadataKey, cancellationToken);
            await LoadDocumentsAsync(connection, cancellationToken);
        }

        public async Task SynchronizeAsync(
            IEnumerable<BookmarkTagDocumentInput> sourceDocuments,
            IEmbeddingClient embeddingClient,
            Action<int, int> reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, IndexedBookmark> synchronizedDocuments = await PrepareDocumentsAsync(
                sourceDocuments,
                embeddingClient,
                reportProgress,
                cancellationToken);
            await ReplaceDocumentsAsync(synchronizedDocuments, cancellationToken);
        }

        public async Task SynchronizeIncrementalAsync(
            IEnumerable<BookmarkTagDocumentInput> sourceDocuments,
            IEmbeddingClient embeddingClient,
            Action<int, int> reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            Dictionary<string, IndexedBookmark> updatedDocuments = await PrepareDocumentsAsync(
                sourceDocuments,
                embeddingClient,
                reportProgress,
                cancellationToken);
            await UpsertDocumentsAsync(updatedDocuments.Values, cancellationToken);
            foreach (KeyValuePair<string, IndexedBookmark> item in updatedDocuments)
                documents[item.Key] = item.Value;
        }

        public async Task SetSyncAnchorAsync(string workId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(workId)) return;

            workId = workId.Trim();
            using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            using SqliteTransaction transaction = connection.BeginTransaction();
            await SetMetadataAsync(connection, transaction, SyncAnchorMetadataKey, workId, cancellationToken);
            transaction.Commit();
            SyncAnchorWorkId = workId;
        }

        public async Task UpsertAsync(
            BookmarkTagDocumentInput input,
            IEmbeddingClient embeddingClient,
            CancellationToken cancellationToken = default)
        {
            if (!IsValidInput(input)) return;

            input = NormalizeInput(input);
            string embeddingText = CreateEmbeddingText(input.Title, input.SourceTags);
            string contentHash = ComputeHash(embeddingText);
            float[] embedding;

            if (documents.TryGetValue(input.WorkId, out IndexedBookmark existing)
                && string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal))
            {
                embedding = existing.Embedding;
            }
            else
            {
                embedding = (await embeddingClient.GenerateEmbeddingsAsync(new[] { embeddingText }, cancellationToken))[0];
            }

            IndexedBookmark document = CreateIndexedBookmark(input, contentHash, embedding);
            using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await UpsertDocumentAsync(connection, null, document, cancellationToken);
            documents[document.WorkId] = document;
        }

        public List<RetrievedBookmark> Search(float[] queryEmbedding, string excludedWorkId, int topK, double minimumSimilarity)
        {
            if (queryEmbedding == null || queryEmbedding.Length == 0 || topK <= 0)
                return new List<RetrievedBookmark>();

            return documents.Values
                .Where((document) => document.Embedding?.Length == queryEmbedding.Length)
                .Where((document) => !string.Equals(document.WorkId, excludedWorkId, StringComparison.Ordinal))
                .Select((document) => new RetrievedBookmark
                {
                    Document = document,
                    Similarity = CosineSimilarity(queryEmbedding, document.Embedding)
                })
                .Where((result) => result.Similarity >= minimumSimilarity)
                .OrderByDescending((result) => result.Similarity)
                .Take(topK)
                .ToList();
        }

        internal static string CreateEmbeddingText(string title, IEnumerable<string> sourceTags)
        {
            string normalizedTitle = title?.Trim() ?? "";
            string tags = string.Join(", ", (sourceTags ?? Array.Empty<string>())
                .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                .Select((tag) => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
            return $"Title: {normalizedTitle}\nPixiv tags: {tags}";
        }

        internal static double CosineSimilarity(float[] left, float[] right)
        {
            if (left == null || right == null || left.Length == 0 || left.Length != right.Length) return 0;

            double dotProduct = 0;
            double leftMagnitude = 0;
            double rightMagnitude = 0;
            for (int index = 0; index < left.Length; index++)
            {
                dotProduct += left[index] * right[index];
                leftMagnitude += left[index] * left[index];
                rightMagnitude += right[index] * right[index];
            }

            if (leftMagnitude == 0 || rightMagnitude == 0) return 0;
            return dotProduct / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
        }

        private async Task LoadDocumentsAsync(SqliteConnection connection, CancellationToken cancellationToken)
        {
            documents.Clear();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT work_id, title, source_tags, assigned_tags, content_hash, embedding, dimension FROM documents;";
            using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                byte[] embeddingBytes = (byte[])reader[5];
                int dimension = reader.GetInt32(6);
                float[] embedding = BytesToVector(embeddingBytes, dimension);
                IndexedBookmark document = new IndexedBookmark
                {
                    WorkId = reader.GetString(0),
                    Title = reader.GetString(1),
                    SourceTags = JsonConvert.DeserializeObject<List<string>>(reader.GetString(2)) ?? new List<string>(),
                    AssignedTags = JsonConvert.DeserializeObject<List<string>>(reader.GetString(3)) ?? new List<string>(),
                    ContentHash = reader.GetString(4),
                    Embedding = embedding
                };
                documents[document.WorkId] = document;
            }
        }

        private async Task<Dictionary<string, IndexedBookmark>> PrepareDocumentsAsync(
            IEnumerable<BookmarkTagDocumentInput> sourceDocuments,
            IEmbeddingClient embeddingClient,
            Action<int, int> reportProgress,
            CancellationToken cancellationToken)
        {
            List<BookmarkTagDocumentInput> inputs = (sourceDocuments ?? Array.Empty<BookmarkTagDocumentInput>())
                .Where(IsValidInput)
                .GroupBy((document) => document.WorkId, StringComparer.Ordinal)
                .Select((group) => NormalizeInput(group.Last()))
                .ToList();
            Dictionary<string, IndexedBookmark> preparedDocuments = new Dictionary<string, IndexedBookmark>(StringComparer.Ordinal);
            List<PendingEmbedding> pendingEmbeddings = new List<PendingEmbedding>();

            foreach (BookmarkTagDocumentInput input in inputs)
            {
                string embeddingText = CreateEmbeddingText(input.Title, input.SourceTags);
                string contentHash = ComputeHash(embeddingText);
                if (documents.TryGetValue(input.WorkId, out IndexedBookmark existing)
                    && string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal)
                    && existing.Embedding?.Length > 0)
                {
                    preparedDocuments[input.WorkId] = CreateIndexedBookmark(input, contentHash, existing.Embedding);
                }
                else
                {
                    pendingEmbeddings.Add(new PendingEmbedding
                    {
                        Input = input,
                        EmbeddingText = embeddingText,
                        ContentHash = contentHash
                    });
                }
            }

            for (int index = 0; index < pendingEmbeddings.Count; index += EmbeddingBatchSize)
            {
                List<PendingEmbedding> batch = pendingEmbeddings.Skip(index).Take(EmbeddingBatchSize).ToList();
                IReadOnlyList<float[]> embeddings = await embeddingClient.GenerateEmbeddingsAsync(
                    batch.Select((item) => item.EmbeddingText).ToList(),
                    cancellationToken);

                for (int batchIndex = 0; batchIndex < batch.Count; batchIndex++)
                {
                    PendingEmbedding pending = batch[batchIndex];
                    preparedDocuments[pending.Input.WorkId] = CreateIndexedBookmark(
                        pending.Input,
                        pending.ContentHash,
                        embeddings[batchIndex]);
                }

                reportProgress?.Invoke(Math.Min(index + batch.Count, pendingEmbeddings.Count), pendingEmbeddings.Count);
            }

            return preparedDocuments;
        }

        private async Task ReplaceDocumentsAsync(
            Dictionary<string, IndexedBookmark> synchronizedDocuments,
            CancellationToken cancellationToken)
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            using SqliteTransaction transaction = connection.BeginTransaction();

            using (SqliteCommand deleteCommand = connection.CreateCommand())
            {
                deleteCommand.Transaction = transaction;
                deleteCommand.CommandText = "DELETE FROM documents;";
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (IndexedBookmark document in synchronizedDocuments.Values)
                await UpsertDocumentAsync(connection, transaction, document, cancellationToken);

            transaction.Commit();
            documents.Clear();
            foreach (KeyValuePair<string, IndexedBookmark> item in synchronizedDocuments)
                documents[item.Key] = item.Value;
        }

        private async Task UpsertDocumentsAsync(
            IEnumerable<IndexedBookmark> updatedDocuments,
            CancellationToken cancellationToken)
        {
            using SqliteConnection connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            using SqliteTransaction transaction = connection.BeginTransaction();
            foreach (IndexedBookmark document in updatedDocuments)
                await UpsertDocumentAsync(connection, transaction, document, cancellationToken);
            transaction.Commit();
        }

        private static async Task UpsertDocumentAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            IndexedBookmark document,
            CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO documents (work_id, title, source_tags, assigned_tags, content_hash, embedding, dimension)
VALUES ($workId, $title, $sourceTags, $assignedTags, $contentHash, $embedding, $dimension)
ON CONFLICT(work_id) DO UPDATE SET
    title = excluded.title,
    source_tags = excluded.source_tags,
    assigned_tags = excluded.assigned_tags,
    content_hash = excluded.content_hash,
    embedding = excluded.embedding,
    dimension = excluded.dimension;";
            command.Parameters.AddWithValue("$workId", document.WorkId);
            command.Parameters.AddWithValue("$title", document.Title);
            command.Parameters.AddWithValue("$sourceTags", JsonConvert.SerializeObject(document.SourceTags));
            command.Parameters.AddWithValue("$assignedTags", JsonConvert.SerializeObject(document.AssignedTags));
            command.Parameters.AddWithValue("$contentHash", document.ContentHash);
            command.Parameters.AddWithValue("$embedding", VectorToBytes(document.Embedding));
            command.Parameters.AddWithValue("$dimension", document.Embedding.Length);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static async Task<string> GetMetadataAsync(
            SqliteConnection connection,
            string key,
            CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM metadata WHERE key = $key;";
            command.Parameters.AddWithValue("$key", key);
            object value = await command.ExecuteScalarAsync(cancellationToken);
            return value == null || value == DBNull.Value ? null : Convert.ToString(value);
        }

        private static async Task SetMetadataAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string key,
            string value,
            CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"
INSERT INTO metadata (key, value) VALUES ($key, $value)
ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static IndexedBookmark CreateIndexedBookmark(
            BookmarkTagDocumentInput input,
            string contentHash,
            float[] embedding)
        {
            return new IndexedBookmark
            {
                WorkId = input.WorkId,
                Title = input.Title,
                SourceTags = input.SourceTags,
                AssignedTags = input.AssignedTags,
                ContentHash = contentHash,
                Embedding = embedding
            };
        }

        private static BookmarkTagDocumentInput NormalizeInput(BookmarkTagDocumentInput input)
        {
            return new BookmarkTagDocumentInput
            {
                WorkId = input.WorkId.Trim(),
                Title = input.Title?.Trim() ?? "",
                SourceTags = input.SourceTags
                    .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                    .Select((tag) => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AssignedTags = input.AssignedTags
                    .Where((tag) => !string.IsNullOrWhiteSpace(tag))
                    .Select((tag) => tag.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private static bool IsValidInput(BookmarkTagDocumentInput input)
        {
            return input != null
                && !string.IsNullOrWhiteSpace(input.WorkId)
                && input.SourceTags != null
                && input.AssignedTags != null
                && input.AssignedTags.Any((tag) => !string.IsNullOrWhiteSpace(tag));
        }

        private static string ComputeHash(string value)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash);
        }

        private static byte[] VectorToBytes(float[] vector)
        {
            byte[] bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private static float[] BytesToVector(byte[] bytes, int dimension)
        {
            if (bytes == null || bytes.Length != dimension * sizeof(float))
                throw new InvalidDataException("SQLite 中的 Embedding 資料已損壞");

            float[] vector = new float[dimension];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return vector;
        }

        private sealed class PendingEmbedding
        {
            public BookmarkTagDocumentInput Input { get; set; }
            public string EmbeddingText { get; set; }
            public string ContentHash { get; set; }
        }
    }

    internal sealed class IndexedBookmark
    {
        public string WorkId { get; set; }
        public string Title { get; set; }
        public List<string> SourceTags { get; set; }
        public List<string> AssignedTags { get; set; }
        public string ContentHash { get; set; }
        public float[] Embedding { get; set; }
    }

    internal sealed class RetrievedBookmark
    {
        public IndexedBookmark Document { get; set; }
        public double Similarity { get; set; }
    }
}
