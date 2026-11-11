# Zendiator

[English](README.md)

コンパイル時に型付きディスパッチを生成する、.NET 10 向けの小さな Mediator です。
Roslyn Incremental Source Generator がリクエストごとの `SendAsync` オーバーロード、
構造体の継続ノード、DI 登録を生成します。型付きの要求配送に、実行時のアセンブリ走査、
リフレクション呼び出し、`dynamic` は使いません。

## 0.5.1の使い方

このガイドは、0.5.1で同期処理・非同期処理・Streamを使う例です。
修正と実験的な opt-in producer 機能は、[0.5.1リリースノート](docs/release/0.5.1-release-notes.ja.md)を参照してください。
.NET 10 SDKとC# 14を使います。0.4.0からの更新には破壊的変更があるため、
[0.5.0リリースノート](docs/release/0.5.0-release-notes.ja.md)を確認してください。

```shell
dotnet add package Zendiator --version 0.5.1
```

NuGet.orgで0.5.1が公開されるまでは、同じ版のローカルビルド済みpackageを使ってください。
[packageを使う実行例](docs/getting-started-0.5.0.ja.md)に、設定とサンプルがあります。
そのガイドの 0.5.0 の package 例を使う場合、今回の版ではすべて 0.5.1 に揃えてください。

## 登録と実行順序

`[ZendiatorHandler]`はHandlerを明示する属性です。`Handlers<TRequest>`で使うHandlerを選び、`Pipeline<TRequest>`でHookの順序を指定します。

0.5.0では、Handlerの属性と明示的な登録を使います。0.4.0のinterface実装やアセンブリ指定だけに依存した登録は、そのまま移せません。

Behaviorの並びは`Pipeline`の順序付きリストで指定します。既存Behaviorのretryやcacheなどの意味を、自動でHookへ置き換えるものではありません。継続の呼び出しを制御するBehaviorを移す際は、`Stage.Wrapper<T>()`の契約と対象経路を確認してください。

## Streamの開始と再列挙

0.5.0では標準IAsyncEnumerableを返すHandler、または名前付きcursorを開くHandlerを使います。MediatorのStreamAsync(request, apiToken)は標準IAsyncEnumerableとしてsource objectを返します。typed terminalにはStreamSource(request, apiToken).ConsumeAsync(sink, enumerationToken)を使います。StreamSourceの返り値はreadonlyの計画値で、IAsyncEnumerableではありません。既定のChannelや別producer taskは置きません。

guard付きの経路では、pending Moveの完了前に別MoveやDisposeを始めると拒否します。Hook・Before・middleware・owned sessionを持たず、API側と列挙側の両tokenが取消不可のiterator経路では、GetAsyncEnumeratorで入力を取得し、producerの操作契約に従います。この経路でもEOFや失敗後を含めてDisposeAsyncをawaitしてください。実際の取消tokenを省略して経路を選ばないでください。Moveを一度awaitしてからDisposeし、scopeは後始末まで保持してください。false Filterは同じMove内で次itemへ進み、terminalのfalseは正常な全体停止です。sink/serializer例外はsource middlewareへ注入しません。

[Streamの移行・実行契約](docs/streaming-0.5.0.ja.md)

## DI scopeの所有

生成登録の既定は、`IZendiator`がScoped、Handler・Hook・session factoryがTransientです。通常は各Mediatorが依存の型ごとに最初に公開された成功結果を捕捉し、その後の呼出しでも使います。

非同期Hookの実行経路を使う閉じた`SendAsync` routeでは、借用するHandlerと全Hookが参照型で、Zendiatorのsession/factory契約やWrapperを使わず、異なるreceiver型が2個以上ある場合、route単位の依存セットを捕捉します。このセットは初回dispatchの取消確認後、既存の非同期実行内で最初のHookより前に取得します。外側Hookの失敗で未使用になるHandlerも含め、DIが全receiverを取得します。Transientのreceiverは、ほかのrouteやMediatorの明示的な`IServiceProvider`経由で取得するものと異なる場合があります。

同じ依存または依存セットを並行して解決した呼出しは、公開された同じ捕捉instanceを使います。ある解決の例外はほかの成功結果で隠しません。生成・破棄は標準DIの責任です。[詳細な条件と制約](docs/getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)を参照してください。

Streamの返り値をscopeの外へ持ち出し、scopeを破棄してから列挙を始めないでください。途中で`break`した場合も、`await foreach`による列挙子の後始末が終わるまでscopeを保持します。

`SendAsync`が返す`ValueTask`は、例のように1回だけ`await`します。同じ`ValueTask`を保存して複数回消費しないでください。

## CodeFix

Zendiator.CodeFixesは任意の別packageです。CodeFixを使う場合は、`Zendiator`と同じ版の`Zendiator.CodeFixes`を`PrivateAssets="all"`で追加し、同じpackage feedからrestoreしてください。現在のVisual Studioでプロジェクトを開き、ZEN0035の修正候補から`Add ZendiatorHandler attribute`を選びます。

登録順、wrapperのretryやcache、scopeの境界は手動で移行してください。既存のwrapper本文は意図を確認せずhookへ変換しません。

[CodeFix package](src/Zendiator.CodeFixes/README.md)

## 性能比較

[今回の実装](https://github.com/Htkym/zendiator/commit/88483069db6409e092efa0d5ef63452777f577c9)を2026-11-11に同じ環境で比較しました。以下の2表のN/A以外の101セルは、すべて今回測定した値です。各セルは **平均時間 / managed割当量（B/op）** です。時間とメモリは別の指標です。

|ケース|Zendiator (8848306)|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Send / Behavior 0|1.21 ns / 0 B|7.18 ns / 0 B|35.18 ns / 200 B|35.84 ns / 64 B|25.84 ns / 0 B|28.20 ns / 0 B|
|Send / Behavior 5|9.38 ns / 0 B|16.78 ns / 0 B|130.72 ns / 1,016 B|79.88 ns / 544 B|34.16 ns / 0 B|155.00 ns / 0 B|
|同一scopeで再取得＋Send / Behavior 0|24.51 ns / 0 B|35.45 ns / 0 B|54.81 ns / 200 B|77.00 ns / 64 B|53.51 ns / 0 B|63.15 ns / 0 B|
|同一scopeで再取得＋Send / Behavior 5|40.05 ns / 0 B|44.60 ns / 0 B|148.61 ns / 1,016 B|115.75 ns / 544 B|57.53 ns / 0 B|185.89 ns / 0 B|
|新規scope＋1 Send＋破棄 / Behavior 5|148.91 ns / 704 B|113.39 ns / 568 B|208.90 ns / 1,400 B|151.09 ns / 920 B|406.93 ns / 1,712 B|413.65 ns / 856 B|
|Notification / 16 subscribers / 同期|107.85 ns / 0 B|N/A|900.10 ns / 4,784 B|188.95 ns / 0 B|146.04 ns / 0 B|685.74 ns / 0 B|
|Stream 1024件 / Behavior 5 / 同期|12.13 μs / 1,016 B|89.41 μs / 1,106 B|182.81 μs / 3,593 B|90.55 μs / 1,688 B|89.69 μs / 1,104 B|106.04 μs / 1,808 B|
|Stream 1024件 / Behavior 5 / 非同期|147.51 μs / 116,362 B|229.28 μs / 1,753 B|448.74 μs / 4,724 B|227.71 μs / 2,338 B|226.34 μs / 1,755 B|280.33 μs / 2,538 B|
|Stream途中終了 / 16件入力 / Behavior 5 / 同期|365.93 ns / 1,016 B|254.44 ns / 1,104 B|717.95 ns / 3,592 B|340.93 ns / 1,688 B|282.08 ns / 1,104 B|572.81 ns / 1,808 B|
|Stream途中終了 / 16件入力 / Behavior 5 / 非同期|1.84 μs / 1,456 B|1.96 μs / 1,752 B|3.03 μs / 4,720 B|2.13 μs / 2,336 B|2.02 μs / 1,752 B|2.47 μs / 2,536 B|
|Behavior短絡 / 3段 / 繰り返し|4.49 ns / 0 B|4.77 ns / 0 B|77.26 ns / 704 B|53.36 ns / 496 B|27.00 ns / 0 B|55.23 ns / 384 B|
|非同期Hook成功 / 初回|790.62 ns / 480 B|1.20 μs / 504 B|1.19 μs / 1,352 B|1.01 μs / 808 B|1.04 μs / 560 B|1.73 μs / 856 B|
|非同期Hook成功 / 繰り返し|30.31 ns / 0 B|48.63 ns / 0 B|556.91 ns / 944 B|514.80 ns / 496 B|67.41 ns / 0 B|487.82 ns / 480 B|
|非同期Hook失敗 / 初回・外側|3.62 μs / 1,712 B|8.12 μs / 3,808 B|8.64 μs / 3,384 B|6.16 μs / 3,216 B|6.44 μs / 2,968 B|9.82 μs / 4,872 B|

新規scopeの時間は作成・初回解決・送信・破棄を含みます。Streamは後始末を含む操作全体の値で、途中終了は16件入力のうち1件を消費して停止します。同期／非同期は列挙側の完了パターンを分けています。初回・繰り返し・外側Hook失敗は測定範囲が異なるため、その差を単純な例外処理コストとは解釈できません。N/Aは対象の比較結果がないことを示し、ゼロではありません。

### 補足の利用場面

|ケース|Zendiator (8848306)|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Stream 1024件 / Behavior 0 / 非同期|140.56 μs / 115,457 B|49.48 μs / 393 B|77.98 μs / 865 B|51.86 μs / 457 B|49.60 μs / 393 B|53.53 μs / 392 B|
|Relay 1024件 / 5段 / 非同期|153.22 μs / 116,361 B|49.62 μs / 392 B|236.21 μs / 3,323 B|49.36 μs / 937 B|49.83 μs / 393 B|54.89 μs / 393 B|
|Stream途中終了 / 16件入力 / Behavior 0 / 同期|58.43 ns / 160 B|39.96 ns / 144 B|134.95 ns / 536 B|73.01 ns / 208 B|66.07 ns / 144 B|66.29 ns / 144 B|

両表には競合の時間や割当量が少ない利用場面も含めています。これらの観測値は全般的な速度優位、無条件の非劣化、条件を揃えた変更前後の改善を示していません。

### 測定条件

Windows 11 x64、Intel Core Ultra 7 258V、SDK 10.0.403、.NET runtime 10.0.14（dotnet host 11.0.0）、DI 10.0.12、BenchmarkDotNet 0.15.8、Release、workstation GC、affinity mask 1。ウォームアップ20回、実測12回、要求測定時間500 ms、各ケース1 launchです。初回・cold失敗ケースは16384 invocations、unroll factor 1です。BDNのオーバーヘッド調整・外れ値処理後の統計対象は9–12点です。

UseCaseの64セルと、保存済み定義から復元したFoundatio／ShortCircuit／Hの37セルは別の測定群で、各群の比較可能なライブラリ間で順序を交代しています。1 launchかつ測定時刻が異なるため、独立した再実行による再現性は証明できません。両測定群でZendiatorとAbstractionsのDLL bytesは一致しています。

[丸め前の結果とばらつき](benchmarks/results/20261111-8848306-summary.csv)にMean、StdDev、標準誤差、信頼区間の半幅、統計対象Nを記載しました。[全数値観測](benchmarks/results/20261111-8848306-readme.json)にはBDNの全観測、job条件、assembly hash、元reportのhashを含めています。10月の結果を今回の表へ混在させていません。

数ns級の微差はオーバーヘッド調整の影響を受けやすく、軽量な処理を使ったこの測定から実アプリの起動時間やHTTP・非同期I/Oの応答時間は予測できません。APIやDI登録の形はライブラリ間で異なり、標準列挙とtyped terminalも別の処理です。Foundatioはintercepted経路の値で、native runtimeのopen generic経路とは異なります。

[測定範囲](benchmarks/README.md#read-the-comparison)と[Streamの比較](benchmarks/README.md#stream-comparison-boundaries)も参照してください。リンク先の過去の0.4.0データは別の測定です。

## 制限と移行

- [既知の制限](docs/release/known-limitations.md)
- [0.4.0リリースノート](docs/release/0.4.0-release-notes.ja.md)
- [公開済み0.4.0の使い方](https://github.com/Htkym/zendiator/blob/v0.4.0/README.ja.md)
- [MediatRからの移行](docs/migrating-from-mediatr.ja.md)
