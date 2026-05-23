# LRCLIB Lyrics Integration Status

最終更新: 2026-05-23

このドキュメントは、VizVid lyrics fork (`com.github.keisxr.vvmw-lyrics`) に対して行ったLRCLIB歌詞連携まわりの実装状況と、パッケージID変更に伴う移植対応をまとめる。

## 現在の位置づけ

Unity/VRC側には、歌詞データを受け取り、表示し、誤った歌詞を報告するための受け皿を実装済み。

一方で、LRCLIB検索・選曲・除外履歴・nginx公開を担当するサーバー側はまだ未実装。現時点のUnity側は「プレイリスト項目ごとに設定された歌詞取得URLへ `VRCStringDownloader` でアクセスし、返ってきたJSONを表示する」構成になっている。

## Package / Assembly

VisVid本家との競合を避けるため、パッケージIDとassembly名をfork用に変更した。

- Package ID: `com.github.keisxr.vvmw-lyrics`
- Display name: `VizVid (Lyrics Fork)`
- Runtime asmdef: `KeisXR.VVMW`
- Editor asmdef: `KeisXR.VVMW.Editor`

既存コードのnamespaceは `JLChnToZ.VRC.VVMW` のまま維持している。これは既存serialized referenceや既存コードとの互換性を保つため。

パッケージID変更に伴い、次の互換対応も入れている。

- `InternalsVisibleTo("KeisXR.VVMW.Editor")` を追加
- `EditorI18NSource` に `Packages/com.github.keisxr.vvmw-lyrics/Resources/*.json` を追加
- UdonSharp program asset内のassembly名参照を `KeisXR.VVMW` に更新
- `ytdlp-regions.json` は実際のpackage resolved pathから読むように変更

これにより、Editor inspectorで翻訳キーがそのまま表示される問題と、internal memberへEditor asmdefからアクセスできない問題を解消している。

## Runtime: Core

主な実装ファイル:

- `Runtime/VVMW/Core_LRCLIB.cs`
- `Runtime/VVMW/Core.cs`
- `Runtime/VVMW/FrontendHandler.cs`
- `Runtime/VVMW/FrontendHandler_Lyrics.cs`
- `Runtime/VVMW/FrontendHandler_PlayList.cs`
- `Runtime/VVMW/FrontendHandler_QueueList.cs`
- `Runtime/VVMW/StreamLinkAssigner.cs`
- `Runtime/VVMW/UIHandler_OmniInput.cs`

`Core` には歌詞状態・表示データ・取得処理を追加した。

### 歌詞状態

`Core_LRCLIB.cs` で以下の状態を定義している。

- `LYRICS_NONE`: 歌詞なし、または歌詞機能無効
- `LYRICS_LOADING`: 歌詞取得中
- `LYRICS_SYNCED`: 同期歌詞あり
- `LYRICS_PLAIN`: プレーン歌詞あり
- `LYRICS_INSTRUMENTAL`: インストゥルメンタル
- `LYRICS_NOT_FOUND`: 歌詞なし
- `LYRICS_ERROR`: 取得またはparseエラー
- `LYRICS_REPORTED`: 誤った歌詞として報告済み

### Inspector設定

`Core` に以下の設定を追加している。

- `enableLyrics`: 歌詞機能の有効/無効
- `lyricsOffset`: 表示タイミングの補正秒数
- `maxSyncedLyricsLines`: LRC同期行の最大保持数
- `lyricsUpdateInterval`: 同期歌詞の表示更新間隔

### 歌詞取得

`Core.SetLyricsSource()` / `Core.SetLyricsSourceAndLoad()` で歌詞取得URLと報告URLを受け取り、`LoadLRCLIB()` で `VRCStringDownloader.LoadUrl()` を実行する。

返却JSONは `VRCJson.TryDeserializeFromJson()` でparseする。想定しているレスポンス項目は以下。

- `id`
- `trackName`
- `artistName`
- `plainLyrics`
- `syncedLyrics`
- `instrumental`

`syncedLyrics` が有効な場合はLRC timestampをparseし、`lyricsTimes` / `lyricsLines` に展開する。複数timestamp付き行にも対応している。同期歌詞がない場合は `plainLyrics` を表示用に保持する。

### 同期歌詞表示

同期歌詞は `Core.Time + lyricsOffset` を基準に現在行を計算する。

UI向けには以下の3行を公開している。

- `LyricsPreviousLine`
- `LyricsCurrentLine`
- `LyricsNextLine`

行が変わると `_OnLyricsLineChange` イベントを送る。歌詞データそのものが変わると `_OnLyricsData` イベントを送る。

### 誤った歌詞の報告

`Core._ReportBadLyrics()` を追加した。報告可能条件は以下。

- 報告中ではない
- 報告URLが設定されている
- 現在の歌詞状態が `LYRICS_SYNCED` / `LYRICS_PLAIN` / `LYRICS_INSTRUMENTAL`

ボタンが押されると報告URLへ `VRCStringDownloader.LoadUrl()` でアクセスし、UI状態を `LYRICS_REPORTED` に更新する。

現時点ではVRChat側で報告内容をPOSTできないため、報告URLへのGETアクセスをサーバー側で「この歌詞は違う」イベントとして扱う前提。

## Runtime: Frontend / Playlist

主な実装ファイル:

- `Runtime/VVMW/FrontendHandler_Lyrics.cs`
- `Runtime/VVMW/FrontendHandler_PlayList.cs`
- `Runtime/VVMW/FrontendHandler_QueueList.cs`

`FrontendHandler` にプレイリスト項目ごとの歌詞URL配列を追加した。

- `playListLyricsUrls`
- `playListBadLyricsUrls`

再生対象が決まったタイミングで `SetCoreLyricsSource()` を呼び、該当entryの歌詞URLと報告URLを `Core` に渡す。再生停止、queue遷移、URL入力などでplaylist entryと紐づかない再生に切り替わる場合は `_ClearLyricsSource()` で歌詞表示を消す。

## Runtime: UI

主な実装ファイル:

- `Runtime/VVMW/UIHandler_Lyrics.cs`
- `Runtime/VVMW/UIHandler.cs`

`UIHandler` に歌詞表示用の参照とイベント処理を追加した。

### 表示領域

以下のrootを持つ想定。

- `lyricsPanelRoot`
- `lyricsSyncedRoot`
- `lyricsPlainRoot`
- `lyricsStatusRoot`
- `lyricsReportButtonObject`

同期歌詞の場合は前行・現在行・次行を表示する。プレーン歌詞の場合は全文表示する。取得中、未発見、instrumental、error、reportedの場合はstatus表示へ切り替える。

### 報告ボタン

`lyricsReportButton` は `Button.onClick` から `_ReportBadLyrics` にbindする。表示文言はruntime language key `LyricsReportButton` を使う。

## Editor

主な実装ファイル:

- `Editor/VVMW/CoreEditor.cs`
- `Editor/VVMW/PlayListEditorWindow.cs`
- `Editor/VVMW/LrclibLyricsPanelGenerator.cs`
- `Resources/editor-lang.json`
- `Resources/lang.json`

### Core Inspector

`CoreEditor` に歌詞設定を追加した。

- Enable Lyrics
- Lyrics Offset
- Max Synced Lyrics Lines
- Lyrics Update Interval

### Playlist Editor

`PlayListEditorWindow` でプレイリスト項目ごとの歌詞取得URLと誤り報告URLを編集できるようにしている。

### Lyrics Panel Generator

`LrclibLyricsPanelGenerator` を追加した。

メニュー:

- `Tools/VizVid/LRCLIB/Generate Lyrics Panel Prefab`
- VizVid menu配下の `Modules/Lyrics Panel`

歌詞表示用panelを生成し、`UIHandler` のlyrics関連serialized fieldへ割り当てる補助を行う。

注意: prefab生成結果はまだUnity上での実地確認が必要。

## Localization

Runtime表示用に `Resources/lang.json` へ以下のキーを追加した。

- `LyricsLoading`
- `LyricsNotFound`
- `LyricsInstrumental`
- `LyricsError`
- `LyricsReported`
- `LyricsReportButton`

Editor表示用に `Resources/editor-lang.json` へCore/Frontend/UIHandlerの歌詞関連キーを追加した。

対応言語は既存の言語セットに合わせて追加している。

## World Project Import

World側では `Packages/manifest.json` に以下のlocal package参照を追加している。

```json
"com.github.keisxr.vvmw-lyrics": "file:C:/Users/Kei/Documents/VVMW/Packages/idv.jlchntoz.vvmw"
```

NomSeek VizVid Connector側もfork後のpackage/asmdefを参照するように変更している。

- dependency: `com.github.keisxr.vvmw-lyrics`
- asmdef reference: `KeisXR.VVMW`
- UdonSharp type string: `JLChnToZ.VRC.VVMW.FrontendHandler, KeisXR.VVMW`

## 未実装 / 要検討

### サーバー側

まだ未実装。

必要なもの:

- LRCLIB検索APIへの問い合わせ
- 曲名/作者/動画タイトル/URLから検索queryを作る処理
- 複数候補からの選択ルール
- 同期歌詞が間違っていた場合の除外記録
- 報告URLへのGETを受けてbad lyricsとして保存する処理
- nginx配下でVRChatからアクセス可能なHTTPS endpointとして公開する設定

### フォールバック方針

タイトルだけで検索すると、複数候補から誤った同期歌詞を選ぶことがある。現時点ではUnity側で再検索・補正するのではなく、「この歌詞は違う」報告を送るだけにしている。

サーバー側では、同一動画URLまたは同一track keyに対してbad reportされたLRCLIB idを次回以降除外する案が有力。

### 検証

未完了:

- Unity Editorでの完全compile確認
- Lyrics Panel prefab生成結果の確認
- ClientSim / VRChat Build & Testでの再生中歌詞更新確認
- 報告URLアクセスの動作確認
- サーバー実装後の実LRCLIB取得確認

直近のUnity batchmode compileは、対象World projectが既にUnityで開かれていたため実行できなかった。

## Known Caveats

- `VRCStringDownloader` はTrusted URL制約を受ける。最終的な歌詞取得URL/報告URLはVRChatでアクセス可能なtrusted HTTPS endpointとして提供する必要がある。
- VRChat/UdonSharpでは `async/await` や通常のHTTP POSTを使えないため、Unity側は `VRCStringDownloader` + GET中心の設計にしている。
- `Core.OnStringLoadSuccess` / `OnStringLoadError` を歌詞取得にも使っているため、他のstring download用途を追加する場合はURL照合を崩さないこと。
- package pathに依存する処理は、fork package id `com.github.keisxr.vvmw-lyrics` と旧id `idv.jlchntoz.vvmw` の両方を考慮する必要がある。
