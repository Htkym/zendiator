# 利用条件別のベンチマーク

[English](README.md)

チェックインしたfixtureで、ZendiatorとImmediate.Handlers 4.2.0、DispatchR.Mediator 2.3.1、Mediator 3.0.2、MediatR 14.2.0を比べます。入口と登録方法は各ライブラリの公開仕様に沿っています。全体はSend 125件、features 55件、streams 120件、relay 60件、74比較キーです。対応しない組合せは未測定として扱います。

## 比較の読み方

[README](../README.ja.md#性能比較)には、2026-10-08の全体比較から有利・不利な条件を含む8例を載せています。ソース版は `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55` です。[丸めていない抜粋データ](results/20261008-1c41d2b-summary.csv)にはMean、信頼区間の半幅であるError、StdDev、標準誤差、N、管理ヒープの割り当てを別列で載せています。

測定環境はWindows 11 x64、Intel Core Ultra 7 258V、SDK 10.0.401、.NET 10.0.12、DI 10.0.12、BenchmarkDotNet 0.15.8、Releaseです。affinity mask 1、warmup20、measurement12、要求iteration 500 ms、各ケース1 launchで、比較キーごとに先頭のライブラリを交代しました。394件の正しさのgateと、保存した各consumerのartifact gateが通っています。BDNのoverhead補正と外れ値処理後のNは各ケース9〜12でした。

測る区間はケースごとに異なります。

| ケース | 測る操作 |
|---|---|
| Typed | 取得済みの入口でSend |
| ResolveSend | 同じScopeで入口を再取得してSend |
| ScopeK1 / ScopeK10 | Scope作成、初回取得、1回 / 10回のSend、破棄 |
| ScopeOnly / ScopeResolve | Scope作成・破棄だけ / さらに入口を初回取得 |
| FirstSend | 初回取得とSendだけ。Scope作成・破棄は測定外 |
| Stream | 作成だけ、または指定範囲の列挙と破棄 |
| Relay | 同期の前処理で、次のEnumerableを直接返す |

FirstSendは測定iterationごとに事前作成したScopeを16384個使い、unroll factorは1です。Streamは1項目でなく操作全体の値です。Handlerは軽い処理で、NotificationとStreamには非同期に中断する条件もありますが、SendのHandlerは同期完了します。API形状とDI登録はライブラリ間で異なります。1 launchの値から、独立実行での順位再現、アプリ起動やHTTP・I/Oの時間、変更前後の因果的な改善、全条件の順位は確定しません。

## 再現手順

.NET 10、PowerShell、Python 3を用意します。パッケージ版はlock fileで固定しています。リポジトリのルートから実行します。

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
```

`-Group all` は、ライブラリの型ごとのブロックで全体を測ります。BDN条件は同じですが、上の比較とは実行順が違います。新しい `.local/benchmarks/<timestamp>` にログと全BDN JSONを保存し、既存の出力先は上書きしません。

比較キーごとに先頭を交代する場合は、SDK 10.0.401を使い、変更のないcommit済みcheckoutから実行します。出力はcheckoutの外に置きます。

```powershell
python benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py --group all --output D:/zendiator-results/new-run --dry-run
```

計画を確認した後、`--dry-run` を外すと測定します。`--max-cases 2` なら2ケースだけの途中結果を作れます。同じ出力先に `--resume` を指定し、`--max-cases` を外すと続行します。同じcheckout、commit、SDK、matrixと、保存した実行物の有効な証明が必要です。ビルドや別の測定と同時に実行しません。checkout内に別のcheckoutがあるとBDNが複数のprojectを見つけるため、独立したcheckoutを使います。

fixtureとmatrixは [Zendiator.UseCaseBenchmarks](Zendiator.UseCaseBenchmarks) にあります。ケースを意図して変える場合だけ `Generate.ps1` / `build_case_lists.py` で再生成し、差分と正しさのgateを確認します。`Run-Jit.ps1` や `--breakdown` はコード・割り当ての別診断で、競合ライブラリとの時間比較には混ぜません。
