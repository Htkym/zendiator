# リクエストを登録して送る

[English](getting-started.md)

Zendiatorは.NET 10とC# 14で使えます。[GettingStartedサンプル](../samples/Zendiator.Sample.GettingStarted/Program.cs)では、同期リクエストで足し算をし、非同期リクエストの前後であいさつ文を整え、Streamから数値を受け取ります。`1.0.0-rc.1` を取得できるNuGetフィードを設定し、リポジトリのルートで実行してください。

```shell
dotnet run --project samples/Zendiator.Sample.GettingStarted -c Release
```

```text
Send: 42
SendAsync: Hello, Ada!
StreamAsync: 20, 40
```

## リクエストとハンドラーを定義する

アプリには `Zendiator` と `Microsoft.Extensions.DependencyInjection` を追加します。リクエストの型だけを共有するプロジェクトでは `Zendiator.Abstractions` を使えます。`Zendiator` と `Zendiator.Abstractions` のバージョンは揃えてください。

```csharp
using Zendiator;

namespace GettingStarted;

[ZendiatorComposition("gettingStarted")]
public sealed class App;

public sealed record Greet(string Name) : IRequest<string>;

[Handler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}");
    }
}
```

`App` は登録をまとめる型です。この例では `GettingStarted` 名前空間に置いたので、アプリからは `GettingStarted.Generated.App.IZendiator` を使えます。属性の文字列はこの構成を識別する名前です。

## ハンドラーを登録して呼び出す

起動時のコードで `GettingStarted`、`GettingStarted.Generated.App`、`Microsoft.Extensions.DependencyInjection`、`Zendiator`、`Zendiator.DependencyInjection` をインポートします。次の例では `greeting` に `Hello, Ada` が入ります。

```csharp
var services = new ServiceCollection();
services.AddZendiator<App>(c =>
{
    c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
});

await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var greeting = await mediator.SendAsync(new Greet("Ada"));
```

設定ラムダは `AddZendiator` の呼び出し箇所に書きます。登録処理をデリゲートで渡すときは、この呼び出しを自分のメソッドやラムダで包んでください。`services?.AddZendiator<App>(...)` と書くと、`services` がnullのときは登録を省きます。[登録に関する診断](diagnostics/configuration.md#zen0020)

## ハンドラーの前後に処理を加える

名前の余分な空白を取り、あいさつの末尾に感嘆符を付けてみます。先ほどの設定ラムダの `Handlers<Greet>` の後に次の行を加え、二つのフックを定義します。

```csharp
c.Pipeline<Greet>(DispatchFamily.SendAsync,
    Stage.Hook<TrimName>(), Stage.Hook<AddExclamation>());
```

```csharp
public sealed class TrimName : ISendRequestTransform<Greet>
{
    public Greet Before(Greet request, CancellationToken token) =>
        request with { Name = request.Name.Trim() };
}

public sealed class AddExclamation : ISendResponseTransform<Greet, string>
{
    public string After(Greet request, string response, CancellationToken token) =>
        response + "!";
}
```

これで `SendAsync(new Greet(" Ada "))` の結果は `Hello, Ada!` になります。Beforeフックは登録順に、Afterフックは逆順に動きます。キャッシュに値があればそのまま返すなど、次の処理を呼ぶかどうかを決めたいときはラッパーを使います。

## 呼び出し方を選ぶ

| 用途 | 名前付き構成での登録 | 呼び出し |
|---|---|---|
| 結果を同期で返す | `DispatchFamily.SendSync` | `mediator.Send(request)` |
| 処理の完了や結果を非同期で待つ | `DispatchFamily.SendAsync` | `await mediator.SendAsync(request)` |
| 複数のハンドラーから結果を受け取る | `SendAllSync` または `SendAllAsync` で、ハンドラーを実行順に指定 | `SendAll` または `await SendAllAsync` |
| 項目を順次受け取る | `DispatchFamily.Stream` でハンドラーとパイプラインを明示 | `StreamAsync` の結果を `await foreach` で列挙 |

Streamの入力や項目の絞り込み、列挙後の破棄については、[Streamの使い方](streaming.ja.md)を参照してください。

ジェネリックなリクエストは、`Handlers` に `typeof(Lookup<>)` と `typeof(LookupHandler<>)` のように登録します。呼び出すときは `new Lookup<Product>("p-7")` のように具体的な型を指定します。[ジェネリックな問い合わせの例](../samples/Zendiator.Sample.GenericLookup/Program.cs)・[複数ハンドラーとラッパーの例](../samples/Zendiator.Sample.ComposedApp/Program.cs)

## リクエストを送って通知する

型引数のない `AddZendiator` に、メッセージ、ハンドラー、購読者の型を含むアセンブリを登録します。購読者は `INotificationHandler<TNotification>` を実装します。次の例では、[サンプルアプリ](../samples/Zendiator.Sample.Application/DependencyInjection.cs)と、その契約を定義したプロジェクトの型を使います。

```csharp
services.AddZendiator(configuration =>
{
    configuration.Namespace = "Zendiator.Sample.Application.Generated";
    configuration.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
    configuration.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
});
```

同じスコープからMediatorを取得し、コマンドの処理が終わってから購読者に通知します。

```csharp
var mediator = scope.ServiceProvider.GetRequiredService<
    global::Zendiator.Sample.Application.Generated.IZendiator>();
var id = Guid.NewGuid();
await mediator.SendAsync(new PurgeHouseholdCommand(id));
await mediator.PublishAsync(new HouseholdPurged(id));
```

`PublishAsync` は同じプロセス内の購読者を順に呼び、それぞれの処理が終わるのを待ちます。いずれかが失敗すると、その時点で終了します。`AddZendiator<App>` のような名前付き構成で指定できるのはSendとStreamです。その `Handlers` や `Pipeline` に `DispatchFamily.Notification` を渡すとZEN0029が出ます。

## 処理が終わるまでスコープを保持する

Mediatorは既定でScopedです。同じMediatorは、Transientとして登録したハンドラーも再利用します。リクエストごとに変わる状態はリクエスト自身か、その処理が所有するセッションに持たせます。複数の経路から同じインスタンスを使う場合は、`AddZendiator` を呼ぶ前にScopedまたはSingletonで登録してください。送信やStreamの破棄が完了してからスコープを閉じます。

返された `ValueTask` は1回だけ `await` します。`SendAsync` は依存サービスの取得や同期コールバックで失敗すると、`ValueTask` を返す前に例外を投げる場合があります。例外を扱うときは、呼び出しと `await` を同じ `try` ブロックに書いてください。[依存サービスの寿命と例外](release/known-limitations.md)
