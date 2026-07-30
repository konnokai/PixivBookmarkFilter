# Repository Guide

## Project Shape

- `PixivBookmarkFilter.sln` contains the .NET 8 console app and its xUnit test project.
- Runtime flow is `Program.cs` -> `BookmarkFilterApplication`: Pixiv HTTP access lives in `Clients/`, recommendation orchestration in `Services/`, and the SQLite vector index in `Storage/BookmarkTagIndex.cs`.
- There is no repository CI, formatter, lint, typecheck, or codegen task. Use tests plus a Release build for verification.

## Commands

- Full tests: `dotnet test "PixivBookmarkFilter.sln"`
- Release build: `dotnet build "PixivBookmarkFilter.sln" -c Release`
- One test class: `dotnet test "PixivBookmarkFilter.Tests/PixivBookmarkFilter.Tests.csproj" --filter "FullyQualifiedName~BookmarkTagIndexTests"`
- One test method: `dotnet test "PixivBookmarkFilter.Tests/PixivBookmarkFilter.Tests.csproj" --filter "FullyQualifiedName~TagSuggestionTests.OpenAiValidation_RejectsUnknownAndDuplicateTags"`
- Run manually: `dotnet run --project "PixivBookmarkFilter/PixivBookmarkFilter.csproj"`. This is interactive, calls Pixiv, mutates bookmark tags, and downloads images; do not use it as routine verification.

## Runtime Files And Secrets

- `RagSettings.example.json` is the only runtime JSON copied by the project. Real `RagSettings.json` is ignored and is loaded from the executable directory first, then the process working directory.
- `TagConvertList.json` and `IgnoreDownloadTag.json` are loaded only from `AppDomain.CurrentDomain.BaseDirectory`; when absent they silently become empty collections. For `dotnet run`, that directory is `PixivBookmarkFilter/bin/Debug/net8.0/`.
- `TagSuggestionIndex.db` is created beside the executable. Changing `embeddingModel` intentionally clears indexed documents and the synchronization anchor.
- `UserData.json` contains the Pixiv `PHPSESSID` and CSRF token, is written relative to the process working directory, and is not ignored by the current `.gitignore`. Never stage or commit it.
- The default LM Studio endpoint is `http://localhost:1234/v1`. LM Studio failures fall back to OpenAI or lexical matching; OpenAI is active only when enabled and an API key is present.
- Successful runs download to `Desktop/Pixiv收藏分類儲存/`. Image download must complete before the app adds bookmark tags.

## Behavioral Invariants

- Preserve the recommendation order: exact matches against the current user tag list -> RAG -> OpenAI when RAG is empty or the user enters `#AI` -> lexical/manual fallback.
- RAG and OpenAI suggestions must be existing entries in the latest user tag list and are capped at five.
- Embeddings contain only the work title and original Pixiv tags. Assigned bookmark tags are recommendation labels, not embedding input.
- `#AI` must be entered alone; numbered suggestions may be combined. `-` removes the bookmark.

## Code And Tests

- Match the existing C# style: explicit `using` directives, block-scoped namespaces, four-space indentation, and no nullable-reference annotations unless the project settings change.
- Unit tests are offline: HTTP is faked and SQLite tests use per-test temporary databases. External Pixiv, LM Studio, and OpenAI smoke tests are separate manual checks.
