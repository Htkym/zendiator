# WP09: 型付き失敗結果の比較と採用判断案

正式測定と集計の独立照合は完了している。変更の採用は、最終統合、全体テスト、公開API、package、ほかの改善との組合せの各gateを通過することを条件とする。公開、統合、開発計画全体の完了を示す文書ではない。

## 判断案

WP09は、対象を限定して採用する方向とする。元の代表ケースL4に対応するColdOuterFailureでは、3roundすべてで処理時間が26.9350～29.6854%短くなり、報告割当量が64B/op減った。補助ケースのWarmSendでは、処理時間が17.7666～27.7389%長くなった。この悪化は採用判断に含める。

同じroundの同じ5peerと比較した順位は、WarmとColdの両方でbaselineもcandidateも1位だった。Coldでは次点との差が広がり、Warmでは差が縮まったものの、今回の測定では順位を維持した。代表Coldの時間と割当量の利得、およびWarmの順位維持を採用方向の理由とする。数値上の順位上昇は観測していない。「順位が上がった」とは記述しない。

WarmとColdの発生頻度は不明である。両者の時間差を相殺した値や、一般のアプリケーション全体での純利益は算出しない。数値上の順位上昇を採用の必須条件とする場合、その条件は今回の結果では立証できていない。

## 変更の内容と適用範囲

比較対象はbaseline `7c69f2a7408a68f36cf25abc3c6ac74ef00c5054` とcandidate `7c758807ce7be6a5eb8df13d44ac5376385589e3` である。

candidateは、対象の生成経路にprivateな `SendOutcome` と `ValueTask<SendOutcome>` を返す実行本体を置く。実行本体は成功結果または捕捉した例外を返し、既存の `ValueTask<TResult>` 境界で呼出し側に渡す。`IsCompletedSuccessfully` がtrueの場合は、`GetAwaiter().GetResult()` で一度だけ結果を取り出す。取り出した結果に例外があれば、その例外を `AsyncValueTaskMethodBuilder<TResult>.SetException` に渡す。`IsCompletedSuccessfully` がfalseの場合は、完了済みのfault/cancelも含めて `AwaitOutcome` で一度だけawaitする。awaitで結果が得られ、その結果に例外がある場合は `ExceptionDispatchInfo` で伝える。結果型を公開APIへ追加する変更ではない。

この経路は、閉じた非同期Sendで、requestとhandlerが参照型、戻り値があり、handler factoryがなく、1個以上の参照型Hookを持つ場合に限定される。各HookのBefore、Success、Exception、FinallyがValueTaskで、Hook factoryがなく、route依存セットの計画が成立することも条件となる。依存セットには異なるreceiver型が2個以上必要である。generic、open registration、factoryを含む経路や既存の同期Before専用fast pathへ、一律に適用する変更ではない。

例外の同一性、OperationCanceledExceptionと取消token、observerによる例外の置換、Finallyの優先順位、呼出し順序を維持する。各callbackと内部ValueTaskの消費は一度に限る。非CLS throwには専用のcatchとFinallyの境界を残し、例外ラッピングの設定による既存の挙動を維持する。factoryを含む経路は既存の取得・cleanup処理を使う。これらは最終統合時にも確認する契約であり、今回の性能測定だけで全条件を検証したという意味ではない。

確認した固定sourceは `CanonicalCapabilitySourceEmitter.cs`、`CanonicalAsyncFailureDataEmitter.cs`、`CanonicalAsyncCallEmitter.cs`、`CanonicalRouteDependenciesEmitter.cs`、`CanonicalAsyncFastPathEmitter.cs` である。`CanonicalAsyncOutcomeBoundaryTests.cs` には非CLSラッピングの両設定を扱う検証がある。

## 比較の条件

正式peer42シリーズは2026年11月8日15:45:03～16:31:06 UTCに実行された。SDK 10.0.403、runtime 10.0.14、DI 10.0.12、BenchmarkDotNet 0.15.8の固定条件を用いた。

5peerのWarm/Coldを各3round測定した30件と、Zendiatorのbaseline/candidateのWarm/Coldを各3round測定した12件を合わせ、42件とした。peerはMediatRHistorical、Mediator、DispatchR、Immediate、Foundatioである。各anchorは同じroundの同じ5peerと個別に6者順位を計算し、もう一方のanchorを競合相手として数えない。別時期のpeer結果や.NET 11の結果は混ぜていない。

Warmは保持したnative entryを再利用し、invocation数とunrollは自動設定、iteration setup/cleanupはない。Coldは16384回、unroll 1で、freshな状態を準備・解放するcallbackを使う。WarmへColdの定義を適用していない。各cellはWarmup 20、Actual 12、launch 1を保持する。

## 全roundの結果

以下はkept Resultの平均である。時間の増減率は `(candidate / baseline - 1) × 100` で、正の値は悪化を表す。B/opはGCの生の割当bytesを総operation数で割り、最も近い整数へ丸めた報告値である。ちょうど中間の場合は偶数を選ぶ。

| ケース | round | baseline ns/op | candidate ns/op | 時間の増減 | 報告B/op | 個別6者順位 |
| --- | --- | ---: | ---: | ---: | --- | --- |
| WarmSend | 1 | 23.834 | 28.069 | +17.7666% | 0 → 0 | 1位 → 1位 |
| WarmSend | 2 | 22.880 | 28.744 | +25.6324% | 0 → 0 | 1位 → 1位 |
| WarmSend | 3 | 22.855 | 29.195 | +27.7389% | 0 → 0 | 1位 → 1位 |
| ColdOuterFailure | 1 | 5070.548 | 3704.797 | −26.9350% | 1776 → 1712 | 1位 → 1位 |
| ColdOuterFailure | 2 | 5217.172 | 3668.436 | −29.6854% | 1776 → 1712 | 1位 → 1位 |
| ColdOuterFailure | 3 | 5125.694 | 3738.284 | −27.0677% | 1776 → 1712 | 1位 → 1位 |

Warmの増加は4.235～6.340ns/op、Coldの短縮は1.366～1.549µs/opだった。Warmの0B/opは丸め後の値であり、あらゆる条件で割当がないことを証明する値ではない。

Warmの次点はround 1がDispatchRの59.637ns/op、round 2と3がImmediateの43.676、43.237ns/opだった。次点の時間はcandidateのそれぞれ2.125、1.519、1.481倍である。

Coldの次点は全roundでDispatchRであり、5882.534、5960.696、5994.159ns/opだった。次点/Zendiatorの時間比はbaselineの1.160、1.143、1.169倍からcandidateの1.588、1.625、1.603倍へ広がった。この差の拡大が、元の代表Coldケースにおける利得である。

## 変動と解釈の限界

42件すべてが成功し、210個のcollection pin、所有processが0になったこと、およびcleanupを確認済みである。全504件のActual、840件のWarmup、479件のkept Resultを区別して保存した。BDNによるResultの外れ値処理を、raw Actualの削除として扱っていない。

anchorのkept CVは約1.01～3.20%だった。一方、ImmediateのWarm round 1にはCV 29.95%と実行中の位相変化がある。このcellを除外・差替えせず、ほかのroundとともに残した。MinIterationTimeと外れ値の警告も保持している。Coldの固定16384回を含め、警告がある結果を都合よく除外していない。

3roundは探索的な比較であり、統計的保証や将来の順位保証ではない。旧3%ラベルや事後に変更した閾値を採否の拒否条件として使わない。ほかのhardware、SDK、runtime、pipeline、実アプリの遅延へ結果を一般化しない。

## 旧18件との関係

先行するZendiator単独の18件は、別シリーズとして保全する。FirstSendの時間変化はround順に+10.4179%、−13.3770%、−8.8678%で、方向は混在していた。round 2のbaselineのkept CVは29.53%で、この変動も保持している。報告割当量は各roundとも480→480B/opで、増減は0Bだった。これは初回successのcontrolであり、今回のpeer42にはFirstSendのpeer順位は含まれない。

旧18件のWarm/Coldもそのまま残す。peer42は同時間帯の代表順位を確かめるために別途固定したシリーズであり、有利な旧roundと入れ替えた結果ではない。

## 集計と証拠の保全

peer42の公式集計v1では、保存済みchildへ移動したFoundatioのinterceptor証拠について、3種類のpinの保存先変換に漏れがあった。失敗した集計、測定raw、旧sourceは保全した。新しいpost-exit集計v2は、期待hashを維持して保存先を解決し、旧公式source、新analyzer、adapter、および実行したbytesを確認するrunnerの来歴を結合した。

独立照合では、全42件の統計とGC、210個のpin、同roundの6順位block、Foundatioの実timed childとcompiled interceptor、writer・timed child・外側owned launcherの三層identityを確認した。最終照合でも公式集計v2との一致が確認されている。集計の修正のために再測定はしていない。

証拠の共通rootは `D:\gitroot\zendiator-perf-artifacts-20261108` である。

- 正式raw: `wp09-peer42-v1/FINAL.json`。SHA256 `7A9BC0944A365F150B0999FF7DE9E69630DA466099B01A211F09846BB85E5603`。
- 公式集計: `wp09-peer42-aggregation-v2/SUMMARY.json`。SHA256 `419288E88BDE85DDECC8D1C04E3F8FAF95736D80CAB8EECD749CD89CAD5352C2`。全cellと全roundは同directoryの `ANALYSIS.json` を参照する。
- 独立結果: `wp09-peer42-independent-sem-20261108-v1/conclusions.json`。SHA256 `0ECCF43ED47A5CC5E927612157802E49DCEC2D8EF8273CAA9FE95DAF70736396`。
- 最終集計照合: `wp09-peer42-final-aggregation-independent-sem-20261108-v1/review.json`。SHA256 `E398A61224E7BE714F998A562136033B43B8FBAF01B0494CF639F5F4BFDC542E`。
- 旧18件: `wp09-pairs-v1` と `wp09-aggregation-v3`。旧失敗集計や中間版と区別して保全する。

## 次に確認する事項

採用判断を確定する前に、最終統合、全体テスト、公開API、package、ほかの改善との組合せの各gateを完了する。統合したsourceが今回の限定経路と例外・取消・cleanup・単一消費の契約を維持していることも確認する。
