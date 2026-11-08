# WP02 dispose-only: 正式比較と不採用の記録

## 結論

2026-11-08 の dispose-only 候補は不採用とし、WP02 の検討を閉じる。既定実装は baseline のまま維持する。候補のソースと証拠は保全するが、既定経路へ統合しない。採用に都合のよい結果を待つための追加 peer 測定は行わない。

小さな改善も採用対象とし、トレードオフがあれば代表ケースの順位を上げる利益と比較する、という現在の基準で判断した。旧計画の固定 3% 閾値や有意水準だけを理由に拒否したのではない。EarlyBreak Stream5 sync の全 3 対の時間短縮と通常経路の割り当て削減は、観測された利益として残す。一方、複数の時間悪化と稀な dispose の追加割り当てを上回る、代表ケースの順位上昇につながる利益は確認できなかった。

元の rare-state 候補 `19efabb142cb12c13b275c4d995e6402215f638b` の経緯は [旧 WP02 記録](wp02.md) に残す。本書の dispose-only 候補と、旧候補の成功 campaign は別の比較である。

## 候補と正式条件

- Baseline: `7c69f2a7408a68f36cf25abc3c6ac74ef00c5054`、`D:\gitroot\zendiator-perf-base-20261107`、detached HEAD。
- Candidate: `f340ad5b3ac084eef69086af77d1b7da694c3e0c`、`D:\gitroot\zendiator-wp02-fixed-20261108`、detached HEAD。
- 変更: pending-Move の表現を元のまま保持し、dispose の 2 参照を遅延 `DisposeState` に分離する。通常 cursor の固定参照を 1 個減らす案。
- .NET runtime `10.0.14`、SDK `10.0.403`、DI `10.0.12`。DI file version `10.0.1226.42308`、SHA256 `2D0E268BF76B4CE36A55EBB2767354DB956C6660DE65D6CCCFC35C957FB12CFC`。
- Release、affinity 1、20 Warmup、12 Actual、要求 iteration time 500 ms、launch 1。10 ケース全量、3 対。順序は AB / BA / AB（A=baseline、B=dispose）。
- 実施時刻: 2026-11-08 08:08:09～08:39:23 UTC。V2 の 60 セルのみを今回の成功結果として扱う。

## 入力と実行した binary の追跡

非計時の locked restore と非 incremental Release build で入力を固定した。各 run の前後で detached HEAD、clean source、revision、sourceDigest、SDK、runtime、DI、親 host 側の runtime / benchmark / generator DLL SHA を検査し、同一 revision の同一性 guard も維持した。

親 host の DLL と BDN が実際に実行した child の DLL は別 image である。親 host の SHA を実 child の証明に代用していない。各 child の setup 全 assembly tuple が final に保持され、final 全 map が binding と一致し、389 ケースを検証する consumer gate の map に完全に含まれることを確認した。各 variant の全 30 child で REQUIRED6 の fullName / MVID / SHA が一致した。後から load された assembly を許容しつつ、final map 全体の証明を保った。

| 検証項目 | V2 の結果 |
| --- | --- |
| 正式セル / 対比較 | 60 / 30 |
| Warmup / Actual / 採用された Result | 1,200 / 720 / 665 |
| 実 child に結び付いた consumer gate | 60 × 389 Passed |
| owned process receipt | 144 件すべて exited、exitCode 0、cleanupVerified=true |
| 終了後の所有 process | live 0 |
| 独立照合した証拠ファイル | 17,304 件の SHA 一致 |

`sourceDigest` と親 host の事前固定値は次のとおり。実 child の REQUIRED6 tuple は集計の `actualRequired6PerVariant` に別途記録している。

| variant | sourceDigest | 親 runtime DLL SHA256 | 親 benchmark DLL SHA256 | 親 generator DLL SHA256 |
| --- | --- | --- | --- | --- |
| baseline | `F6966CCBCEDF4757889A56772D519EF9DCEE8551C9245F517E67D89DEFD670B7` | `98983A99017A972586293DCE4FC5A720D593F8C984FCE3EAAF82327A74B16A78` | `587D221C4532E5E363AECE133F2CEF295E604E9965DBBB6D8B4527F50E46237F` | `E9B84F590321CBCF4C0E3DEB00348175CDB5EAD100FB3F718D709A4541AA0B1A` |
| dispose | `884CF64479F726E8F6AC6EB65759B2CB69BF12FD4254C7E9235B6F92FDD70A6A` | `89EC5A75343CA4597D33F18FEB5481C6533D5FA2DE9520E4690676BB301BF7C7` | `932B4B1D5718267B4D561B012848028F062D1861D8BAB1D2EB6CA06AC2A9AE32` | `3549F77971A133308696F2B6884BAB14A25AE4D8761193BD1FAF8516C63F78A4` |

## 全 30 対の結果

時間差は `(candidate / baseline - 1) × 100`。負が短縮、正が増加。平均は BDN が保持した Result の ns/op で、表は小数点以下 3 桁、時間差は 4 桁に丸めた。元の精度は `pairs.csv` に保持する。B/op は BDN の報告値で、差は candidate − baseline。`false` は同期、`true` は非同期。全対を残し、不利な対を除外していない。

| Type | Method | Count | Async | 対 | baseline ns/op | candidate ns/op | 時間差 % | baseline B/op | candidate B/op | B/op 差 |
| --- | --- | ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| ZendiatorRelay5 | Full | 1024 | false | 1 | 14100.533 | 12466.668 | -11.5873 | 1216 | 1168 | -48 |
| ZendiatorRelay5 | Full | 1024 | false | 2 | 12169.569 | 13313.588 | +9.4007 | 1216 | 1168 | -48 |
| ZendiatorRelay5 | Full | 1024 | false | 3 | 11238.003 | 11454.725 | +1.9285 | 1216 | 1168 | -48 |
| ZendiatorRelay5 | Full | 1024 | true | 1 | 170153.731 | 166273.167 | -2.2806 | 116562 | 116514 | -48 |
| ZendiatorRelay5 | Full | 1024 | true | 2 | 151616.806 | 166008.482 | +9.4921 | 116561 | 116513 | -48 |
| ZendiatorRelay5 | Full | 1024 | true | 3 | 149504.203 | 153395.271 | +2.6026 | 116562 | 116514 | -48 |
| ZendiatorStream0 | EarlyBreak | 16 | false | 1 | 65.665 | 62.980 | -4.0888 | 160 | 152 | -8 |
| ZendiatorStream0 | EarlyBreak | 16 | false | 2 | 64.825 | 63.201 | -2.5048 | 160 | 152 | -8 |
| ZendiatorStream0 | EarlyBreak | 16 | false | 3 | 57.289 | 57.756 | +0.8141 | 160 | 152 | -8 |
| ZendiatorStream0 | EarlyBreak | 16 | true | 1 | 1427.480 | 1642.132 | +15.0372 | 600 | 592 | -8 |
| ZendiatorStream0 | EarlyBreak | 16 | true | 2 | 1612.212 | 1624.489 | +0.7615 | 600 | 592 | -8 |
| ZendiatorStream0 | EarlyBreak | 16 | true | 3 | 1424.001 | 1445.553 | +1.5135 | 600 | 592 | -8 |
| ZendiatorStream0 | Full | 1024 | false | 1 | 10461.270 | 11248.852 | +7.5285 | 160 | 152 | -8 |
| ZendiatorStream0 | Full | 1024 | false | 2 | 11261.581 | 11036.075 | -2.0024 | 160 | 152 | -8 |
| ZendiatorStream0 | Full | 1024 | false | 3 | 10258.065 | 10630.932 | +3.6349 | 160 | 152 | -8 |
| ZendiatorStream0 | Full | 1024 | true | 1 | 160357.032 | 166162.614 | +3.6204 | 115457 | 115450 | -7 |
| ZendiatorStream0 | Full | 1024 | true | 2 | 161894.157 | 164880.551 | +1.8447 | 115458 | 115450 | -8 |
| ZendiatorStream0 | Full | 1024 | true | 3 | 143087.842 | 147437.481 | +3.0398 | 115457 | 115449 | -8 |
| ZendiatorStream5 | EarlyBreak | 16 | false | 1 | 421.145 | 403.650 | -4.1540 | 1216 | 1168 | -48 |
| ZendiatorStream5 | EarlyBreak | 16 | false | 2 | 427.784 | 420.964 | -1.5943 | 1216 | 1168 | -48 |
| ZendiatorStream5 | EarlyBreak | 16 | false | 3 | 385.969 | 374.318 | -3.0185 | 1216 | 1168 | -48 |
| ZendiatorStream5 | EarlyBreak | 16 | true | 1 | 2303.539 | 2092.253 | -9.1723 | 1656 | 1608 | -48 |
| ZendiatorStream5 | EarlyBreak | 16 | true | 2 | 2186.123 | 2073.067 | -5.1715 | 1656 | 1608 | -48 |
| ZendiatorStream5 | EarlyBreak | 16 | true | 3 | 1924.690 | 2114.004 | +9.8361 | 1656 | 1608 | -48 |
| ZendiatorStream5 | Full | 1024 | false | 1 | 12991.397 | 11388.174 | -12.3407 | 1216 | 1168 | -48 |
| ZendiatorStream5 | Full | 1024 | false | 2 | 12593.531 | 13572.025 | +7.7698 | 1216 | 1168 | -48 |
| ZendiatorStream5 | Full | 1024 | false | 3 | 12477.163 | 17568.903 | +40.8085 | 1216 | 1168 | -48 |
| ZendiatorStream5 | Full | 1024 | true | 1 | 167435.118 | 166156.354 | -0.7637 | 116562 | 116514 | -48 |
| ZendiatorStream5 | Full | 1024 | true | 2 | 156512.387 | 166311.083 | +6.2607 | 116562 | 116514 | -48 |
| ZendiatorStream5 | Full | 1024 | true | 3 | 149708.721 | 150483.698 | +0.5177 | 116561 | 116513 | -48 |

### ケース別の中央値と割り当て差の範囲

| Type / Method / Count / Async | 3 対の時間差の中央値 % | B/op 差の範囲 |
| --- | ---: | ---: |
| ZendiatorRelay5 / Full / 1024 / false | +1.9285 | -48 ～ -48 |
| ZendiatorRelay5 / Full / 1024 / true | +2.6026 | -48 ～ -48 |
| ZendiatorStream0 / EarlyBreak / 16 / false | -2.5048 | -8 ～ -8 |
| ZendiatorStream0 / EarlyBreak / 16 / true | +1.5135 | -8 ～ -8 |
| ZendiatorStream0 / Full / 1024 / false | +3.6349 | -8 ～ -8 |
| ZendiatorStream0 / Full / 1024 / true | +3.0398 | -8 ～ -7 |
| ZendiatorStream5 / EarlyBreak / 16 / false | -3.0185 | -48 ～ -48 |
| ZendiatorStream5 / EarlyBreak / 16 / true | -5.1715 | -48 ～ -48 |
| ZendiatorStream5 / Full / 1024 / false | +7.7698 | -48 ～ -48 |
| ZendiatorStream5 / Full / 1024 / true | +0.5177 | -48 ～ -48 |

## 採否に影響した観測

- EarlyBreak Stream5 sync16（L2）は全 3 対で短縮した。中央値は約 −3.02%、BDN B/op は各対で −48。この小さな利益を認めたうえで比較した。
- Stream0 Full async1024（L5）は全 3 対で +1.84～+3.62%、中央値 +3.04%。BDN の丸め後の B/op 差は対順に `[-7, -8, -8]`。未丸めの値はおおむね 8 B の削減に近いが、全対を厳密に −8 B と書き換えていない。
- Full Stream5 sync1024 の第 3 対は +40.8085%。候補の保持 12 点は平均 17.5689 µs、SD 0.2069 µs、CV 1.1778%。baseline の保持 10 点は平均 12.4772 µs、SD 0.2456 µs、CV 1.9683%。候補の Actual 全 12 点の範囲 17.3359～17.9247 µs は、baseline の Actual 範囲 12.1205～13.5820 µs より高い。単一の外れ値ではなく、run 全体が高い値にとどまる plateau として保全する。原因は確定していない。
- 10 ケース中、全 3 対で短縮したのは 1 ケース、全 3 対で増加したのは 2 ケース、方向が混在したのは 7 ケース。Warmup の段階的な変化や MinIterationTime 警告も保全した。JIT や製品変更を原因と断定していない。

3 対から求めた log-ratio の t95 区間は探索的で、多重比較を調整していない。round 間の独立性と log-ratio の正規性は未確認である。exact sign 検定の最小両側 p は 0.25。run 内の Result 点を独立した反復 run として扱わず、統計的な有意差の有無だけで採否を決めない。

## 既知 peer との距離と順位の限界

別時間帯の初回 CSV では、L2 は Zendiator 391.71 ns、Immediate 270.06 ns であった。既知のこの相手に届くには約 31% の短縮が必要で、今回の約 2～4% の改善幅では差を埋められない。割り当ても今回の候補 1168 B/op に対し、歴史値の Immediate は 1104 B/op である。

ただし L2 の初回 CSV はこの 2 library だけで、全主要 peer の最近傍を確認した資料ではない。Foundatio は別時間帯、WP04 の opt-in 比較も別系列であり、本比較へ混ぜて新しい順位を算出しない。対応する peer 行のないケースでは最近傍を主張しない。未測定の近接 peer が存在しないと証明したわけでもない。

既知の距離は判断の文脈に限って使う。L2 の利益は既知の差を埋めず、L5 の全対悪化や Full5sync の不利な plateau、稀な dispose の追加費用を上回る順位上向きの利益は未証明である。この証拠で候補を不採用とし、有利な順位が出るまで peer 測定を繰り返さない。各時間変動を製品変更の因果効果として断定する判断でもない。

## 稀な dispose の割り当て診断は別資料

V1 期の同 source / 同 IL の別 binary による診断では、通常・未使用 cursor が −8 B、faulted dispose または pending/joined dispose が同一 calling thread 上で +24 B だった。診断候補の SHA は `29FE…` で、V2 の親 host `89EC…` や実 child `FEFFE…` と異なる。この診断を現在の V2 binary の測定と呼ばず、V2 の時間結果や順位に混ぜない。process-wide total は thread 単位の差と区別し、+24 B を全 race の割り当て上限とはしない。

## 失敗した V1 と V2 の区別

`wp02-dispose-pairs-v1` は 60 child の実行が成功していても、最後の same-revision identity guard が失敗した正式 campaign である。今回の成功 60 セルへ 1 件も含めない。原 source、controller、raw、失敗状態を保全した。

同じ f340ad5 commit で、GitTask 10.0.111 が symbolic HEAD と packed ref から revision を取得できず、InformationalVersion と PDB SourceLink が欠落したことを独立診断で確認した。CLI git は revision を解決でき、比較した全 method の IL / EH は同一だった。内部 reader の原因や maintenance の起動者は未確定である。

V2 は exact commit の新しい detached worktree で、非 compile GitTask 診断の SourceRevisionId / SourceRoot RevisionId 一致と警告 0 を確認した。通常の locked restore / build から新たに入力を固定し、全 60 セルを測定した。古い binary のコピーや有利な round の選別で guard を通していない。これにより今回観測した SourceLink 障害の条件を避け、V2 の同一性を検証した。

## 証拠の場所と SHA256

以下の相対パスの基点は `D:\gitroot\zendiator-perf-artifacts-20261108`。raw、診断、失敗記録、集計を別フォルダのまま保全している。

- 正式 raw: `wp02-dispose-pairs-v2`。`controller.json` → 各 run の `session.json` → case の `artifact-binding.json` → setup / final / gate proof / 保存 bundle を追跡できる。
- 集計: `wp02-dispose-aggregation-v2`。全数値は `cells.csv` と `pairs.csv`、proof と receipt の hash は `proof-pins.json` と `process-receipt-pins.json`。
- 固定入力: `wp02-dispose-controller-v2`。`FROZEN-INPUTS.json`、`PREPARATION.json`、matrix / policy、GitTask 診断、同 run の RTK 完全 build log を保持。
- 独立 raw / 判断: `wp02-raw-independent-sem-20261108-v1`。`review.json`、`conclusions.json`、`decision-scope.json`。
- 過去の障害診断: `wp02-independent-sem-20261108-v1/review.json`。失敗 raw は `wp02-dispose-pairs-v1`、稀な経路の別診断は `wp02-disposal-diagnostic-v1`。

| 証拠 | SHA256 |
| --- | --- |
| `wp02-dispose-aggregation-v2/summary.json` | `C36ED954870DEC28B10D8E44F08CC88DE851E0606CC3A038E0DCCF54B4D1DB59` |
| `wp02-raw-independent-sem-20261108-v1/review.json` | `84E0BDCD393B0AE0E8EE97B06117C4A0ADD430F7F5A7BB207BDEF506911701A2` |
| `wp02-raw-independent-sem-20261108-v1/conclusions.json` | `58B6F76433AF5875BEE166CB4E2E8FDA7D988231F3875D5375E0E25827CCDC55` |
| `wp02-raw-independent-sem-20261108-v1/decision-scope.json` | `43693E435B0FBFEE44DE9F2FAB729D7B9CDA8B8FB8CE757C8540C13F9825776F` |
| `wp02-dispose-controller-v2/run.ps1` | `5BD89491D478684108D7807ED63715C84E1AEA97B4BEBBAEA9816E0BD5B0255D` |
| `wp02-dispose-controller-v2/PREPARATION.json` | `749200F9F229CD9C0DB1E2C596FB15DDD4AD7D70E832ACF2F09967A6D042C600` |
| `wp02-dispose-controller-v2/FROZEN-INPUTS.json` | `D1076C70A7078736E87B5B9662E2C840D970177CBFC95BC3D243415995E851F7` |
| `wp02-aggregation-authoring-v2/aggregate_dispose60_v2.py` | `98FA9D4114B3534F0407D42E4EF0C2412A530815C393AC75A474C76BED3D8CEB` |
| `wp02-independent-sem-20261108-v1/review.json` | `2361F8B2323069BF968D9F0964D7384B6C4C20D6BD0626ED3C2F3ACC42274780` |
