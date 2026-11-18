# 開発版の代表14ケースの測定結果

コミット `4138d03f0a3315a630fb8250cd97260aad396d47` では、READMEの代表14ケースすべてでZendiatorの平均時間が最小だった。正順・逆順のどちらでも、Zendiatorの信頼区間の上限が、その行で測定した全競合の下限を下回った。各順序で83セルを測定した。NotificationはImmediate.Handlersに該当する結果がないため5社、ほかの行は6社で比較した。[丸め前の全166件、信頼区間、割当量、測定記録のハッシュ](4138d03-representative-summary.csv)

## 平均時間

「次点」には各順序で最速だった競合を示す。時間は1操作あたりで、Streamの途中終了は16件の入力から最初の1件を読み、破棄が終わるまでを含む。ImmediateはImmediate.Handlersを指す。

| ケース | 単位 | 正順 Zendiator | 正順 次点 | 逆順 Zendiator | 逆順 次点 |
|---|---|---:|---|---:|---|
| Send・Behavior 0段 | ns | 1.89 | Immediate 9.57 | 2.15 | Immediate 11.51 |
| Send・Behavior 5段 | ns | 6.64 | Immediate 23.51 | 7.41 | Immediate 34.36 |
| 同一scopeで再取得＋Send・0段 | ns | 39.89 | Immediate 52.94 | 39.93 | Immediate 51.22 |
| 同一scopeで再取得＋Send・5段 | ns | 55.40 | Immediate 59.78 | 38.33 | Immediate 61.13 |
| 新規scope＋1 Send＋破棄・5段 | ns | 145.66 | Immediate 172.30 | 140.34 | Immediate 170.38 |
| Notification・16購読者・同期 | ns | 153.20 | DispatchR 209.26 | 168.60 | DispatchR 241.95 |
| Stream全1,024件・5段・同期 | µs | 13.29 | Immediate 109.26 | 14.51 | DispatchR 117.12 |
| Stream全1,024件・5段・非同期 | µs | 196.97 | DispatchR 325.61 | 200.76 | DispatchR 326.89 |
| Stream途中終了・5段・同期 | ns | 103.64 | Immediate 345.02 | 118.06 | Immediate 368.19 |
| Stream途中終了・5段・非同期 | µs | 1.824 | Immediate 2.274 | 1.858 | Immediate 2.549 |
| Behavior短絡・3段・繰り返し | ns | 4.971 | Immediate 5.449 | 5.299 | Immediate 6.613 |
| 非同期Hook成功・初回 | µs | 1.104 | DispatchR 1.388 | 1.142 | DispatchR 1.502 |
| 非同期Hook成功・繰り返し | ns | 42.87 | Immediate 72.12 | 43.72 | Immediate 63.05 |
| 非同期Hook失敗・初回・外側 | µs | 5.734 | DispatchR 8.913 | 5.415 | MediatR 8.576 |

同期Stream途中終了の割当量は両順ともZendiatorが200 B/op、Immediateが1,104 B/opだった。旧READMEのZendiatorは393.74 ns・1,016 B/opで、測定時期もソースも異なる。今回も新規scopeでのSendはZendiatorが624 B/op、Immediateが568 B/opだった。非同期Streamの全件列挙ではZendiatorが約115.5 kB/opを割り当てる。時間と割当量はそれぞれの列で判断できる。[0.7.0の測定値](0.7.0-2bc3726-summary.csv)・[今回の全測定値](4138d03-representative-summary.csv)

## 実装

5段がそれぞれ前処理を行い、同じ要求とトークンで開いた上流カーソルをそのまま返す場合、生成コードは各段のmiddlewareを呼び、1個のカーソルで列挙と破棄を管理する。入力の取得時期、段間のキャンセル確認、非同期Openのコンテキスト境界を保つ。適用対象は、全段の転送をコンパイル時に確認できる、参照型の名前付き入力カーソルを使う経路である。[適用条件とinterceptorを使う場合の扱い](../../docs/streaming-iterators.md#forwarding-middleware-on-a-named-cursor)

Send側では、同期成功時のwrapperの状態から例外処理用のbuilderを外した。また、既定の依存キャッシュで取得済みの受け手を読む際、非nullの参照を直接返すようにした。これらを組み合わせた製品経路の順位が上表の結果である。[wrapperの生成](../../src/Zendiator.SourceGenerator/Emission/CanonicalWrapperSourceEmitter.cs)・[依存キャッシュの生成](../../src/Zendiator.SourceGenerator/Emission/CanonicalClosedServiceCacheEmitter.cs)

## 測定条件と再現

Windows 11 x64（ビルド26300.9974）、Intel Core Ultra 7 258V、SDK 10.0.404、.NET 10.0.14、BenchmarkDotNet 0.15.8。Release、workstation GC、affinity mask 1、warmup 20回、計測12回、要求500 ms、順序ごとに各セル1 launchとした。4スイートのpackage lockと既存のworkloadを使った。ZendiatorのStream入力は名前付きカーソルで、競合はコンパイラ生成のiteratorを使う。Streamの各段で行う処理もライブラリ間で異なる。[公開表のケース定義と比較条件](../performance-plan/README.md#public-row-mapping-and-comparison-boundaries)

測定したコミットをクリーンにcheckoutし、`post-060-full-baseline.json` の先頭83件を元の順でcustom matrixに保存する。リポジトリのルートから次のPowerShellで測定時のmatrixを再作成できる。`$matrix` は作業ツリー外の新しいファイルの絶対パスとする。

```powershell
$all = Get-Content -Raw 'benchmarks/performance-plan/post-060-full-baseline.json' | ConvertFrom-Json
ConvertTo-Json -Depth 4 -InputObject @($all[0..82]) | Set-Content -Encoding utf8 -LiteralPath $matrix
```

`run_interleaved.py` に `--group custom --matrix <matrix> --expect-sdk 10.0.404 --output <output>` を渡す。正順・逆順は別の出力先を使う。逆順では、同じ引数に `--reverse` を加え、`--phase prepare`、各セルを1から83まで順に `--phase case --case-index <i> --resume`、最後に `--phase finish --resume` を実行する。[既存の測定ドライバ](../Zendiator.UseCaseBenchmarks/run_interleaved.py)

両順のmatrixのSHA-256は `A7FA58B6935F61799E3F328CDEEC12C1D00FA0388FC0F048BD7C5F003D6C3111`。各セルで測定したconsumerの検証が完了し、子プロセスの開始時刻から完全な逆順を確認した。正順の最初と最後の子の開始時刻は2026-11-18 14:10:26–15:00:55 UTC、逆順は同日15:03:06–16:09:40 UTCだった。

逆順は67セル終了後、Windowsが再利用したPIDの所有権を確認できず一時停止した。待機中に最後の3行を独立した18セルとして計測した。PID解放後に元のドライバが所有権を再確認し、元のセッションの68番から再開した。上表とCSVには、正順・逆順それぞれの元の83セルを使用した。順位が適用される範囲は、このマシン、このバージョン、公開表の14ケースである。
