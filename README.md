# 影片與音樂下載器

一款簡單的 Windows 桌面程式。貼上影片網址、選擇影片或音訊格式，即可下載到指定資料夾。預設使用 Windows 的「下載」資料夾，也支援已重新設定位置的下載資料夾。

**[下載 Windows 64 位元版](https://github.com/Buffett111/youtube-downloader-gui/releases/latest)**：在 Release 的 Assets 下載 `SimpleDownloader-v1.1.1-windows-x64.zip`。解壓縮即可使用，不需要自行建置程式。版本更新內容與原始碼下載也放在同一頁。

## 開始使用

1. 將下載的 ZIP 完整解壓縮到一般資料夾，保留 `影片與音樂下載器.exe.config` 與 EXE 在同一個資料夾；設定檔用於高 DPI 顯示支援。
2. 雙擊 `影片與音樂下載器.exe`。
3. 貼上網址，選擇「影片」或「音樂」、輸出格式與影片畫質。
4. 如需變更儲存位置，按「瀏覽…」。
5. 按「開始下載」。第一次會自動安裝所需工具，再讀取影片資訊。
6. 在「確認下載內容」視窗查看影片標題、下載數量、輸出格式與儲存位置。可勾選個別影片，或按「全選」下載整份播放清單；按「確認下載」才會開始下載影音。

也可以先按右下角的「安裝下載工具」。安裝完成後，這個按鈕會改成「更新下載工具」。使用者不需要安裝 Python、設定環境變數或輸入命令列。

更新時請先關閉舊版，再將新版 ZIP 完整解壓縮。已安裝的下載工具可繼續使用，不需要重新安裝。v1.1.1 修正高縮放比例下的網址欄、格式與品質選單、按鈕跑版；視窗空間不足時可捲動內容，不需要更改 Windows 的顯示縮放設定。

需要 Windows 10／11 **64 位元**與 .NET Framework 4.8。近期 Windows 通常已包含此執行環境。首次安裝工具及下載影片時需要網路連線；安裝工具需要下載數個檔案，請讓進度完成。

## 格式與操作

| 選項 | 輸出內容 |
| --- | --- |
| MP4 | 影片與聲音，優先使用常見的 H.264／AAC 格式 |
| MKV | 影片與聲音，保留所選上限內的最佳可用影音 |
| MP3 | 從最佳可用音訊轉成高品質 MP3 |
| M4A | 優先使用 M4A 音訊，必要時轉換 |
| WAV | 未壓縮音訊，檔案較大；不會增加來源本來沒有的音質 |
| 原始音訊 | 保留來源的音訊格式，不重新編碼，例如 WebM 或 M4A |

影片可選最高可用畫質、4K、1440p、1080p、720p，預設 1080p。畫質選項代表上限，來源沒有這個畫質時會選擇可用格式。MP4 優先考慮相容的影音編碼，MKV 則優先選擇可用畫質。

開始下載前會先用 yt-dlp 讀取資訊，這個階段不下載影音檔。確認視窗列出清單中的影片，可全選、全不選，或逐一勾選，並即時顯示「已選 N / 共 M 部影片」。點選項目後，下方會顯示完整標題。未選取任何影片時，確認按鈕不會啟用。

如果連結同時指定某部影片與播放清單，預設只勾選該影片，可按「全選」改成整份清單；純播放清單網址則預設勾選全部可讀取的項目。已刪除、私人或資訊不完整的項目會明確標示，無法勾選，但不影響其餘項目的選取。

按「取消」、Esc 或關閉確認視窗，都不會開始下載；若影片資訊讀取失敗，也不會直接跳過確認。確認後會依選定的影片網址與 ID 逐一下載，避免播放清單順序變動時改下載其他影片。個別項目失敗時會記錄原因並繼續處理其餘已選項目，結束後顯示完成與失敗數量。

檔名包含影片標題及影片 ID，預設不覆寫已存在的檔案。下載過程可按「取消」停止目前項目及後續佇列，已完成的檔案會保留，未完成的下載暫存檔可供 yt-dlp 續傳。畫面會顯示目前處理第幾部及該影片的進度。下載影音與合併／轉檔是不同階段，進度會依目前階段顯示。

「開啟資料夾」可查看下載結果；「顯示詳細紀錄」可查看工具安裝與 yt-dlp 回傳的訊息。需要重試時，可先按「更新下載工具」，再重新下載。

## 一鍵安裝如何運作

程式從官方 GitHub 專案取得目前版本，下載並驗證 SHA-256 後安裝：

- [yt-dlp](https://github.com/yt-dlp/yt-dlp)：官方 Windows 獨立執行檔，包含 Python 與 yt-dlp 所需的 EJS 元件。
- [FFmpeg／ffprobe](https://github.com/yt-dlp/FFmpeg-Builds)：合併影音、擷取與轉換音訊。
- [Deno](https://github.com/denoland/deno)：供 yt-dlp 使用的 JavaScript 執行環境。

工具儲存在 `%LOCALAPPDATA%\SimpleDownloader\tools`，不需要系統管理員權限，也不修改系統 PATH。已安裝的工具可供之後開啟的程式直接使用。更新會先下載及驗證，再替換舊版本；下載或驗證失敗時，原先的工具仍會保留。

影片網址解析與下載由 yt-dlp 負責。GUI 使用它的官方參數與進度輸出，不另外實作 YouTube 解析、登入或驗證流程。相關設定依據 [yt-dlp 使用文件](https://github.com/yt-dlp/yt-dlp#usage-and-options) 與 [EJS 安裝說明](https://github.com/yt-dlp/yt-dlp/wiki/EJS)。

## 原始碼與建置

使用 C# 5、Windows Forms 及 .NET Framework；不需要 NuGet 套件。

```text
src/
  MainForm.cs          圖形介面及操作流程
  DownloadConfirmForm.cs  下載前的標題、數量、影片勾選與目的地確認
  DownloadService.cs   yt-dlp 參數、下載進度與取消操作
  ToolInstaller.cs     官方工具下載、雜湊驗證及更新
  Program.cs           程式進入點與單一執行個體
  AssemblyInfo.cs      程式版本資訊
  app.config          .NET Framework 4.8 與每個螢幕的 DPI 支援設定
  app.manifest         權限、Windows 及 DPI 設定
  app.ico              程式圖示
build.ps1              建置程式
tests/                 行為、確認視窗與本機影音整合測試
```

開發者可在原始碼資料夾執行 `powershell -ExecutionPolicy Bypass -File .\build.ps1`，輸出位於 `release`。這個步驟僅供開發者重新建置，一般使用者直接開啟 `.exe` 即可。

## 測試

在 Windows 的專案資料夾執行以下指令，以本機模擬程序驗證格式、參數、播放清單勾選、確認與取消流程，不會下載 YouTube 影音：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

版面回歸測試會在本機執行 100%～200% 的合成縮放與不同視窗尺寸檢查，並將預覽圖存至 `work\dpi-tests`。合成縮放不會變更 Windows 設定，也不代表已在每一種實體螢幕組合測試：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\run-layout-tests.ps1
```

已透過 GUI 安裝工具後，也可用自製影音檔驗證實際的 yt-dlp、FFmpeg 下載與轉檔流程：

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\run-local-media-integration.ps1 -ToolsDirectory "$env:LOCALAPPDATA\SimpleDownloader\tools"
```

測試會將產生的檔案寫入 `work`，測試與建置工作檔不包含在使用者版本。第三方工具的授權與官方來源列於 `THIRD_PARTY_NOTICES.md`。
