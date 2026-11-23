# LRCLIB Lyrics Integration Status

最終更新: 2026-11-23

このドキュメントは、VizVid lyrics fork (`com.github.keisxr.vvmw-lyrics`) に対して行ったLRCLIB歌詞連携まわりの実装状況と、パッケージID変更に伴う移植対応をまとめる。

## 現在の位置づけ

Unity/VRC側には、歌詞データを受け取り、表示し、誤った歌詞を報告するための受け皿を実装済み。

LRCLIB検索・選曲・除外履歴・nginx公開を担当するサーバー側は `https://k3isd.net/lrc/` で稼働している。Unity側は「プレイリスト項目ごとに設定された歌詞取得URL」または「NomSeek VRCURL slotに対応した歌詞取得URL」へ `VRCStringDownloader` でアクセスし、返ってきたJSONを表示する構成になっている。

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

### 歌詞リレー（代表取得）

`Runtime/VVMW/LyricsRelay.cs`。`Core.lyricsRelay` に割り当てると、歌詞サーバーへのリクエストを1曲につき1人分に減らす。未割り当て、または `Core.synced` がオフのときは従来どおり各自が取得する。

- 動画を再生した人（Core のオーナー）が代表として取得し、返ってきたJSONを同期する。他の人と後から入った人は同期された結果を解析するだけでサーバーにアクセスしない。
- 同期するJSONは `syncedLyrics` があれば `plainLyrics` を落として縮める。
- 代表がサーバーに到達できなかった場合（HTTPステータスなし: DNS失敗・接続拒否など、または `fetchTimeout` 秒間進捗なし: Untrusted URL で無応答など）は、まだ試していないプレイヤーのうち最小IDの人に交代する。
- サーバーがHTTPエラーを返した場合は到達できているので交代せず、そのエラーを全員に配る。
- 誰も到達できなかった場合は `LYRICS_UNREACHABLE` になり、UIは歌詞の現在行に `LyricsUnreachable`、次行に最後のエラー内容を表示する。
- 代表が取得中に退出した場合は次の人がすぐ引き継ぐ。誰も取得を始めない場合は `claimTimeout` 秒後に最小IDの人が取得する。
- リレーが別のキー（Quest用URLの動画IDなど）で埋まっていて決まらない場合は、`claimTimeout + fetchTimeout` 秒後に各自で取得する。
- 取得するとリレーのオーナーが移るため、Core と同じ GameObject には置かない（動画同期のオーナーを動かさないため）。

到達不可の判定は `IVRCStringDownload.ErrorCode` が 100〜599 の範囲外であることで行っている。到達不可時に VRChat が実際に返す値は公式ドキュメントに記載がないため、実機での確認が必要。

## Runtime: Frontend / Playlist

主な実装ファイル:

- `Runtime/VVMW/FrontendHandler_Lyrics.cs`
- `Runtime/VVMW/FrontendHandler_PlayList.cs`
- `Runtime/VVMW/FrontendHandler_QueueList.cs`

`FrontendHandler` にプレイリスト項目ごとの歌詞URL配列を追加した。

- `playListLyricsUrls`
- `playListBadLyricsUrls`
- `dynamicLyricsMediaUrlPrefix`
- `dynamicLyricsRequestUrlPool`
- `dynamicLyricsReportUrlPool`

再生対象が決まったタイミングで `SetCoreLyricsSource()` を呼び、該当entryの歌詞URLと報告URLを `Core` に渡す。再生停止、queue遷移、URL入力などでplaylist entryと紐づかない再生に切り替わる場合は `_ClearLyricsSource()` で歌詞表示を消す。

URL入力などのplaylist外再生では、`dynamicLyricsMediaUrlPrefix` に一致する再生URLからslot番号を抽出し、`dynamicLyricsRequestUrlPool[slot]` / `dynamicLyricsReportUrlPool[slot]` を `Core` に渡す。通常のYouTube URLをVRC内で任意のlyrics URLへ動的変換することはできないため、playlist外で歌詞を出す場合はNomSeekの `https://api.u2b.cx/vrcurl/{pool}/{slot}` 形式の再生URLに対応する。

### 動画IDフォールバック (URL直接入力)

専用の歌詞URLを持たない再生 (URL欄への直接入力、歌詞URL未設定のplaylist項目、default URLなど) では、`Core` が再生中のURLからYouTube動画ID (11文字) を取り出し、事前生成した固定URLプール経由でサーバーへ伝える。UdonはVRCUrlを実行時に組み立てられないため、IDを2文字ずつに分けて「その2文字に対応する固定URL」へ順にアクセスする。

- 対応URL: `youtube.com/watch?v=`、`youtu.be/`、`/shorts/`、`/live/`、`/embed/`、`/v/`、`youtube-nocookie.com`。前段にresolverが付いたURLでも、文字列中にこれらが含まれていれば拾う
- プール: `Core.lyricsVideoIdUrlPool` (20,544本) と `Core.lyricsVideoIdReportUrl`。Core inspectorの歌詞設定にある「URL プールを生成」で作る。未生成ならこのフォールバックは動かず、従来どおり歌詞なしになる
- 各clientが自分の再生URLからIDを求めるため、同期変数は増やしていない
- 歌詞URLやNomSeek slotがある再生はそちらが優先される

生成されるURL (`{c}` は `A-Z a-z 0-9 - _` の64文字):

```text
lyricsVideoIdUrlPool[chunk * 4096 + i * 64 + j] = {endpoint}/yt/{pool}/id/{chunk}/{c_i}{c_j}   (chunk = 0..4)
lyricsVideoIdUrlPool[5 * 4096 + i]              = {endpoint}/yt/{pool}/id/5/{c_i}
lyricsVideoIdReportUrl                          = {endpoint}/yt/{pool}/report
```

clientはchunk 0から順に1本ずつ、応答を待ってから次を送る。応答JSONの `videoId` が自分の動画IDと一致した時点でそれを歌詞として採用し、残りのchunkは送らない。一致しなければ次のchunkへ進み、6本送っても一致しなければ1回だけ最初からやり直し、それでも駄目なら `LYRICS_NOT_FOUND` にする。

`VRCStringDownloader` は5秒に1本の制限があるため、サーバーが未知の動画は初回のみ表示まで最大30秒程度かかる。サーバーが早い段階で答えられれば5秒程度で出る。

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

NomSeek VRCURL slot向けに以下の生成補助を追加している。

- `NomSeek Pool`: NomSeek側のpool id。空のままでは生成できない
- `Max Slot`: 生成する最大slot番号。`0..Max Slot` のURL配列を作る
- `Detect`: `Assets/Nomlas/NomSeek/VRCURLSetter.prefab` からpool idと最大slot番号を読み取る
- `Generate Dynamic Lyrics Pool`: NomSeekのVRCURL slotと `lrcserve` の `/nomseek` endpointを対応付ける

生成後の設定値:

```text
dynamicLyricsMediaUrlPrefix = https://api.u2b.cx/vrcurl/{pool}/
dynamicLyricsRequestUrlPool[i] = https://k3isd.net/lrc/nomseek/{pool}/{i}/lyrics
dynamicLyricsReportUrlPool[i] = https://k3isd.net/lrc/nomseek/{pool}/{i}/report
```

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
"com.github.keisxr.vvmw-lyrics": "file:C:/Users/Kei/Documents/VVMW/Packages/com.github.keisxr.vvmw-lyrics"
```

NomSeek VizVid Connector側もfork後のpackage/asmdefを参照するように変更している。

- dependency: `com.github.keisxr.vvmw-lyrics`
- asmdef reference: `KeisXR.VVMW`
- UdonSharp type string: `JLChnToZ.VRC.VVMW.FrontendHandler, KeisXR.VVMW`

## 未実装 / 要検討

### サーバー側

`lrcserve` として実装済み。主なendpoint:

- `GET /lrc/v1/lyrics`
- `GET /lrc/v1/report`
- `GET /lrc/pool/{pool}/{slot}/lyrics`
- `GET /lrc/nomseek/{pool}/{slot}/lyrics`
- `GET /lrc/nomseek/{pool}/{slot}/report`

動画IDフォールバック用 (2026-11-23に実装、`curl` で動作確認済み):

- `GET /lrc/yt/{pool}/id/{chunk}/{chars}`
- `GET /lrc/yt/{pool}/report`

実装者向けの詳しい仕様 (状態の持ち方、レスポンス例、リクエスト列の例、受け入れ確認) は [lrcserve-video-id-spec.md](lrcserve-video-id-spec.md) にまとめた。以下はその要約。

`/yt/.../id/` に求められる挙動:

- clientごと (接続元IP + pool) に、受け取ったchunkを順に溜める。`chunk = 0` を受けたらそのclientの分を捨てて最初から溜め直す
- 歌詞を返すときは、既存のレスポンス項目に加えて `videoId` (対象の11文字) を必ず含める。clientは `videoId` が自分のIDと完全一致したときだけ採用するので、推測で返して外れても誤表示にはならない
- 6 chunk揃ったら `https://www.youtube.com/watch?v={id}` として既存の検索処理に回し、結果を `videoId` 付きで返す。歌詞が無い場合も `videoId` を付けて `plainLyrics` / `syncedLyrics` を空で返すと、clientは即座に「歌詞なし」と確定できる
- 途中のchunkでも、溜まった先頭部分に一致する既知の動画 (同じinstanceの他clientが直前に問い合わせた動画、NomSeek経由で見た動画など) があれば、その歌詞を先に返してよい。これが効くと2人目以降は1本目で歌詞が出る
- まだ答えられないときは `{}` などの `videoId` を含まないJSON、または404を返す。clientは次のchunkへ進む。404以外のエラーは取得失敗として扱われる
- `/yt/{pool}/report` は、そのclientに最後に返した動画への「この歌詞は違う」報告として扱う

同じIPから複数人が別々の動画を同時に送ると溜めたchunkが混ざりうるが、同じinstanceでは全員が同じ動画を再生しているため実害は出にくい。混ざった場合もclient側の `videoId` 照合で弾かれ、やり直しになる。

自前 `/vrcurl` slot案はUXが複雑になったためUnity側の導線から撤回した。現行のplaylist外再生対応はNomSeekのVRCURL slotに乗り、歌詞取得・誤り報告だけを `lrcserve` の `/nomseek` endpointで処理する。

### フォールバック方針

タイトルだけで検索すると、複数候補から誤った同期歌詞を選ぶことがある。現時点ではUnity側で再検索・補正するのではなく、「この歌詞は違う」報告を送るだけにしている。

サーバー側では、同一動画URLまたは同一track keyに対してbad reportされたLRCLIB idを次回以降除外する案が有力。

### 検証

未完了:

- Unity Editorでの完全compile確認
- Lyrics Panel prefab生成結果の確認
- ClientSim / VRChat Build & Testでの再生中歌詞更新確認
- NomSeek VRCURL slot URL再生時のVRChat Client実地確認
- 動画IDフォールバックのVRChat Client実地確認 (C# / UdonSharpのcompileは確認済み)

直近のUnity batchmode compileは、対象World projectが既にUnityで開かれていたため実行できなかった。

## Known Caveats

- `VRCStringDownloader` はTrusted URL制約を受ける。最終的な歌詞取得URL/報告URLはVRChatでアクセス可能なtrusted HTTPS endpointとして提供する必要がある。
- VRChat/UdonSharpでは `async/await` や通常のHTTP POSTを使えないため、Unity側は `VRCStringDownloader` + GET中心の設計にしている。
- `Core.OnStringLoadSuccess` / `OnStringLoadError` を歌詞取得にも使っているため、他のstring download用途を追加する場合はURL照合を崩さないこと。
- package pathに依存する処理は、fork package id `com.github.keisxr.vvmw-lyrics` と旧id `idv.jlchntoz.vvmw` の両方を考慮する必要がある。
