# WP10報告の出典対応

## 結果と再現用データ

以下のパスは、別途保管する実験成果物 `zendiator-perf-artifacts-20261108` をルートとした相対パスです。リポジトリ内のリンクではなく、成果物を照合するための識別子として記載します。既存公式auditのreport/rawLogおよび各SHA256が、生結果と各セルの対応を示します。

- wp10-runtime-async-analysis-v2-r1/audit.json: 既存公式分析。passed=true、cases=126。各セルの生JSON・ログへの参照、統計値、割当、実loaded assembly tupleを収録。
- wp10-independent-numeric-review-v1/paired-ranks.csv: 全63ペアの平均時間・時間変化率・B/op・順位。本文表はlibrary=Zendiatorの4条件×3ラウンドを抽出。時間と変化率は小数第3位表示、B/opとrankは原値。
- wp10-independent-numeric-review-v1/adverse-pairs.csv: 全23悪化ペア。改善40・悪化23は全ライブラリ合計。
- wp10-independent-numeric-review-v1/cell-statistics.csv: 全126セルの反復数・平均・標本SD・中央値・CV・B/op。stock除外は63セル・72値、kept Nは9〜12。
- wp10-independent-numeric-review-v1/warning-log-details.csv: HのMinIterationTimeに対応する42ログ。重複除外後は21条件。buildの0警告ログ12件とは区別。
- wp10-independent-numeric-review-v1/segment-receipts.csv: 全12 segmentの完了記録。exitCode=0、cleanupVerified=true、ownedProcessesZero=true。segment01の継承を識別。
- wp10-independent-numeric-review-v1/verification.json: 独立数値再計算・照合の範囲と結果。不一致0。新たな測定による追試結果ではなく、既存生結果の数値再検算。

## コンパイル条件と実行環境

- ON source commit: c2a612d7dc92db6ec44771288988654eb7a93070。
- OFF source commit: 471d1784c4341508e4e8255082ebaf28f8d8b000。
- SDKとBenchmarkDotNet: wp10-runtime-async/formal-series-v1/01-pair1-off-usecase/runs/custom/results/0001-full.json のHostEnvironmentInfo。DotNetCliVersion=11.0.100-rc.3.26522.148、BenchmarkDotNetVersion=0.16.0-preview.2、RuntimeVersion=.NET 11.0.0 (11.0.0-rc.3.26522.148, 11.0.26.52348)。公式auditに記録されたreportSHA256は6F78E3B69DB54A85C667131431D9056F4A4EE3D6C6BB9FB17821EC8575576B2E。
- Runtimeとcontrols: wp10-runtime-async/formal-series-v2/12-pair3-on-h/05-HZendiatorBenchmarks/child-40944/setup-held.json のruntime.frameworkVersionとruntime.controls。frameworkVersion=11.0.0-rc.3.26522.148。DOTNET_TC_OnStackReplacement、COMPlus_TC_OnStackReplacement、DOTNET_TC_CallCountingDelayMs、COMPlus_TC_CallCountingDelayMsの4キーのみ明示0。TieredPGO/ReadyToRun等の他の列挙controlはnull/未設定。
- DI package指定: 10.0.12。実loaded assembly identityはaudit.cells[].actualBindingsで識別する。DI実装はVersion=10.0.0.0、DI.AbstractionsおよびCoreLibはVersion=11.0.0.0。package指定を全assemblyの同一版指定と解釈しない。

## 技術上の制約

対象は.NET 11 RC3の実験であり、.NET 10 shipping製品への統合効果ではない。既存NuGet ILのcompiler変換、最終統合ソースへの外挿、統計的保証を主張しない。

独立ラウンドはn=3。各セル内のkept N=9〜12とは別である。全悪化ペアとstock BenchmarkDotNetの除外を維持する。ABBAABの順序、長い停止間隔、Hの短い反復時間を解釈上の留保とする。

補強PE/PDB・generated-sourceの意味検証、新しいisolated A/B・bootstrap・ownedの追加補強、強化chronology検証は未実施。旧metadata-v3のPE/MVID/MethodDef/SourceLink等の確認はportable PDB document検証ではない。独立数値再検算もarchive/bundle全体のhash再監査を代替しない。

## 数値成果物のSHA256
- wp10-independent-numeric-review-v1/paired-ranks.csv : 23E39327541E7ED88AAB3BA4D830A9B2675FDAF3737116B477BB9865DA9E1E16
- wp10-independent-numeric-review-v1/adverse-pairs.csv : 30D48D884A43B75CD113BEE12A21BCD12722EAA57786FE5A1C66D53B2A84AE4A
- wp10-independent-numeric-review-v1/cell-statistics.csv : 70C67949CB01BFB0445B822E7EE99FA19BBCB53C337BA99443738D58EDDBDF19
- wp10-independent-numeric-review-v1/warning-log-details.csv : 9DA2D84C9154F609B467ABAD103CE73E010ED5EF41E5B573E6E2BFA9DCF1A87C
- wp10-independent-numeric-review-v1/segment-receipts.csv : 1D265ADCF4BB08E5E6494DC9B7ED2A627CBD1C4E85AE52048FE284240FB49DE2
- wp10-independent-numeric-review-v1/verification.json : A9F47F7E756DDD460054C2B54862173A710066EEE7F0983386C2787ACFEE389D
- wp10-runtime-async-analysis-v2-r1/audit.json : 6CD489DCD17577BFBCE4594911A3A93F8ECA1BF43B02F861B38D7708DF73FC7A
