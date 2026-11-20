# Zendiator 1.0.1への更新

[English](upgrading.md)

1.0.0から更新する場合は、利用しているZendiatorパッケージのバージョンをそろえて再ビルドしてください。1.0.1で生成されるMediatorでは、依存先のキャッシュの既定値が `ClosedServiceCacheMode.Auto` に変わります。以前の配置を使うには、構成マーカーの `ClosedServiceCache` に `ClosedServiceCacheMode.Inline` を指定します。[キャッシュの設定](release/known-limitations.md#dependency-capture-and-lifetime)

0.7.0を使っているアプリでは、既存のリクエストとハンドラーの定義をそのまま使えます。DIのライフタイムも0.7.0と同じです。1.0.0では、本体とAbstractionsのバージョンが異なる場合や、登録メソッドを直接デリゲートに代入した場合にビルド時にエラーが出ます。また、Streamのミドルウェアが上流を包む列挙子を返した場合の処理が修正されています。

## パッケージの更新

Zendiatorを登録するプロジェクトと、`Zendiator.Abstractions` を明示的に参照しているプロジェクトを更新します。次のパスはアプリのプロジェクトに合わせて置き換えてください。パッケージのバージョンを一元管理している場合は、管理元の両方のバージョンを更新します。

```shell
dotnet add App/App.csproj package Zendiator --version 1.0.1
dotnet add Contracts/Contracts.csproj package Zendiator.Abstractions --version 1.0.1
dotnet build App/App.csproj
```

`Zendiator` だけを参照するアプリには、依存パッケージとしてAbstractionsが入ります。通常のPackageReferenceを使ったビルドでは、本体とAbstractionsのバージョンが異なるとエラーが出ます。各プロジェクトの参照をそろえて復元し直してください。任意の `Zendiator.CodeFixes` を導入している場合は、同じバージョンに更新し、`PrivateAssets="all"` を維持します。

## DI登録

既存の `services.AddZendiator<App>(...)` は引き続き使えます。`getServices()?.AddZendiator<App>(...)` と書くと、`getServices()` を一度だけ評価し、結果がnullなら登録を省きます。クラス名を指定して静的メソッドとして呼ぶこともできます。

`ZendiatorServiceCollectionExtensions.AddZendiator<App>` を直接デリゲートに代入するコードには、ビルド時に[ZEN0020](diagnostics/configuration.md#zen0020)が出ます。コールバックを渡したい場合は、ラムダかメソッドの中で `AddZendiator` を呼んでください。次の例は[MediatR移行ガイド](migrating-from-mediatr.ja.md)のリクエストとハンドラーを使います。

```csharp
Action<IServiceCollection> register = collection =>
    collection.AddZendiator<App>(c =>
        c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler)));

register(services);
```

## Streamの列挙と破棄

利用側ではこれまでどおり `mediator.StreamAsync(request, token)` を呼び、`await foreach` で列挙します。ミドルウェアは `next.OpenAsync(request, token)` の結果を直接返しても、ローカル変数に入れてから返しても構いません。Zendiatorは実際に返された列挙子と有効なキャンセルトークンを確認し、上流から直接読み取れるかを判断します。ミドルウェアが上流を別の列挙子で包んで返した場合は、その列挙子を通して要素を読み、破棄します。

最初の1件で列挙を打ち切る場合も、破棄が終わるまでDIスコープを維持してください。列挙子を手動で扱う場合は、実行中の `MoveNextAsync` をawaitしてから `DisposeAsync` を呼びます。ミドルウェアから上流を再度開けるのは、前の列挙子が末尾まで正常に進み、後始末を終えた後です。[Streamの利用ガイド](streaming.ja.md)

## 0.6以前から更新する場合

| 利用中のバージョン | 確認する箇所 |
|---|---|
| 0.6.x以前 | 0.7.0以降、一部の閉じた `SendAsync` 経路では、同期Beforeフックが動く前に必要なハンドラーとフックをすべて構築します。外側のフックが失敗しても、後段のハンドラーのコンストラクターがすでに実行されている場合があります。コンストラクターに副作用がある場合は実行時期を確認してください。別の経路でも同じインスタンスを使う必要があるサービスは、ScopedまたはSingletonで登録します。 |
| 0.5.x | `[ZendiatorHandler]` を `[Handler]` に変更します。標準の `IStreamMiddleware<TRequest,TItem>` から型付きの `OpenAsync<TNext>` を使えます。上流をインターフェース型で受け取る既存の実装も引き続き使えます。型付きメソッドだけを実装したクラスに、インターフェース型の上流を渡す場合は、`IStreamMiddleware<TRequest,TItem>` 経由で呼んでください。 |
| 0.4.x以前 | 非同期イテレーターを返すStreamには `IStreamHandler<TRequest,TItem>.Handle`、名前付きの列挙子を開くStreamには `IStreamSourceHandler<TRequest,TItem,TCursor>.OpenAsync` を実装します。名前付き構成ではハンドラーと各段を明示的に登録します。入力を開く前に前処理が必要なら、`IAsyncStreamBefore<TRequest>` を実装して `StreamBefore` で登録します。 |

0.4.xで `StreamAsync` を呼んだ時点で行っていた入力検証を `StreamBefore` に移すと、検証のタイミングは最初の `MoveNextAsync` に変わります。列挙中のエラーも捕捉してください。Sendの場合は、`ValueTask` が返る前に依存の解決やコールバックで例外が発生することがあるため、呼び出しとawaitを同じ `try` に置きます。同じMediatorはTransientで登録したハンドラーも再利用します。送るたびに新しいハンドラーを取得すると想定していたアプリでは、繰り返し送信や再試行時の動作を確認してください。[互換性とライフタイム](release/known-limitations.md)
