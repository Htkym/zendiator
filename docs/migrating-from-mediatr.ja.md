# MediatRからの移行

[English](migrating-from-mediatr.md)

MediatRの `ISender`、`IPublisher`、`IMediator` を使っているアプリでは、リクエストとその呼び出し元を一組ずつZendiatorに移せます。APIの対応表はMediatR 12と15を対象としています。Zendiator 1.0.0-rc.1には.NET 10とC# 14が必要です。

## リクエストとハンドラー

RCを提供するNuGetフィードを選び、ハンドラーを登録するプロジェクトに次のパッケージを追加します。リクエストや通知の型だけを定義するプロジェクトでは、`Zendiator.Abstractions` だけを参照できます。アプリ全体で `Zendiator` と `Zendiator.Abstractions` のバージョンをそろえてください。

```shell
dotnet add package Zendiator --version 1.0.0-rc.1
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

| MediatR | Zendiator |
|---|---|
| `IRequest<TResult>` | `Zendiator.IRequest<TResult>` |
| `Task<TResult>` を返す `IRequestHandler<TRequest, TResult>.Handle` | `ValueTask<TResult>` を返す `IRequestHandler<TRequest, TResult>.HandleAsync` |
| 戻り値のない `IRequest` | `IRequest` と、`ValueTask` を返す `IRequestHandler<TRequest>.HandleAsync` |
| `ISender.Send(request, token)` | `IZendiator.SendAsync(request, token)` |
| `IPublisher.Publish(notification, token)` | 通知を登録した `IZendiator` の `PublishAsync(notification, token)` |
| `CreateStream(request, token)` | `IZendiator.StreamAsync(request, token)` |

リクエストを一つのハンドラーで処理する例です。ハンドラーに `[Handler]` を付け、`AddZendiator<App>` の中で登録します。実行すると `Hello, Ada!` と表示されます。

```csharp
using Microsoft.Extensions.DependencyInjection;
using Migration.Generated.App;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Migration;

[ZendiatorComposition("greetings")]
public sealed class App;

public sealed record Greet(string Name) : IRequest<string>;

[Handler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}!");
    }
}

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(c =>
            c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler)));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Console.WriteLine(await mediator.SendAsync(new Greet("Ada")));
    }
}
```

`ISender` を受け取っていたクラスには、この例の `Migration.Generated.App.IZendiator` を注入します。`SendAsync` の引数は具体的なリクエスト型です。`object` や `IRequest<string>` の変数に入れている場合は、呼び出し前に具体的な型を判定します。別のアセンブリにあるハンドラーの登録方法は[利用ガイド](getting-started.ja.md)にあります。

## パイプラインの処理

`IPipelineBehavior` で入力の検証、リクエストや応答の変換、例外の記録をしている場合は、対応するフックを実装し、`Stage.Hook<T>()` で登録します。[GettingStartedサンプル](../samples/Zendiator.Sample.GettingStarted/Program.cs)には、ハンドラーを呼ぶ前に名前の空白を取り除き、処理後に応答の末尾に感嘆符を付ける例があります。元のパイプラインで一番外側にあった処理を `Pipeline` の先頭に書きます。

キャッシュに値があればハンドラーを呼ばない処理や、失敗した呼び出しを再試行する処理には、`IAsyncSendWrapper<TRequest,TResponse>` を実装し、`Stage.Wrapper<T>()` で登録します。後続の処理を呼ぶ箇所で `next.InvokeAsync(request, token)` を実行します。呼び出しごとに一度だけawaitし、完了してから次の呼び出しを始めてください。[ComposedAppサンプル](../samples/Zendiator.Sample.ComposedApp/Program.cs)にキャッシュと再試行の例があります。

## 通知

同じ `IZendiator` からリクエストと通知を送る場合は、型引数のない `AddZendiator` にアセンブリを登録します。次の例は[Sample.Application](../samples/Zendiator.Sample.Application/DependencyInjection.cs)と[Sample.Contracts](../samples/Zendiator.Sample.Contracts/)の型を使います。最初のコードでサービスを登録し、次のコードをDIスコープ内で実行します。`token` には呼び出し元のキャンセルトークンを渡します。

```csharp
services.AddZendiator(c =>
{
    c.Namespace = "Zendiator.Sample.Application.Generated";
    c.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
    c.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
});
```

```csharp
var mediator = scope.ServiceProvider.GetRequiredService<
    global::Zendiator.Sample.Application.Generated.IZendiator>();
var id = Guid.NewGuid();
await mediator.SendAsync(new PurgeHouseholdCommand(id), token);
await mediator.PublishAsync(new HouseholdPurged(id), token);
```

購読者は `INotificationHandler<TNotification>.HandleAsync` を実装し、`ValueTask` を返します。`PublishAsync` は購読者を一つずつ呼び、最初の失敗で配送を止めます。順番に意味がある場合は `[HandlerOrder(Order = ...)]` で指定します。既知の通知型に購読者がいなければ正常に終了します。MediatRで並列配送を使っていたアプリは、通知の実行順と、購読者が失敗した後の処理を確認してください。

リクエストの例で使った `AddZendiator<App>` はSendとStreamの登録に使います。この形式の `Handlers` や `Pipeline` に `DispatchFamily.Notification` を渡すと[ZEN0029](diagnostics/configuration.md#zen0029)が出ます。

## Stream

非同期イテレーターを使っているハンドラーは、`IAsyncEnumerable<TItem>` を返す処理を引き継げます。`IStreamHandler<TRequest,TItem>.Handle(request, token)` を実装して `[Handler]` を付け、`DispatchFamily.Stream` でハンドラーを登録します。`AddZendiator<App>` を使う場合は、ミドルウェアがなくても `Pipeline<TRequest>(DispatchFamily.Stream)` を宣言します。

呼び出し側では `await foreach` で読み、キャンセルトークンを渡します。APIの呼び出し元と列挙する側が別々のトークンを持つ場合は、前者を `StreamAsync` に、後者を `WithCancellation` に渡します。列挙子の破棄が終わるまでDIスコープを維持してください。HTTPエンドポイントが結果を返した後に応答の書き出しが続く場合も同様です。既存の列挙子を使う方法や要素を変換する方法は[Streamの利用ガイド](streaming.ja.md)にあります。

## スコープ、例外、キャンセル

既定のMediatorはScopedです。同じMediatorは取得済みのハンドラーとフックを再利用します。Transientで登録していても、同じスコープの再試行や2回目の送信で同じインスタンスを使うことがあります。フックのある一部の閉じたSend経路では、最初のフックが失敗する場合にも後段のハンドラーを先に構築します。コンストラクターに副作用がある場合は実行時期を確認し、共有するハンドラーにリクエストごとの状態を保存しないでください。

失敗を捕捉するときは、`SendAsync` の呼び出しとawaitの両方を `try` の中に置きます。依存の解決、キャンセル、同期コールバックで、`ValueTask` を返す前に例外が発生する場合があります。返された `ValueTask` は一度だけawaitします。移行するアプリで使う処理に合わせて、キャンセル、フックの失敗、Streamを1件で打ち切る場合も確認してください。[互換性とライフタイム](release/known-limitations.md)
