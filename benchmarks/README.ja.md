# Mediatorの性能比較

[English](README.md)

## ケース別の測定結果

Windows 11 x64上で、.NET 10.0.14のReleaseビルドをBenchmarkDotNet 0.15.8で測定しました。

| ケース | Zendiator 1.0.1 | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send / Behavior 0 | 1.84 ns / 0 B | 11.20 ns / 0 B | 57.30 ns / 200 B | 58.15 ns / 64 B | 39.16 ns / 0 B | 40.85 ns / 0 B |
| Send / Behavior 5 | 8.29 ns / 0 B | 23.26 ns / 0 B | 201.46 ns / 1,016 B | 129.84 ns / 544 B | 47.19 ns / 0 B | 209.79 ns / 0 B |
| 同一スコープで再取得してSend / Behavior 0 | 32.23 ns / 0 B | 49.77 ns / 0 B | 80.44 ns / 200 B | 97.64 ns / 64 B | 70.73 ns / 0 B | 82.08 ns / 0 B |
| 同一スコープで再取得してSend / Behavior 5 | 41.24 ns / 0 B | 56.67 ns / 0 B | 196.21 ns / 1,016 B | 146.43 ns / 544 B | 80.05 ns / 0 B | 224.51 ns / 0 B |
| 新規スコープで1回Sendして破棄 / Behavior 5 | 126.58 ns / 560 B | 152.24 ns / 568 B | 260.63 ns / 1,400 B | 219.39 ns / 920 B | 652.91 ns / 1,712 B | 384.20 ns / 856 B |
| Notification / 16 subscribers / 同期 | 144.53 ns / 0 B | N/A | 1,207.51 ns / 4,784 B | 229.39 ns / 0 B | 209.52 ns / 0 B | 1,005.43 ns / 0 B |
| Stream 1,024件 / Behavior 5 / 同期 | 15.013 µs / 928 B | 107.542 µs / 1,104 B | 214.211 µs / 3,592.5 B | 108.836 µs / 1,688.5 B | 106.151 µs / 1,104.5 B | 127.107 µs / 1,808 B |
| Stream 1,024件 / Behavior 5 / 非同期 | 166.127 µs / 1,586 B | 313.403 µs / 1,755 B | 613.929 µs / 4,726 B | 320.580 µs / 2,339.5 B | 314.207 µs / 1,756 B | 353.049 µs / 2,539 B |
| Stream途中終了 / 16件入力 / Behavior 5 / 同期 | 306.31 ns / 928 B | 347.98 ns / 1,104 B | 977.11 ns / 3,592 B | 450.96 ns / 1,688 B | 377.69 ns / 1,104 B | 744.31 ns / 1,808 B |
| Stream途中終了 / 16件入力 / Behavior 5 / 非同期 | 2.105 µs / 1,256 B | 2.436 µs / 1,752 B | 4.843 µs / 4,720 B | 2.735 µs / 2,336 B | 2.426 µs / 1,752 B | 3.022 µs / 2,536 B |
| Behavior短絡 / 3段 / 繰り返し | 5.64 ns / 0 B | 6.48 ns / 0 B | 116.34 ns / 704 B | 75.54 ns / 496 B | 41.13 ns / 0 B | 77.22 ns / 384 B |
| 非同期Hook成功 / 初回 | 1.023 µs / 448 B | 1.711 µs / 504 B | 1.560 µs / 1,352 B | 1.349 µs / 808 B | 1.338 µs / 560 B | 2.154 µs / 856 B |
| 非同期Hook成功 / 繰り返し | 41.48 ns / 0 B | 61.46 ns / 0 B | 780.85 ns / 944 B | 612.56 ns / 2,900.5 B | 82.21 ns / 0 B | 600.23 ns / 480 B |
| 非同期Hook失敗 / 初回・外側 | 5.492 µs / 1,712 B | 10.858 µs / 3,808 B | 10.142 µs / 3,416 B | 9.032 µs / 3,216 B | 8.069 µs / 2,968 B | 13.138 µs / 4,872 B |

[測定順ごとの結果と99.9%信頼区間（CSV）](results/f2973af-representative-summary.csv)・[1.0.0の再測定結果（CSV）](results/8140637-allocation-baseline.csv)

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
