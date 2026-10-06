# ユースケース別ベンチマーク

[English version](README.md)

`Zendiator.UseCaseBenchmarks` を現行実装の標準的な競合比較に使います。Send の Behavior 0/1/3/5 段、Void、Generic、Notification の購読者 0/1/4/16 件、Stream の生成・全列挙・部分列挙・早期終了・途中キャンセルを、固定した5ライブラリの公式入口で比較します。生成された fixture もリポジトリに置き、比較条件をレビューできます。

リポジトリ直下で .NET 10、PowerShell、Python 3 を使います。

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group all
```

`smoke` は新 Scope の Send と非同期 Notification の2件を確認します。`all` は Send 125件、Void・Generic・Notification 55件、Stream 120件、前処理だけの Stream Behavior（`relay`）60件です。固定パッケージを復元して Release ビルドし、各グループの測定前に正しさ394件を確認します。BenchmarkDotNet は CPU affinity 1、ウォームアップ20回、測定12回、指定 iteration time 500 ms、各ケース1 launch・別子プロセスです。BenchmarkDotNet 0.15.8 は benchmark type ごとに実行し、ここでは1 type が1ライブラリなので、ライブラリ別の連続ブロックになります。同じケースのライブラリを交互に実行したり、先頭ライブラリを交代したりはしていません。小さな時間差には長時間の状態変化が影響し得ます。実際に交互実行するには、親driverが比較キーごとに1ケース・1ライブラリを選択して順に起動し、正当性・子件数・ソース・DLL hashの確認を維持する必要があります。実行後にケース数、失敗数、ソースの不変性、製品 DLL の hash を照合します。

Send は取得済み入口（`Typed`）、同じ Scope での再取得（`ResolveSend`）、新しい Scope での1回と10回（`ScopeK1`、`ScopeK10`）に加えて、新しい Scope での初回取得を段階別に測ります。`ScopeOnly` は Scope の作成と破棄だけ、`ScopeResolve` は入口の初回取得までを含みます。`FirstSend` は iteration ごとの setup で作った Scope を1回の操作に1つずつ使い、入口の初回取得と Send だけを測ります（invocation 16384 回、unroll 1。cleanup で全 Scope の使用を確認）。HTTP や MVC のパイプライン全体は含みません。`relay` は各ライブラリの Stream Behavior を、前処理の後に next の列挙元をそのまま返す非 async 実装にそろえた独立ケースです。前処理を実行する時点と例外の出方はライブラリごとに異なるため、測定前の確認で `correctness.json` に記録します。

結果は毎回、新しい Git 管理外の `.local/benchmarks/<timestamp>` に保存します。BDN の生 JSON、警告を含むログ、子プロセスのアセンブリ hash、ソースの revision と digest、SDK 版、失敗ログを残します。全件成功時は、各ケースの mean・median・標準偏差・確保量を `all-results.csv` に、比較表を `RESULT.ja.md` に出力します。`-Group send`、`features`、`streams`、`relay` は一つのグループだけを実行します。`-OutputRoot` で新しい出力先を指定できます。既存の出力先は上書きしません。

fixture を変えるときは、`Zendiator.UseCaseBenchmarks` にある `Generate.ps1` または `build_case_lists.py` を実行し、生成された C# と JSON の差分を確認します。

機械語を調べるときは `pwsh -NoProfile -File benchmarks/Run-Jit.ps1` を実行します。Send0/Send5 の新 Scope 経路をウォームアップして、指定したメソッドの Tier1 出力を `.local` に残します。`-Pattern` で対象を変えられます。機械語のサイズだけで速度改善を判断しません。

確保量を分解するときは、Release ビルド後に `benchmarks/Zendiator.UseCaseBenchmarks` を作業ディレクトリとして `dotnet ./bin/Release/net10.0/Zendiator.UseCaseBenchmarks.dll --breakdown` を実行します。環境変数 `COLD_RUN` に新しい保存先の絶対パスを指定してください。`allocation-breakdown.json` は Scope、入口、Behavior の取得、Send の境界別、`resolver-scale.json` は 1/2/6/32/64/128 種類の依存を順方向・逆方向で取得した確保量です。反射を使う規模診断と簡易タイマーの値は、競合の速度比較には使いません。`--stream-breakdown` は Zendiator と Immediate の Stream0／Stream5／前処理だけの5段（16・1024 項目、同期・非同期）について、全列挙1回あたりの確保量を測り、GC の allocation tick の標本で型別に按分して `stream-allocations.json` に保存します。標本による按分なので、型別の値は推定です。

Send/Void は事前作成した要求と軽い同期完了ハンドラーを使います。Notification と Stream は実際の非同期中断を含みますが、非同期中断する Send、コンテナー構築、Send の例外経路、Native AOT 速度は未測定です。Immediate は要求別の生成入口、Zendiator は共通の `IZendiator.SendAsync` を使います。各ケースは1 launch のため、全体の最速順位や僅差の優劣は主張しません。

### 今後のライブラリ横断実行順

`Zendiator.UseCaseBenchmarks/interleaved_plan.py` はグループのmatrixから比較キー単位の順序を作ります。同じケースのライブラリを隣接させ、先頭ライブラリをキーごとに交代します。`--verify-log` はBDNの `run.log` をその順番と照合し、今回のtype別ブロック実行を不一致として検出します。このツールは順序の計画と照合のみで、現行 `Run-Comparison.ps1` はまだ計画どおりに実行しません。

実行driverは計画の各行についてBDNの単一ケースだけを別artifact先で起動し、起動順を保存して生結果を最後に集約する必要があります。グループ前の正当性gate、locked build、ソースとDLLのhash、子件数、FirstSendの実operation確認を維持します。実ログが計画と一致した測定だけをライブラリ横断の交互実行と記載します。