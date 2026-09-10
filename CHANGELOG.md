# Changelog

## 0.1.1

公開APIと生成される利用側コードを維持した、内部構成の整理。

- ライブラリ、サンプル、テスト、手書きのベンチマークを原則1ファイル1型に整理した。
  同名のインターフェース、必要な入れ子型、生成済みのベンチマークデータは維持した。
- Source Generator を型解析、DI設定、配送経路、通知、同期処理、ストリーム、コード出力の役割ごとに分割した。
  解析状態は生成処理ごとに保持し、入力変更時に前回の状態が残らないことを確認するテストを追加した。
- リリースのソースマニフェストを、固定した3ファイルから `src` 配下の全C#ソースへ変更した。
- 252テストと、13構成における変更前後の生成コード・診断の一致を確認した。
  利用側のコード変更は不要。既知の制限は継続する。

## 0.1.0

初版公開。型付き送信、native void、generic 要求、逐次通知、複数配送、同期／ref struct、
ストリーム、DI-first 構成、Native AOT 対応を含む。`0.x` の公開であり、API を永久に固定するものではなく、
`1.0.0` の前に変更される場合がある。

- 複数契約を実装したハンドラーの配送先、generic 応答の `new()` 制約、DI 設定の型名照合、
  アセンブリ登録の重複、Behavior の完全修飾名、同期 void の generic 要求、
  `allows ref struct` を含む複合制約の生成、Stream の内部破棄時の token 切断を修正した。
- 独自の寿命判定キャッシュを削除し、ハンドラーと Behavior の再利用・検証・破棄を標準 DI に統一した。
  生成される `CachePolicy`、holder、Mediator の `IDisposable` 実装を削除した。
- Package Description から一般的な allocation-free の表現を削除した。
  現行の性能値は `docs/release/0.1.0-release-notes.md` に記載の正式測定だけを使う。
  RC1／Hardening-1 の旧数値は現行値として使用しない。
- SourceLink を 10.0.111 へ更新した。
- 両パッケージに NuGet アイコン (`icon.png`、128x128 PNG。原本 `docs/icon/icon.png`) を同梱した。
- 公開 workflow は tag commit から CI で生成・検証した artifact を使い、
  consumer／DI／AOT 検証と SHA256 manifest 照合を経て、同じ bytes を publish する。
  再 pack と `--skip-duplicate` は使わない。
- 251 テスト、24 プロジェクトの format、win-x64 Native AOT AOT01〜AOT12、
  package consumer 検証を通過した。既知の制限は `docs/release/known-limitations.md` を見る。

## 0.1.0-preview.1

この節の旧性能値は `HistoricalSuperseded`。0.1.0 の現行性能値としては使用しない。

初版プレビュー。型付き送信、構造体の継続処理、DI 登録、診断、複数アセンブリ対応まで。

- `IRequest<TResponse>` を基本契約にし、`ICommand<TResponse>` と `IQuery<TResponse>` を派生させた。
  戻り値なしは `IRequest` の native void 経路（`ValueTask`。旧 `Unit` 形式は互換経路として残るが混在は診断）。
- 生成した `Zendiator`／`IZendiator` に具体的なリクエスト型を受ける `SendAsync` を出した。
  `IRequest` 型や `object` による汎用送信 API はない。
- Behavior は DI 解決の `class`、継続は生成した `readonly struct` にした。
  小さい `Order` が外側。重複した型・`Order` は ZEN0004。
- ハンドラーのない要求は ZEN0001、重複は ZEN0002、契約不備と応答不一致は ZEN0003、
  生成先の宣言不備は ZEN0005で報告する。
- `AddZendiator()` は `TryAdd` で登録し、事前登録と `Transient` を尊重する。
  登録の冪等性、`IZendiator` と `Zendiator` の同一性、スコープ分離、非同期破棄をテストで固定した。
- `configuration.ServiceLifetime` で Singleton と Transient を選べる。
  既定は Scoped のまま。全登録が同じ有効期間を共有し、先勝ちで置き換えない。
- 同期完了する無割り当てのハンドラー／Behavior で、0 段・1 段の送信あたり追加割り当て 0 B を確認した。
  Behavior のない経路は継続 struct を介さず直接送る。`SendAsync` 入口の重複した
  キャンセル確認をなくし、各ノードの確認は残した。
- フルモードのトリム公開と実行を確認した。Native AOT リンクは
  `scripts/aot-matrix-test.ps1` でパッケージ経由に検証する（AOT01-AOT12）。
- 通知（逐次 `PublishAsync`）とストリーム（`StreamAsync`、遅延・取消・破棄対応）、
  `IMultiRequest` による複数配送、`ISyncRequest` による同期と ref struct に対応した。
- MessagePipe への直接・推移的依存を製品・テスト・サンプルから除いた。
