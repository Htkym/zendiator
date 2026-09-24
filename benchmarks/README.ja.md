# ユースケース別ベンチマーク

[English version](README.md)

`Zendiator.UseCaseBenchmarks` を現行実装の標準的な競合比較に使います。Send の Behavior 0/1/3/5 段、Void、Generic、Notification の購読者 0/1/4/16 件、Stream の生成・全列挙・部分列挙・早期終了・途中キャンセルを、固定した5ライブラリの公式入口で比較します。生成された fixture もリポジトリに置き、比較条件をレビューできます。

リポジトリ直下で .NET 10、PowerShell、Python 3 を使います。

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group all
```

`smoke` は新 Scope の Send と非同期 Notification の2件を確認します。`all` は Send 80件、Void・Generic・Notification 55件、Stream 120件です。固定パッケージを復元して Release ビルドし、各グループの測定前に正しさ304件を確認します。BenchmarkDotNet は CPU affinity 1、ウォームアップ20回、測定12回、指定 iteration time 500 ms、各ケース1 launch・別子プロセスです。実行後にケース数、失敗数、ソースの不変性、製品 DLL の hash を照合します。

結果は毎回、新しい Git 管理外の `.local/benchmarks/<timestamp>` に保存します。BDN の生 JSON、警告を含むログ、子プロセスのアセンブリ hash、ソースの revision、失敗ログを残します。全件成功時は、各ケースの mean・median・標準偏差・確保量を `all-results.csv` に、比較表を `RESULT.ja.md` に出力します。`-Group send`、`features`、`streams` は一つのグループだけを実行します。`-OutputRoot` で新しい出力先を指定できます。既存の出力先は上書きしません。

fixture を変えるときは、`Zendiator.UseCaseBenchmarks` にある `Generate.ps1` または `build_case_lists.py` を実行し、生成された C# と JSON の差分を確認します。

機械語を調べるときは `pwsh -NoProfile -File benchmarks/Run-Jit.ps1` を実行します。Send0/Send5 の新 Scope 経路をウォームアップして、指定したメソッドの Tier1 出力を `.local` に残します。`-Pattern` で対象を変えられます。機械語のサイズだけで速度改善を判断しません。

Send/Void は事前作成した要求と軽い同期完了ハンドラーを使います。Notification と Stream は実際の非同期中断を含みますが、非同期中断する Send、コンテナー構築、Send の例外経路、Native AOT 速度は未測定です。Immediate は要求別の生成入口、Zendiator は共通の `IZendiator.SendAsync` を使います。各ケースは1 launch のため、全体の最速順位や僅差の優劣は主張しません。
