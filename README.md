# PixivBookmarkFilter

PixivBookmarkFilter 是一個 .NET 8 主控台工具，用來整理尚未分類的 Pixiv 公開收藏。程式會讀取你現有的收藏標籤，下載作品原圖，再把選定的標籤加回 Pixiv 收藏。

程式會直接修改 Pixiv 收藏資料。第一次使用時，建議先用少量收藏確認下載路徑與標籤規則是否符合預期。

## 功能

- 自動套用作品原始標籤中，已存在於個人收藏標籤清單的項目。
- 使用 Embedding 模型與本機 SQLite 索引，從過往已分類收藏提供 RAG 標籤建議。
- RAG 沒有結果時，可改由 OpenAI 從現有收藏標籤中挑選建議。
- AI 服務不可用時，仍可使用字串相似度建議或手動輸入標籤。
- 下載單張與多頁作品原圖，並依收藏標籤分資料夾存放。
- 可設定標籤與下載資料夾的對應，以及不需下載圖片的標籤。

## 執行需求

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- 可正常登入的 Pixiv 帳號
- Pixiv 的 `PHPSESSID` 與 `X_CSRF_TOKEN`
- 選用：提供 OpenAI 相容 Embedding API 的模型服務
- 選用：OpenAI API Key，或支援 Chat Completions API 的相容服務

## 快速開始

在專案根目錄執行：

```powershell
dotnet run --project "PixivBookmarkFilter/PixivBookmarkFilter.csproj"
```

首次執行時，程式會要求輸入 `PHPSESSID` 與 `X_CSRF_TOKEN`。驗證成功後，資料會寫入目前工作目錄下的 `UserData.json`。

> `UserData.json` 內含可用來存取 Pixiv 帳號的憑證，而且目前不在 `.gitignore` 中。請勿分享、上傳或提交這個檔案。

程式只處理公開收藏，並從最新的未分類收藏開始逐筆詢問。圖片預設下載到：

```text
桌面/Pixiv收藏分類儲存/
```

## 操作方式

需要手動選擇標籤時，可輸入：

| 輸入 | 用途 |
| --- | --- |
| `#1` | 選擇第 1 個建議標籤 |
| `#1 #2` | 同時選擇多個建議標籤 |
| `#AI` | 放棄目前的 RAG 建議，改用 OpenAI；必須單獨輸入 |
| `Fate FGO` | 直接輸入一個或多個標籤 |
| `-` | 取消這筆 Pixiv 收藏 |

標籤建議順序如下：

1. 作品原始標籤與目前收藏標籤的完全相符項目
2. 本機 RAG 索引
3. OpenAI，僅在 RAG 沒有結果或輸入 `#AI` 時使用
4. 字串相似度比對
5. 手動輸入

RAG 與 OpenAI 最多各顯示 5 個建議，而且只會回傳目前收藏標籤清單內已存在的標籤。

## RAG 與 OpenAI 設定

專案附有 `PixivBookmarkFilter/RagSettings.example.json`。若要自訂設定，可在專案根目錄建立 `RagSettings.json`；執行已建置的程式時，也可以把它放在執行檔旁邊。程式會優先讀取執行檔目錄，再讀取目前工作目錄。

```json
{
  "embeddingModelBaseUrl": "http://localhost:1234/v1",
  "embeddingModel": "text-embedding-bge-m3",
  "retrievalTopK": 20,
  "minimumSimilarity": 0.6,
  "openAiEnabled": true,
  "openAiBaseUrl": "https://api.openai.com/v1",
  "openAiApiKey": "",
  "openAiModel": "gpt-5.6-luna",
  "openAiReasoningEffort": "low",
  "maxSuggestions": 5
}
```

| 設定 | 說明 |
| --- | --- |
| `embeddingModelBaseUrl` | Embedding 模型的 OpenAI 相容 API 位址 |
| `embeddingModel` | Embedding 模型名稱 |
| `retrievalTopK` | 每次 RAG 搜尋最多取回的收藏數量 |
| `minimumSimilarity` | RAG 搜尋的最低餘弦相似度，範圍為 `-1` 到 `1` |
| `openAiEnabled` | 是否啟用 OpenAI 建議 |
| `openAiBaseUrl` | Chat Completions API 的基底網址 |
| `openAiApiKey` | OpenAI 或相容服務的 API Key |
| `openAiModel` | 使用的聊天模型 |
| `openAiReasoningEffort` | 傳給模型的 reasoning effort |
| `maxSuggestions` | 建議數量，上限固定為 5 |

OpenAI 只有在 `openAiEnabled` 為 `true` 且 `openAiApiKey` 不為空時才會啟用。Embedding 模型或 OpenAI 請求失敗時，程式會回到其他建議方式，不會因 AI 服務無法使用而中止手動分類。

### 本機索引

RAG 索引存放在執行檔旁的 `TagSuggestionIndex.db`。Embedding 內容只包含作品標題與 Pixiv 原始標籤；使用者指定的收藏標籤只作為推薦答案，不會放進 Embedding 文字。

修改 `embeddingModel` 後，程式會自動清空既有向量與同步進度，並用新模型重建索引。

## 下載資料夾設定

`TagConvertList.json` 用來把收藏標籤轉成下載資料夾名稱。例如：

```json
{
  "Fate/Grand Order": "Fate",
  "初音ミク": "VOCALOID"
}
```

`IgnoreDownloadTag.json` 用來略過含有指定標籤的作品下載。例如：

```json
[
  "不下載",
  "僅分類"
]
```

這兩個檔案只會從執行檔目錄讀取；缺少檔案時會視為空設定。使用 `dotnet run` 時，Debug 執行檔目錄通常是：

```text
PixivBookmarkFilter/bin/Debug/net8.0/
```

含有忽略下載標籤的作品仍會新增收藏標籤，只是不下載圖片。

## 下載行為

- 單張作品直接存進標籤對應的資料夾。
- 多頁作品會存進以作品 ID 命名的子資料夾。
- 含 `R-18` 標籤的檔名會加上下載時間前綴。
- 動圖目前不支援下載，但程式仍會繼續新增收藏標籤。
- 圖片下載不完整時，程式不會新增該筆收藏標籤，並會停止這次處理。

## 開發與測試

執行全部測試：

```powershell
dotnet test "PixivBookmarkFilter.sln"
```

執行 Release 建置：

```powershell
dotnet build "PixivBookmarkFilter.sln" -c Release
```

測試不會連線到 Pixiv、Embedding 模型服務或 OpenAI。HTTP 呼叫使用假的處理器，SQLite 測試則使用每次測試建立的暫存資料庫。

## 專案結構

```text
PixivBookmarkFilter/
├─ Application/    主流程與收藏處理
├─ Clients/        Pixiv、Embedding 模型、OpenAI HTTP 用戶端
├─ Configuration/  本機與 RAG 設定
├─ Parsing/        主控台輸入解析
├─ Services/       標籤建議與圖片下載
└─ Storage/        SQLite 向量索引

PixivBookmarkFilter.Tests/
└─ xUnit 單元測試
```

## 授權

本專案使用 MIT License，詳見 `LICENSE.txt`。
