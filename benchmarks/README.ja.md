# Mediatorの性能比較

[English](README.md)

リクエスト、通知、ストリーム、フックの代表14ケースを、正順と逆順で測定しました。Zendiatorの平均時間は14ケースすべてで、どちらの順序でも最も短くなりました。測定に使ったのはリリース候補の作成前のZendiator（`0d53c53`）、Immediate.Handlers 4.3.0、MediatR 15.0.0、Mediator 3.0.2、DispatchR.Mediator 2.3.1、Foundatio.Mediator 1.3.3です。通知はImmediate.Handlersに対応するケースがないため、5ライブラリで比較しました。

## 1回あたりの平均時間

次点の欄には、それぞれの測定順でZendiatorに次いで平均時間が短かったライブラリを載せています。表中のImmediateはImmediate.Handlersです。各ライブラリの平均時間、99.9%信頼区間、割り当て量は[全166件の測定結果](results/0d53c53-representative-summary.csv)で確認できます。

| 処理 | 単位 | 正順 Zendiator | 正順 次点 | 逆順 Zendiator | 逆順 次点 |
|---|---|---:|---|---:|---|
| Send（ミドルウェアなし） | ns | 1.645 | Immediate 11.82 | 1.739 | Immediate 11.41 |
| Send（同期Beforeフック5つ） | ns | 6.708 | Immediate 20.95 | 6.397 | Immediate 20.25 |
| 同じスコープで再取得してSend（ミドルウェアなし） | ns | 36.69 | Immediate 47.57 | 35.25 | Immediate 44.22 |
| 同じスコープで再取得してSend（5段） | ns | 36.98 | Immediate 58.94 | 35.57 | Immediate 59.94 |
| スコープを作成し、1回送信して破棄（5段） | ns | 126.38 | Immediate 173.62 | 135.44 | Immediate 159.43 |
| 同期処理を行う16個のハンドラーに通知 | ns | 140.12 | Mediator 229.14 | 145.94 | DispatchR 204.91 |
| 同期ストリームを1,024件すべて読む（5段） | µs | 15.22 | Immediate 110.91 | 15.12 | Mediator 110.22 |
| 非同期ストリームを1,024件すべて読む（5段） | µs | 201.29 | DispatchR 317.36 | 192.90 | DispatchR 323.66 |
| 同期ストリームを1件で打ち切る（5段） | ns | 317.31 | Immediate 385.47 | 296.50 | DispatchR 379.99 |
| 非同期ストリームを1件で打ち切る（5段） | µs | 2.260 | Immediate 2.508 | 2.048 | Immediate 2.283 |
| ミドルウェア3段の先頭で短絡（繰り返し） | ns | 4.830 | Immediate 6.008 | 4.635 | Immediate 5.444 |
| 非同期フックが成功（初回） | µs | 1.023 | DispatchR 1.335 | 0.995 | DispatchR 1.231 |
| 非同期フックが成功（繰り返し） | ns | 37.93 | Immediate 58.05 | 36.08 | Immediate 57.34 |
| 非同期フックの最初の段で失敗（初回） | µs | 4.422 | Mediator 7.776 | 4.578 | DispatchR 7.846 |

13ケースでは両方の測定順で、Zendiatorの信頼区間の上限が全競合の下限を下回りました。非同期ストリームを1件で打ち切るケースでは、両方の測定順で競合と信頼区間が重なりました。処理時間とメモリ割り当て量で順位が異なる場合もあります。正順で非同期ストリームを1,024件すべて読んだとき、Zendiatorの平均時間は201.29 µs、割り当て量は116,322 Bでした。DispatchRは317.36 µs、1,756 Bでした。

## 測定に含めた処理

どのケースでも、サービスの登録とDIプロバイダーの構築は計測前に済ませています。5段のSendでZendiatorが呼ぶのは、受け取ったリクエストをそのまま返す同期Beforeフック5つです。スコープの作成やMediatorの再取得を明記したケース以外は、取得済みのMediatorを使います。短絡のケースでは先頭のミドルウェアが結果を返すため、ハンドラーは実行されません。非同期フックのケースでは各フックが同期的に完了します。初回の測定時間には、作成済みのスコープからMediatorを取得する時間も含みます。

ストリームの全件列挙では1,024件を読みます。途中終了では入力16件のうち最初の1件を読み、列挙子を破棄するまでを測ります。Zendiatorは名前付きのカーソルを5段のミドルウェアで転送します。比較先はコンパイラが生成したイテレーターを使い、各段で列挙して `yield` します。表の時間には、こうした入力の実装の違いも表れています。

## 手元で測定する

測定環境はWindows 11 x64、.NET SDK 10.0.404、.NET 10.0.14、BenchmarkDotNet 0.15.8です。ReleaseビルドとワークステーションGCを使い、論理プロセッサを1つに固定しました。ライブラリと処理の組み合わせ83通りをそれぞれ別のプロセスで実行し、20回のウォームアップ後に12回測定しました。1回の反復時間の設定は500 msです。

Python 3と上記の.NET SDK、ランタイムを用意してください。Gitの作業ツリーに未コミットの変更や未追跡ファイルがない状態で、リポジトリのルートから[代表ケース](representative.json)を正順・逆順で1回ずつ実行します。出力先にはリポジトリの外にある未使用のディレクトリを指定してください。

```powershell
$driver = 'benchmarks/Zendiator.UseCaseBenchmarks/run_interleaved.py'
$matrix = 'benchmarks/representative.json'
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-forward
python $driver --group custom --matrix $matrix --expect-sdk 10.0.404 --output ../zendiator-reverse --reverse
```

このコマンドで測定するのは、手元にチェックアウトしたコミットです。CSVには `0d53c53` を測定したときの値を収録しています。
