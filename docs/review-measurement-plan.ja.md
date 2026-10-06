# レビュー修正後の重点測定計画（未実行）

基準harnessは `f6091677a3ecc8ad6f7b33d758e082ab41b03e56` の `benchmarks/` 全体とする。`run_interleaved.py` は比較キーごとにライブラリを隣接させ、先頭を交代する計画を作り、BDNを単一ケースずつ起動する。実ログ、BDN JSON、子証跡を計画と照合したroundだけを採用する。旧 `6a9a50b` の360件はライブラリ別ブロック実行の履歴であり、新しいroundやP0と結合しない。

## 先行確認

LithoSharpの重い作業がない枠で、harnessコミットのcleanなworktreeから `--group custom --matrix benchmarks/Zendiator.UseCaseBenchmarks/review-notification.json --max-cases 2` を新規出力先で実行する。locked restore、Release build、394件gate、BDN子2件、ログ順・JSON・親子DLL hash・再開を確認する。所要は約2～5分を見込む。異常なら全件やABラウンドに進まない。これは部分smokeであり比較結果として扱わない。4グループ360件の新たな交互順fullrunは別枠で、過去の約2時間に単一ケース起動の追加時間が乗るため、2～3時間以上を見込んで予約する。

## 通知の改修前後（4ケース）

改修前の製品・生成器基準は `6a9a50bb43660b07d2033bad45eedef2a9e786bc`、通知契約修正後は `95b042b597fb50297234d2512e8a73500ebc71b1` とする。測定用の2つの隔離clean worktreeはharness `f6091677...` から派生させ、改修前側には通知生成器の2ファイルだけを `6a9a50b` から復元して専用のローカル測定commitを作る。改修後側はharnessの通知生成器を使う。両側の `benchmarks/` tree SHA、matrix SHA、lockfile、SDK 10.0.401、.NET runtime、BDN 0.15.8、affinity 1、warmup 20、measurement 12、指定500 ms、launch 1、実行planを一致させる。派生commitのfull SHA、ソースmanifest、生成コードSHAと親子DLL SHAを保存し、差分が通知生成器の2ファイルだけであることを測定前に確認する。

`review-notification.json` は `ZendiatorNotification1.Dispatch`（同期成功）と `DispatchAsync`（非同期中断）、および同条件の `MediatRHistoricalNotification1` 2件を対照とする。改修前・後・後・前・前・後・後・前（ABBAを2回）で各4 round、計32子ケース。各roundに394件gate、4/4ケース、失敗0、証跡4件、版内の子DLL hash一種類を要求する。予定時間は子ケース約10～15分にbuild・gate・照合を足して20～30分程度。旧生成器は契約回帰テスト2件に失敗することを既に確認済みであり、測定用の既知の差として明記する。

同期成功の局所割当は `6a9a50b` と修正後でいずれも8,192回あたり0 B、旧async方式は72 B/回だった。これはDebugの局所確認で、BDNの速度結果ではない。通知の速度・割当はroundごとのmedian、N、標準偏差、割当量を対に比較する。改修後が対照の揺れを超えて5%以上遅い場合は原因を調べる。5%未満やround間で符号が混じる差は保留とする。正当性の復元と性能の判定は分ける。

## P0 FINAL / FINAL2（10ケース）

保存済みP0の `FINAL` を改修前、`FINAL2` を改修後として、それぞれの製品`src`スナップショットから隔離clean測定commitを作る。スナップショットの101ファイルと元のSHAを照合し、改行差を正規化した内容確認も残す。両側のbenchmark本体・matrix・lockfileは上記同一harnessに固定する。旧P0の255ケース用runnerや、DLLがあるとbuildを省く `Measure.ps1` は使わない。版をまたぐDLLの同一性は仮定せず、版内・round間で同じ子DLL hashを要求する。

`review-p0-ab.json` は Zendiator Send0/5 の ScopeK1/K10 4件、Zendiator Stream5 Full 1024項目の同期/非同期2件、Immediate Send0/5 ScopeK1 2件、Immediate Stream5 Full 1024項目の同期/非同期2件、計10件。上と同じABBA×2で各版4 round、計80子ケース。locked restore/Release、394件gate、実行plan・SDK・BDN条件と証跡確認を通知測定とそろえる。子ケース約25～30分にbuild・gate・照合を足して30～45分、枠は45～60分を見込む。

割当面は対象Zendiator Send 4件でFINAL2が各roundともFINALより8 B少ないことを再現条件とする。速度の改善またはStream退行を主張するには、対応4組の時間差が同方向、paired median差が5%以上で、Immediate対照の変動と対象のround間ばらつきより明確に大きいことを求める。満たさなければ保留する。FirstSendは現行の実iterationが4.54～45.88 msで短く、この10件には含めない。速度判断が必要ならinvocation設定を別設計し、新SHAで独立に測る。

いずれの計画も現時点では重い測定を起動していない。2ケースsmokeの実施時刻と、その後の重点ラウンドの順番は同じPCのLithoSharp作業と調整する。
