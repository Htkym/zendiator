# Zendiator 高速化改修の実施記録

2026-11-07 受領の改修計画に沿って実施する。計画の調査基準と現在の origin/main は、ともに `7c69f2a7408a68f36cf25abc3c6ac74ef00c5054` である。README の過去値はこの main の新規実測として扱わない。

## 実施順序

最初に WP00 の再現基盤を固定し、WP01・WP02・WP03 を一つずつ評価する。各案の採用条件を満たした変更だけを統合する。WP04 以降は初期評価と残存費用に基づいて判断する。

| 作業 | 対象 | 状態 |
| --- | --- | --- |
| WP00 | baseline・fixture・証跡の固定 | 初回 31 + Foundatio 7 + Short/H 3 = 41 ケースの baseline を完了し、各実測 child の証跡を保存。別枠の WP03 候補比較 Short/H 18 ケース・controls 36 ケースも完了。追加 HWarm490 は定義不一致で保留 |
| WP01 | 適格な Stream OpenAsync の直接呼び出し | 代表条件では棄却。warm Tier1 で対象 dispatch は除去済み |
| WP02 | cursor の稀な状態の分離 | 交互 3 ペア・60 ケースを完了。同期 B/op 削減は確認したものの、時間保護は未証明のため保留・非統合 |
| WP03 | closed 依存 getter の warm/cold 分離 | 完了・棄却。98 件の限定テスト、Send 保護 18 ペアと Short/H 9 ペアを完了。凍結 3% 基準で保護失敗 3 件となり非統合。詳細は [実験記録](../performance/experiments/wp03.md) |
| WP04 | generic struct upstream の任意契約 | feature/default 各 3 ペア・計 120 ケースと証跡監査を完了。約 200 B/op 削減は再現したが、時間保護未証明のため保留・非統合。強制棄却条件には未該当。詳細は [実験記録](../performance/experiments/wp04.md) |
| WP05 | 明示的な eager constructor injection | allocation 費用分解を完了。既存 LazyBlocks の 56 B/op 削減を確認。時間と新 eager mode は未判定 |
| WP06 | producer 状態の列挙期間中保持 | 手書き実証の semantic と allocation 診断を完了。生成 pooled 比の allocation 改善は確認できず、時間は未測定 |
| WP07 | producer と owner の completion 融合 | WP06 後も二重 completion が主要費用であると示す profile 証拠が未成立のため、条件未成立・非着手 |
| WP08 | SendAsync wrapper の軽量 completion 境界 | 元 SendAsync fixture は確認済み。生成コード・native code で軽量 completion 対象の残存費用が未特定のため、条件未成立・非着手 |
| WP09 | Hook failure の最終完了処理 | 元 fixture は回収済み。最終 throw と builder による例外完了が主要費用だと示す profile が未成立のため、条件付き研究は非着手 |
| WP10 | .NET 11 runtime async | P3 の条件付き研究として別系列で評価。現在は未実施で、.NET 10 の改修効果へ算入しない |

## 保持する契約

公開 API は生成 Mediator 経由のままにする。DI の登録 override、活性化・破棄の所有権、scope、lazy 取得、同時初回取得の CAS winner 共有、再入、null・失敗後の再試行、値型の box identity を維持する。

Stream の要素・順序、request 変更、Filter/Map、token 結合、列挙の独立性、early break、EOF、cleanup、例外、upstream の open 制御を維持する。ValueTask は一回だけ消費し、その消費と producer 側の完了処理がともに終了するまでは source を再利用しない。ExecutionContext、AsyncLocal、culture、SynchronizationContext の境界を維持する。

Task.Yield、token check、probe、cleanup を減らす変更は採用しない。新規のコードコメントと XML コメントは英語とする。main への直接 push、force push、merge、release はしない。

## 測定と判定

正式性能測定は同一ホストで一つずつ行う。baseline と候補は同一 fixture・SDK・runtime・依存・設定で交互に測り、まずは独立した 3 組を残す。許容差は baseline のばらつきから候補測定前に決める。

時間と B/op は別々に評価する。ばらつきを超える改善が再現しない案は改善未証明とする。競合首位を主張するときは、同一ホスト・runtime・workload で主要競合を再測定する。既定 API、typed terminal、pooled producer、その他の任意機能は別系列とする。

[baseline 記録](../performance/baseline-2026-11-07.md)に fixture 対応、環境、再現コマンド、未確認事項を残す。実験の結果は `docs/performance/experiments/` に記録する。

受領計画の全体完了範囲は、全 WP の実装・採用ではなく、L1〜L7・A1・A2 の改善結果または改善未証明の理由、既存契約の維持、opt-in と default の区別、commit／PR と再現可能な証跡をそろえることとする。採用案の baseline 比の効果と同条件の競合比較は区別し、未測定の仮説を実現済みの成果として扱わない。

## 現在の進捗（2026-11-09）

上記は2026-11-07時点の実施記録として保持する。以降の進捗は次のとおりであり、改修計画全体の完了を示すものではない。

- WP01・WP02・WP03・WP06・WP07の現候補は不採用で終了した。WP05のexplicit eager opt-in、WP08のrefasync、WP09のtyped asyncoutcomeは実装を採用した。WP04のopt-inは暫定状態で、正式なfinal210のphase1後に判断する。
- WP10は.NET 11 RCの126/126セルの実験、既存公式分析、独立数値照合、日本語校正を完了した。[実験結果](../performance/wp10-runtime-async-results.md)に利得・悪化・制約を併記した。.NET 10 shippingへの効果とは分けて扱い、新しいPE/PDB・generated-sourceの意味検証などの補強監査は未検証のままとする。
- 残る工程は、fresh A/B/S/EとStrict consumerの実API・protocol・stageの確認完了、master freezeとpreflight、final210、9代表ケースの結果・採否・再現証跡の最終更新である。final210はphase1の117セルでWP04を判断し、別途GOを得た後にphase2の93セルを進める。
- 現行shipping sourceのCI成功は、最終性能比較の完了を代替しない。原計画§15.2・§16.3に沿って、同条件でのbaseline比と競合比較を区別し、既存契約、opt-inとdefaultの区別、再現可能な証跡をそろえて全体の完了を判断する。
