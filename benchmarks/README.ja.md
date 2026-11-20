# Mediatorの性能比較

[English](README.md)

Zendiator（`f2973af`）、Immediate.Handlers 4.3.0、MediatR 15.0.0、Mediator 3.0.2、DispatchR.Mediator 2.3.1、Foundatio.Mediator 1.3.3を代表14ケースで比較しました。正順・逆順ともに、Zendiatorの平均時間が全ケースで最も短く、割り当て量も同率を含めて全ケースで最小でした。通知はImmediate.Handlersに対応するケースがないため、5ライブラリで比較しました。

## 1回あたりの平均時間

次点の欄には、各ケースでZendiatorに次いで平均時間が短かったライブラリを示します。表中のImmediateはImmediate.Handlersです。[6ライブラリの平均時間と割り当て量](../README.ja.md#性能)・[99.9%信頼区間を含む全166件のCSV](results/f2973af-representative-summary.csv)

| 処理 | 単位 | 正順 Zendiator | 正順 次点 | 逆順 Zendiator | 逆順 次点 |
|---|---|---:|---|---:|---|
| Send（ミドルウェアなし） | ns | 2.04 | Immediate 13.19 | 1.65 | Immediate 9.22 |
| Send（同期Beforeフック5つ） | ns | 9.58 | Immediate 26.84 | 7.01 | Immediate 19.67 |
| 同じスコープで再取得してSend（ミドルウェアなし） | ns | 33.52 | Immediate 54.41 | 30.94 | Immediate 45.13 |
| 同じスコープで再取得してSend（5段） | ns | 35.75 | Immediate 57.85 | 46.74 | Immediate 55.50 |
| スコープを作成し、1回送信して破棄（5段） | ns | 131.93 | Immediate 155.14 | 121.22 | Immediate 149.34 |
| 同期処理を行う16個のハンドラーに通知 | ns | 143.94 | DispatchR 209.27 | 145.13 | DispatchR 209.78 |
| 同期ストリームを1,024件すべて読む（5段） | µs | 15.242 | DispatchR 106.468 | 14.783 | DispatchR 105.834 |
| 非同期ストリームを1,024件すべて読む（5段） | µs | 167.413 | Immediate 311.844 | 164.840 | Mediator 311.326 |
| 同期ストリームを1件で打ち切る（5段） | ns | 306.07 | Immediate 348.98 | 306.55 | Immediate 346.99 |
| 非同期ストリームを1件で打ち切る（5段） | µs | 1.985 | DispatchR 2.426 | 2.224 | Immediate 2.395 |
| ミドルウェア3段の先頭で短絡（繰り返し） | ns | 4.88 | Immediate 5.72 | 6.39 | Immediate 7.23 |
| 非同期フックが成功（初回） | µs | 1.050 | DispatchR 1.296 | 0.995 | Mediator 1.357 |
| 非同期フックが成功（繰り返し） | ns | 37.28 | Immediate 60.46 | 45.68 | Immediate 62.46 |
| 非同期フックの最初の段で失敗（初回） | µs | 5.755 | DispatchR 7.679 | 5.229 | Mediator 8.254 |

Zendiatorの平均時間の99.9%信頼区間が全競合の信頼区間を下回ったのは、正順で14ケース、逆順で13ケースでした。1,024件の非同期ストリームの割り当て量は正順1,586 B、逆順1,586 Bでした。新規スコープで1回送信したケースは正順560 B、逆順560 Bです。同じマシンで1.0.0を再測定した値は、それぞれ116,322 Bと624 Bでした。[1.0.0の再測定結果](results/8140637-allocation-baseline.csv)

## 測定に含めた処理

どのケースでも、サービスの登録とDIプロバイダーの構築は計測前に済ませています。5段のSendでZendiatorが呼ぶのは、受け取ったリクエストをそのまま返す同期Beforeフック5つです。スコープの作成やMediatorの再取得を明記したケース以外は、取得済みのMediatorを使います。短絡のケースでは先頭のミドルウェアが結果を返すため、ハンドラーは実行されません。非同期フックのケースでは各フックが同期的に完了します。初回の測定時間には、作成済みのスコープからMediatorを取得する時間も含みます。

ストリームの全件列挙では1,024件を読みます。途中終了では入力16件のうち最初の1件を読み、列挙子を破棄するまでを測ります。Zendiatorは名前付きのカーソルを5段のミドルウェアで転送します。比較先はコンパイラが生成したイテレーターを使い、各段で列挙して `yield` します。表の時間には、こうした入力の実装の違いも表れています。 非同期入力では全ライブラリが各要素で1回 `Task.Yield()` を実行します。改修版のZendiatorでは、入力カーソルの非同期メソッドに1.0.0から利用できる `[GeneratePooledProducer]` を指定しました。[実装例](../docs/pooled-producer.md)

## 手元で測定する

測定環境はWindows 11 x64、.NET SDK 10.0.404、.NET 10.0.14、BenchmarkDotNet 0.15.8です。ReleaseビルドとワークステーションGCを使い、論理プロセッサを1つに固定しました。ライブラリと処理の組み合わせ83通りをそれぞれ別のプロセスで実行し、20回のウォームアップ後に12回測定しました。1回の反復時間の設定は500 msです。

Python 3と上記の.NET SDK、ランタイムを用意してください。Gitの作業ツリーに未コミットの変更や未追跡ファイルがない状態で、リポジトリのルートから[代表ケース](representative.json)を正順・逆順で1回ずつ実行します。出力先にはリポジトリの外にある未使用のディレクトリを指定してください。

```powershell
$driver = 'benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py'
$matrix = 'benchmarks/representative.json'
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-forward
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-reverse --reverse
```

このコマンドで測定するのは、手元にチェックアウトしたコミットです。[改修版 `f2973af` のCSV](results/f2973af-representative-summary.csv)・[1.0.0採用時の `0d53c53` のCSV](results/0d53c53-representative-summary.csv)
