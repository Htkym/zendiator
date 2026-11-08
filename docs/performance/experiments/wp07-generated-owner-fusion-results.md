# WP07 生成owner融合の測定結果と採否

## 結論

今回の候補はmain製品へ取り込みません。実装commit `00f16bff3ab17afb640e897866b471e87f45a402` と測定証拠は保全し、feature source、Work marker、selectorはmainへ取り込みません。

対象のStream0非同期全件列挙では、FusedはBclPooledに対して16項目で全3対14～18%、1024項目で全3対25～33%短縮しました。一方、共有cursorを使うStream5同期1024項目は全3対7～47%悪化しました。Stream0 typed非同期1024項目も全3対0.62～1.89%悪化し、約23～24 B/op増えています。対象の非同期全件列挙には約224 B/opの固定費もあります。これらの費用に対して、総合的に上向く利益は十分に立証されていません。

小さな改善を拒否する判断ではありません。旧3%閾値は使わず、数値上の順位上昇も一律の採用条件にはしません。共有経路とtyped経路への費用を局在化した別候補なら再検討できます。今回のsourceを採用するまで測定を繰り返す方針にはしません。

## 測定対象と有効な証拠

実験は明示的なexperimental opt-inです。Defaultは非計時controlで、既定生成コードのbytesは不変です。元のStream0、Stream5、Relay5のhandler、registration、consumerを維持し、共有WorkStreamCursorのpending helperを3variantで比較しました。SameProducerのWork.Streamは別系列です。初回moveとtyped terminalの融合アルゴリズムは変更していませんが、共有cursorのfield配置や周辺費用は非選択経路にも及びます。

A=BclPooled、B=MarkerOnly、C=Fusedとし、12条件×3variant×3roundの108cellをABC、CBA、ABC順で実行しました。main54、shared36、typed18です。3contrastはBclPooled→MarkerOnly、MarkerOnly→Fused、BclPooled→Fusedです。36条件比較組に各3対があり、計108対です。同じ観測を共有するcontrastは独立した追加標本ではありません。

正式v3は2026-11-08 18:56:07～19:46:50 UTCの約50分43秒で108/108完了し、exit0、144owned cleanup receiptと外側owned0を確認しました。旧v2は11/108で中断しており、不成立として別途保全しました。旧11件、非正式preflight、正式v3を混合していません。中断原因は未確定です。

SDK 10.0.403、runtime 10.0.14、DI 10.0.12、Release、affinity1、Warmup20、Actual12、要求iteration500ms、launch1、runtime controlsは未設定（記録値null）で、setup/final一致を確認しました。標準BDN Generator/Executorとfixed-copy Builderを使用し、CoreLibを含む8個の名前付きassemblyを実子のsetup/finalで照合しました。copy対象はCoreLibを除く7DLL＋3PDBです。全transitive/native assemblyの固定を主張しません。

copy時sourceとdestinationのSHAをreceiptへ結合し、後刻は歴史的receiptとretained bundleを照合しました。上書きされ得る元BDN出力を後から再hashしていません。各実子の8pins、保存389gate、同一consumer bytesに結合したMode49gate、wrapperの.notcs exact1を確認しました。独立レビューは108raw、108contrast、9有限差分傾き、144ownedと必要証拠について89,456照合を完了し、公式集計と一致しました。

## 代表的なtradeoff

Stream0同期16項目にはFused/BclPooledが+43.94%の不利な対もあります。他2対は-0.42%、+1.87%です。Stream0 early非同期の対は+2.92%、-0.91%、+5.19%で、初回だけでは融合しません。多くの非融合経路でも約8 B/op増えました。すべての不利な対を以下に残しています。

Stream5 typed同期round3にはBclPooled 19.330 µs、MarkerOnly 12.518 µs、Fused 12.455 µsという別plateauが含まれます。この比を融合による因果的な大幅利得とは断定しません。Stream5同期全件列挙round3のFused 17.540 µsという安定した不利plateauも除外していません。

旧別時期peerの49.9～78.0 µsと、今回Fused非同期1024項目の86.7～93.4 µsは距離を理解するための文脈に限ります。同時・同条件のpeer比較ではないため、新しい順位も、順位改善が不可能という証明も導けません。Defaultは非計時であり、今回のBclPooled比較をDefault比として扱いません。

## 全条件の絶対値

各欄はround1 / round2 / round3です。meanはµs、B/opはraw GC分母から算出した実値です。表示を丸めており、正確な値はRESULT.jsonとraw JSONに保持しています。

| 条件 | variant | mean µs | B/op |
|---|---|---:|---:|
| ZendiatorStream0.Full 16 async | BclPooled | 3.5009 / 3.4662 / 3.4703 | 768.0113 / 768.0112 / 768.0288 |
| ZendiatorStream0.Full 16 async | MarkerOnly | 3.6806 / 3.6745 / 3.5311 | 768.0295 / 768.0306 / 768.0299 |
| ZendiatorStream0.Full 16 async | Fused | 2.8677 / 2.9807 / 2.9326 | 992.0090 / 992.0216 / 992.0091 |
| ZendiatorStream0.Full 1024 async | BclPooled | 122.3986 / 124.1294 / 130.0112 | 769.3421 / 769.3421 / 769.3784 |
| ZendiatorStream0.Full 1024 async | MarkerOnly | 123.7981 / 125.3818 / 127.2131 | 769.3421 / 769.0099 / 769.0303 |
| ZendiatorStream0.Full 1024 async | Fused | 86.6657 / 93.3962 / 87.0457 | 992.7083 / 992.7445 / 992.5075 |
| ZendiatorStream0.Full 16 sync | BclPooled | 0.2287 / 0.2282 / 0.2208 | 160.0007 / 160.0000 / 160.0007 |
| ZendiatorStream0.Full 16 sync | MarkerOnly | 0.2427 / 0.2270 / 0.2288 | 160.0007 / 160.0007 / 160.0008 |
| ZendiatorStream0.Full 16 sync | Fused | 0.3291 / 0.2273 / 0.2249 | 168.0010 / 168.0008 / 168.0000 |
| ZendiatorStream0.Full 1024 sync | BclPooled | 11.0260 / 10.0280 / 10.7498 | 160.0662 / 160.0000 / 160.0656 |
| ZendiatorStream0.Full 1024 sync | MarkerOnly | 10.2803 / 10.0630 / 10.6798 | 160.0678 / 160.0676 / 160.0667 |
| ZendiatorStream0.Full 1024 sync | Fused | 10.4320 / 9.9076 / 10.2384 | 168.0703 / 168.0651 / 168.0653 |
| ZendiatorStream0.EarlyBreak 16 async | BclPooled | 1.4785 / 1.4303 / 1.3885 | 488.0056 / 488.0047 / 488.0046 |
| ZendiatorStream0.EarlyBreak 16 async | MarkerOnly | 1.3812 / 1.4581 / 1.5323 | 488.0052 / 488.0046 / 488.0082 |
| ZendiatorStream0.EarlyBreak 16 async | Fused | 1.5217 / 1.4173 / 1.4605 | 496.0050 / 496.0050 / 496.0050 |
| ZendiatorStream0.EarlyBreak 16 sync | BclPooled | 0.0582 / 0.0586 / 0.0591 | 160.0002 / 160.0002 / 160.0000 |
| ZendiatorStream0.EarlyBreak 16 sync | MarkerOnly | 0.0590 / 0.0589 / 0.0574 | 160.0002 / 160.0002 / 160.0002 |
| ZendiatorStream0.EarlyBreak 16 sync | Fused | 0.0580 / 0.0581 / 0.0586 | 168.0002 / 168.0002 / 168.0002 |
| ZendiatorStream5.Full 1024 async | BclPooled | 129.1312 / 124.3571 / 125.5705 | 1873.4366 / 1873.1724 / 1873.4366 |
| ZendiatorStream5.Full 1024 async | MarkerOnly | 129.3383 / 123.9725 / 126.7117 | 1873.4571 / 1873.4571 / 1873.4571 |
| ZendiatorStream5.Full 1024 async | Fused | 126.8313 / 126.7717 / 124.9539 | 1881.4571 / 1881.4783 / 1881.5000 |
| ZendiatorStream5.Full 1024 sync | BclPooled | 11.3071 / 11.2501 / 11.9597 | 1216.2297 / 1216.2203 / 1216.0000 |
| ZendiatorStream5.Full 1024 sync | MarkerOnly | 11.5857 / 11.1943 / 11.2743 | 1216.3036 / 1216.0770 / 1216.1648 |
| ZendiatorStream5.Full 1024 sync | Fused | 12.1514 / 12.6818 / 17.5399 | 1224.1141 / 1224.1102 / 1224.0000 |
| ZendiatorStream5.EarlyBreak 16 sync | BclPooled | 0.3771 / 0.3866 / 0.3921 | 1216.0012 / 1216.0012 / 1216.0013 |
| ZendiatorStream5.EarlyBreak 16 sync | MarkerOnly | 0.3786 / 0.3792 / 0.3806 | 1216.0012 / 1216.0015 / 1216.0080 |
| ZendiatorStream5.EarlyBreak 16 sync | Fused | 0.3906 / 0.3786 / 0.3807 | 1224.0013 / 1224.0012 / 1224.0012 |
| ZendiatorRelay5.Full 1024 async | BclPooled | 125.1673 / 124.2634 / 126.6323 | 1873.1724 / 1873.1724 / 1873.1860 |
| ZendiatorRelay5.Full 1024 async | MarkerOnly | 126.6406 / 123.1167 / 129.5383 | 1872.4113 / 1872.4048 / 1873.4857 |
| ZendiatorRelay5.Full 1024 async | Fused | 123.9306 / 125.3114 / 125.6919 | 1881.4571 / 1881.4783 / 1881.4783 |
| ZendiatorStream0.FullTyped 1024 async | BclPooled | 114.5683 / 113.6227 / 116.1912 | 873.4366 / 873.3784 / 873.3973 |
| ZendiatorStream0.FullTyped 1024 async | MarkerOnly | 120.3473 / 117.3728 / 117.3430 | 872.9714 / 873.3784 / 872.9623 |
| ZendiatorStream0.FullTyped 1024 async | Fused | 116.7357 / 115.3466 / 116.9109 | 896.4031 / 897.3784 / 897.3973 |
| ZendiatorStream5.FullTyped 1024 sync | BclPooled | 12.9626 / 13.4388 / 19.3300 | 1176.2742 / 1176.1092 / 1176.0000 |
| ZendiatorStream5.FullTyped 1024 sync | MarkerOnly | 12.6267 / 12.9408 / 12.5182 | 1176.0405 / 1176.1493 / 1176.2873 |
| ZendiatorStream5.FullTyped 1024 sync | Fused | 13.0918 / 13.1484 / 12.4551 | 1184.2227 / 1184.1466 / 1184.1527 |

## 全3contrastと全不利対

時間差はcandidate/baseline−1の百分率です。正値は悪化、負値は短縮を表します。各欄はround1 / round2 / round3です。

| 条件 | baseline→candidate | 時間差 % |
|---|---|---:|
| ZendiatorStream0.Full 16 async | BclPooled→MarkerOnly | +5.1337 / +6.0087 / +1.7509 |
| ZendiatorStream0.Full 16 async | MarkerOnly→Fused | -22.0865 / -18.8822 / -16.9472 |
| ZendiatorStream0.Full 16 async | BclPooled→Fused | -18.0867 / -14.0081 / -15.4930 |
| ZendiatorStream0.Full 1024 async | BclPooled→MarkerOnly | +1.1434 / +1.0090 / -2.1522 |
| ZendiatorStream0.Full 1024 async | MarkerOnly→Fused | -29.9943 / -25.5105 / -31.5749 |
| ZendiatorStream0.Full 1024 async | BclPooled→Fused | -29.1939 / -24.7590 / -33.0475 |
| ZendiatorStream0.Full 16 sync | BclPooled→MarkerOnly | +6.1277 / -0.5575 / +3.6065 |
| ZendiatorStream0.Full 16 sync | MarkerOnly→Fused | +35.6292 / +0.1388 / -1.6794 |
| ZendiatorStream0.Full 16 sync | BclPooled→Fused | +43.9402 / -0.4195 / +1.8666 |
| ZendiatorStream0.Full 1024 sync | BclPooled→MarkerOnly | -6.7631 / +0.3491 / -0.6516 |
| ZendiatorStream0.Full 1024 sync | MarkerOnly→Fused | +1.4754 / -1.5438 / -4.1328 |
| ZendiatorStream0.Full 1024 sync | BclPooled→Fused | -5.3874 / -1.2001 / -4.7575 |
| ZendiatorStream0.EarlyBreak 16 async | BclPooled→MarkerOnly | -6.5779 / +1.9487 / +10.3617 |
| ZendiatorStream0.EarlyBreak 16 async | MarkerOnly→Fused | +10.1700 / -2.8010 / -4.6896 |
| ZendiatorStream0.EarlyBreak 16 async | BclPooled→Fused | +2.9231 / -0.9068 / +5.1862 |
| ZendiatorStream0.EarlyBreak 16 sync | BclPooled→MarkerOnly | +1.4314 / +0.5813 / -2.7554 |
| ZendiatorStream0.EarlyBreak 16 sync | MarkerOnly→Fused | -1.6034 / -1.3717 / +2.0541 |
| ZendiatorStream0.EarlyBreak 16 sync | BclPooled→Fused | -0.1950 / -0.7984 / -0.7579 |
| ZendiatorStream5.Full 1024 async | BclPooled→MarkerOnly | +0.1604 / -0.3093 / +0.9088 |
| ZendiatorStream5.Full 1024 async | MarkerOnly→Fused | -1.9384 / +2.2579 / -1.3872 |
| ZendiatorStream5.Full 1024 async | BclPooled→Fused | -1.7811 / +1.9417 / -0.4910 |
| ZendiatorStream5.Full 1024 sync | BclPooled→MarkerOnly | +2.4644 / -0.4959 / -5.7308 |
| ZendiatorStream5.Full 1024 sync | MarkerOnly→Fused | +4.8828 / +13.2884 / +55.5748 |
| ZendiatorStream5.Full 1024 sync | BclPooled→Fused | +7.4675 / +12.7266 / +46.6590 |
| ZendiatorStream5.EarlyBreak 16 sync | BclPooled→MarkerOnly | +0.3890 / -1.8994 / -2.9230 |
| ZendiatorStream5.EarlyBreak 16 sync | MarkerOnly→Fused | +3.1699 / -0.1556 / +0.0238 |
| ZendiatorStream5.EarlyBreak 16 sync | BclPooled→Fused | +3.5713 / -2.0520 / -2.8998 |
| ZendiatorRelay5.Full 1024 async | BclPooled→MarkerOnly | +1.1771 / -0.9228 / +2.2948 |
| ZendiatorRelay5.Full 1024 async | MarkerOnly→Fused | -2.1399 / +1.7826 / -2.9693 |
| ZendiatorRelay5.Full 1024 async | BclPooled→Fused | -0.9880 / +0.8434 / -0.7427 |
| ZendiatorStream0.FullTyped 1024 async | BclPooled→MarkerOnly | +5.0441 / +3.3005 / +0.9913 |
| ZendiatorStream0.FullTyped 1024 async | MarkerOnly→Fused | -3.0009 / -1.7263 / -0.3683 |
| ZendiatorStream0.FullTyped 1024 async | BclPooled→Fused | +1.8918 / +1.5172 / +0.6194 |
| ZendiatorStream5.FullTyped 1024 sync | BclPooled→MarkerOnly | -2.5914 / -3.7061 / -35.2393 |
| ZendiatorStream5.FullTyped 1024 sync | MarkerOnly→Fused | +3.6833 / +1.6042 / -0.5042 |
| ZendiatorStream5.FullTyped 1024 sync | BclPooled→Fused | +0.9965 / -2.1614 / -35.5658 |

## 割当の有限差分

Stream0.Full asyncの16→1024項目について、(B/op1024−B/op16)/1008を算出しました。9本とも小さな正値で、Fusedの約224 B/op増は主に固定費として観測されています。この2点だけで普遍的なゼロ項目比例割当を証明したわけではありません。

| round | variant | 16項目 B/op | 1024項目 B/op | 追加1項目あたり B |
|---|---|---:|---:|---:|
| 1 | BclPooled | 768.011312 | 769.342105 | 0.001320231 |
| 1 | MarkerOnly | 768.029463 | 769.342105 | 0.001302225 |
| 1 | Fused | 992.009003 | 992.708333 | 0.000693780 |
| 2 | BclPooled | 768.011227 | 769.342105 | 0.001320315 |
| 2 | MarkerOnly | 768.030557 | 769.009901 | 0.000971571 |
| 2 | Fused | 992.021574 | 992.744526 | 0.000717214 |
| 3 | BclPooled | 768.028805 | 769.378378 | 0.001338862 |
| 3 | MarkerOnly | 768.029859 | 769.030303 | 0.000992504 |
| 3 | Fused | 992.009146 | 992.507463 | 0.000494361 |

## 統計上の制約と既存proofとの区別

各cellのActual12は保持し、BDNのResult選択後のNは12が56cell、11が36cell、10が14cell、9が2cellです。除外されたActual sampleは計70、採用されたsampleは1226、最大CVは11.61%です。最終判断の対応対は条件ごとに3対だけであり、広い母集団や全機種への一般化はできません。

MinIterationTime警告は7cellです。実行番号22、23、34、36、60、98、107で、観測最短iterationはそれぞれ78.5、61.837、76.16、90.786、82.058、76.762、69.523 msでした。要求500msを固定しても実観測が常に500msになるわけではありません。警告と不利な対を除外していません。

手書きretained→fused proofの-144 Bは、その別実装間の結果です。isolated generated proofの約+224 B、今回の代表108の対象async約+224 Bとは比較対象が異なります。手書きproofの割当減を製品生成経路の割当減として転用しません。

今回の結論は本候補の採否です。未実施のfinal combination、全体テスト、API/package検証を完了扱いにはしません。新peer比較も未実施です。

## 証拠

artifactの基点は `D:\gitroot\zendiator-perf-artifacts-20261108` です。

- 公式結果: `wp07-representative-controller-v3/RESULT.json`、SHA256 `196C2AC0CB9E8CF3136D36B7D2469F754970FE92E653FCB4C33764EF65933451`。
- 正式controller: `wp07-representative-pairs-v3/controller.json`、SHA256 `D34C56F859F9C386F0E4DD31D16FA983675A4C0908835FA1AD332A4F844FC831`。
- 完了receipt: `wp07-representative-formal-supervisor-v3/supervisor.json`、SHA256 `54CD8A264AA4AC2552683333E692154D94C72BCD1B8A259FD2E728A9425AC0DD`。
- 独立照合: `wp07-representative-independent-sem-20261108-v3/MANIFEST.json`、SHA256 `DC471237CAAE6CB890E8D66C6CBD2640CF19373141C8A78ABC416988BBF8A196`。
- 独立結論: `wp07-representative-independent-sem-20261108-v3/conclusions.json`、SHA256 `EE21C5F9536F81EE17120A3291D10A7817C42F84D06149E5DEA02C97A8E0D8BB`。
