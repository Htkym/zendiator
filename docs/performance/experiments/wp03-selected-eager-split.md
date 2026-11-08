# WP03 明示eager依存だけのwarm/cold分離

## 判定

候補 `3f307a363a74ba3e169216b2129bca42ff94c1d5` は今回採用せず、明示eager依存の既存getterを維持する。5段のscope作成からSendまでを含む代表ケースが3対中2対で遅くなり、warm Typedも方向が安定しなかった。BDNの報告B/opは全対で変わらず、代表ケースの順位を上げる便益は立証できていない。

小さい改善を数値の閾値だけで切り捨てた判定ではない。速い2対だけを選べばTypedの約47～53%短縮になるが、同じ入力で約132%遅かった対も有効な結果として残す。FirstSendの見かけの短縮は、短いiterationとActual計測区間中のiteration平均の推移を踏まえ、限定的に解釈する。

旧候補の全getter分離と今回の候補は別の変更である。旧候補 `3bafd184ed662b357430a2409a7c017cfb1d513b` の54件と当時の判定は [旧WP03記録](wp03.md) に保全する。今回の24件を旧候補へ合算しない。

## 変更と検証

今回のbaselineは、6依存を明示eager指定したfixtureを含む `04371cebe9f3f85981b6549325536d1c13902002` である。候補は同じfixtureに対し、選択されたeager依存の `Required_*` / `Service_*` getterだけでcache hitを残し、cold部分を `Resolve_*` helperへ分ける。未選択依存と既定lazy経路は従来の形を維持する。新しいeager指定、Inlining属性、cache、DI所有権は追加しない。

2つのemitterと関連cache試験を変更した。null・例外後の再試行、再入、provider-only constructorのfallback、struct/参照型の形を確認し、関連19件と対象4ファイルのformatを通した。生成比較ではGUID由来catalog identityと改行差を区別し、catalog以外の402ファイルはCRLFをLFにそろえた比較で一致した。生成物の全byte同一性を証明したものとしては扱わない。最初の厳密比較の失敗も保全した。

正式入力では、baselineと候補をそれぞれclean detached HEADへ固定した。SDK 10.0.403、runtime 10.0.14、DI 10.0.12、Release、affinity 1、warmup 20回、measurement 12回、1 launchで、AB/BA/ABの順に4ケースを測った。500 msは要求iteration時間であり、実測時間が必ず500 msになるという意味ではない。FirstSendは16384 invocation、unroll 1、fresh-scope callbackを維持した。

正式計測は2026-11-08 06:59:22～07:12:55 UTCに完了した。全6runがexit 0、24件の実childに結び付いたartifact gateがそれぞれ389件すべてPassed、72件のowned receiptがexit 0かつcleanup確認済みだった。同じvariantの必要6 assembly、runtime/DI、controls、要求と実childの結合も確認した。

## 全3対の結果

時間差は候補 / baseline − 1であり、負値が短縮を表す。BDN報告B/op差は全12対で0だった。中央値だけで採否を決めない。

| Scopedケース | 第1対 | 第2対 | 第3対 | 対比の中央値 |
| --- | ---: | ---: | ---: | ---: |
| Send5.Typed | +132.174995% | −52.704588% | −47.429991% | −47.429991% |
| Send5.FirstSend | −30.079054% | −12.873174% | −30.737089% | −30.079054% |
| Send5.ScopeK1 | +29.816034% | −0.456831% | +9.499061% | +9.499061% |
| Send0.ScopeK1 | −0.903624% | −6.224284% | +2.396984% | −0.903624% |

baselineのround平均の幅は、Typed 10.876531%、FirstSend 4.999396%、Scope5 5.426039%、Scope0 6.330921%だった。各roundの平均・標本標準偏差・kept数・CV・GC値と全対比は保存CSV/JSONから追える。

不利だった第1対の候補Typedは、kept 12件が20.55～22.71 ns、平均21.434 ns、標本標準偏差0.872 ns、CV 4.07%だった。単一の外れ値で平均が膨らんだ形ではない。Actual全12件も21.43～23.59 nsにあり、残り2対の候補の平均4.841 / 4.860 nsとは異なるplateauだった。実childのassembly、runner、controls、profileにおいて、記録上の違反は検出しなかった。自動invocation数やiteration時間の差は観測したが、JIT等の原因を立証したものではない。この対を除外しない。

FirstSendはbaseline/candidate全6件でMinIterationTime警告があり、Actual iterationは5.53～15.90 msだった。baselineのCVは約40～42%で、Actualの各iteration平均が約750 nsから350 ns付近へ推移した。3対すべての負値を、そのまま安定状態で約30%速くなった根拠にはしない。固定定義を都合よく変更して元の結果を置き換えることもしない。

## 独立照合と限定診断

独立した解析で24件、12対、480 Warmup、288 Actual、274 keptを再計算したところ、集計結果と一致した。保存bundle 6600ファイル、証拠6981個の一意SHA、24 gateと72 ownedを確認した。各対の不利な値やBDNが除外したActualも保存されている。

原因の確認には、既存のSend5NativeProbeとRunnerProbeだけを使う最大4プロセス・4分の診断を計画した。最初のbaseline RunnerProbeが2026-11-08 07:57:47～50 UTCにSetupで停止した。既存の証拠コードがentry assemblyのRunnerProbe.deps.jsonを期待し、指定した保存BDN depsと一致しなかったためである。対象dispatchのnative/ILは取得できず、製品側の原因を示す結果には至っていない。入力SHAは前後不変、ownedは0だった。

計画した残り3プロセス、再試行、build、追加の時間測定は実行しなかった。この診断失敗を製品不具合の根拠や、元のTyped対の無効化理由にはしない。Scope5の競合30件案も未実行であり、順位改善を主張しない。修正可能な具体的な原因が得られなかったため、今回の候補を不採用として閉じる。

## 証拠の所在

すべてのローカル相対pathの起点は `D:\gitroot\zendiator-perf-artifacts-20261108` である。測定、解析、失敗した診断はそれぞれ別folderへ保存し、既存版を上書きしていない。

| 証拠 | 相対path | SHA256 |
| --- | --- | --- |
| 固定入力 | `wp03-eager-split-controller-v1/FROZEN-INPUTS.json` | `C2D436E938A86DD8DD397863EBB0B84DCCE97AEFFFD0DB49F8441114CAEFAD67` |
| controller | `wp03-eager-split-controller-v1/run.ps1` | `E1BF8F59CA9613FEFB7EDB9B3CFB777166434E928CF3DA77F296DA8B25B47F4E` |
| 全24件の集計 | `wp03-eager-split-aggregation-v1/summary.json` | `1EEAF4A079AEE8A127C51D925691CA72703DC0047FC7EB3412937640E0EC178F` |
| 独立raw照合 | `wp03-raw-independent-sem-20261108-v1/review.json` | `BB07C7449929A2E7961093DEC83A57DFA4369981EE1A88A375E34F22E4BC8CE0` |
| 独立解釈 | `wp03-raw-independent-sem-20261108-v1/conclusions.json` | `6028B3603CE265F4999E9C528534BF6323228D19F72C2BE4E04F2E145C9B3B6F` |
| 限定JIT診断 | `wp03-eager-split-jit-diagnostic-v1/CONCLUSIONS.json` | `81526A03B8AA20051AD29D9741C2C2EAB4090E278F55083F2EA6E66CB16F042F` |

生データは `wp03-eager-split-pairs-v1`、launch記録は `wp03-formal-launch-v1` にある。今回は候補の製品差分を採用branchへ統合せず、baselineと同じ既定・eager getterを残す。commit・通常push・CIは採用済み変更と最終資料をまとめる工程で扱い、mainへのmergeやreleaseは行わない。