# Changelog

## 未リリース

- 複数の依存を保持する最初のページで、参照用配列の確保を省くようにしました。
- 依存型を確定できる小規模な構成では、最初の保持配列を依存型数に合わせて小さくするようにしました。
- ストリーム列挙器が初期化に必要な情報を列挙元と共有し、列挙開始時の確保量を減らしました。
- ストリームの初回処理が非同期で中断する場合の状態機械を小さくしました。
- 未開始・破棄済みのストリーム列挙器について、後始末が不要な場合の破棄処理を簡略化しました。
- ストリーム列挙器の転送状態を統合しました。初期化失敗後の `MoveNextAsync()` は `false` を返し、内部の列挙器が未生成のまま参照される問題を修正しました。停止後も `DisposeAsync()` による後始末は必要です。
- `SendAllAsync` / `SendAllSync` の結果を固定長配列で返すようにしました。戻り値の契約は `IReadOnlyList<T>` のままですが、`List<T>` へのキャストには対応しません。
- 同期要求だけの構成でも、公開された単一のハンドラー型・Behavior なし等の条件を満たせば単一依存用 Resolver を使用します。対象となる生成 Mediator の基底型が変わります。
- 同期の複数結果を呼び出し元の `Span<T>` に書き込む `SendAllSync(request, destination, cancellationToken)` を追加しました。生成 `IZendiator` を手動実装している場合は、新しいメソッドの実装が必要です。

## 0.3.0

- 既定の Scoped Mediator に対し、生成される Handler と Behavior の登録を Scoped から Transient に変更しました。同じ Mediator 内では最初に取得したインスタンスを引き続き再利用します。
- 生成される Mediator は `IDisposable` を実装しなくなりました。依存サービスの破棄は DI が担います。送信やストリーム列挙が終わるまでスコープを維持してください。
- スコープやルート Provider の破棄後に、保持済みの Mediator から送信したときの `ObjectDisposedException` 保証を廃止しました。`ZendiatorRootLifetime` と Resolver の `Dispose()` も削除しました。
- 生成される汎用 Mediator の基底型が変更されました。通常の `AddZendiator()` と `IZendiator.SendAsync` の使い方は変わりません。
- Analyzer の警告 ZEN0021～ZEN0023、構成ごとのサービス番号、適用可能な経路の単一依存用 Resolver、ユースケース別ベンチマークを追加しました。移行時の注意点は [0.3.0 リリースノート](docs/release/0.3.0-release-notes.ja.md)を参照してください。

## 0.2.0

- 標準 DI による遅延取得に実行経路を統一しました。同じ Mediator は Handler・Behavior のサービス型ごとに最初に取得したインスタンスを保持します。Transient 登録も対象です。
- Mediator と生成される依存サービスの既定の有効期間は Scoped です。依存サービスの生成と破棄は DI が担います。
- Source Generator の増分生成を改善しました。通常の `AddZendiator()` を使用できます。

## 0.1.2

- ストリーム列挙の処理を改善しました。公開 API と利用側のコードに変更はありません。

## 0.1.1

- 内部構成を整理しました。公開 API と利用側のコードに変更はありません。

## 0.1.0

- 型付き送信、戻り値のない要求、汎用要求、逐次通知、複数配送、同期要求、ストリーム、DI 登録、Native AOT 対応を追加しました。
