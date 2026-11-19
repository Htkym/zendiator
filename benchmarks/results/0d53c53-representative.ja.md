# Stream の転送判定を修正した開発版の測定

READMEの代表14ケースすべてで、正順・逆順ともにZendiatorの平均時間が最小だった。13ケースではZendiatorの信頼区間の上限が、両順とも全競合の下限を下回った。残る非同期Stream途中終了は両順とも平均が最小だったが、競合との信頼区間が重なり、速度差は確定できない。[全166件の平均、信頼区間、割当量、測定記録のハッシュ](0d53c53-representative-summary.csv)

対象は `0d53c537a66c768346cfc5f04dae07e45fe435ff`。ミドルウェアを通常どおり登録し、利用側は `mediator.StreamAsync(request, token)` で列挙する。各ミドルウェアが実際に返した列挙子と、対応する上流で開いた列挙子が同一で、キャンセルトークンも一致すれば、Mediator が末端から直接読み取る。途中で列挙子を包んだ場合は、その列挙子の `MoveNextAsync`、`Current`、`DisposeAsync` を使う。[利用例](../../docs/streaming-iterators.md#forwarding-or-decorating-a-cursor)

## 代表ケースの平均時間

各行の次点は、その順序で平均時間が最小だった競合。ImmediateはImmediate.Handlersを指す。

| ケース | 単位 | 正順 Zendiator | 正順 次点 | 逆順 Zendiator | 逆順 次点 |
|---|---|---:|---|---:|---|
| Send・Behavior 0段 | ns | 1.645 | Immediate 11.82 | 1.739 | Immediate 11.41 |
| Send・Behavior 5段 | ns | 6.708 | Immediate 20.95 | 6.397 | Immediate 20.25 |
| 同一scopeで再取得＋Send・0段 | ns | 36.69 | Immediate 47.57 | 35.25 | Immediate 44.22 |
| 同一scopeで再取得＋Send・5段 | ns | 36.98 | Immediate 58.94 | 35.57 | Immediate 59.94 |
| 新規scope＋1 Send＋破棄・5段 | ns | 126.38 | Immediate 173.62 | 135.44 | Immediate 159.43 |
| Notification・16購読者・同期 | ns | 140.12 | Mediator 229.14 | 145.94 | DispatchR 204.91 |
| Stream全1,024件・5段・同期 | µs | 15.22 | Immediate 110.91 | 15.12 | Mediator 110.22 |
| Stream全1,024件・5段・非同期 | µs | 201.29 | DispatchR 317.36 | 192.90 | DispatchR 323.66 |
| Stream途中終了・5段・同期 | ns | 317.31 | Immediate 385.47 | 296.50 | DispatchR 379.99 |
| Stream途中終了・5段・非同期 | µs | 2.260 | Immediate 2.508 | 2.048 | Immediate 2.283 |
| Behavior短絡・3段・繰り返し | ns | 4.830 | Immediate 6.008 | 4.635 | Immediate 5.444 |
| 非同期Hook成功・初回 | µs | 1.023 | DispatchR 1.335 | 0.995 | DispatchR 1.231 |
| 非同期Hook成功・繰り返し | ns | 37.93 | Immediate 58.05 | 36.08 | Immediate 57.34 |
| 非同期Hook失敗・初回・外側 | µs | 4.422 | Mediator 7.776 | 4.578 | DispatchR 7.846 |

同期Stream途中終了では、次点に対して正順で17.7%、逆順で22.0%短く、割当量は両順とも976 B/opだった。非同期Stream途中終了の信頼区間は、正順でZendiatorが2.013–2.507 µs、Immediateが2.309–2.706 µs、逆順でZendiatorが1.847–2.249 µs、Immediateが2.107–2.459 µsだった。

## 書き方と所有権の確認

以前の開発版では、直接 `return next.OpenAsync(request, token)` と書いた箇所を別のソース生成器がインターセプトすると、最初の読み出しで `InvalidCastException` が発生した。修正版の回帰テストでは、7段の各段が同じ入力を装飾する。直接返す場合とローカル変数を経由する場合の両方で、元の値42に7段の処理が加わり、途中終了時に入力と7個の装飾用列挙子がそれぞれ1回ずつ破棄された。[回帰テスト](../../tests/Zendiator.Generator.Tests/CanonicalStandardStreamContractTests.cs)

別のコンシューマーでは、0・1・2・3・4・5・6・7・9・12・17段の各構成で、入力を同期・非同期で開き、11件の入力を1件、4件、11件、またはEOFまで読んだ。各段の呼び出し順、値、読み出し回数、単回の破棄を照合した。同期で4件読む操作を各条件10,000回実行すると、直接返す場合とローカル変数を経由する場合で、11構成すべての割当量が一致した。例として、2段で528 B/op、5段で960 B/op、6段で1,104 B/op、17段で2,688 B/opだった。このコンシューマーの入力と処理内容はREADMEの速度比較とは異なる。

このコミットから作ったNuGetパッケージを別アプリから参照し、managed版とwin-x64 NativeAOT版の両方で、標準列挙、型付き終端、従来のミドルウェアとの相互呼び出し、破棄を確認した。[パッケージの検証プログラム](../../scripts/struct-upstream-package-smoke-test.ps1)

Releaseビルドとソリューションの全3,687テストが通過し、スキップは0件だった。

## 同期Stream途中終了の試作比較

READMEと同じ16件の入力で1件目を読み、列挙子の破棄までを計時した。各行は独立した測定で、同じ回のImmediate.Handlersを併記する。

| コミットと変更 | Zendiator 平均 ns/op | Immediate 平均 ns/op | Zendiator B/op |
|---|---:|---:|---:|
| `53a169d` 実際の戻り値で転送を判定し、5段の上限を撤去 | 470.66 | 415.68 | 1,016 |
| `332586f` 新しい子カーソルの初回の排他取得と参照保持を簡略化 | 439.03 | 371.00 | 976 |
| `0d53c53` 切り離した段の破棄と排他の解放を簡略化 | 362.32 | 385.93 | 976 |

最後の回の信頼区間はZendiatorが352.62–372.02 ns、Immediateが374.46–397.40 nsだった。生成器はミドルウェアの構文、登録段数の上限、ベンチマークの型名や入力件数を転送の判定に使わない。ベンチマーク本体と依存パッケージのロックファイルは3回とも同じものを使った。従来の開発版で記録した200 B/opは、修正前の実装で測った値である。[従来の測定記録](4138d03-representative.md)

## 再現条件

READMEの14行に対応する83セルを公開マトリクスから取り出し、競合を含む各セルを独立した子プロセスで測定した。Notificationは適用可能な5社、ほかは6社。Release、.NET SDK 10.0.404、.NET 10.0.14 x64、workstation GC、CPU affinity 1、ウォームアップ20回、計測12回、指定反復時間500 ms、各セル1 launch。Streamの各社の入力実装とDI構成は公開ベンチマークの定義に従う。[公開マトリクス](../performance-plan/post-060-full-baseline.json)・[比較対象の違い](../performance-plan/README.md#public-row-mapping-and-comparison-boundaries)

対象コミットのクリーンな作業ツリーで、`$matrix` にツリー外のJSONファイルのパスを指定して次を実行する。

```powershell
$all = Get-Content -Raw benchmarks/performance-plan/post-060-full-baseline.json | ConvertFrom-Json
ConvertTo-Json -Depth 4 -InputObject @($all[0..82]) | Set-Content -Encoding utf8 -LiteralPath $matrix
```

正順は `run_interleaved.py --group custom --matrix <matrix> --expect-sdk 10.0.404 --output <output>` で実行した。逆順は別の出力先を指定し、`--reverse --phase prepare`、1～83番の `--reverse --phase case --case-index <i> --resume`、`--reverse --phase finish --resume` の順に実行する。両順で使ったマトリクスのSHA-256は `A7FA58B6935F61799E3F328CDEEC12C1D00FA0388FC0F048BD7C5F003D6C3111`。[測定ドライバ](../Zendiator.UseCaseBenchmarks/run_interleaved.py)
