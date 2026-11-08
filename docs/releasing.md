# 署名とリリース手順

## 公開までの流れ

完全自動公開ではなく、Windows EXE の署名と最終確認を手動で行います。

| 順序 | 処理 | 実施場所 |
| --- | --- | --- |
| 1 | マージ済みのコードに `v0.1.0` などのタグを付けて push | 開発者 |
| 2 | .NET ランタイム同梱の実行ファイルをビルド | Actions |
| 3 | 各 OS の実行ファイルで `--help` を実行 | Actions |
| 4 | ZIP を Actions 成果物として保存し、macOS ZIP だけを下書き Release に添付 | Actions |
| 5 | Windows 成果物をダウンロードし、EXE を署名・タイムスタンプ付与・検証 | 開発者の署名環境 |
| 6 | 署名済み EXE を ZIP 化して下書き Release に添付 | 開発者 |
| 7 | 最終配布 ZIP 全部の SHA-256 を生成・添付し、Release を公開 | 開発者 |

起動確認ではディスクの上書きを実行しません。Actions で作られる Windows ZIP は
`DiskCleanTool-win-x64-unsigned.zip` です。未署名 ZIP は Release に添付しません。
macOS ZIP も署名・公証前なので、下書きに置いただけでは一般公開されません。

タグはこのワークフローを含むコミットに付けてください。

```powershell
git tag v0.1.0
git push origin v0.1.0
```

同じタグの Release が既に存在する場合、作成ステップは失敗します。
再実行によって公開済みの配布物や、手動で署名した配布物を上書きしないためです。
途中で失敗した場合は Actions のログと下書きの有無を確認し、
既存の下書きに不足した添付物だけを手動で追加してください。

## Windows EXE の署名

GitHub Actions の対象実行を開き、`win-x64` 成果物をダウンロードします。
成果物を展開すると、未署名 ZIP が入っています。この ZIP も展開して
`DiskCleanTool.exe`、README、LICENSE を取り出します。

完成した EXE に署名します。**署名後に EXE を再ビルド・編集しないでください。**
証明書や秘密鍵、PIN、パスワードをリポジトリやログに保存しないでください。
USB トークンなどの対話操作が必要なら、手元で署名する運用のままで問題ありません。

Windows SDK の SignTool を使える環境での例です。
`CERTIFICATE_THUMBPRINT` は利用する証明書の拇印、
`TIMESTAMP_URL` は署名サービスが指定する RFC 3161 タイムスタンプ URL に置き換えます。
クラウド署名や専用ツールの場合は、その提供元の手順を利用してください。

```powershell
signtool sign /sha1 CERTIFICATE_THUMBPRINT /fd SHA256 /tr TIMESTAMP_URL /td SHA256 .\DiskCleanTool.exe
if ($LASTEXITCODE -ne 0) { throw "署名に失敗しました。" }
signtool verify /pa /all /v .\DiskCleanTool.exe
if ($LASTEXITCODE -ne 0) { throw "署名検証に失敗しました。" }
.\DiskCleanTool.exe --help
if ($LASTEXITCODE -ne 0) { throw "起動確認に失敗しました。" }
```

証明書ストアや署名方式に応じて SignTool の引数を調整してください。
署名の検証結果で、意図した発行元とタイムスタンプを確認してください。
署名しても SmartScreen の警告が必ず消えるとは限りません。

署名済み EXE と README、LICENSE、docs ディレクトリを ZIP に入れます。
以下は、それらが現在のディレクトリにある場合の例です。
同名 ZIP が既にある場合は内容を確認してから作り直してください。

```powershell
Compress-Archive -Path .\DiskCleanTool.exe, .\README.md, .\LICENSE, .\docs -DestinationPath .\DiskCleanTool-win-x64.zip
gh release upload v0.1.0 .\DiskCleanTool-win-x64.zip --repo kkato233/disk-clean-tool
if ($LASTEXITCODE -ne 0) { throw "アップロードに失敗しました。" }
```

`gh` の操作には対象リポジトリへの認証が必要です。
GitHub の下書き Release 編集画面からのアップロードでも構いません。

## macOS と最終チェックサム

macOS の Developer ID 署名・公証は Windows EXE 署名とは別の工程です。
macOS 版は任意の寄付を推奨する形で配布します。
支援先は [GitHub Sponsors (@kkato233)](https://github.com/sponsors/kkato233) です。
スポンサー受付ページの公開までは受付準備中です。
寄付はダウンロードや利用の必須条件ではありません。
寄付金額の累計が **100 米ドル相当を超えるまでは署名・公証を行いません**。
100 米ドル相当ちょうどでは条件を満たしません。条件を満たしたら、
Apple Developer Program に加入し、署名・公証への対応を進める方針です。
資金条件の達成と技術的な対応完了は別であり、署名済み版の公開時期は確約しません。

現在のワークフローは macOS の署名・公証を実施しません。
未署名版を公開する場合は、寄付・署名方針と、
Gatekeeper が警告を表示したり実行をブロックしたりする可能性をリリース説明に明記してください。
署名・公証を導入した場合は、macOS の最終 ZIP を置き換えてからチェックサムを計算します。

下書きに添付した最終版の ZIP をすべて専用の空ディレクトリへダウンロードします。
以下は Windows x64 と macOS の 2 種類を配布する場合の例です。
署名前のローカル ZIP からではなく、実際に添付した ZIP から計算します。

```powershell
gh release download v0.1.0 --repo kkato233/disk-clean-tool --pattern "DiskCleanTool-*.zip"
if ($LASTEXITCODE -ne 0) { throw "ダウンロードに失敗しました。" }
$names = @(
    "DiskCleanTool-win-x64.zip",
    "DiskCleanTool-osx-x64.zip",
    "DiskCleanTool-osx-arm64.zip"
)
$lines = foreach ($name in $names) {
    $hash = Get-FileHash -LiteralPath $name -Algorithm SHA256 -ErrorAction Stop
    "{0}  {1}" -f $hash.Hash.ToLowerInvariant(), $name
}
$lines | Set-Content -LiteralPath SHA256SUMS.txt -Encoding ascii
gh release upload v0.1.0 .\SHA256SUMS.txt --repo kkato233/disk-clean-tool
if ($LASTEXITCODE -ne 0) { throw "チェックサムのアップロードに失敗しました。" }
```

添付済みファイルの修正が必要なら、下書きの該当添付物を置き換え、
`SHA256SUMS.txt` も作り直してください。公開後の配布物は原則として変更せず、
修正版には新しいバージョンタグを使用してください。

## 公開前チェック

- Release がまだ下書きであり、タグ・ソース・バージョンが意図したものか確認する。
- Windows ZIP に入った EXE の署名・発行元・タイムスタンプを確認する。
- 最終 ZIP を展開して各 OS で `--help` が動作するか確認する。
- 未署名の Windows ZIP が Release に混ざっていないか確認する。
- 最終 ZIP と `SHA256SUMS.txt` が一致するか確認する。
- macOS の寄付・署名方針、実際の署名・公証状況、Gatekeeper の警告、ディスク消去の制限をリリース説明に明記する。

確認後、GitHub の Release 編集画面で公開します。CLI なら次の操作です。

```powershell
gh release edit v0.1.0 --repo kkato233/disk-clean-tool --draft=false
if ($LASTEXITCODE -ne 0) { throw "公開に失敗しました。" }
```

公開までは一般利用者向けのダウンロードリンクを「公開準備中」として扱います。
署名用証明書の準備や実際の署名、タグの push、Release の公開は、この設定追加だけでは行われません。
