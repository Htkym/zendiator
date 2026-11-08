# WP06: retained producer 実証の正式比較と不採用判断

## 結論

列挙中に producer の状態を保持する手書き実証は、今回の対象ケースでは採用しない。既存の生成 pooled 版を維持し、新しい製品 opt-in や generator 統合は導入しない。

既存の生成 pooled 版に対し、retained 実証は raw と Mediator の両系列で全 3 対とも遅く、allocation も各対で約 256 B/op 増えた。時間差の中央値は raw が +27.3160%、Mediator が +2.0824%。小さな改善でも採用する基準に照らしても、今回の比較で正味の利得は得られていない。競合順位の上昇につながる利点も、この実証からは示せない。

この判断は `Count=1024 / Asynchronous=True / Early=False` の共通 consumer を使った手書き実証に限る。retained 方式のあらゆる実装や用途を否定する結果ではない。元の Relay 比較、競合との順位、同期・早期終了ケースの性能保護、任意の C# を扱う生成 API の効果は未証明である。

## 実験の範囲

- 正式実行は 2026-11-08 06:18:06.992～06:25:19.626 UTC（15:18:06.992～15:25:19.626 JST）。6 methods × 3 rounds の 18 cells を完走した。
- SDK 10.0.403、.NET 10.0.14、DI 10.0.12、BenchmarkDotNet 0.15.8。Release、affinity 1、warmup 20、actual 12、target 500 ms、launch 1。
- 各 round で raw 3 methods、次に Mediator 3 methods を実行。round 1/3 は Ordinary → GeneratedPooled → RetainedProof、round 2 は逆順。各系列で順序を反転した 3 対として比較する。
- 1 operation は cursor 作成、1,024 項目の列挙、Current の読出し、DisposeAsync の完了まで。Provider/scope、request、未 cancel の API token の準備は計測外。
- Task.Yield、元の同期 prefix、項目ごとの 2 回の cancellation check、値、probe、once-only cleanup を保った共通処理を比較した。raw は生成 owner を通らず、Mediator は生成 StreamAsync と通常の owner/completion/cleanup を通る。
- retained は手書きの研究 fixture。既存 GeneratePooledProducer の生成機能ではなく、WP07 の completion 融合も含めない。

## 結果

以下の代表値は、各 round の kept samples の平均を求め、その 3 個の平均値の中央値を取ったもの。B/op も各 round の値の中央値であり、全 iteration を一つの独立標本としてまとめた値ではない。時間と allocation は小さいほどよい。

| Method | round 平均の中央値 (µs/op) | round 平均の範囲 (µs/op) | B/op の中央値 |
| --- | ---: | ---: | ---: |
| RawOrdinary | 80.7856 | 78.0375～80.8262 | 114896.5094 |
| RawGeneratedPooled | 56.8002 | 56.0809～58.5448 | 208.4355 |
| RawRetainedProof | 72.1556 | 71.5106～72.3158 | 464.3821 |
| MediatorOrdinary | 149.0424 | 143.9537～150.0003 | 115449.1408 |
| MediatorGeneratedPooled | 129.5408 | 122.7034～129.7062 | 760.7941 |
| MediatorRetainedProof | 131.9919 | 131.7571～132.4072 | 1017.0385 |

### 同じ round の generated pooled 版との差

差は retained − generated pooled、割合は `(retained / generated pooled − 1) × 100`。正の値は retained 側の悪化を表す。

| 系列 | Round | 時間差 (µs/op) | 時間差 (%) | Allocation 差 (B/op) | Allocation 差 (%) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Raw | 1 | +16.0747 | +28.6634 | +255.9167 | +122.7798 |
| Mediator | 1 | +9.2885 | +7.5698 | +256.2443 | +33.6812 |
| Raw | 2 | +15.5156 | +27.3160 | +255.9953 | +122.8147 |
| Mediator | 2 | +2.2163 | +1.7109 | +256.2312 | +33.6794 |
| Raw | 3 | +12.9658 | +22.1468 | +256.2048 | +123.0705 |
| Mediator | 3 | +2.7010 | +2.0824 | +255.8813 | +33.6174 |

raw の時間差は +22.1468～+28.6634%、Mediator は +1.7109～+7.5698%。allocation 差は raw が +255.9167～+256.2048 B/op、Mediator が +255.8813～+256.2443 B/op だった。Mediator の小さい時間差にはばらつきがあるため統計的有意性は主張しないが、全 3 対で時間の改善は観測されず、allocation 増加と交換できる利点も見つからない。

### Ordinary との比較

Ordinary に対する時間減少率は、retained が raw で 7.5373～11.4810%、Mediator で 8.3095～12.1621% であり、既存 generated pooled は raw で 27.5306～29.7255%、Mediator で 12.9736～14.7619% だった。retained の Ordinary 比の改善は、既存の生成 pooled 版を上回る追加効果ではないため、採用根拠にはしない。

Ordinary 比の allocation 差も各対で generated pooled が約 −114,688 B/op、retained が約 −114,432 B/op であり、既存 pooled の方が小さい。3 方式の全組合せ × raw/Mediator × 3 rounds の差は `paired-comparisons.csv` と `RESULT.json` に保持している。

### Mediator − raw の対内差

同じ round・同じ producer について、別々の launch で測った Mediator の end-to-end 値から raw の値を引いた差を示す記述量である。厳密な内部部品の費用や因果関係を分解した値ではない。retained の対内差が小さいことだけを見ても、Mediator 全体が既存 pooled より速いことにはならない。採否の根拠は、raw と Mediator それぞれの系列で比較した generated pooled 対 retained の正式結果である。

| Producer | Round | 時間差 (µs/op) | Allocation 差 (B/op) |
| --- | ---: | ---: | ---: |
| Ordinary | 1 | +65.9161 | +552.4343 |
| GeneratedPooled | 1 | +66.6225 | +552.3586 |
| RetainedProof | 1 | +59.8363 | +552.6863 |
| Ordinary | 2 | +69.1742 | +552.6314 |
| GeneratedPooled | 2 | +72.7406 | +552.3539 |
| RetainedProof | 2 | +59.4413 | +552.5898 |
| Ordinary | 3 | +68.2569 | +552.6249 |
| GeneratedPooled | 3 | +71.1614 | +552.9799 |
| RetainedProof | 3 | +60.8966 | +552.6564 |

## 全 18 cells と統計の扱い

kept `Workload/Result` の Nanoseconds / Operations から N、mean、標本標準偏差（分母 N−1）、CV、median を再計算した。CV は標本標準偏差 / mean × 100。BDN の OriginalValues、N、mean、標準偏差、median と照合済み。B/op は run.log の GC allocated bytes / GC operations から再計算し、BDN の丸め後表示とも一致した。B/op の分母は timing の kept iteration 数ではない。

全 cells の raw JSON に warmup 20 回・actual 12 回ずつを残している。actual は合計 216 回、BDN の外れ値処理後の kept は合計 197 回。各 cell の N は下表の通りで、失敗 cell の除外・自動再実行・追加の標本選別は行っていない。BDN が除いた actual も元 raw に保持し、診断・preflight の値を正式統計に混ぜていない。

| Round | Method | N | Mean (µs) | Sample SD (µs) | CV (%) | Median (µs) | B/op |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | RawOrdinary | 11 | 78.0375 | 0.7870 | 1.0085 | 77.6744 | 114896.2035 |
| 1 | RawGeneratedPooled | 11 | 56.0809 | 0.6422 | 1.1452 | 56.1854 | 208.4355 |
| 1 | RawRetainedProof | 11 | 72.1556 | 0.6239 | 0.8647 | 72.2132 | 464.3522 |
| 1 | MediatorOrdinary | 10 | 143.9537 | 0.8862 | 0.6156 | 143.8723 | 115448.6378 |
| 1 | MediatorGeneratedPooled | 10 | 122.7034 | 1.4074 | 1.1470 | 122.3438 | 760.7941 |
| 1 | MediatorRetainedProof | 10 | 131.9919 | 2.9162 | 2.2094 | 131.7742 | 1017.0385 |
| 2 | RawRetainedProof | 10 | 72.3158 | 1.2819 | 1.7726 | 72.2278 | 464.4355 |
| 2 | RawGeneratedPooled | 11 | 56.8002 | 0.4256 | 0.7492 | 56.9224 | 208.4402 |
| 2 | RawOrdinary | 11 | 80.8262 | 2.4264 | 3.0020 | 80.9804 | 114896.5094 |
| 2 | MediatorRetainedProof | 11 | 131.7571 | 2.2403 | 1.7004 | 130.6742 | 1017.0253 |
| 2 | MediatorGeneratedPooled | 12 | 129.5408 | 7.5188 | 5.8042 | 127.2539 | 760.7941 |
| 2 | MediatorOrdinary | 11 | 150.0003 | 6.2565 | 4.1710 | 149.5839 | 115449.1408 |
| 3 | RawOrdinary | 12 | 80.7856 | 4.4390 | 5.4948 | 78.7571 | 114896.5159 |
| 3 | RawGeneratedPooled | 12 | 58.5448 | 1.4311 | 2.4445 | 58.0371 | 208.1772 |
| 3 | RawRetainedProof | 11 | 71.5106 | 2.3109 | 3.2315 | 70.9324 | 464.3821 |
| 3 | MediatorOrdinary | 11 | 149.0424 | 5.5223 | 3.7052 | 150.3232 | 115449.1408 |
| 3 | MediatorGeneratedPooled | 11 | 129.7062 | 2.2786 | 1.7567 | 129.1484 | 761.1571 |
| 3 | MediatorRetainedProof | 11 | 132.4072 | 3.9447 | 2.9792 | 132.2181 | 1017.0385 |

`cells.csv` と `RESULT.json` には丸め前の統計、kept 値、各 raw report の path/hash を保持する。round 間の差や小さい差の確実性を過大評価せず、pooled iteration の信頼区間や競合順位をこの 18 cells から推定しない。

## 実行同一性と保存範囲

- 実際の timed child の setup/cleanup で、固定 input、Count/Asynchronous/Early、.NET 10.0.14、semantic gate、process identity、明示した 8 assemblies の tuple を照合した。setup と cleanup の 8 対象は同一。
- 対象は proof fixture、Zendiator、Zendiator.Abstractions、Microsoft.Extensions.DependencyInjection、同 Abstractions、BenchmarkDotNet、BenchmarkDotNet.Annotations、System.Private.CoreLib。全 transitive assemblies や native runtime map を固定したという意味ではない。
- 標準 BDN Generator/Executor を維持し、Builder 成功後に固定 7 DLL / 3 PDB を実 child の出力へ複写。source/destination の集合と hash を照合し、CoreLib は複写しなかった。
- 257 個の入力ファイルと 11 個の frozen copies を固定入力として照合した。全 18 cells の OWNED に exit0 / cleanupVerified が記録されており、controller の ownedProcessesZero=true と、各 launcher identity が実行中でないことを確認した。
- 各 cell の終了後、次の cell が同じ BDN output を再使用する前に、実 wrapper の .cs / .csproj / .notcs と所定の生成 DLL / PDB / JSON を case ごとに保存した。今回の保存は各 19 files、うち所定の .notcs はちょうど 1 件。全 build tree の複製ではない。
- copy 直前・直後の source と destination の hash 一致を記録。最終照合では保存した destination と記録済み sourceSHA256/destinationSHA256 を使い、後続 cell に上書きされた元の生成 .notcs / DLL / PDB の現在値とは比較しない。固定 7 DLL / 3 PDB の source/destination 照合は別に行う。

## Guard と版の経緯

旧 V1 preflight は、host と BDN が再buildした DLL の SHA 不一致で失敗した。V2 では標準 BDN の consumer build を保ったまま固定 reference の copy/hash guard を加え、非正式 1 case が成功した。V3 は製品ソースや host DLL を再buildせず、V2 に wrapper の .notcs 保存、所定ファイル 1 件の確認、source/destination hash と boundary/reparse 確認を追加した。

BDN の .notcs と生成出力は各 host で同じ path を再使用するため、全実行の終了後にまとめて保存する方式では前の cell の内容を失う。V3 の保存は各 cell の owned process 停止後、次の cell 開始前に行う。正式の 18 cells の実行順序、20 warmups / 12 actual、cell 300 秒・全体 40 分上限、失敗時停止と owned cleanup、既存 admission guard は維持した。

V3 の非正式 1 case は exit0 / owned0。wrapper の欠損・破損・余分な .notcs・copy 時の破損・境界逸脱の 6 negative と、analyzer の非有限値・不正 Operations・厳密な method/parameter/job schema・GC 分母・重複/欠落など 83 negative を確認した。V1/V2 の失敗・成功 preflight、旧 allocation 診断、各 freeze は変更せず保存している。

旧 allocation 診断と今回の正式 B/op は異なる計測条件の記録であり、両者を結合して効果を推定しない。[旧 WP06 記録](wp06.md)の値と当時の未検証事項は歴史的記録として残す。

## 証跡

ローカル証跡の基点: `D:\gitroot\zendiator-perf-artifacts-20261108`。以下はこの基点からの相対 path。

- `wp06-proof-pairs-v3`: 全 18 raw、run.log、OWNED、setup/cleanup/build receipt、各 actual-wrapper、controller.json、正式入力コピー。
- `wp06-proof-analysis-v3/RESULT.json`: 詳細統計、全 paired comparison、Mediator − raw、各証跡 path/hash、照合結果。
- `wp06-proof-analysis-v3/cells.csv`、`paired-comparisons.csv`、`mediator-minus-raw.csv`、`method-summary.csv`: 集計表。
- `wp06-proof-controller-v3/INPUTS.json`、`PREPARATION.json`、`FROZEN-COPIES.json`、`V2-to-V3.diff`、`PREFLIGHT-READBACK.json`、`WRAPPER-NEGATIVE.json`: 固定入力と保全方法。
- `wp06-proof-analysis-v3/analyze.py`、`validate_analyzer.py`、`VALIDATION.json`: 独立集計のコードと schema negative。
- `wp06-preflight-v3` および以前の V1/V2 関連 directory: 非正式診断。正式 18 cells から除外。

| 対象 | Revision / SHA256 |
| --- | --- |
| Source revision | `0ac177a80f641f0484a4c6a2a62ec18905fd72d7` |
| V3 controller | `9694BD9B12B6D8DFA2576EE44E79B1194CBF2EC7A96E5FBE55E38D25E30093DD` |
| V3 INPUTS.json | `8EA91D440300154C0A0D64A7C2E2176E9C876AF79ABE8C8BEEC5C872953E8122` |
| RESULT.json | `2BC7527F82ED0489074C892FADE2870AB41FE78CD3757637A69DC020CA4D7066` |
| analyze.py | `DB15A1C505F115FD8FD7D09F34B29F339581E0FE8FDF40AB10F33BD779B010DE` |
| validate_analyzer.py | `440781D8E27E4A3BD82476A8E66A1E5B461DED71BBBCE41E5DFA8FEC68772B48` |

## 独立レビューの状態

固定 analyzer の厳密な readback に加え、2026-11-08 06:51:45 UTC に別担当の raw 独立再計算が完了した。18 cells / 197 kept / 216 Actual / 360 Warmup、各 cell の 8 pins と 7 DLL / 3 PDB、18 個の保存 .notcs と 18 個の OWNED、合計 477 個の一意なファイルの SHA を照合し、公開 RESULT.json と一致した。独立レビューも、async 1,024 項目の full enumeration に限った手書き retained 実証の不採用判断を支持する。元の競合順位・Relay・生成 API 統合へは一般化しない。

レビュー原本は、上記の証跡基点にある `wp06-raw-independent-sem-20261108-v2/review.json`（SHA256: `90A6F5C483C52DAA21B5F20552DEAD38E29AEA378D94999ED3D40D61D5C294F2`）。v1 は reviewer 側が OWNED に重複した pid field を期待したため KeyError で停止した版であり、不変のまま保存した。実際の processIdentity schema に合わせた v2 を有効なレビューとして扱う。これは測定や製品の失敗ではなく、この訂正のための再測定・build は行っていない。
