# Graph Report - .  (2026-07-15)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 98 nodes · 130 edges · 23 communities (21 shown, 2 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `c27a78cf`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- ProgressBar
- .Main
- Program
- PixivBookmarkFilter
- Community 4
- StringEncrypt
- IllustMetadata.cs
- Work
- List
- string

## God Nodes (most connected - your core abstractions)
1. `Program` - 21 edges
2. `ProgressBar` - 14 edges
3. `PixivBookmarkFilter` - 7 edges
4. `Work` - 5 edges
5. `IllustMetadata` - 5 edges
6. `StringEncrypt` - 5 edges
7. `BookmarkTagsMetadata` - 3 edges
8. `BookmarksMetadata` - 3 edges
9. `Tag` - 3 edges
10. `Public` - 2 edges

## Surprising Connections (you probably didn't know these)
- None detected - all connections are within the same source files.

## Import Cycles
- None detected.

## Communities (23 total, 2 thin omitted)

### Community 0 - "ProgressBar"
Cohesion: 0.16
Nodes (9): bool, double, IDisposable, int, IProgress, string, ProgressBar, Timer (+1 more)

### Community 1 - ".Main"
Cohesion: 0.30
Nodes (4): ConsoleColor, Dictionary, List, Task

### Community 2 - "Program"
Cohesion: 0.26
Nodes (7): HttpClient, IEnumerable, Program, Rune, string, TagSuggestion, UserData

### Community 3 - "PixivBookmarkFilter"
Cohesion: 0.18
Nodes (7): PixivBookmarkFilter, List, BookmarkTagsMetadata, Public, ExtraMetaData, TagSuggestion, UserData

### Community 4 - "Community 4"
Cohesion: 0.25
Nodes (7): net8.0, HtmlAgilityPack (1.11.71), Microsoft.AspNetCore.SystemWebAdapters (1.3.0), Microsoft.CSharp (4.7.0), Newtonsoft.Json (13.0.3), System.Data.DataSetExtensions (4.5.0), Microsoft.NET.Sdk

### Community 5 - "StringEncrypt"
Cohesion: 0.33
Nodes (3): CookieContainer, StringEncrypt, Uri

### Community 6 - "IllustMetadata.cs"
Cohesion: 0.43
Nodes (6): DateTime, List, IllustMetadata, ResultBody, Tag, Urls

### Community 7 - "Work"
Cohesion: 0.53
Nodes (5): DateTime, List, BookmarkData, BookmarksMetadata, Work

## Knowledge Gaps
- **11 isolated node(s):** `ExtraMetaData`, `ResultBody`, `net8.0`, `HtmlAgilityPack (1.11.71)`, `Microsoft.CSharp (4.7.0)` (+6 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **2 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PixivBookmarkFilter` connect `PixivBookmarkFilter` to `IllustMetadata.cs`, `Work`?**
  _High betweenness centrality (0.259) - this node is a cross-community bridge._
- **Why does `Program` connect `Program` to `.Main`, `PixivBookmarkFilter`?**
  _High betweenness centrality (0.228) - this node is a cross-community bridge._
- **Why does `StringEncrypt` connect `StringEncrypt` to `PixivBookmarkFilter`?**
  _High betweenness centrality (0.070) - this node is a cross-community bridge._
- **What connects `ExtraMetaData`, `ResultBody`, `net8.0` to the rest of the system?**
  _11 weakly-connected nodes found - possible documentation gaps or missing edges._