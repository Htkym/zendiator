# 利用条件別のベンチマーク

[English](README.md)

チェックインしたfixtureで、ZendiatorとImmediate.Handlers 4.2.0、DispatchR.Mediator 2.3.1、Mediator 3.0.2、MediatR 14.2.0を比べます。入口と登録方法は各ライブラリの公開仕様に沿っています。全体はSend 125件、features 55件、streams 120件、relay 60件、74比較キーです。対応しない組合せは未測定として扱います。

## 比較の読み方

[0.4.0の過去の測定結果](#historical-040-results)には、2026-10-08の全体比較から有利・不利な条件を含む8例を載せています。ソース版は `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55` です。[丸めていない抜粋データ](results/20261008-1c41d2b-summary.csv)にはMean、信頼区間の半幅であるError、StdDev、標準誤差、N、管理ヒープの割り当てを別列で載せています。

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

コマンドは、checkoutしたソース版のfixtureを実行します。過去の表を再現する場合は、
表で固定した`1c41d2b`のソースと、それに対応する依存関係を使います。
現在の0.5.0を実行しても、当時の実装を再現したことにはなりません。
ZendiatorのSendと、Notification以外のfeatureはcanonical compositionを使い、
StreamとRelayはnative cursorを使います。Notificationは分離したlegacy登録を維持します。
このfixtureを用意したことは、最終版0.5.0候補の新しい測定結果を示すものではありません。

.NET 10、PowerShell、Python 3を用意します。パッケージ版はlock fileで固定しています。
リポジトリのルートから実行します。

```powershell
pwsh -NoProfile -File benchmarks/Run-Comparison.ps1 -Group smoke
```

`-Group all`は、チェックイン済みの全matrixをライブラリの型ごとのブロックで実行します。
過去の表で使った、比較キーごとにライブラリを交代する順序とは異なります。
未加工のBenchmarkDotNet JSONとログも、結果とともに保存します。
測定するソース版、依存関係、workload、実行順は比較条件の一部です。
同じコマンドを使ったことだけでは、条件の一致や現在の版の順位は確認できません。

## Stream比較の範囲

現在のZendiatorのStream fixtureは、名前付きの型付きcursorとcanonical compositionを使います。
競合は、それぞれのproducerと登録形状を使います。
Full/EarlyBreakは、StreamAsyncによる標準列挙を使います。FullTypedとEarlyBreakTypedは、
classのsinkを渡してStreamSource(...).ConsumeAsync(...)を使います。
typed planはIAsyncEnumerableではなく、消費ごとに非公開のownerを作ります。
EarlyBreak16は、16項目の入力から1項目を消費します。16項目を消費する条件ではありません。

標準列挙、typed terminal、HTTPは別々に評価します。native cursorとコンパイラーが生成するiteratorでは、
producer側の処理にかかる費用が異なります。そのため、これらは製品経路全体の比較であり、
dispatcherだけを分離した時間測定ではありません。protocolの変更や割当量の削減だけでは、
速度が向上したとは判断できません。typedな消費も、時間や割当量の削減を保証しません。

<a id='historical-040-results'></a>

## 0.4.0の過去の測定結果

ここに載せるのは、以下に記載した過去のソース版の結果です。現在の0.5.0候補の結果ではありません。

取得済みの入口、初回取得、Scopeの作成、Streamの消費を分けて比べた観測値です。時間と管理ヒープの割り当ては別の指標として選定に使ってください。

|条件|Zendiator Mean ns|B/op|比較先|Mean ns|B/op|
|---|---:|---:|---|---:|---:|
|既存Scopeで入口を再取得してSend・Behavior 5|32.25|0|Immediate|46.07|0|
|新Scope＋初回取得＋Send＋破棄・Behavior 0|79.43|376|Immediate|96.47|368|
|新Scope＋初回取得＋Send＋破棄・Behavior 5|169.08|568|Immediate|121.61|568|
|事前作成Scopeで初回取得＋Send・Behavior 5|807.98|440|Immediate|605.65|440|
|同期Notification・購読者16|121.77|0|DispatchR|175.40|0|
|同期Stream・全1024項目・Behavior 0|14,190.35|216|Immediate|14,222.54|144|
|同期Stream・16項目の入力を1項目で終了・Behavior 0|62.57|216|Immediate|42.84|144|
|同期Relay・全1024項目・前処理5段|15,028.75|216|DispatchR|13,610.87|144|

上表の既存ScopeのSendはImmediateより平均時間が低い一方、Behaviorを含む初回取得や新Scopeで1回送る条件は高い値でした。Stream0の全1024項目では時間差が約0.23%と小さく、順位を確定できません。割り当てはZendiatorが72 B多く、途中で列挙を終える条件やRelayにも不利な例があります。

2026-10-08にソース版 `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55` を測定しました。Windows 11 x64、Intel Core Ultra 7 258V、SDK 10.0.401、.NET 10.0.12、BenchmarkDotNet 0.15.8、Release、affinity mask 1、warmup20、measurement12、要求iteration 500 ms、各ケース1 launchです。表の比較先はImmediate.Handlers 4.2.0とDispatchR.Mediator 2.3.1、DIは10.0.12です。

MeanはBDNのoverhead補正と外れ値処理後の平均で、Nは9〜12でした。[丸めていない抜粋CSV](results/20261008-1c41d2b-summary.csv)にはMean、信頼区間の半幅であるError、StdDev、N、割り当てを別列で載せています。全体比較は74比較キー・全360件です。1 launchの比較から、独立実行での順位再現、全条件の非劣化、変更前後の因果的な改善は認定しません。

Handlerは軽い処理で、ライブラリ間のAPI形状とDI登録も同一ではありません。ScopeK1はScopeの生成・破棄を含み、FirstSendは含みません。StreamとRelayは1項目でなく、列挙・破棄を含む操作全体の値です。Relayの前処理は同期的に実行し、次のEnumerableを直接返します。アプリ起動、HTTP全体や非同期I/Oへの予測には使いません。[ベンチマークの説明](#比較の読み方)に実行条件と再現手順があります。
