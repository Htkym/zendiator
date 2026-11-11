# WP05：明示的な eager 依存注入

## 判断

明示指定の opt-in として採用する。既定の依存取得は lazy のまま維持し、ベンチマーク用の6依存指定を製品の既定設定へ広げない。

同期5段 Send の scope 全体は3対とも改善し、候補÷基準の時間比の中央値から計算した処理時間の短縮率は41.4142%だった。同じ回で比較した5ライブラリ内の順位は、基準の2位・3位・2位から候補の1位・1位・1位へ上がった。一方、指定した依存を使わない Send0 は12.7392%遅くなり、144 B/op増えた。用途を限定して選べる opt-in として、代表ケースの順位改善を優先する判断である。

これは今回の同期5段 fixture と6依存指定の結果であり、全アプリケーションや全構成で同じ改善を保証するものではない。Foundatio を含む6ライブラリ全体の順位は、この実験では測っていない。

## 実装と互換性

composition 属性へ EagerDependencies を追加した。対象は、生成時に選択された closed Send route の依存で、同期stageに属する公開参照型に限定される。重複指定、無関係な型、open generic、session/factory 経路などは対象にしない。

native DI が、IServiceProvider と指定された型付き依存を受ける公開 constructor を選ぶと、依存を既存の個別 cache へ入れる。既存の IServiceProvider のみの constructor と未指定サービスの lazy 取得は維持する。手動で既存 constructor を呼ぶ場合や、native DI がその constructor を選ぶ場合には eager 解決を行わない。登録不足を必ず constructor 時の例外へ変える機能ではない。

破棄の所有権は native DI のままである。route dependency carrier を使う構成は eager 対象から除外し、constructor と carrier による transient の二重 activation を避ける。Before だけの同期 hook と、carrier を使わない struct handler の対照も確認した。

関連10試験と389ケースの consumer gate が成功した。独立コードレビューで発見した carrier 判定漏れは、lowering と同じ UsesDependencyCarrier 判定を共有する修正で解消し、2つの回帰ケースを含めて確認した。全機能を組み合わせた最終検証は最終概要へ記録する。

## 比較条件と結果

製品候補は e7b04b974db5388f2a8e6dde7574dcffb65f9d7d と carrier 修正92bc95a1f05ceba079ec5235c499262fac0cc8c3。測定用の明示6依存設定は04371cebe9f3f85981b6549325536d1c13902002。基準は同じ製品候補の既定 lazy 設定で、機能の有効・無効だけを比較した。

SDK10.0.403、.NET10.0.14、DI10.0.12、BenchmarkDotNet0.15.8、affinity1、warmup20、actual12、要求iteration500ms、launch1。実際の retained Result は BenchmarkDotNet の通常の外れ値処理後であり、各Nと標本SD/CVも保存した。独自に都合の悪い値を除外していない。

2026-11-08 02:14:04～02:41:11 UTC に、8ケースをAB/BA/ABの3対、計48セルで実行した。全6runが正常終了し、120件の所有プロセスreceiptでexit0とcleanupVerifiedを確認した。

| ケース | 時間比の中央値による変化 | 対の方向 | B/op |
| --- | --- | --- | --- |
| Send5 ScopeK1 | −41.4142% | 3対すべて改善 | 704→704 |
| Send5 FirstSend | −37.0170% | 3対すべて改善 | 576→576 |
| Send5 cached Typed | −1.7988% | 改善・悪化が混在 | 0→0 |
| Send0 ScopeK1：指定依存未使用 | ＋12.7392% | 3対すべて悪化 | 584→728 |

ScopeK1 の時間比は0.575375、0.603542、0.585858。依存を先に受け取ることで allocation が消えたとは主張しない。未使用経路の144 B/op増加は、先に作られた6依存の費用である。

同時比較の対照ライブラリにも変動がある。DispatchR の中央値は＋9.070%、Immediate は−4.296%、Mediator は＋8.111%、MediatRHistorical は＋0.626%で、改善・悪化が混在するものもある。これらの変動を Zendiator の製品変更の効果として数えない。

## 証拠と独立確認

raw：D:\gitroot\zendiator-perf-artifacts-20261108\wp05-eager-pairs-v2

集計：D:\gitroot\zendiator-perf-artifacts-20261108\wp05-eager-aggregation-v1

独立確認：D:\gitroot\zendiator-perf-artifacts-20261108\wp05-independent-sem-20261108-v2

独立確認のreview.jsonはSHA256 F186F351C6325C0E9443318AE315CFF01D4403D9C924E9A67C1DA1474A2017C0。1,628 checksで失敗0。48件のraw retained ResultからN・平均・標本SD・中央値・B/opを再計算し、全対の比率、順位、120件のexit、setup→final→semanticのassembly SHA/MVID、requestのhashとoutcomesを照合した。

BenchmarkDotNet は製品参照も再buildするため、親hostのDLLと実childのDLLが同じhashであるとは限らない。実childと、そのchildに結び付いた semantic proof のSHA/MVIDの一致を確認する。389ケースのconsumer gateは追加assemblyをloadするため、map全体の件数一致ではなく、実child対象mapの完全包含を検査する。

元のsummary.jsonのcontrollerSHA256はcontroller.jsonという実行manifestのhashである。run.ps1のscript hashとは別であり、元データは変更していない。scriptのSHA256は58F90F8AEEA97B81D107F1C32B725588265DE126F7A54E00A5BB4A30CC3D131D。

先行した[allocation費用分解](wp05.md)の56 B/op削減は既存LazyBlocks設定の診断であり、今回のeager機能の成果とは区別する。旧来の3%ラベルは履歴として保全し、最新の「小改善も採用し、tradeoffは代表ケースの順位で評価する」指示に基づいて判断した。mainへのmergeとreleaseは行っていない。

## 2026-11-11：自動選択の検討

自動eager化は追加しない。生成器は候補となるclosed Send依存を特定できるが、静的参照やhandlerの存在だけでは、Mediator解決後にそのrouteが実行されることを証明できない。分岐・short-circuit・キャンセル、登録factoryやoverride、constructorの副作用・例外、scope/lifetimeと活性化順序もある。純粋に見えるconstructorでもDIの実登録までは静的に固定できない。

初回Sendで一括解決しても未実行の内側依存を生成し得る。実際の呼出し位置で必要な依存だけを解決し以後cacheする方式は、既存のlazy getterが既に実装している。履歴から次のscopeをeager化する推測も行わない。未指定時は既存lazy、意図的に活性化時点を早めてよい用途だけEagerDependenciesを明示する。新しいflagや推測modeは追加していない。WP04の標準typed契約とは別の判断である。
