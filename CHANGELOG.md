# Changelog

## 0.5.0 (unreleased)

- Add explicit handler markers, named compositions and ordered handler/pipeline declarations. Dedicated hooks can be generated together; arbitrary wrappers retain their own execution boundaries.
- Replace the previous Stream handler contract with `IStreamHandler<TRequest, TItem>` returning `IAsyncEnumerable<TItem>`, or `IStreamSourceHandler<TRequest, TItem, TCursor>` opening a named cursor through `OpenAsync`. Guarded paths acquire input on the first move; eligible flat iterator routes with noncancelable tokens acquire input at `GetAsyncEnumerator` and require consumer disposal after EOF or failure. `StreamAsync` returns standard `IAsyncEnumerable<T>` for normal enumeration and HTTP serialization. `StreamSource(...).ConsumeAsync(...)` provides a separate typed terminal without a performance guarantee.
- Give guarded Stream enumerations independent ownership, lazy activation, sequential operation guards and deterministic cleanup. Callers must retain their DI scope and consume each returned `ValueTask` once. Internal helpers do not guarantee ambient-context isolation or return to a UI context.
- Fuse completion forwarding across eligible Stream middleware that return the exact upstream cursor. Other cursor shapes retain their execution boundaries. Full enumeration can benefit, while extra fixed state can increase allocation and early-stop cost.
- Combine source waits and asynchronous Filter/Map processing in one Stream move loop. This changes ambient-context flow into subsequent source pulls and outer cleanup, and can retain rejected item references during a later source wait. See the known limitations before migrating ambient-dependent stages.
- Let eligible plain `SendAsync` routes return the handler's original `ValueTask`. Dependency resolution and synchronous callback failures, including cancellation, can now throw during the call; synchronous ambient changes can be visible to the caller.
- Register one Scoped `IZendiator` entry and Transient defaults for user dependencies. Each Mediator reuses captured dependencies. Eligible closed asynchronous Hook routes acquire a route-local receiver set before the first Hook, including receivers left unused by an outer failure. Transient captures can differ across routes; construction exceptions can occur earlier. Native DI retains activation and disposal ownership. Use after scope disposal is unsupported.
- Keep closed dependency storage inline by default, with explicit `ClosedServiceCacheMode.LazyBlocks` for suitable sparse compositions. Open generic captures use a small cache before dictionary promotion. Neither layout guarantees faster dispatch for every workload.
- Split generated outputs and share registration analysis. Some parsing and the legacy renderer remain; generation-time improvements have not been established.
- Add the separately installed `Zendiator.CodeFixes` package for the missing-handler-marker fix and document-scoped Fix All. It does not automate semantic migration.

See [getting started and migration](docs/getting-started-0.5.0.md) and
[known limitations](docs/release/known-limitations.md). These are breaking APIs. The
[0.5.0 release notes](docs/release/0.5.0-release-notes.md) describe migration and
package availability. The README contains selected results from the measured
0.5.0 candidate, including unfavorable time and allocation results. Historical
0.4.0 tables remain separate. No universal non-regression or performance lead is
established.

## 0.4.0

- 同期のStream入力Validatorを明示登録するAPIを追加しました。検証は入口で1回実行し、HandlerとBehaviorは列挙時に開始します。
- 同期の複数結果を呼び出し元の `Span<T>` に書く `SendAllSync` を追加しました。生成 `IZendiator` の手動実装には新しいオーバーロードが必要です。
- `SendAllAsync` / `SendAllSync` の結果実体を固定長配列にしました。`IReadOnlyList<T>` の契約は維持し、`List<T>` へのキャストは対応しません。
- Streamの初期化失敗後に列挙器を停止するようにしました。停止後も `DisposeAsync` による後始末が必要です。
- 依存保持、Streamの開始・破棄、Notificationの確保と繰り返し取得を整理しました。返された `ValueTask` の単回消費とScopeの維持が必要です。
- 同期専用などの構成で生成Resolverの基底型が変わる場合があります。本体とGeneratorの版を揃えて再コンパイルしてください。
- 固定ソース版 `1c41d2b` の全360件から、得意な条件と不利な条件をREADMEへ抜粋しました。各ケース1 launchの観測で、速度の一般化はしません。

[0.4.0 リリースノート](docs/release/0.4.0-release-notes.ja.md)

## 0.3.0

- 既定の Scoped Mediator に対し、生成される Handler と Behavior の登録を Scoped から Transient に変更しました。同じ Mediator 内では最初に取得したインスタンスを引き続き再利用します。
- 生成される Mediator は `IDisposable` を実装しなくなりました。依存サービスの破棄は DI が担います。送信やストリーム列挙が終わるまでスコープを維持してください。
- スコープやルート Provider の破棄後に、保持済みの Mediator から送信したときの `ObjectDisposedException` 保証を廃止しました。`ZendiatorRootLifetime` と Resolver の `Dispose()` も削除しました。
- 生成される汎用 Mediator の基底型が変更されました。通常の `AddZendiator()` と `IZendiator.SendAsync` の使い方は変わりません。
- Analyzer の警告 ZEN0021～ZEN0023、構成ごとのサービス番号、適用可能な経路の単一依存用 Resolver、ユースケース別ベンチマークを追加しました。移行時の注意点は [0.3.0 リリースノート](https://github.com/Htkym/zendiator/blob/v0.3.0/docs/release/0.3.0-release-notes.ja.md)を参照してください。

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
