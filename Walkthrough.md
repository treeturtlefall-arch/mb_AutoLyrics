# Walkthrough — mb_AutoLyrics 開発記録と引き継ぎガイド

> [!IMPORTANT]
> このドキュメントは、MusicBee用歌詞取得プラグイン **mb_AutoLyrics** (v0.2) の開発経緯・アーキテクチャ・技術的発見・拡張方法をまとめたものである。
> **新規セッションのAIエージェントや別LLMが開発を引き継ぐ際は、必ず本書と `mb_AutoLyrics/` 配下のソースを読んだ上で作業を開始すること。**

---

## 1. プロジェクトの目的

MusicBee上で再生中の楽曲の歌詞を**複数のWebソースから自動探索・取得**し、**MusicBeeのメタデータ（歌詞タグ/歌詞ファイル）に記録**するプラグイン。
ユーザーは約14.7万曲の日本語中心のライブラリを持つため、日本語歌詞サイトのカバー率が最重要。

- 開発の起点: `noriokun4649/mb_KashiNaviLyricsPlugin`（MITライセンス）を骨格として参考にし、実質的に書き直したもの
- 設計思想の参考: `pschichtel/LyricsReloaded`（設定駆動・テストツリーン分離）と `slonopot/Beenius`（類似度マッチ・タイトル正規化）
- **ライセンスはMIT**（LyricsReloadedはGPL-3.0のため、コードの直接コピーは禁止。思想の参考のみ）

## 2. 現在の実装状況（2026-08-25 時点）

| 機能 | 状態 |
|---|---|
| 歌詞ナビ プロバイダ | ✅ 実装・実サイトテスト済み |
| うたてん プロバイダ | ✅ 実装・実サイトテスト済み |
| J-Lyric プロバイダ | ✅ 実装・実サイトテスト済み |
| 歌ネット プロバイダ | ✅ 実装・実サイトテスト済み（Cloudflare対策にcurl.exe経由） |
| プチリリ プロバイダ | ✅ 実装・実サイトテスト済み（CSRFトークンフロー） |
| オリコン プロバイダ | ✅ 実装・実サイトテスト済み（米津玄師/LemonでE2E確認済み）。artistId解決はBrave検索+永続キャッシュ（§5.6） |
| LRCLIB プロバイダ | ✅ 実装・実サイトテスト済み（洋楽+Jポップ、曲長マッチ対応） |
| Genius プロバイダ | ✅ 実装・実サイトテスト済み（トークン不要のsearch API + HTMLスクレイピング） |
| 設定パネル（プロバイダON/OFF、類似度閾値） | ✅ 実装・MusicBee上で動作確認済み |
| 設定のXML永続化 | ✅ `%APPDATA%\MusicBee\AutoLyrics\settings.xml` |
| MusicBeeへのメタデータ書き込み | ✅ プラグイン側は不要（`PluginType.LyricsRetrieval` のため本体が自動保存） |
| 同期歌詞(LRC)対応 | ✅ 実装・実サイトテスト済み（LRCLIBのタイムスタンプ付きLRC取得に対応） |
| Genius API（洋楽用） | ⬜ 未実装（現在は非公式search API + スクレイピングで稼働） |
| 自動取得イベント（再生時フック） | ⬜ 未実装 |

**次のユーザー希望ステップ: さらなるプロバイダの追加**（§7 参照。うたまっぷはサービス終了、うたてん/J-Lyrics/プチリリ/歌ネットは実装済み）

## 3. ディレクトリ構成

```text
mb_AutoLyrics/
├─ Walkthrough.md              ← 本書
├─ README.md                   ← 使い方・ビルド手順
├─ LICENSE                     ← MIT License
├─ .gitignore                  ← Git除外設定
├─ references/                 ← 参考用にクローンした元リポジトリ（Gitコミット除外）
│  ├─ KashiNavi_ref/
│  └─ PetitLyrics_ref/
├─ mb_AutoLyrics.sln
├─ MusicBeePlugin/          # プラグイン本体 (net48 クラスライブラリ)
│  ├─ MusicBeePlugin.csproj # SDK形式。TargetFramework=net48, UseWindowsForms=true
│  │                        # Microsoft.NETFramework.ReferenceAssemblies パッケージ必須
│  ├─ MusicBeeInterface.cs  # MusicBee公式API定義
│  ├─ Plugin.cs             # Initialise / Configure(設定UI) / SaveSettings
│  ├─ Plugin.Lyrics.cs      # LyricProviders配列 / GetProviders / RetrieveLyrics
│  ├─ Settings.cs           # PluginSettings (XmlSerializerで永続化)
│  ├─ WebClientEx.cs        # Cookie+Referer+8秒タイムアウト対応WebClient
│  ├─ Common/
│  │  ├─ CurlHelper.cs      # curl.exe経由の安全なGET（Cloudflare/JA3回避）
│  │  └─ HtmlCleaner.cs     # ルビ・タグ除去
│  ├─ Matching/TextMatcher.cs      # 正規化 + Levenshtein類似度 + カッコ書き除去
│  └─ Providers/
│     ├─ ILyricsProvider.cs # 歌詞プロバイダ抽象
│     ├─ ISyncedLyricsAwareProvider.cs # 同期歌詞プロバイダ抽象
│     ├─ IDurationAwareProvider.cs # 曲長考慮プロバイダ抽象
│     ├─ IDebuggableProvider.cs    # CLIデバッグ抽象
│     ├─ ProviderRegistry.cs       # プロバイダ一元管理
│     ├─ KashiNaviProvider.cs
│     ├─ UtaTenProvider.cs     # うたてん (utaten.com)
│     ├─ JLyricProvider.cs     # J-Lyric (j-lyric.net)
│     ├─ UtaNetProvider.cs     # 歌ネット (uta-net.com, curl.exe経由)
│     ├─ PetitLyricsProvider.cs # プチリリ（CSRFトークンフロー）
│     ├─ LrcLibProvider.cs      # LRCLIB（洋楽第一選択、同期歌詞/LRC、curl.exe経由）
│     ├─ GeniusProvider.cs      # Genius（洋楽第二選択、search API + HTMLスクレイピング）
│     └─ OriconProvider.cs     # オリコン（Brave検索+キャッシュでartistId解決、最下位優先）
├─ LyricsTester/            # MusicBee不要のCLIテスター (net48 コンソールアプリ)
│  └─ Program.cs            # 使い方: LyricsTester <artist> <title> [album] [--synced] [--provider <名前>] [--debug]
└─ mb_AutoLyrics.Tests/     # xUnit単体テストプロジェクト
   ├─ TextMatcherTests.cs
   └─ HtmlCleanerTests.cs
```

## 4. ビルド & デプロイ & テストの手順

### ビルド
```powershell
cd mb_AutoLyrics
dotnet build mb_AutoLyrics.sln -c Release
# 成果物: MusicBeePlugin\bin\Release\net48\mb_AutoLyrics.dll
```
- .NET SDK 8 で net48 をビルドするため `Microsoft.NETFramework.ReferenceAssemblies` NuGetパッケージがcsprojに必須
- Visual Studio不要

### デプロイ
```powershell
# MusicBeeが起動中だとDLLがロックされるため、必ず終了させてから実行
Copy-Item "MusicBeePlugin\bin\Release\net48\mb_AutoLyrics.dll" `
  "$env:APPDATA\MusicBee\Plugins\" -Force
```
- 配置後は **MusicBeeの再起動が必要**（プラグインは起動時にロードされる）
- 配置確認は両ファイルの `Get-FileHash` 比較が確実

### CLIテスター（MusicBee外での動作検証）
```powershell
.\LyricsTester\bin\Release\net48\LyricsTester.exe "米津玄師" "Lemon"
# 全プロバイダを順に試す。--provider "Uta-Net" で絞り込み、--debug で検索ページの解析情報を表示
```
- **新しいプロバイダ実装時は必ずLyricsTesterで通してからMusicBeeに配置すること**（ループが速い）
- 注意: PowerShellコンソール上の日本語出力は文字化けするがファイルリダイレクト（`> out.txt` + `Get-Content -Encoding UTF8`）で正しく確認できる

### MusicBee側の確認手順
1. `設定 → プラグイン → AutoLyrics` が有効か
2. `設定 → タグ(2) → 歌詞 → 歌詞プロバイダ` に「歌詞ナビ」「Uta-Net」「J-Lyric」が表示されるか（表示されない＝`GetProviders()`の戻りが古い。§5の罠を参照）
3. `設定 → プラグイン → AutoLyrics → 設定` でON/OFFと閾値を調整

## 5. 【超重要】開発中に判明した技術的発見・罠

### 5.1 歌詞ナビ (kashinavi.com)
- **2023年以降にサイト構造が大幅変更**され、元のmb_KashiNaviLyricsPluginのパーサーは完全に動かない
  - 曲ページURL: `/song_view.html?ID` → **`/lyrics/ID/`**
  - 検索結果行・歌詞ブロックのHTMLも変更
- **3つのレイアウトがランダムに混在配信される**（同じURLでもアクセスごとに違うHTMLが返る）
  1. 新形式: `<div class="kashi-japanese-block">歌詞</div>`（+Romajiブロックが別div）
  2. 旧形式: `<div class="kashi" oncopy=...>歌詞</div>`
  3. 最古形式: `<div style="user-select:none; ...">歌詞</div>`（classなし）
  - → `LyricsBlockRegexes` 配列で3段フォールバック（UtaNetProvider等でも同様の多段構えを推奨）
- **検索クエリはShift_JISバイトでURLエンコード必須**（UTF-8の%エンコードだと日本語検索が全滅する）
  - `KashiNaviProvider.EncodeQuery()` を参照
- 検索は `search.php?r=kyoku&search=<語>&m=bubun&start=1`（部分一致、スペース区切りAND検索）
- User-Agent と Referer ヘッダを設定しないと別HTML（歌詞なしページ）を返されるケースがある

### 5.2 Uta-Net (utaten.com)
- **wwwなし** (`www.utaten.com` はDNS解決失敗。`utaten.com` を使う)
- 検索エンドポイントは **`https://utaten.com/lyric/search?title=<語>&sort=popular_sort_asc`**（旧 `/search/?search_type=common&word=` は無効で人気ランキングが返る）
- 検索結果行: `<p class="searchResult__title"><a href="/lyric/{id}/">曲名</a>` + `<td class="searchResult__artist">...<a href="/artist/...">歌手</a>`
- 歌詞ページ: `<div class="lyricBody">` 内の `<div class="medium">` が日本語歌詞本体
  - **ネストしたdivがあるため、非貪欲正規表現では取れない**。`FindMatchingDivClose()` による div バランスカウントで抽出
  - ふりがなは `<span class="rt">よみ</span>` として挿入される → **除去必須**（でないと歌詞に読み仮名が混入）
  - `<br />` の後にソース上の改行が入るため、`\s*<br\s*/?\s*>\s*` → `\n` の一括置換で整形しないと空行が二重化する

### 5.3 J-Lyric (j-lyric.net)
- UTF-8サイト。検索は **`https://j-lyric.net/search.php?kt=<語>&ct=2`**（kt=曲名, ct=2=中間一致。`key` パラメータはJS経由でないと無効）
- 検索結果行: `<p class="mid"><a href="/artist/{aid}/{lid}.html">曲名</a></p><p class="sml">歌：<a href="...">歌手</a>`
- 歌詞ページ: `<p id="Lyric">歌詞</p>`
  - ふりがなは `<ruby><rb>字</rb><rp>(</rp><rt>よみ</rt><rp>)</rp></ruby>` → **rp/rtを除去、rbの中身は残す**

### 5.4 プチリリ (petitlyrics.com) — CSRFトークンフロー
- **htsign氏の `netsphere-labs/mb_PetitLyricsPlugin`（`references/PetitLyrics_ref/` にクローン済み）の実装が決定打**。2018年のものだがフローは現行でも有効
- 取得フロー（順序厳守）:
  1. `GET /search_lyrics?title=<語>` で検索（結果は `<span class="lyrics-list-title">` / `lyrics-list-artist`）
  2. **`GET /lyrics/{id}`（曲ページ本体）を訪問してセッション確立** ← これを省略するとPOSTが400になる（元実装のコメント「向こうのサーバーを欺く為」）
  3. `GET /lib/pl-lib.js` から **セッション毎に生成される `X-CSRF-Token`** を正規表現で抽出
  4. **ヘッダを `Headers.Clear()` して作り直し**（`Accept: */*`, `Pragma: no-cache`, `X-Requested-With`, `X-CSRF-Token` 等）して `POST /com/get_lyrics.ajax`（body: `lyrics_id={id}`）
  5. レスポンスは `{"lyrics":"<Base64>"}` の配列 → 行ごとにBase64デコード
- 歌詞はcanvas描画のコピーガリアリだが、上記APIでテキストが取れる
- 同一曲の投稿が複数ある場合は最初のマッチを採用
- `Expect: 100-continue` 無効化（`ServicePointManager.Expect100Continue = false`）も `WebClientEx` に実装済み

### 5.5 歌ネット (www.uta-net.com) — Cloudflare対策
- **Cloudflareのチャレンジ（"Just a moment..."）が.NET FrameworkのTLSフィンガープリント(JA3)をブロック**する（HTTP 403）。python/curlは通るのでヘッダではなくTLSレイヤーの問題
- **回避策: `curl.exe`（Windows 10 1803+に同梱）をプロセス起動してGETする**（`UtaNetComProvider.CurlGet()`）。`--fail` 付きで非2xxをnullに
- 検索: `https://www.uta-net.com/search/?Keyword=<語>&target=tit&type=in`（UTF-8）
- 結果行: `<a href="/song/{id}/"...><span class="fw-bold songlist-title">曲名</span>` + `<td class="sp-none fw-bold"><a href="/artist/...">歌手</a>`
- 歌詞: `<div id="kashi_area" itemprop="text">歌詞</div>`（シンプルに非貪欲で取れる）

### 5.6 オリコン (www.oricon.co.jp) — 検索エンジン依存のartistId解決
- 歌詞サービスは現役。URL形式:
  - アーティスト歌詞一覧: `https://www.oricon.co.jp/prof/{artistId}/lyrics/title/`
  - 曲ページ: `/prof/{artistId}/lyrics/{songId}/`
  - 歌詞本文: `<div class="all-lyrics"...><p>歌詞</p>`（ルビなし、非貪欲で取得可）
- **【重要】オリコンのページはShift_JISエンコード**（`<meta charset="shift_jis">`）。UTF-8でデコードすると文字化けしてアーティスト名検証が必ず失敗する。`Encoding.GetEncoding("Shift_JIS")`を使用
- **サイト内検索（/search/result.php）は404で完全廃止**。フォームだけ残存している罠
- **artistId解決は検索エンジンに依存**:
  - 第一案のDuckDuckGo HTML検索は**長期のIPレート制限（202 anomaly）で実質使用不能化**
  - 現行: **Brave Search** (`https://search.brave.com/search?q=site:oricon.co.jp/prof {artist} 歌詞`) を**curl.exeプロセス呼び出し**で利用（.NET HttpClientはTLS指紋でタイムアウトさせられるため）
  - **検索エンジンは連続使用ですぐ429/anomalyになる**。対策として3秒スロットル+5秒待機リトライ1回+**永続キャッシュ**（`{settingsDir}\AutoLyrics\oricon_artists.cache`: `正規化アーティスト名\tartistId` のTSV）。アーティスト毎に生涯1回のエンジンアクセスで済む
  - artistIdの妥当性検証: 歌詞一覧ページ内にアーティスト名が存在するか確認（Shift_JISデコード後）
- **歌詞一覧は30曲/ページのページングあり**（`/lyrics/title/p/{n}/`）。多作なアーティストだと5ページ超えることがある（米津玄師=126曲/5ページ、Lemonはp5）。`SearchSongIdWithPagination` で最大8ページまで追跡
- **プロバイダ配列の最後（最優先度最低）に登録**することがユーザー指定の運用方針

### 5.7 LRCLIB (lrclib.net) — 洋楽の第一選択
- **認証不要・完全無料のJSON API**。洋楽のカバー率が非常に高く、Jポップもそこそこ拾える
- エンドポイント:
  - `GET /api/get?artist_name=&track_name=&album_name=&duration=` … 完全一致検索（album/durationを渡すと**厳密一致条件になる**ので、ヒットしない場合はalbum無しで再試行が必要）
  - `GET /api/search?track_name=&artist_name=` … 部分一致候補リスト（`q=`も可）
  - レスポンス: `plainLyrics`（平文）と `syncedLyrics`（LRC形式）両方。タグ保存にはplainLyricsを使う
- **実装上の注意**:
  - 曲名からカッコ書き（`(Single Ver.)`等）を除去したクエリでの再試行が必須。ただし`TextMatcher.StripBracketGroups`は比較用正規化（小文字化・記号除去）まで行うため**検索クエリには使えない**。プロバイダ内に素のカッコ除去（`TitleBracketRegex`）を用意した
  - `IDurationAwareProvider.FetchLyrics(artist, title, album, duration?)` を実装。MusicBeeからは`RetrieveLyrics`の`sourceFileUrl`経由で`Library_GetFileProperty(file, FilePropertyType.Duration)`を呼び、曲長(秒)を渡す。曲長±2秒でスコア加算
  - 候補選択: 曲名類似度×2 + アーティスト類似度 + durationボーナス。instrumentalフラグの曲は除外
  - JSONパースは`JavaScriptSerializer`（csprojに`System.Web.Extensions`参照追加済み）
- **品質注意**: ユーザー投稿型のため、中国語→ベトナム語翻訳混載などの低品質エントリがある。日本サイト優先の順位設計はこの理由でもある

### 5.8 Genius (genius.com) — 洋楽の第二選択
- **公式APIは歌詞本文を返さない**が、フロントエンド用の非公式検索APIが**トークン不要**で使える:
  - `GET https://genius.com/api/search/multi?q={artist title}` → JSON `response.sections[].hits[]`（`type=="song"`のみ使用。`result.title` / `result.url` / `result.primary_artist.name`）
  - 曲ページHTMLの `<div data-lyrics-container="true">` に歌詞がある。**属性がclassより先に来る**ので正規表現は `<div[^>]*data-lyrics-container="true"[^>]*>` の形で書くこと
  - コンテナdivはネストするため、開始タグ位置から `<div`/`</div>` を数えるバランス走査で内容を切り出す（`ExtractBalancedDivContent`）
  - `data-exclude-from-selection="true"` のdiv（ヘッダー・ボタン類）は事前にバランス走査で除去（`RemoveBlocksWithMarker`）
  - ページ先頭に `[曲名 歌詞]` というテキストノードが混入するケースがあるため、抽出後に `^\[.+歌詞\]` 行を除去
  - `[Verse 1]` `[Chorus]` 等のセクションマーカーはGeniusの正規フォーマットなので保持する
- マッチング: `title`と`primary_artist.name`で厳格判定（v0.4ポリシー準拠）。日本語曲もヒットするが品質は日本サイトの方が上
- **KKBOX (www.kkbox.com) は不採用**: 曲ページがAWS WAFのJSチャレンジ（202+goku_props）でブロックされ、curl/.NETから取得不可。検索ページだけは通るが歌詞は曲ページにある

### 5.9 共通のマッチング戦略（Beeniusの発想を移植）
- **検索語からカッコ書きを除去してから検索する**（`(Single Ver.)` 等が残っているとAND検索で該当ゼロになる）
  - `TextMatcher.StripBracketGroups()`
- マッチ判定は「完全一致 → 正規化後のLevenshtein類似度（閾値は設定パネルで50〜100%調整可）→ カッコ除去後の比較」の多段
- 閾値は `Plugin.Settings.TitleMatchThresholdPercent / ArtistMatchThresholdPercent` で全プロバイダ共有
- **【重要】アーティスト厳格マッチ（v0.4〜）**: アーティスト名が指定されている場合、**アーティストが一致した候補のみ返す**。曲名だけ一致する同名異アーティスト曲（実例: 「永遠の微笑み」は成底ゆう子とバナナフリッターズの両方が存在）を誤取得しないための必須ルール。アーティストが空の場合のみ曲名一致の最初の候補へフォールバックを許可。全プロバイダ共通の実装パターン:
  ```csharp
  if (artistMatched) return id;   // ループ内
  // ループ後:
  if (string.IsNullOrEmpty(artist) && bestTitleMatch != null) return ...; // artist無指定時のみ
  return null;
  ```

### 5.10 MusicBeeプラグイン一般
- **メタデータへの歌詞保存はプラグイン側で実装不要**。`about.Type = PluginType.LyricsRetrieval` + `RetrieveLyrics()` で歌詞文字列を返せばMusicBee本体が保存する（見つからない時は `null` を返す）
- MusicBeeは `GetProviders()` の戻りをタグ(2)のプロバイダ一覧にマージする
- **設定パネルのz-order罠**: WinFormsでは先に `Controls.Add` したコントロールが前面に描画される。GroupBoxを先に追加すると後から追加したコントロールが隠れる → **隠れる側を先に追加するか、追加後に `BringToFront()` / 位置再計算**
- **フォントスケール罠**: MusicBeeの設定フォントはパネルに追加された時点で適用される。パネル追加**前**に `PreferredWidth` を測ると実幅より小さくなり重なる → **`AddRange` 後に `label.Right` で位置決めする**
- DLLは `%APPDATA%\MusicBee\Plugins\` に置く（Program Files側のPluginsフォルダには他社製歌詞プラグイン `mb_OriconLyricsPlugin.dll` `mb_GLyrics.dll` 等が既存）

### 5.11 検証済みテストケース
| アーティスト / 曲名 | 結果 |
|---|---|
| 米津玄師 / Lemon | 全プロバイダ成功 |
| Ado / うっせぇわ | 全プロバイダ成功 |
| YOASOBI / 夜に駆ける (Single Ver.) | カッコ除去により全プロバイダ成功 |
| バナナフリッターズ / 永遠の微笑み | **同名異アーティスト曲の判別テスト**: J-Lyricのみ正曲ヒット（成底ゆう子の同名曲は拒否）、他はnot found |
| 存在しない曲 | 全プロバイダ安全にnull |

## 6. アーキテクチャ上の決定事項

- **プロバイダ登録は2箇所**: `Plugin.Lyrics.cs` の `LyricProviders` 配列（MusicBee用）と `LyricsTester/Program.cs` の providers 配列（テスター用）。**両方に追加すること（追加忘れが実際に発生した事故あり）**
- 設定は `PluginSettings`（XMLシリアライズ）。プロバイダのON/OFFは `Providers` リスト（Name+Enabled）で管理し、`EnsureDefaults()` が新プロバイダを自動でenabled状態で追加
- `Plugin.Settings` はinternal static。プロバイダから閾値を直接読む設計
- HTTPクライアントは元リポジトリの `WebClientEx`（Cookie+Referer維持）を継続使用。将来HttpClient化の余地あり
- エラーハンドリング: `RetrieveLyrics` は例外を握りつぶしてnull返し（MusicBeeがクラッシュしない最優先）

## 7. 次のステップ: プロバイダ追加ガイド（ユーザー希望）

新しい歌詞ソース（例: うたまっぷ、petitlyrics、Genius等）を追加する手順:

1. **サイト調査（最重要）**: Python(requests)等で以下を確認
   - 検索URLとパラメータ（フォームのHTMLを読む。JSで組み立てられるパラメータ名に注意）
   - 文字エンコーディング（Shift_JIS系サイトは `EncodeQuery` 相当が必要）
   - 検索結果行・曲ページ・歌詞ブロックのHTML構造（複数レイアウトの混在がないか、旧曲/新曲で違わないか）
   - ふりがな（ruby/rt）の有無とマークアップ
   - Bot対策（UA/Referer/Cookie要求、うたまっぷはHTTPSタイムアウトが確認済み＝要検証）
2. **`Providers/` に `XxxProvider.cs` を新規作成**: `ILyricsProvider` を実装。既存6実装をテンプレにする
3. **`Plugin.Lyrics.cs` の `LyricProviders` 配列に追加**（忘れやすい！）
4. **`LyricsTester/Program.cs` の providers 配列にも追加**
5. **LyricsTesterで実サイトテスト**（§5.11のテストケース＋独自ケース）
6. ビルド → MusicBee終了 → デプロイ → 再起動してタグ(2)に反映されているか確認

### 候補ソースのメモ
- **うたまっぷ (utamap.com)**: HTTPS接続がタイムアウト（2026-08-25確認）。HTTPや別経路を要検討
- **Genius API**: 公式API（要アクセストークン）。洋楽・ボカロ補完用。Beeniusが設計参考になるがコードコピーは不可
- **プチリリ (petitlyrics)**: 同期歌詞(LRC)を提供。LRC対応と合わせて検討の価値あり
- **歌詞GET / 歌ネット等**: 未調査。同様の手順で調査から開始

## 8. 既知の未解決事項

- 設定パネルのUIは固定座標レイアウト。プロバイダが大幅に増えると横にあふれる → FlowLayoutPanel化を推奨
- `KashiNaviProvider.DebugSearch()` はデバッグ用の残骸（LyricsTesterの--debugで使用）。プロダクション品質には整理の余地あり
- 同一楽曲での複数サイトへの連続リクエスト時のレートリミット/遅延は未実装（一括歌詞取得でサイト遮断されるリスク）
- MusicBeeの「歌詞を自動的に取得」設定との連携（優先順位）はユーザー側のタグ(2)設定依存

## 9. デバッグ用インターフェース

- `IDebuggableProvider.DebugSearch(artist, title)` を実装したプロバイダは、LyricsTesterの `--debug` で検索過程の詳細（ページ長・正規表現ヒット数・候補一覧）を表示できる
- 対応済み: KashiNaviProvider / PetitLyricsProvider / LrcLibProvider / GeniusProvider / OriconProvider。

---

## 10. v0.4.0 OSS公開に伴う改修記録（2026-09-04）

GitHubでのOSS公開にあたり、以下のバグ修正・最適化・インフラ整備を実施した。

### 10.1 バグ修正 & 堅牢化
1. **GeniusProvider の境界外例外バグ**:
   - `RemoveBlocksWithMarker` メソッドでタグ不整合時に `end = result.Length` となり、文字列長を超えて `ArgumentOutOfRangeException` が発生する潜在不具合を修正。安全な境界チェックを実装。
2. **プチリリの検索精度向上**:
   - 検索URLに `artist` パラメータ（`&artist=...`）を付与。同名異曲が多い人気曲（米津玄師「Lemon」等）が1ページ目の10件から漏れて `not found` になっていた問題を解消。
3. **オリコンの高速化 & 誤判定防止**:
   - `ArtistPageMatches` 内でページ全体の巨大テキスト（数万文字）に対して無駄なLevenshtein計算を行っていた処理を撤廃。HTMLの `<title>` および `<h1>` からアーティスト名をピンポイント抽出して照合するよう高速化。
4. **Settings.Save のディレクトリ自動生成**:
   - 設定保存先 `AutoLyrics/settings.xml` の親ディレクトリ（`AutoLyrics` フォルダ）が存在しない場合、初回保存で失敗していた問題を `Path.GetDirectoryName()` による確実なディレクトリ生成に修正。

### 10.2 パフォーマンス最適化 & 同期歌詞（LRC）対応
1. **8秒タイムアウト制御**:
   - `WebClientEx` のデフォルトタイムアウト（.NET標準の100秒）を **8秒** に設定。応答しないサイトがあってもMusicBee本体がフリーズせず即座にフォールバック。
2. **TLS 1.2 明示的有効化**:
   - .NET Framework 4.8 環境下でモダンな HTTPS サーバー（Cloudflare等）とのハンドシェイクが失敗・遅延する問題を解消。
3. **LRCLIB の Cloudflare JA3 ブロック回避**:
   - LRCLIB に対する .NET の TLS ハンドシェイクが Cloudflare で保留されていたため、`CurlHelper.Get` を利用するようリファクタリング。0.2秒での高速応答を実現。
4. **同期歌詞（LRC）のネイティブ対応**:
   - `ISyncedLyricsAwareProvider` を新設。MusicBee側の要求および設定パネル（`同期歌詞(LRC)を優先` チェックボックス）に応じて、タイムスタンプ付きLRC形式（`[mm:ss.xx]`）の歌詞を返却可能に。

### 10.3 OSS公開インフラ
1. **単体テスト導入**:
   - `mb_AutoLyrics.Tests`（xUnit / .NET Framework 4.8）を新設。`TextMatcher`（カッコ除去、類似度、正規化）および `HtmlCleaner`（ルビ除去、タグ整形、エンティティデコード）の単体テスト **17件が全合格**。
2. **GitHub Actions CI/CD (`.github/workflows/build.yml`)**:
   - push / PR 時の自動ビルド＆テスト実行。
   - `v*` タグプッシュ時に配布用ZIP（`mb_AutoLyrics.dll` + `README.md` + `LICENSE`）を自動ビルドし、GitHub Releases へ自動公開。
   - ※GitHub側の `Settings` -> `Actions` -> `General` -> `Workflow permissions` で `Read and write permissions` の有効化、およびワークフロー内の `permissions: contents: write` が必須。
3. **公開リポジトリ**:
   - [https://github.com/treeturtlefall-arch/mb_AutoLyrics](https://github.com/treeturtlefall-arch/mb_AutoLyrics) (MIT License)