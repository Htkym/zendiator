# Stream Hybrid の暫定採用と新基準の測定計画

## 採用状態

2026-10-07のユーザー指示により、Stream Hybrid の実装 `2561b98775cf679aba86df1388fb326717b42abb` を暫定採用した。性能不低下の合格には変更していない。

重点比較は主比較32 childと完全未登録12 child、計44 childが完了した。対象ケースに追加割当は観測されず、新しい Scope の入口は両roundとも760 Bから544 Bへ216 B減った。混在構成の未登録Fullは平均6.798 ns、約2.3%の増加候補であり、性能不低下は未確認として残す。この経路の生成コード、token処理、resolverは旧新で一致し、余分な検証処理は見つからなかった。親子runtime DLLの差も、同一のmanaged codeとbuild/debug識別情報の差に切り分けた。時間の原因は確定していない。

## 次の全体比較

暫定採用後の文書commitを含む一つの固定commitを、cleanな兄弟worktreeへ展開する。競合比較のharness・matrix・lockfile・SDK・runtimeを全区間で固定し、完了した新しい360ケースを次の改善の基準にする。部分結果を完成した基準とは呼ばない。FINAL、FINAL2、P0、過去の競合fullrun、今回の44 childは履歴として別に残し、新基準のCSVには混ぜない。

現行 `run_interleaved.py --group all --dry-run` で次の構成を確認した。比較キーごとにライブラリを隣接させ、先頭のライブラリを交代する既存の実行計画を維持する。

| group | child数 | 比較キー数 | 区間ごとのchild数 |
|---|---:|---:|---|
| send | 125 | 25 | 50 / 50 / 25 |
| features | 55 | 13 | 47 / 8 |
| streams | 120 | 24 | 50 / 50 / 20 |
| relay | 60 | 12 | 50 / 10 |

現行360ケースにValidator登録ケースはない。まずこの競合matrixを維持して新基準を取得する。Hybridの登録時性能は、同期入口という条件をそろえた専用の補足比較として扱い、対応するAPIを持たないライブラリの未測定値を作らない。今回の44 childを新基準の測定済みケースとして再利用しない。登録時の追加測定が必要になった場合は、別matrixと補足表を使う。

## 一回の時間と途中再開

固定条件はlocked restore、Release、SDK 10.0.401、.NET 10.0.12、BDN 0.15.8、affinity 1、warmup 20、measurement 12、指定500 ms、launch 1である。FirstSendの16384 invocationsとunroll 1も維持する。各groupの394-case gateと、保存consumerの正当性・binding・版内DLL同一性確認を省略しない。

今回の重点比較のwall時間は約35～39秒/childだった。全体のケースは異なるため目安に限るが、360 childは約3.5～4.5時間に準備・gate・照合を加える見込みとなる。以前のブロック実行の約2時間を、そのまま隣接実行の所要時間には使わない。一度に全groupを連続実行せず、上表の区間に分け、実際の所要時間に応じて同じ区間を途中再開する。

`--max-cases` は新規実行数ではなく累積完了数である。全体sessionでは `50, 100, 125, 172, 180, 230, 280, 300, 350, 360` を順に指定し、2回目以降は同じworktree・出力・固定commitで `--resume` を付ける。区間は比較キーの境目にそろえる。閾値に届かず時間で区切った場合は、次の枠でも同じ閾値を指定する。完了済みケースは既存の証跡確認を通ったものだけを再利用する。

既存driverに経過時間の上限はないため、件数指定だけで一時間以内と保証しない。測定開始前に、今回使った所有commandの180秒上限を再利用し、全体の55分上限と新しい比較キーを開始する前の残時間確認を付ける。一時間には準備・gate・cleanupも含める。timeoutしたケースは有効値に数えず、失敗証跡と所有プロセスの終了確認を残して再開する。他sessionを止めて枠を空けない。測定条件や精度を縮める方法は使わない。

## 環境と集計

Windowsでは同じCPU、dotnet host、SDK、runtime、GC、電源設定を固定する。各区間の時刻とCPU/D:負荷を記録し、区間の間に環境やDLLが変わった場合は同じ基準として集計しない。launch 1、raw N、有効N、median、標準偏差、割当量と区間の中断を表に残す。一launchの平均だけで小差や全体最速を確定しない。

Colabは保存データの集計や静的なコード比較に活用できる。Linux/Colabで別途実測する場合は、CPU・OS・runtime条件が異なる独立した基準として記録し、Windowsの継続データには混ぜない。

現在は計画と文書だけを更新している。LithoレビューとCLI調査の並行状況を親が確認し、測定枠と時間上限の準備を調整してから開始する。

## 以前の重点測定計画

以下は2026-10-06時点の計画を保全した記録であり、新基準の実行指示ではない。


基準harnessは `0412efa9453aeccba8d46d55aefd2355ff448e24` の `benchmarks/` 全体とする。`run_interleaved.py` は比較キーごとにライブラリを隣接させ、先頭を交代する計画を作り、BDNを単一ケースずつ起動する。実ログ、BDN JSON、子証跡を計画と照合したroundだけを採用する。旧 `6a9a50b` の360件はライブラリ別ブロック実行の履歴であり、新しいroundやP0と結合しない。

## 先行確認

2026-10-06に `cde3cdea977e54473b2a3ef95f32eae93b6f5616` の隔離clean worktreeで2ケースsmokeを実行した。SDK 10.0.401、.NET 10.0.12、locked restore、Release build、394/394 gateの後、`ZendiatorNotification1.Dispatch → MediatRHistoricalNotification1.Dispatch` の順でBDN子2件が完了した。各ケースはログheader・BDN JSON・子証跡が1件、子Zendiator DLL hashは同じで、owned processは0件だった。部分smokeのためgroup最終outcomeは書いていない。出力は `D:\gitroot\zendiator-driver-smoke-cde3cde-20261006\.local\benchmarks\interleaved-smoke-cde3cde-20261006-02` に保存した。

主checkoutでの初回試行は `.local` 内に同名csprojが複数あり、BDNのproject探索で失敗した。失敗出力を保全し、以降は兄弟位置の隔離worktreeを使う。基準harness `0412efa9...` ではbuild前の同名project1件チェックを追加し、主checkoutでは拒否、隔離worktreeでは通ることを軽い検証で確認した。ABラウンドと新たな360件fullrunは未実行。fullrunは過去の約2時間に単一ケース起動の追加時間が乗るため、2～3時間以上を別枠で予約する。

## 通知の改修前後（4ケース）

改修前の製品・生成器基準は `6a9a50bb43660b07d2033bad45eedef2a9e786bc`、通知契約修正後は `95b042b597fb50297234d2512e8a73500ebc71b1` とする。測定用の2つの隔離clean worktreeはharness `0412efa9...` から派生させ、改修前側には通知生成器の2ファイルだけを `6a9a50b` から復元して専用のローカル測定commitを作る。改修後側はharnessの通知生成器を使う。両側の `benchmarks/` tree SHA、matrix SHA、lockfile、SDK 10.0.401、.NET runtime、BDN 0.15.8、affinity 1、warmup 20、measurement 12、指定500 ms、launch 1、実行planを一致させる。派生commitのfull SHA、ソースmanifest、生成コードSHAと親子DLL SHAを保存し、差分が通知生成器の2ファイルだけであることを測定前に確認する。

`review-notification.json` は `ZendiatorNotification1.Dispatch`（同期成功）と `DispatchAsync`（非同期中断）、および同条件の `MediatRHistoricalNotification1` 2件を対照とする。改修前・後・後・前・前・後・後・前（ABBAを2回）で各4 round、計32子ケース。各roundに394件gate、4/4ケース、失敗0、証跡4件、版内の子DLL hash一種類を要求する。予定時間は子ケース約10～15分にbuild・gate・照合を足して20～30分程度。旧生成器は契約回帰テスト2件に失敗することを既に確認済みであり、測定用の既知の差として明記する。

同期成功の局所割当は `6a9a50b` と修正後でいずれも8,192回あたり0 B、旧async方式は72 B/回だった。これはDebugの局所確認で、BDNの速度結果ではない。通知の速度・割当はroundごとのmedian、N、標準偏差、割当量を対に比較する。改修後が対照の揺れを超えて5%以上遅い場合は原因を調べる。5%未満やround間で符号が混じる差は保留とする。正当性の復元と性能の判定は分ける。

## P0 FINAL / FINAL2（10ケース）

保存済みP0の `FINAL` を改修前、`FINAL2` を改修後として、それぞれの製品`src`スナップショットから隔離clean測定commitを作る。スナップショットの101ファイルと元のSHAを照合し、改行差を正規化した内容確認も残す。両側のbenchmark本体・matrix・lockfileは上記同一harnessに固定する。旧P0の255ケース用runnerや、DLLがあるとbuildを省く `Measure.ps1` は使わない。版をまたぐDLLの同一性は仮定せず、版内・round間で同じ子DLL hashを要求する。

`review-p0-ab.json` は Zendiator Send0/5 の ScopeK1/K10 4件、Zendiator Stream5 Full 1024項目の同期/非同期2件、Immediate Send0/5 ScopeK1 2件、Immediate Stream5 Full 1024項目の同期/非同期2件、計10件。上と同じABBA×2で各版4 round、計80子ケース。locked restore/Release、394件gate、実行plan・SDK・BDN条件と証跡確認を通知測定とそろえる。子ケース約25～30分にbuild・gate・照合を足して30～45分、枠は45～60分を見込む。

割当面は対象Zendiator Send 4件でFINAL2が各roundともFINALより8 B少ないことを再現条件とする。速度の改善またはStream退行を主張するには、対応4組の時間差が同方向、paired median差が5%以上で、Immediate対照の変動と対象のround間ばらつきより明確に大きいことを求める。満たさなければ保留する。FirstSendは現行の実iterationが4.54～45.88 msで短く、この10件には含めない。速度判断が必要ならinvocation設定を別設計し、新SHAで独立に測る。

いずれの計画も現時点では重い測定を起動していない。2ケースsmokeの実施時刻と、その後の重点ラウンドの順番は同じPCのLithoSharp作業と調整する。
