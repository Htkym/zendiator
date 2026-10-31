# Zendiator

[English](README.md)

コンパイル時に型付きディスパッチを生成する、.NET 10 向けの小さな Mediator です。
Roslyn Incremental Source Generator がリクエストごとの `SendAsync` オーバーロード、
構造体の継続ノード、DI 登録を生成します。型付きの要求配送に、実行時のアセンブリ走査、
リフレクション呼び出し、`dynamic` は使いません。

## 0.5.0候補の使い方

このガイドは、未公開の0.5.0候補packageで同期処理・非同期処理・Streamを使う例です。
.NET 10 SDKとC# 14を使います。候補packageはNuGet.orgには公開していません。

[候補packageを使う実行例](docs/getting-started-0.5.0.ja.md)には、同じ版のpackageを使う設定とサンプルがあります。

## 登録と実行順序

`[ZendiatorHandler]`はHandlerを明示する属性です。`Handlers<TRequest>`で使うHandlerを選び、`Pipeline<TRequest>`でHookの順序を指定します。

0.5.0候補では、Handlerの属性と明示的な登録を使います。0.4.0のinterface実装やアセンブリ指定だけに依存した登録は、そのまま移せません。

Behaviorの並びは`Pipeline`の順序付きリストで指定します。既存Behaviorのretryやcacheなどの意味を、自動でHookへ置き換えるものではありません。継続の呼び出しを制御するBehaviorを移す際は、`Stage.Wrapper<T>()`の契約と対象経路を確認してください。

## Streamの開始と再列挙

0.5.0では名前付きcursorを開くHandlerを使います。MediatorのStreamAsync(request, apiToken)は標準IAsyncEnumerableとしてsource objectを返します。typed terminalにはStreamSource(request, apiToken).ConsumeAsync(sink, enumerationToken)を使います。StreamSourceの返り値はreadonlyの計画値で、IAsyncEnumerableではありません。既定のChannelや別producer taskは置きません。

pending Moveの完了前に別MoveやDisposeを始めると拒否します。Moveを一度awaitしてからDisposeし、scopeは後始末まで保持してください。false Filterは同じMove内で次itemへ進み、terminalのfalseは正常な全体停止です。sink/serializer例外はsource middlewareへ注入しません。

[Streamの移行・実行契約](docs/streaming-0.5.0.ja.md)

## DI scopeの所有

生成登録の既定は、`IZendiator`がScoped、Handler・Hook・session factoryがTransientです。通常は各Mediatorが依存の型ごとに最初に公開された成功結果を捕捉し、その後の呼出しでも使います。

非同期Hookの実行経路を使う閉じた`SendAsync` routeでは、借用するHandlerと全Hookが参照型で、Zendiatorのsession/factory契約やWrapperを使わず、異なるreceiver型が2個以上ある場合、route単位の依存セットを捕捉します。このセットは初回dispatchの取消確認後、既存の非同期実行内で最初のHookより前に取得します。外側Hookの失敗で未使用になるHandlerも含め、DIが全receiverを取得します。Transientのreceiverは、ほかのrouteやMediatorの明示的な`IServiceProvider`経由で取得するものと異なる場合があります。

同じ依存または依存セットを並行して解決した呼出しは、公開された同じ捕捉instanceを使います。ある解決の例外はほかの成功結果で隠しません。生成・破棄は標準DIの責任です。[詳細な条件と制約](docs/getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)を参照してください。

Streamの返り値をscopeの外へ持ち出し、scopeを破棄してから列挙を始めないでください。途中で`break`した場合も、`await foreach`による列挙子の後始末が終わるまでscopeを保持します。

`SendAsync`が返す`ValueTask`は、例のように1回だけ`await`します。同じ`ValueTask`を保存して複数回消費しないでください。

## CodeFix

CodeFixを使う場合は、`Zendiator`と同じ候補版の`Zendiator.CodeFixes`を`PrivateAssets="all"`で追加し、同じpackage feedからrestoreしてください。現在のVisual Studioでプロジェクトを開き、ZEN0035の修正候補から`Add ZendiatorHandler attribute`を選びます。

登録順、wrapperのretryやcache、scopeの境界は手動で移行してください。既存のwrapper本文は意図を確認せずhookへ変換しません。

[CodeFix package](src/Zendiator.CodeFixes/README.md)

## 性能比較

以下は2026-10-31に完了した測定対象ソースの観測値です。現在のブランチHEADを再測定した値ではありません。0.5.0候補の代表的な処理を、同じ測定環境で比較しました。各セルは **平均時間 / 割当量（B/op）** です。時間とメモリは別の指標であり、どのライブラリも条件によって結果が変わります。表は全条件を網羅していません。

|ケース|Zendiator 候補|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Send / Behavior 0|1.45 ns / 0 B|8.79 ns / 0 B|40.06 ns / 200 B|46.52 ns / 64 B|28.83 ns / 0 B|31.25 ns / 0 B|
|Send / Behavior 5|10.16 ns / 0 B|16.81 ns / 0 B|135.93 ns / 1,016 B|83.05 ns / 544 B|33.28 ns / 0 B|148.19 ns / 0 B|
|同一scopeで再取得＋Send / Behavior 0|27.30 ns / 0 B|40.73 ns / 0 B|71.09 ns / 200 B|83.82 ns / 64 B|56.91 ns / 0 B|68.67 ns / 0 B|
|同一scopeで再取得＋Send / Behavior 5|30.13 ns / 0 B|45.40 ns / 0 B|151.70 ns / 1,016 B|113.12 ns / 544 B|57.99 ns / 0 B|180.64 ns / 0 B|
|新規scope＋1 Send＋破棄 / Behavior 5|151.47 ns / 704 B|117.48 ns / 568 B|202.87 ns / 1,400 B|178.18 ns / 920 B|633.17 ns / 1,712 B|412.67 ns / 856 B|
|Notification / 16 subscribers / 同期|111.33 ns / 0 B|N/A|918.50 ns / 4,784 B|183.21 ns / 0 B|159.29 ns / 0 B|692.02 ns / 0 B|
|Stream 1024件 / Behavior 5 / 同期|13.41 μs / 1,216 B|100.17 μs / 1,107 B|201.14 μs / 3,593 B|101.33 μs / 1,689 B|99.35 μs / 1,105 B|116.64 μs / 1,809 B|
|Stream 1024件 / Behavior 5 / 非同期|165.82 μs / 116,562 B|263.09 μs / 1,754 B|490.69 μs / 4,726 B|261.25 μs / 2,339 B|245.04 μs / 1,755 B|289.13 μs / 2,539 B|
|Stream途中終了 / 16件入力 / Behavior 5 / 同期|415.19 ns / 1,216 B|291.92 ns / 1,104 B|813.08 ns / 3,592 B|372.38 ns / 1,688 B|308.40 ns / 1,104 B|624.94 ns / 1,808 B|
|Stream途中終了 / 16件入力 / Behavior 5 / 非同期|1.81 μs / 1,656 B|1.96 μs / 1,752 B|2.99 μs / 4,720 B|2.08 μs / 2,336 B|1.92 μs / 1,752 B|2.37 μs / 2,536 B|
|Behavior短絡 / 3段 / WarmDispatch|7.09 ns / 0 B|3.99 ns / 0 B|78.10 ns / 704 B|51.94 ns / 496 B|27.93 ns / 0 B|54.64 ns / 384 B|
|H成功 / FirstSend|739.22 ns / 480 B|1.24 μs / 504 B|1.21 μs / 1,352 B|1.04 μs / 808 B|1.01 μs / 560 B|1.71 μs / 856 B|
|H成功 / WarmSend|24.33 ns / 0 B|45.00 ns / 0 B|492.60 ns / 944 B|440.95 ns / 496 B|61.58 ns / 0 B|451.39 ns / 480 B|
|H失敗 / ColdOuterFailure|6.54 μs / 2,520 B|8.09 μs / 3,809 B|6.55 μs / 3,448 B|5.87 μs / 3,216 B|6.00 μs / 2,968 B|9.57 μs / 4,872 B|

新規scopeの行は作成・初回解決・送信・破棄を含みます。Streamは1要素あたりではなく、生成・消費・後始末を含む操作全体です。途中終了は16件入力のうち1件で停止します。同期／非同期は列挙側の完了パターンを分けています。Hは非同期Hook経路の成功・外側失敗を測るシナリオで、FirstSend、WarmSend、ColdOuterFailureは測定範囲が異なります。これらの差を単純な例外処理コストとは解釈できません。N/Aは対象の比較結果がないことを示し、ゼロではありません。

### 結果を見る際の注意

- 新規scopeや同期Streamの途中終了では、Zendiatorより時間・割当量が小さい競合があります。
- 表の非同期Stream（Behavior 5、1024件）はZendiatorの平均時間が小さい一方、割当量は116,562 Bで、競合の1,754–4,726 Bより大きくなっています。
- 表外の**非同期Stream（Behavior 0、1024件）**はZendiatorが149.19 μs / 115,458 B、競合が53.90–92.56 μs / 393–865 Bで、時間・メモリとも不利でした。
- 表外の**非同期Relay（5段、1024件）**はZendiatorが150.97 μs / 116,562 B。Immediate、Mediator、DispatchR、Foundatioは51.94–53.65 μs / 393–937 B、MediatRは246.09 μs / 3,323 Bでした。
- 同期Stream（Behavior 0、16件入力から途中終了）もZendiatorが58.49 ns / 160 Bに対しImmediateは42.80 ns / 144 Bでした。表外の結果を含め、全般的な速度優位や無条件の性能改善は示していません。

### 測定条件

対象ソース: `927827653e229b01afdd8bb97c00b9093ad049ab`（0.5.0候補）。Windows 11 x64、Intel Core Ultra 7 258V、SDK 10.0.403、.NET 10.0.14、DI 10.0.12、BenchmarkDotNet 0.15.8、Release、affinity mask 1。20 warmups、12 measurements、要求iteration時間500 ms、各ケース1 launchです。

測定は中断前と復旧後の異なる実行時間帯に分かれます。1 launchでは独立した再実行による再現性や、厳密に制御した変更前後の改善を証明できません。数ns級の微差は測定オーバーヘッド調整の影響にも注意が必要です。Handlerは軽量で、APIやDI登録の形もライブラリ間で異なります。実アプリの起動時間、HTTP、非同期I/Oの応答時間を予測する値ではありません。標準列挙とtyped terminalも別の処理として評価してください。

Foundatioの表内結果はintercepted経路です。別のopen generic比較で用いるnative runtime経路とは区別します。過去の0.4.0結果をこの表へ混ぜておらず、この表も0.5.0候補の全構成を代表する順位ではありません。

測定範囲の読み方は[ベンチマークの説明](benchmarks/README.md#read-the-comparison)と[Streamの比較境界](benchmarks/README.md#stream-comparison-boundaries)を参照してください。そこにある0.4.0の表・旧測定データは今回の数値の出典ではありません。現在のfixtureを実行するだけで、ここに固定したソース・依存版・測定順の一致を保証するものでもありません。

## 制限と移行

- [既知の制限](docs/release/known-limitations.md)
- [0.4.0リリースノート](docs/release/0.4.0-release-notes.ja.md)
- [公開済み0.4.0の使い方](https://github.com/Htkym/zendiator/blob/v0.4.0/README.ja.md)
- [MediatRからの移行](docs/migrating-from-mediatr.ja.md)

