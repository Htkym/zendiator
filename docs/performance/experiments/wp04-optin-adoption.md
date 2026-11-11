# WP04：struct stream middleware の opt-in 採用

## 現在の判断

2026-11-11の明示的なユーザー判断により、既存WP04をopt-inとして採用する。実装は作業branchへ統合済みで、既定経路は変更しない。phase1と同期Relay限定診断を完了し、追加測定は終了した。全改善計画のphase2評価やmerge/releaseの完了とは区別し、旧HOLD判断と全rawを履歴として保全する。

根拠は、対象となるStream5経路の割り当てが3対を通じて200 B/op減り、既定構成の生成sourceを変えない点である。型付きのstruct portを選ぶ構成に限定し、既存の参照型middleware経路はそのまま残す。

速度の向上は確定していない。多くの対は小さな変化だったが、feature有効のEarlyBreak同期で1対に約43%の悪化、feature無効のFull同期で別の1対に約46%の悪化を観測した。これらの不利な値も除外していない。すべての対で一方向に遅くなったケースはなかったが、それだけで時間回帰がないと保証するものではない。

## 変更と費用

公開IStructStreamMiddleware<TRequest,TItem>契約と、concrete generic portを用いる限定経路を追加した。既存のinterface形状を置き換えず、使用する構成で明示的に選ぶ。

同期Fullの対象ケースは1216 B/opから1016 B/opへ減った。過去の同fixtureではImmediateが1104 B/opだったが、この旧記録を用いて新しい時間順位や6ライブラリ全体の順位を作らない。今回の120セルでは、新しいpeer全体の順位比較は行っていない。

トレードオフは、新しいopt-in契約の維持と対象構成の制約である。性能順位を下げる普遍的な時間コストは確認できておらず、既定利用者の生成sourceは不変であるため、確実な割り当て削減を選べる経路として残す。時間については不確実性を明示し、最終統合の代表ケースと合わせて評価する。

## 検証と証拠

製品変更は2822a638c8aacfacdc3816f31af22a838e983a2b、実child追加gateは998abae44668e374b3d8119f385be9abe133b390。作業branchではそれぞれecc8b5b、ac2a265として統合した。

既定の生成108ファイルと既存shimのbyte同一性、96件のstruct upstream parity、3つの実際のdriver型、389件のconsumer gateを確認した。関連runtime/generator試験、API、pack、managedとNativeAOTのcanaryも先行実験で成功した。新しいgateは性能測定の実child bundleに対して追加の96件とdriver識別を要求する。元の389件を減らして代用するものではない。

正式120セル、raw、全対の比率、独立レビュー、集計訂正の履歴は[WP04元実験](wp04.md)と隣接JSON/CSVへ保存した。最終統合の検証結果と最新の採用判断は最終概要へ追記する。mainへのmergeとreleaseは行っていない。

## 2026-11-11：phase1 と同期 Relay 限定診断後の結論

ユーザーは約200 B/op削減と時間の両方向の観測を確認し、「これなら採用方向でいいと思います」「同期relayだけは採用した上で改善できないか検討してみて」と判断した。既存WP04を明示的なopt-inとして維持し、同期Relayの追加診断を終了する。既定設定への切替、全A→B変更の一括採用、merge/releaseの承認とは扱わない。

対応はA＝canonical baseline 7c69f2a、B＝final d2f3c4adのordinary経路、S＝同じphysical Bでstruct upstreamを選ぶ別compositionである。WP05はEagerDependenciesによるeager依存注入（B→E）であり、この200 B/op削減とは異なる。

fc05 phase1は117/117セルと保存結果解析が成功した。B→Sの対象6ケースすべてで約200 B/op削減を維持し、同期Relay Full Count1024の同round比は+7.283%、+3.986%、-0.203%だった。3対中央値+3.986%を統計的に確定した恒常的悪化とは扱わない。代表5ライブラリの同round順位はB/Sで同じだった。

続く同期Relayだけの診断は、既存Count16/1024・B/SをAB/BA/ABの3対、12セルで実施した。全24 main/gate commandのexit0・cleanup・owned0を回収し、全6対で約1216→1016 B/opを維持した。

| Count | 同round S/B時間変化（1 / 2 / 3） | 3比率の中央値 |
| --- | --- | --- |
| 16 | -8.197% / -3.481% / -3.562% | -3.562% |
| 1024 | -5.082% / -35.635% / +0.187% | -5.082% |

時間は各BDN raw meanNsを丸める前に比較した。Count1024のround2はBが17.324 μsで、他2回のBは12.169/11.555 μs、Sは11.551/11.151/11.576 μsだった。このB高値を除外せず、今回の中央値を速度改善確定や「悪化を修正した」という主張には使わない。1回のCPU観測は約10%でディスク計数器は取得不能だった。開始前のimport接続不足と6セル目選択前のlibrary条件不足は保全し、取得済み5セルを再測定せず残り7セルを完了したため、round2 Count16対は修正待ちの時間差を含む。

Count16と1024の2点から固定費と要素数比例費を仮定した差の傾きは-0.566、-6.105、+0.042 ns/itemで、一貫した正の追加費用を特定できなかった。保存生成sourceの主要MoveNext/identity lease本体は識別子の差を除き同一であり、native JIT codeや配置まで同一という証明ではない。安全な局所改善の原因は特定できず、推測によるinlining指定、portのclass化、token・ownership・cleanup検査の削除は行わない。製品sourceの追加変更も追加測定もここでは不要とした。

### API と NativeAOT の適用範囲

- canonical製品API3組は属性・parameter名の比較を含む既存レビューを再利用する。生成consumer A→B/B→Eの標準directional比較はexit0だが、属性完全一致は無効で、System.Security.Permissionsの非致命的resolver診断4件を保全している。生成consumerの全属性同値・完全な推移参照closureは未証明である。既存レビュー範囲では未承認の破壊的差なしと判定されたもので、全API同値とは言わない。この検証範囲を明記する限定opt-in採用の留保として残し、再API比較は要求しない。
- win-x64 managed/NativeAOTのstruct-upstream package canary成功と、値型を使うopen-generic DI閉包2ケースのLIMITを分ける。後者はNativeAOTで必要なruntime generic構築ができない対象構成の未対応であり、生成struct portそのものがNativeAOT非対応という意味ではない。対象構成の利用者には実際の制約となるため対応済みとは案内しない。現行のサポート範囲でopt-inを残す妨げとはせず、全型・全RIDのAOT保証を要求するなら未解決事項となる。詳細は[既存制限](../../release/known-limitations.md#native-aot-and-dispatch-boundaries)を維持する。

### 保存済み証拠と残作業

- phase1：`D:\gitroot\zendiator-perf-artifacts-20261108\fc05\formal-output\phase1\ANALYSIS.json`、SHA256 `A1CC8CF802AF30227EB43B163B9F39D0C9CF2DB8F04798E4036563CD56C4A84D`。
- 限定診断：`C:\Users\h_tky\Documents\Codex\2026-11-10\task\zen-wp04-relay-diagnostic-v3\ANALYSIS.json`、SHA256 `A4E05460425C86E282A7163F576E58D170022C94DC7526B232938432C9E0E227`。同folderのHANDOFFとFINALが元raw・全receiptへ結合する。v1/v2の失敗原本と旧phase1結果は不変保全した。
- WP04の追加性能改修・再測定は終了。実装は既存のopt-inのままであり、新たな公開API・既定値変更はない。残るのはこの文書差分の確認と作業branchでの通常の変更確定である。採用統合に必要な正規Git確認で、作業branchは`perf/plan-20261107`、変更前HEADは`0c9a8795380bcb5ff6e4304fb770543465709b64`と確認した。実装`ecc8b5ba4e3a330bd4b81172f94f7e00afe2f99a`とgate`ac2a26589532ea69bbaabb169f0a23e4fa387e74`はいずれもその祖先で、新しい製品実装の追加や再取り込みは不要である。今回の文書更新を同branchで通常commit/pushし、通常CIの結果をhandoffへ残す。
- 全改善計画の完了とは区別する。phase2の93セルやWP05/08/09の最終統合性能採否は別工程で、この診断の終了を自動GOにはしない。
