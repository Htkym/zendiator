# WP10 RuntimeAsync 実験結果

## 結論

.NET 11 RC3で実施したRuntimeAsyncのON/OFF比較は126セルの測定と既存公式範囲の分析を完了し、独立数値レビューの不一致は0でした。ZendiatorではTypedとColdOuterFailureで時間短縮を観測しました。一方、Fullは3ラウンドとも遅くなり、1操作あたり約81.8 KBの割当増加を伴います。代表ケースの順位が下がらなかったことだけで、この負担を相殺したとは判断できません。

本結果は.NET 11 RCの実験結果です。.NET 10向けshipping製品への統合効果や、全条件に共通する利益、RuntimeAsyncの一律採用を示すものではありません。

## 目的と比較の境界

共通の.NET 11 RCランタイム上で、RuntimeAsyncのコンパイル条件を切り替えたときの時間・割当・同一条件内順位を比較します。新しくコンパイルしたZendiatorとadapter/fixtureの影響、および共通ランタイムでの実行を扱います。既存NuGet DLLのILがコンパイラーのフラグによって変換・再コンパイルされたとは主張しません。runtimeだけの効果とcompiler変換だけの効果を分離した因果推定でもありません。

| 項目 | 固定条件 |
|---|---|
| SDK | 11.0.100-rc.3.26522.148 |
| Runtime | 11.0.0-rc.3.26522.148 |
| Microsoft.Extensions.DependencyInjection package | 10.0.12 |
| BenchmarkDotNet | 0.16.0-preview.2（0.16-preview2） |
| ON commit | c2a612d7dc92db6ec44771288988654eb7a93070 |
| OFF commit | 471d1784c4341508e4e8255082ebaf28f8d8b000 |
| Controls | 下記4キーのみexplicit 0。他の列挙controlはnull/未設定 |

explicit 0はDOTNET_TC_OnStackReplacement、COMPlus_TC_OnStackReplacement、DOTNET_TC_CallCountingDelayMs、COMPlus_TC_CallCountingDelayMsの4キーです。TieredPGO/ReadyToRun等の他の列挙controlはnull/未設定であり、全controlsを0固定した実験ではありません。

package版と実行時assembly identityは別です。既存auditの実bindingにはDI assembly Version=10.0.0.0、DI.AbstractionsおよびSystem.Private.CoreLibのVersion=11.0.0.0が記録されています。上表のDI package版を、全依存assemblyの版が同一という意味では用いません。

比較元base 7c69f2aの結果を最終統合ソースへ外挿しません。.NET 10の測定値と本表を混ぜた順位も作りません。

## 測定設計と集計方法

126セルは21群×ON/OFF×独立3ラウンドです。UseCaseはZendiator、MediatRHistorical、Mediator、DispatchR、Immediateの5ライブラリ×3条件の15群です。TypedとScopeK1はLifetime=Scoped、FullはAsynchronous=TrueかつCount=1024です。Hは上記5ライブラリにFoundatioを加えたColdOuterFailureの6群です。FoundatioはUseCase順位に含みません。

各ペアは同じsection/workload/parameters/roundのOFFとONを対応させています。時間変化率は100×(ON平均/OFF平均−1)、割当差はON B/op−OFF B/opです。負の時間変化率は短縮を表します。順位は同じsection/workload/parameters/round/feature内の平均時間昇順であり、UseCaseとHの順位は別です。

全ライブラリ合計63ペアのうち、平均時間の改善は40、悪化は23でした。これはZendiatorだけの集計でも、統計的有意差を満たした件数でもありません。全23悪化ペアを保持し、都合のよいラウンドだけを抽出して集計したものではありません。

独立ラウンド数はn=3です。各セルのkept N=9〜12は、そのラウンド内に残った反復測定数であり、独立ラウンド数とは区別します。stock BenchmarkDotNetの除外を維持し、除外のある63セル・計72値を記録しています。平均・標本SD・中央値・CVの数値照合は独立レビューで実施済みですが、この標本数から統計的保証があるとは主張しません。

## Zendiatorの全12ペア

平均時間はns/op、小数第3位まで表示しています。時間変化率は丸め前のCSV値から計算した値の小数第3位表示です。B/opと順位の矢印はOFF→ONを表します。

| 条件 | ラウンド | OFF ns/op | ON ns/op | 時間変化 | B/op OFF→ON | B/op差 | 順位 OFF→ON |
|---|---:|---:|---:|---:|---:|---:|---:|
| Typed | 1 | 8.887 | 1.950 | -78.059% | 0→0 | 0 | 1→1 |
| Typed | 2 | 8.396 | 1.812 | -78.423% | 0→0 | 0 | 1→1 |
| Typed | 3 | 8.031 | 2.037 | -74.641% | 0→0 | 0 | 1→1 |
| ScopeK1 | 1 | 177.110 | 175.157 | -1.102% | 704→704 | 0 | 2→2 |
| ScopeK1 | 2 | 160.303 | 151.581 | -5.441% | 704→704 | 0 | 2→2 |
| ScopeK1 | 3 | 169.606 | 164.960 | -2.739% | 704→704 | 0 | 2→2 |
| Full | 1 | 156548.893 | 183432.465 | +17.173% | 115463→197236 | +81773 | 5→5 |
| Full | 2 | 156143.023 | 161206.540 | +3.243% | 115448→197216 | +81768 | 5→5 |
| Full | 3 | 147451.790 | 165158.149 | +12.008% | 115448→197216 | +81768 | 5→5 |
| ColdOuterFailure | 1 | 5125.706 | 3466.088 | -32.378% | 1704→1072 | -632 | 1→1 |
| ColdOuterFailure | 2 | 6312.625 | 3527.418 | -44.121% | 1704→1072 | -632 | 2→1 |
| ColdOuterFailure | 3 | 5690.484 | 3886.602 | -31.700% | 1704→1072 | -632 | 1→1 |

Typedは約74.6〜78.4%短縮し、割当0 B/opと1位を維持しました。ScopeK1は約1.1〜5.4%短縮し、704 B/opと2位を維持しました。Fullは約3.2〜17.2%悪化し、81,768〜81,773 B/op増加しました。5位の維持は、この悪化がないことを意味しません。ColdOuterFailureは約31.7〜44.1%短縮し、632 B/op減少しました。順位はラウンド2のみ2位から1位へ上がり、ほかは1位を維持しました。

順位の完全同値はありませんでした。ただし僅差の順位や丸め表示を有意差と解釈しません。小さな改善も検討対象ですが、旧来の3%閾値や数値上の順位上昇を全条件共通の必須条件にはしません。トレードオフは代表ケースの順位と時間・割当を併せて評価し、本結果だけで一律採用を決めません。

## 警告と実行履歴

warningLogs=54の内訳は「0個の警告」を含むbuildログ12件と、HのMinIterationTimeログ42件です。後者は重複を除くと21条件で、最短反復時間は54.019〜99.606 msと100 ms推奨値を下回りました。54件の独立した警告や失敗が起きたという意味ではありません。短い反復時間は精度上の留保として残します。

ラウンドの実行順はOFF→ON、ON→OFF、OFF→ONです。OFF=A、ON=BとするとABBAABで、各条件ではUseCase→Hの順です。実行には長い停止間隔があり、連続した短時間の交互実行としては扱いません。この順序だけで時間変化やマシン状態の交絡が除去されたとは主張しません。

全12 segmentの完了receiptはexit 0、cleanupVerified=true、ownedProcessesZero=trueです。segment01のみ旧formal-series-v1の成功UseCase15セルを継承し、再測定していません。segment02〜12はv2です。旧失敗H1は診断として除外し、正式結果には新規のfull H6を採用しました。完了receiptの照合と記録上のstartToken順の一致は、強化されたchronology監査の完了を意味しません。

## 確認済みの範囲と未検証事項

既存公式auditはpassed=true、cases=126です。独立数値レビューは全126セルのActual/Warmup/Result/OriginalValues、kept統計、GCのgen0/gen1/gen2/bytes/operations、B/op、63ペアの変化率と順位、12件の完了receiptを照合し、不一致0を記録しています。これは既存公式範囲のPASSと独立数値確認です。

次の項目を本結果で代替確認したり、完了扱いにしたりしません。

- 新しいPE/PDBおよびgenerated-sourceの意味検証。
- 新しいisolated A/B、bootstrap、ownedの追加補強。
- 強化されたchronology検証。
- archive/bundle全体の独立したhash再監査。

旧metadata-v3の検証範囲はPE/MVID/MethodDef/SourceLink等までであり、portable PDB document検証ではありません。新しい強化監査は未検証のままです。

## 計画上の位置付け

WP10の実験結果は、126セルの測定、既存公式分析、独立数値レビューの記録として完了できます。製品への採用判断は、Typed/Hの利得とFullの時間・約81.8 KB/opの割当悪化を併記して行います。

final210の117+93、.NET 10統合検証、WP04の採否は未完の別工程です。本報告の完成によってそれらや強化監査を完了とするものではありません。

## 数値根拠

- wp10-independent-numeric-review-v1/findings-ja.md と verification.json: 検証範囲、集計、制約。
- wp10-independent-numeric-review-v1/paired-ranks.csv: 全63ペアの平均時間、割当、順位。本文はZendiator全12ペアを掲載。
- wp10-independent-numeric-review-v1/adverse-pairs.csv: 全23悪化ペア。
- wp10-independent-numeric-review-v1/cell-statistics.csv: 126セルの反復数と統計値。
- wp10-independent-numeric-review-v1/warning-log-details.csv と segment-receipts.csv: 警告ログ42件の内訳と12件の完了記録。
- wp10-runtime-async-analysis-v2-r1/audit.json: 既存公式分析の結果。

参照元の識別用SHA256と本文への対応は[出典対応](wp10-runtime-async-source-map.md)に記載します。