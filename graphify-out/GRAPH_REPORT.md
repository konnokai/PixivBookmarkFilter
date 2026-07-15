# Graph Report - .  (2026-07-15)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 83 nodes · 97 edges · 20 communities
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Community 0
- Community 1
- Community 2
- Community 3
- Community 4
- Community 5
- Community 6

## God Nodes (most connected - your core abstractions)
1. `ProgressBar` - 14 edges
2. `Program` - 13 edges
3. `PixivBookmarkFilter` - 7 edges
4. `Work` - 5 edges
5. `IllustMetadata` - 5 edges
6. `StringEncrypt` - 5 edges
7. `BookmarkTagsMetadata` - 3 edges
8. `BookmarksMetadata` - 3 edges
9. `Tag` - 3 edges
10. `Public` - 2 edges

## Surprising Connections (you probably didn't know these)
- `Program` --references--> `UserData`  [EXTRACTED]
  Pixiv Bookmark Filter/Program.cs → Pixiv Bookmark Filter/UserData.cs

## Import Cycles
- None detected.

## Communities (20 total, 0 thin omitted)

### Community 0 - "Community 0"
Cohesion: 0.28
Nodes (7): ConsoleColor, Dictionary, HttpClient, List, string, Program, Task

### Community 1 - "Community 1"
Cohesion: 0.16
Nodes (9): bool, double, IDisposable, int, IProgress, string, ProgressBar, Timer (+1 more)

### Community 2 - "Community 2"
Cohesion: 0.22
Nodes (6): PixivBookmarkFilter, List, BookmarkTagsMetadata, Public, ExtraMetaData, UserData

### Community 3 - "Community 3"
Cohesion: 0.29
Nodes (3): CookieContainer, StringEncrypt, Uri

### Community 4 - "Community 4"
Cohesion: 0.25
Nodes (7): net8.0, HtmlAgilityPack (1.11.71), Microsoft.AspNetCore.SystemWebAdapters (1.3.0), Microsoft.CSharp (4.7.0), Newtonsoft.Json (13.0.3), System.Data.DataSetExtensions (4.5.0), Microsoft.NET.Sdk

### Community 5 - "Community 5"
Cohesion: 0.43
Nodes (6): DateTime, List, IllustMetadata, ResultBody, Tag, Urls

### Community 6 - "Community 6"
Cohesion: 0.53
Nodes (5): DateTime, List, BookmarkData, BookmarksMetadata, Work

## Knowledge Gaps
- **9 isolated node(s):** `ExtraMetaData`, `ResultBody`, `net8.0`, `HtmlAgilityPack (1.11.71)`, `Microsoft.CSharp (4.7.0)` (+4 more)
  These have ≤1 connection - possible missing edges or undocumented components.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PixivBookmarkFilter` connect `Community 2` to `Community 3`, `Community 5`, `Community 6`?**
  _High betweenness centrality (0.239) - this node is a cross-community bridge._
- **Why does `Program` connect `Community 0` to `Community 2`?**
  _High betweenness centrality (0.153) - this node is a cross-community bridge._
- **What connects `ExtraMetaData`, `ResultBody`, `net8.0` to the rest of the system?**
  _9 weakly-connected nodes found - possible documentation gaps or missing edges._