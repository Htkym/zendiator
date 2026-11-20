# Mediatorの性能比較

[English](README.md)

Zendiator 1.0.1、Immediate.Handlers 4.3.0、MediatR 15.0.0、Mediator 3.0.2、DispatchR.Mediator 2.3.1、Foundatio.Mediator 1.3.3を代表14ケースで比較しました。正順・逆順ともに、Zendiatorの平均処理時間は全ケースで最短でした。

## ケース別の測定結果

各セルは1回あたりの平均処理時間とメモリ割り当て量です。Immediate.Handlersの通知には対応するケースがないため、`N/A` としています。[全ケースの比較表](../README.ja.md#性能)・[99.9%信頼区間を含む全166件のCSV](results/f2973af-representative-summary.csv)

### Send（0段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 2.04 ns / 0 B | 1.65 ns / 0 B |
| Immediate.Handlers 4.3.0 | 13.19 ns / 0 B | 9.22 ns / 0 B |
| MediatR 15.0.0 | 70.45 ns / 200 B | 44.16 ns / 200 B |
| Mediator 3.0.2 | 70.59 ns / 64 B | 45.71 ns / 64 B |
| DispatchR.Mediator 2.3.1 | 45.65 ns / 0 B | 32.68 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 44.90 ns / 0 B | 36.79 ns / 0 B |

### Send（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 9.58 ns / 0 B | 7.01 ns / 0 B |
| Immediate.Handlers 4.3.0 | 26.84 ns / 0 B | 19.67 ns / 0 B |
| MediatR 15.0.0 | 239.07 ns / 1,016 B | 163.85 ns / 1,016 B |
| Mediator 3.0.2 | 153.75 ns / 544 B | 105.94 ns / 544 B |
| DispatchR.Mediator 2.3.1 | 52.36 ns / 0 B | 42.02 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 235.80 ns / 0 B | 183.77 ns / 0 B |

### 同じスコープで再取得してSend（0段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 33.52 ns / 0 B | 30.94 ns / 0 B |
| Immediate.Handlers 4.3.0 | 54.41 ns / 0 B | 45.13 ns / 0 B |
| MediatR 15.0.0 | 82.66 ns / 200 B | 78.22 ns / 200 B |
| Mediator 3.0.2 | 114.87 ns / 64 B | 80.41 ns / 64 B |
| DispatchR.Mediator 2.3.1 | 79.80 ns / 0 B | 61.66 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 86.41 ns / 0 B | 77.75 ns / 0 B |

### 同じスコープで再取得してSend（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 35.75 ns / 0 B | 46.74 ns / 0 B |
| Immediate.Handlers 4.3.0 | 57.85 ns / 0 B | 55.50 ns / 0 B |
| MediatR 15.0.0 | 197.13 ns / 1,016 B | 195.29 ns / 1,016 B |
| Mediator 3.0.2 | 147.42 ns / 544 B | 145.44 ns / 544 B |
| DispatchR.Mediator 2.3.1 | 83.03 ns / 0 B | 77.08 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 226.87 ns / 0 B | 222.15 ns / 0 B |

### 新しいスコープで1回Sendして破棄（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 131.93 ns / 560 B | 121.22 ns / 560 B |
| Immediate.Handlers 4.3.0 | 155.14 ns / 568 B | 149.34 ns / 568 B |
| MediatR 15.0.0 | 268.97 ns / 1,400 B | 252.29 ns / 1,400 B |
| Mediator 3.0.2 | 227.14 ns / 920 B | 211.64 ns / 920 B |
| DispatchR.Mediator 2.3.1 | 774.62 ns / 1,712 B | 531.21 ns / 1,712 B |
| Foundatio.Mediator 1.3.3 | 389.28 ns / 856 B | 379.11 ns / 856 B |

### 同期処理を行う16個のハンドラーに通知

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 143.94 ns / 0 B | 145.13 ns / 0 B |
| Immediate.Handlers 4.3.0 | N/A | N/A |
| MediatR 15.0.0 | 1,249.78 ns / 4,784 B | 1,165.25 ns / 4,784 B |
| Mediator 3.0.2 | 230.08 ns / 0 B | 228.70 ns / 0 B |
| DispatchR.Mediator 2.3.1 | 209.27 ns / 0 B | 209.78 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 1,006.80 ns / 0 B | 1,004.07 ns / 0 B |

### 同期ストリームを全1,024件読む（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 15.242 µs / 928 B | 14.783 µs / 928 B |
| Immediate.Handlers 4.3.0 | 108.246 µs / 1,104 B | 106.837 µs / 1,104 B |
| MediatR 15.0.0 | 216.464 µs / 3,593 B | 211.958 µs / 3,592 B |
| Mediator 3.0.2 | 108.292 µs / 1,688 B | 109.380 µs / 1,689 B |
| DispatchR.Mediator 2.3.1 | 106.468 µs / 1,104 B | 105.834 µs / 1,105 B |
| Foundatio.Mediator 1.3.3 | 127.324 µs / 1,808 B | 126.890 µs / 1,808 B |

### 非同期ストリームを全1,024件読む（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 167.413 µs / 1,586 B | 164.840 µs / 1,586 B |
| Immediate.Handlers 4.3.0 | 311.844 µs / 1,755 B | 314.962 µs / 1,755 B |
| MediatR 15.0.0 | 624.129 µs / 4,726 B | 603.728 µs / 4,726 B |
| Mediator 3.0.2 | 329.834 µs / 2,340 B | 311.326 µs / 2,339 B |
| DispatchR.Mediator 2.3.1 | 316.544 µs / 1,756 B | 311.871 µs / 1,756 B |
| Foundatio.Mediator 1.3.3 | 352.939 µs / 2,539 B | 353.159 µs / 2,539 B |

### 同期ストリームを16件中1件で打ち切る（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 306.07 ns / 928 B | 306.55 ns / 928 B |
| Immediate.Handlers 4.3.0 | 348.98 ns / 1,104 B | 346.99 ns / 1,104 B |
| MediatR 15.0.0 | 951.59 ns / 3,592 B | 1,002.63 ns / 3,592 B |
| Mediator 3.0.2 | 453.85 ns / 1,688 B | 448.07 ns / 1,688 B |
| DispatchR.Mediator 2.3.1 | 377.63 ns / 1,104 B | 377.74 ns / 1,104 B |
| Foundatio.Mediator 1.3.3 | 741.37 ns / 1,808 B | 747.24 ns / 1,808 B |

### 非同期ストリームを16件中1件で打ち切る（5段）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 1.985 µs / 1,256 B | 2.224 µs / 1,256 B |
| Immediate.Handlers 4.3.0 | 2.477 µs / 1,752 B | 2.395 µs / 1,752 B |
| MediatR 15.0.0 | 3.764 µs / 4,720 B | 5.922 µs / 4,720 B |
| Mediator 3.0.2 | 2.655 µs / 2,336 B | 2.814 µs / 2,336 B |
| DispatchR.Mediator 2.3.1 | 2.426 µs / 1,752 B | 2.427 µs / 1,752 B |
| Foundatio.Mediator 1.3.3 | 2.979 µs / 2,536 B | 3.066 µs / 2,536 B |

### 3段の先頭で短絡（繰り返し）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 4.88 ns / 0 B | 6.39 ns / 0 B |
| Immediate.Handlers 4.3.0 | 5.72 ns / 0 B | 7.23 ns / 0 B |
| MediatR 15.0.0 | 111.61 ns / 704 B | 121.07 ns / 704 B |
| Mediator 3.0.2 | 72.43 ns / 496 B | 78.65 ns / 496 B |
| DispatchR.Mediator 2.3.1 | 36.11 ns / 0 B | 46.14 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 72.04 ns / 384 B | 82.39 ns / 384 B |

### 非同期フックの成功（初回）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 1.050 µs / 448 B | 0.995 µs / 448 B |
| Immediate.Handlers 4.3.0 | 1.518 µs / 504 B | 1.905 µs / 504 B |
| MediatR 15.0.0 | 1.570 µs / 1,352 B | 1.550 µs / 1,352 B |
| Mediator 3.0.2 | 1.341 µs / 808 B | 1.357 µs / 808 B |
| DispatchR.Mediator 2.3.1 | 1.296 µs / 560 B | 1.379 µs / 560 B |
| Foundatio.Mediator 1.3.3 | 2.182 µs / 856 B | 2.126 µs / 856 B |

### 非同期フックの成功（繰り返し）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 37.28 ns / 0 B | 45.68 ns / 0 B |
| Immediate.Handlers 4.3.0 | 60.46 ns / 0 B | 62.46 ns / 0 B |
| MediatR 15.0.0 | 626.77 ns / 944 B | 934.94 ns / 944 B |
| Mediator 3.0.2 | 582.95 ns / 2,909 B | 642.18 ns / 2,892 B |
| DispatchR.Mediator 2.3.1 | 82.83 ns / 0 B | 81.59 ns / 0 B |
| Foundatio.Mediator 1.3.3 | 600.53 ns / 480 B | 599.94 ns / 480 B |

### 外側の非同期フックで失敗（初回）

| ライブラリ | 正順 | 逆順 |
|---|---:|---:|
| Zendiator 1.0.1 | 5.755 µs / 1,744 B | 5.229 µs / 1,680 B |
| Immediate.Handlers 4.3.0 | 10.522 µs / 3,808 B | 11.194 µs / 3,808 B |
| MediatR 15.0.0 | 10.140 µs / 3,384 B | 10.144 µs / 3,448 B |
| Mediator 3.0.2 | 9.811 µs / 3,216 B | 8.254 µs / 3,216 B |
| DispatchR.Mediator 2.3.1 | 7.679 µs / 2,968 B | 8.459 µs / 2,968 B |
| Foundatio.Mediator 1.3.3 | 12.730 µs / 4,872 B | 13.546 µs / 4,872 B |

Zendiatorの平均時間の99.9%信頼区間が全競合の信頼区間を下回ったのは、正順で14ケース、逆順で13ケースでした。1,024件の非同期ストリームの割り当て量は正順1,586 B、逆順1,586 Bでした。新規スコープで1回送信したケースは正順560 B、逆順560 Bです。同じマシンで1.0.0を再測定した値は、それぞれ116,322 Bと624 Bでした。[1.0.0の再測定結果](results/8140637-allocation-baseline.csv)

## 測定に含めた処理

どのケースでも、サービスの登録とDIプロバイダーの構築は計測前に済ませています。5段のSendでZendiatorが呼ぶのは、受け取ったリクエストをそのまま返す同期Beforeフック5つです。スコープの作成やMediatorの再取得を明記したケース以外は、取得済みのMediatorを使います。短絡のケースでは先頭のミドルウェアが結果を返すため、ハンドラーは実行されません。非同期フックのケースでは各フックが同期的に完了します。初回の測定時間には、作成済みのスコープからMediatorを取得する時間も含みます。

ストリームの全件列挙では1,024件を読みます。途中終了では入力16件のうち最初の1件を読み、列挙子を破棄するまでを測ります。Zendiatorは名前付きのカーソルを5段のミドルウェアで転送します。比較先はコンパイラが生成したイテレーターを使い、各段で列挙して `yield` します。表の時間には、こうした入力の実装の違いも表れています。非同期入力では全ライブラリが各要素で1回 `Task.Yield()` を実行します。Zendiator 1.0.1では、入力カーソルの非同期メソッドに1.0.0から利用できる `[GeneratePooledProducer]` を指定しました。[実装例](../docs/pooled-producer.md)

## 手元で測定する

測定環境はWindows 11 x64、.NET SDK 10.0.404、.NET 10.0.14、BenchmarkDotNet 0.15.8です。ReleaseビルドとワークステーションGCを使い、論理プロセッサを1つに固定しました。ライブラリと処理の組み合わせ83通りをそれぞれ別のプロセスで実行し、20回のウォームアップ後に12回測定しました。1回の反復時間の設定は500 msです。

Python 3と上記の.NET SDK、ランタイムを用意してください。Gitの作業ツリーに未コミットの変更や未追跡ファイルがない状態で、リポジトリのルートから[代表ケース](representative.json)を正順・逆順で1回ずつ実行します。出力先にはリポジトリの外にある未使用のディレクトリを指定してください。

```powershell
$driver = 'benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py'
$matrix = 'benchmarks/representative.json'
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-forward
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-reverse --reverse
```

このコマンドはチェックアウトしたコードを測定します。1.0.1の測定には、パッケージのバージョンを更新する前に、リリースに含まれる本体とソースジェネレーターのコードを使いました。[測定に使ったソース](https://github.com/Htkym/zendiator/commit/f2973af7aba8726147a7481961f69f424cae3a78)・[1.0.1の測定結果](results/f2973af-representative-summary.csv)・[1.0.0リリース前の測定結果](results/0d53c53-representative-summary.csv)
