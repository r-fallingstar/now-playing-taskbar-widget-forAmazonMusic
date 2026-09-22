# Now Playing - Taskbar Widget for Amazon Music

Amazon Music の再生情報を Windows のタスクバー上に表示する、**非公式の互換フォーク**です。

このリポジトリは [mechanicwb2-hub/now-playing-taskbar-widget](https://github.com/mechanicwb2-hub/now-playing-taskbar-widget) をベースにしており、
Amazon Music の不足した SMTC メタデータを補う
[Fuku856/Amazon-Music-SMTC-Bridge](https://github.com/Fuku856/Amazon-Music-SMTC-Bridge)
と組み合わせて使います。

> **Unofficial project.** Amazon Music / Amazon / Spotify / Discord / AmazonMusic SMTC Bridge の公式製品・公式連携ではありません。
> 元プロジェクトの作者 MechanicWB とも別管理のフォークです。

## v1.0.0

会話内でテストしていた「v6」互換パッチを、公開用の **v1.0.0** としてソースへ統合したものです。

主な変更:

- `AmazonMusicSmtc_...!App` の SMTC セッションを優先して使用
- Amazon Music 本体の不完全な `AmazonMobileLLC...` セッションを選ばない
- アルバムアートを 64 KiB チャンクで読み込み、公開タイミングの競合に対してリトライ
- 表示判定を `Spotify.exe` ではなく Bridge セッション基準に変更
- Spotify 専用 UI Automation を Amazon Music 版では使用しない
- 曲名・ジャケット・余白の左クリックを無効化
- `spotify:` URI を呼ばない
- Amazon Music 版の設定・ログを `%APPDATA%\AmazonMusicTaskbarWidget\` に分離
- Spotify 専用の Like / Shuffle / Repeat / Volume は既定で非表示

## 必要なもの

- Windows 10 2004+ または Windows 11
- Windows 版 Amazon Music
- [AmazonMusic SMTC Bridge](https://github.com/Fuku856/Amazon-Music-SMTC-Bridge)
- ソースからビルドする場合は .NET 8 SDK

Amazon Music 単体の SMTC は曲名以外の情報が欠ける場合があるため、このフォークは **SMTC Bridge のセッションを前提**にしています。

## 使い方

1. Amazon Music と AmazonMusic SMTC Bridge を起動します。
2. Amazon Music で曲を再生します。
3. Releases から Windows x64 版を取得して `AmazonMusicTaskbarWidget.exe` を起動します。
4. タスクバー上のウィジェットを右クリックすると、位置・サイズ・表示ボタンなどを変更できます。

Microsoft Store 版の元ウィジェットと同時起動すると重なることがあるため、テスト時は片方を終了してください。

## 操作

Amazon Music + Bridge で主に利用する機能:

- アルバムアート / 曲名 / アーティスト表示
- 再生 / 一時停止
- 前の曲 / 次の曲
- 再生位置表示
- タスクバー上の自動配置
- 複数モニター
- サイズ / 明るさ / 表示ボタン設定

### 制限事項

- AmazonMusic SMTC Bridge はシーク要求を Amazon Music に中継しないため、進捗バーをクリックしてもシークできない場合があります。
- Spotify 固有の「お気に入り」「Smart Shuffle」「Spotify 内部音量」などはこのフォークの対象外です。
- Bridge 側または Amazon Music 側の仕様変更で動作しなくなる可能性があります。
- このフォークには AmazonMusic SMTC Bridge のコードやバイナリは含まれていません。

## ログ

```text
%APPDATA%\AmazonMusicTaskbarWidget\errors.log
```

正常時には、たとえば次のような行が出ます。

```text
Using AmazonMusic SMTC Bridge session: AmazonMusicSmtc_...!App
AmazonMusic artwork read successfully (... bytes).
```

## ソースからビルド

.NET 8 SDK をインストール後:

```powershell
dotnet publish SpotifyTaskbarWidget.csproj -c Release -r win-x64 -o publish
```

単体で動く self-contained ビルド:

```powershell
dotnet publish SpotifyTaskbarWidget.csproj -c Release -r win-x64 --self-contained true -o publish
```

出力ファイル名は `AmazonMusicTaskbarWidget.exe` です。

## Credits

- Original project: [MechanicWB / now-playing-taskbar-widget](https://github.com/mechanicwb2-hub/now-playing-taskbar-widget)
- Amazon Music SMTC metadata bridge: [Fuku856 / Amazon-Music-SMTC-Bridge](https://github.com/Fuku856/Amazon-Music-SMTC-Bridge)

## License

元プロジェクトは MIT License です。リポジトリ内の [LICENSE](LICENSE) に、元の著作権表示とライセンス全文を保持しています。

このフォークはその条件に従って改変・再配布しています。AmazonMusic SMTC Bridge は別プロジェクトであり、このリポジトリには同プロジェクトのソースを含めていません。
