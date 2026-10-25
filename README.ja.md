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

生成登録の既定は、`IZendiator`がScoped、Handler・Hook・session factoryがTransientです。各Mediatorは依存ごとに最初に公開された成功結果を捕捉し、その後の呼出しでも使います。並行して解決に成功した呼出しは同じ捕捉instanceを使います。ある解決の例外は、ほかの成功結果で隠しません。

Streamの返り値をscopeの外へ持ち出し、scopeを破棄してから列挙を始めないでください。途中で`break`した場合も、`await foreach`による列挙子の後始末が終わるまでscopeを保持します。

`SendAsync`が返す`ValueTask`は、例のように1回だけ`await`します。同じ`ValueTask`を保存して複数回消費しないでください。

## CodeFix

CodeFixを使う場合は、`Zendiator`と同じ候補版の`Zendiator.CodeFixes`を`PrivateAssets="all"`で追加し、同じpackage feedからrestoreしてください。現在のVisual Studioでプロジェクトを開き、ZEN0035の修正候補から`Add ZendiatorHandler attribute`を選びます。

登録順、wrapperのretryやcache、scopeの境界は手動で移行してください。既存のwrapper本文は意図を確認せずhookへ変換しません。

[CodeFix package](src/Zendiator.CodeFixes/README.md)

## 性能比較

入口の形だけで時間や割当の優位は保証しません。標準列挙、typed terminal、HTTPは対象の版・条件・実測結果を分けて評価します。

[0.4.0の過去の測定結果](benchmarks/README.ja.md#historical-040-results)は、この0.5.0候補全体の比較ではありません。
速度が全般的に向上したことや、性能が悪化しないことは確認できていません。
実際に非同期待機するStreamについても、競合に対する速度優位は確認できていません。
途中終了や一部の非同期待機経路では、不利な観測結果が残っています。

## 制限と移行

- [既知の制限](docs/release/known-limitations.md)
- [0.4.0リリースノート](docs/release/0.4.0-release-notes.ja.md)
- [公開済み0.4.0の使い方](https://github.com/Htkym/zendiator/blob/v0.4.0/README.ja.md)
- [MediatRからの移行](docs/migrating-from-mediatr.ja.md)
