# Graph Report - PixivBookmarkFilter  (2026-07-30)

## Corpus Check
- 36 files · ~14,545 words
- Verdict: corpus is large enough that graph structure adds value.

## Summary
- 333 nodes · 600 edges · 55 communities (16 shown, 39 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 41 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `645ffccc`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- OpenAI Tag Suggestions
- Bookmark Tag Index
- Bookmark Processing API
- RAG Tag Suggestions
- Project Composition
- LM Studio Embeddings
- Illustration Image Downloads
- Build Dependencies
- Recommendation Design Plan
- JSON.NET Documentation
- Console Progress Reporting
- Cookie Encryption
- HTML Parsing Library
- RAG Configuration
- OpenCode Plugin Config
- Newtonsoft Package Artwork
- TagSuggestionTests
- .GetRagSuggestionsAsync
- Cosine Similarity Search
- Exact Tag Matching
- Lexical Matcher Fallback
- LM Studio
- minimumSimilarity Result Gate
- Immediate Index Learning
- OpenAI Structured Suggestions
- Personalized Layered Tag Recommendation
- Pixiv Bookmark RAG Index
- RAG Numbered Choices and #AI Action
- RAG Retrieval
- RagSettings.json Configuration
- Similarity-Weighted Label Voting
- SQLite Vector Cache
- text-embedding-bge-m3
- Latest userTagList Validation
- Malformed HTML Tolerance
- Read/Write HTML DOM
- System.Xml Object Model
- XPath
- XSLT
- ZZZ Projects
- Copyright Notice Inclusion Condition
- James Newton-King
- MIT License
- Software Freedom Permissions
- Warranty and Liability Disclaimer
- JArray
- JObject
- JSON Deserialization
- Json.NET
- JSON Serialization
- JsonConvert.DeserializeObject
- JsonConvert.SerializeObject
- Newtonsoft.Json NuGet Package

## God Nodes (most connected - your core abstractions)
1. `BookmarkTagIndex` - 27 edges
2. `PixivBookmarkFilter` - 22 edges
3. `PixivApiClient` - 21 edges
4. `TagSuggestionWorkflow` - 20 edges
5. `OpenAiTagSuggestionClient` - 17 edges
6. `Work` - 16 edges
7. `ProgressBar` - 14 edges
8. `TagSuggestion` - 13 edges
9. `BookmarkTagDocumentInput` - 12 edges
10. `BookmarkFilterApplication` - 10 edges

## Surprising Connections (you probably didn't know these)
- `FakeEmbeddingClient` --implements--> `IEmbeddingClient`  [EXTRACTED]
  PixivBookmarkFilter.Tests/BookmarkTagIndexTests.cs → PixivBookmarkFilter/Clients/LmStudioEmbeddingClient.cs
- `PixivApiClient` --references--> `UserData`  [EXTRACTED]
  PixivBookmarkFilter/Clients/PixivApiClient.cs → PixivBookmarkFilter/Models/UserData.cs
- `BookmarkFilterApplication` --references--> `ImageDownloadService`  [EXTRACTED]
  PixivBookmarkFilter/Application/BookmarkFilterApplication.cs → PixivBookmarkFilter/Services/ImageDownloadService.cs
- `BookmarkFilterApplication` --references--> `TagSuggestionWorkflow`  [EXTRACTED]
  PixivBookmarkFilter/Application/BookmarkFilterApplication.cs → PixivBookmarkFilter/Services/TagSuggestionWorkflow.cs
- `RagTagSuggestionService` --references--> `IEmbeddingClient`  [EXTRACTED]
  PixivBookmarkFilter/Services/RagTagSuggestionService.cs → PixivBookmarkFilter/Clients/LmStudioEmbeddingClient.cs

## Import Cycles
- None detected.

## Communities (55 total, 39 thin omitted)

### Community 0 - "OpenAI Tag Suggestions"
Cohesion: 0.12
Nodes (19): HttpStatusCode, OpenAiSuggestion, CancellationToken, HttpClient, IEnumerable, int, IReadOnlyList, List (+11 more)

### Community 1 - "Bookmark Tag Index"
Cohesion: 0.14
Nodes (19): Action, IReadOnlyCollection, IEmbeddingClient, BookmarkTagDocumentInput, CancellationToken, Dictionary, IEnumerable, int (+11 more)

### Community 2 - "Bookmark Processing API"
Cohesion: 0.10
Nodes (16): ConsoleColor, Dictionary, List, Task, BookmarkFilterApplication, bool, HttpClient, IEnumerable (+8 more)

### Community 3 - "RAG Tag Suggestions"
Cohesion: 0.15
Nodes (13): List, BookmarkHistorySyncResult, TagSuggestion, TagSuggestionSource, IReadOnlyList, List, TagInputParser, TagInputParseResult (+5 more)

### Community 4 - "Project Composition"
Cohesion: 0.09
Nodes (13): PixivBookmarkFilter.Tests, PixivBookmarkFilter, List, BookmarkTagsMetadata, Public, ExtraMetaData, UserData, Task (+5 more)

### Community 5 - "LM Studio Embeddings"
Cohesion: 0.15
Nodes (12): EmbeddingData, IDisposable, CancellationToken, HttpClient, IReadOnlyList, List, string, Task (+4 more)

### Community 6 - "Illustration Image Downloads"
Cohesion: 0.20
Nodes (9): DateTime, List, IllustMetadata, ResultBody, Tag, Urls, HttpClient, Task (+1 more)

### Community 7 - "Build Dependencies"
Cohesion: 0.13
Nodes (14): HtmlAgilityPack (1.11.71), Microsoft.AspNetCore.SystemWebAdapters (1.3.0), Microsoft.CSharp (4.7.0), Microsoft.Data.Sqlite (8.0.29), Microsoft.NET.Test.Sdk (17.14.1), Newtonsoft.Json (13.0.3), System.Data.DataSetExtensions (4.5.0), xunit (2.9.3) (+6 more)

### Community 8 - "Recommendation Design Plan"
Cohesion: 0.22
Nodes (8): Configuration, Goal, Index Learning, Interaction, OpenAI Fallback, RAG Index, RAG Tag Suggestion Implementation Plan, Verification

### Community 9 - "JSON.NET Documentation"
Cohesion: 0.33
Nodes (5): Deserialize JSON, Links, LINQ to JSON, ![Logo](https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/master/Doc/icons/logo.jpg) Json.NET, Serialize JSON

### Community 10 - "Console Progress Reporting"
Cohesion: 0.19
Nodes (8): double, IProgress, bool, int, string, ProgressBar, Timer, TimeSpan

### Community 11 - "Cookie Encryption"
Cohesion: 0.29
Nodes (3): CookieContainer, Uri, StringEncrypt

### Community 17 - "TagSuggestionTests"
Cohesion: 0.13
Nodes (10): HttpMessageHandler, HttpRequestMessage, HttpResponseMessage, CancellationToken, Fact, string, Task, Uri (+2 more)

### Community 18 - ".GetRagSuggestionsAsync"
Cohesion: 0.28
Nodes (8): CancellationToken, IEnumerable, IReadOnlyList, List, Task, RagTagSuggestionService, TagVote, RetrievedBookmark

## Knowledge Gaps
- **58 isolated node(s):** `@opencode-ai/plugin`, `net8.0`, `Microsoft.NET.Test.Sdk (17.14.1)`, `xunit (2.9.3)`, `xunit.runner.visualstudio (2.8.2)` (+53 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **39 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PixivBookmarkFilter` connect `Project Composition` to `OpenAI Tag Suggestions`, `Bookmark Tag Index`, `Bookmark Processing API`, `RAG Tag Suggestions`, `LM Studio Embeddings`, `Illustration Image Downloads`, `Cookie Encryption`, `.GetRagSuggestionsAsync`?**
  _High betweenness centrality (0.178) - this node is a cross-community bridge._
- **Why does `TagSuggestionWorkflow` connect `RAG Tag Suggestions` to `OpenAI Tag Suggestions`, `Bookmark Processing API`, `Project Composition`, `LM Studio Embeddings`, `RAG Configuration`, `.GetRagSuggestionsAsync`?**
  _High betweenness centrality (0.089) - this node is a cross-community bridge._
- **Why does `OpenAiTagSuggestionClient` connect `OpenAI Tag Suggestions` to `TagSuggestionTests`, `RAG Configuration`, `RAG Tag Suggestions`, `LM Studio Embeddings`?**
  _High betweenness centrality (0.068) - this node is a cross-community bridge._
- **What connects `@opencode-ai/plugin`, `net8.0`, `Microsoft.NET.Test.Sdk (17.14.1)` to the rest of the system?**
  _66 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `OpenAI Tag Suggestions` be split into smaller, more focused modules?**
  _Cohesion score 0.12043010752688173 - nodes in this community are weakly interconnected._
- **Should `Bookmark Tag Index` be split into smaller, more focused modules?**
  _Cohesion score 0.1406423034330011 - nodes in this community are weakly interconnected._
- **Should `Bookmark Processing API` be split into smaller, more focused modules?**
  _Cohesion score 0.09871794871794871 - nodes in this community are weakly interconnected._