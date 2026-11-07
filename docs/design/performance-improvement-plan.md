# Zendiator 高速化改修の実施記録

2026-11-07 受領の改修計画に沿って実施する。計画の調査基準と現在の origin/main は、ともに `7c69f2a7408a68f36cf25abc3c6ac74ef00c5054`。README の過去値はこの main の新規実測として扱わない。

## 実施順序

最初に WP00 の再現基盤を固定し、WP01・WP02・WP03 を一つずつ評価する。各案の採用条件を満たした変更だけを統合する。WP04 以降は初期評価と残存費用に基づいて判断する。

| 作業 | 対象 | 状態 |
| --- | --- | --- |
| WP00 | baseline・fixture・証跡の固定 | 準備中。元 ShortCircuit / Hook failure / Foundatio fixture を回収。既存 correctness gate 389 件合格 |
| WP01 | 適格な Stream OpenAsync の直接呼び出し | 代表条件では棄却。warm Tier1 で対象 dispatch は除去済み |
| WP02 | cursor の稀な状態の分離 | 単独試作待ち |
| WP03 | closed 依存 getter の warm/cold 分離 | 単独試作待ち |
| WP04 | generic struct upstream の任意契約 | 初期評価後に判断 |
| WP05 | 明示的な eager constructor injection | 費用分解後に判断。既定の lazy 取得は維持 |
| WP06 | producer 状態の列挙期間中保持 | 初期評価後に判断。既存 pooled producer と分離 |
| WP07 | producer と owner の completion 融合 | WP06 後の profile で必要性を判断 |
| WP08 | SendAsync wrapper の軽量 completion 境界 | 元 fixture は SendAsync と確認。WP03 後に判断 |
| WP09 | Hook failure の最終完了処理 | 元 fixture を回収。profile 後に判断 |
| WP10 | .NET 11 runtime async | .NET 10 の改修効果と分離する別系列。未着手 |

## 保持する契約

公開 API は生成 Mediator 経由のままにする。DI の登録 override、活性化・破棄の所有権、scope、lazy 取得、同時初回取得の CAS winner 共有、再入、null・失敗後の再試行、値型の box identity を維持する。

Stream の要素・順序、request 変更、Filter/Map、token 結合、列挙の独立性、early break、EOF、cleanup、例外、upstream の open 制御を維持する。ValueTask は一回だけ消費し、その消費と producer 側の完了処理がともに終了するまでは source を再利用しない。ExecutionContext、AsyncLocal、culture、SynchronizationContext の境界を維持する。

Task.Yield、token check、probe、cleanup を減らす変更は採用しない。新規のコードコメントと XML コメントは英語とする。main への直接 push、force push、merge、release はしない。

## 測定と判定

正式性能測定は同一ホストで一つずつ行う。baseline と候補は同一 fixture・SDK・runtime・依存・設定で交互に測り、まずは独立した 3 組を残す。許容差は baseline のばらつきから候補測定前に決める。

時間と B/op は別々に評価する。ばらつきを超える改善が再現しない案は改善未証明とする。競合首位を主張するときは、同一ホスト・runtime・workload で主要競合を再測定する。既定 API、typed terminal、pooled producer、その他の任意機能は別系列とする。

[baseline 記録](../performance/baseline-2026-11-07.md)に fixture 対応、環境、再現コマンド、未確認事項を残す。実験の結果は `docs/performance/experiments/` に記録する。
