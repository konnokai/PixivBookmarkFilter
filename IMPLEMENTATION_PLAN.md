# RAG Tag Suggestion Implementation Plan

## Goal

Replace the lexical-only tag suggestions with a personalized, layered recommendation flow:

1. Automatically use exact matches between Pixiv tags and the user's bookmark tags.
2. Search previously categorized bookmarks with an Embedding model and `text-embedding-bge-m3`.
3. Show at most five RAG suggestions plus a separate `#AI` action.
4. Call OpenAI only when RAG has no result or the user explicitly enters `#AI`.
5. Fall back to the existing lexical matcher when OpenAI has no valid result.

All generated suggestions must be validated against the latest `userTagList`.

## Configuration

- Read Embedding model and OpenAI settings from `RagSettings.json`.
- Read the OpenAI API Key from the same settings file.
- Read a configurable OpenAI-compatible API BaseAddress from `openAiBaseUrl`.
- Exclude `RagSettings.json` from source control.
- Commit `RagSettings.example.json` without a key.
- Default Embedding model endpoint: `http://localhost:1234/v1`.
- Default embedding model: `text-embedding-bge-m3`.
- Default OpenAI model: `gpt-5.6-luna`.

## RAG Index

1. Persist the newest observed bookmark work ID as the incremental synchronization anchor.
2. Verify the configured Embedding model `/embeddings` endpoint with a minimal request before fetching any bookmark history.
3. Skip bookmark history retrieval and fall back to OpenAI or lexical matching when the embedding endpoint is unavailable.
4. On startup, fetch bookmark pages from newest to oldest and stop when the anchor or an already indexed work is reached.
5. Fetch all public bookmark pages only for the initial build or when no synchronization boundary can be found.
6. Use the work title and original Pixiv tags as embedding input.
7. Use existing bookmark tags as recommendation labels, never as embedding input.
8. Store metadata and float vectors in a local SQLite cache and upsert only the incremental result set.
9. Re-embed only new or changed works.
10. Remove stale cache entries after a complete scan and invalidate vectors plus the synchronization anchor when the embedding model changes.
11. Load vectors into memory and search with cosine similarity.
12. Rank labels with similarity-weighted voting and return at most five suggestions.

RAG is considered to have no result when no retrieved work reaches `minimumSimilarity`.

## Interaction

When RAG returns suggestions, display up to five numbered choices and one additional action:

```text
RAG 建議標籤：
  #1 Fate
  #2 FGO
  #AI 改用 OpenAI 判斷
```

- `#1` through `#5` select current suggestions.
- Multiple suggestion numbers may be selected together.
- `#AI` must be entered alone and does not count toward the five RAG choices.
- Manual tag input and `-` bookmark removal remain available.

## OpenAI Fallback

OpenAI is called only when RAG has no result or the user enters `#AI`.

The request contains only:

- Work title.
- Original Pixiv tags.
- Current `userTagList`.

OpenAI is called through `POST /v1/chat/completions` with Chat Structured Outputs. It returns at most five structured suggestions containing tag, confidence, reason, and evidence. The program rejects unknown tags, duplicates, malformed confidence values, and invalid responses.

If OpenAI is disabled, unavailable, or returns no valid suggestions, use the existing lexical matcher. If all methods fail, keep manual input available.

## Index Learning

After the user categorizes a work, immediately add or update it in the local index so later works in the same run can use the new example.

## Verification

1. Test cosine similarity and weighted label ranking.
2. Test the five-result RAG limit and user-tag filtering.
3. Test `#AI` as a standalone action.
4. Test automatic OpenAI fallback when RAG has no result.
5. Test OpenAI structured output validation.
6. Test lexical fallback when OpenAI fails.
7. Test SQLite insert, incremental update, synchronization anchor persistence, reload, stale removal, and model invalidation.
8. Run `dotnet test` and `dotnet build -c Release`.
9. Run an Embedding model smoke test when the configured service is available.
