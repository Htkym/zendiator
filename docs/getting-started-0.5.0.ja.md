# 0.5.0候補の使い方

このガイドは、未公開の0.5.0候補packageで同期処理・非同期処理・Streamを使う例です。NuGetで公開済みの0.4.0を使う場合は、[README](../README.ja.md)を参照してください。

## 準備と実行

.NET 10 SDKとC# 14を使います。`Zendiator`と`Zendiator.Abstractions`の候補packageを同じフォルダーに用意します。この例のバージョンは`0.5.0-u01`です。候補packageはNuGet.orgには公開していません。

新しいフォルダーに、次のプロジェクトファイルと`Program.cs`を保存します。[サンプルのファイル](../samples/Zendiator.Sample.GettingStarted/)をコピーしても実行できます。Zendiatorのソースプロジェクトへの`ProjectReference`は使いません。

### Zendiator.Sample.GettingStarted.csproj

<!-- BEGIN project-source -->
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ZendiatorPackageVersion Condition="'$(ZendiatorPackageVersion)' == ''">0.5.0-u01</ZendiatorPackageVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Zendiator" Version="$(ZendiatorPackageVersion)" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0" />
  </ItemGroup>
</Project>
```
<!-- END project-source -->

### Program.cs

<!-- BEGIN program-source -->
```csharp
using System.Runtime.CompilerServices;
using GettingStarted.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace GettingStarted;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(configuration =>
        {
            configuration.Handlers<AddNumbers>(DispatchFamily.SendSync, typeof(AddNumbersHandler));
            configuration.Pipeline<AddNumbers>(DispatchFamily.SendSync);

            configuration.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
            configuration.Pipeline<Greet>(DispatchFamily.SendAsync,
                Stage.Hook<TrimName>(), Stage.Hook<AddExclamation>());

            configuration.Handlers<ReadNumbers>(DispatchFamily.Stream, typeof(ReadNumbersHandler));
            configuration.Pipeline<ReadNumbers>(DispatchFamily.Stream,
                Stage.Hook<KeepEven>(), Stage.Hook<TimesTen>(), Stage.Hook<KeepAtMostForty>());
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();

        var sum = mediator.Send(new AddNumbers(20, 22));
        var greeting = await mediator.SendAsync(new Greet(" Ada "));
        var numbers = new List<int>();
        await foreach (var number in mediator.StreamAsync(new ReadNumbers(6)))
        {
            numbers.Add(number);
        }

        Console.WriteLine($"Send: {sum}");
        Console.WriteLine($"SendAsync: {greeting}");
        Console.WriteLine($"StreamAsync: {string.Join(", ", numbers)}");
    }
}

[ZendiatorComposition("gettingStarted")]
public sealed class App;

public sealed record AddNumbers(int Left, int Right) : ISyncRequest<int>;

[ZendiatorHandler]
public sealed class AddNumbersHandler : ISyncRequestHandler<AddNumbers, int>
{
    public int Handle(AddNumbers request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return request.Left + request.Right;
    }
}

public sealed record Greet(string Name) : IRequest<string>;

[ZendiatorHandler]
public sealed class GreetHandler : IRequestHandler<Greet, string>
{
    public ValueTask<string> HandleAsync(Greet request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new($"Hello, {request.Name}");
    }
}

public sealed class TrimName : ISendRequestTransform<Greet>
{
    public Greet Before(Greet request, CancellationToken token) => request with { Name = request.Name.Trim() };
}

public sealed class AddExclamation : ISendResponseTransform<Greet, string>
{
    public string After(Greet request, string response, CancellationToken token) => response + "!";
}

public sealed record ReadNumbers(int Count) : IStreamRequest<int>;

[ZendiatorHandler]
public sealed class ReadNumbersHandler : IStreamRequestHandler<ReadNumbers, int>
{
    public async IAsyncEnumerable<int> HandleAsync(
        ReadNumbers request, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        for (var number = 1; number <= request.Count; number++)
        {
            token.ThrowIfCancellationRequested();
            yield return number;
        }
    }
}

public sealed class KeepEven : IStreamItemFilter<ReadNumbers, int>
{
    public bool Accept(ReadNumbers request, int item, CancellationToken token) => item % 2 == 0;
}

public sealed class TimesTen : IStreamItemMap<ReadNumbers, int>
{
    public int Map(ReadNumbers request, int item, CancellationToken token) => item * 10;
}

public sealed class KeepAtMostForty : IStreamItemFilter<ReadNumbers, int>
{
    public bool Accept(ReadNumbers request, int item, CancellationToken token) => item <= 40;
}
```
<!-- END program-source -->

PowerShellでは、packageを置いたフォルダーを指定して実行します。`$feed`を手元のパスに変えてください。別の候補バージョンを使う場合は、プロジェクトの`ZendiatorPackageVersion`も合わせます。

```powershell
$feed = 'D:\packages\zendiator-u01'
dotnet restore .\Zendiator.Sample.GettingStarted.csproj --source $feed --source https://api.nuget.org/v3/index.json
dotnet run --project .\Zendiator.Sample.GettingStarted.csproj -c Release --no-restore
```

実行すると、次の3行を出力します。

```text
Send: 42
SendAsync: Hello, Ada!
StreamAsync: 20, 40
```

## 登録と実行順序

`[ZendiatorHandler]`はHandlerを明示する属性です。`Handlers<TRequest>`で使うHandlerを選び、`Pipeline<TRequest>`でHookの順序を指定します。Hookがない同期処理にも、空の`Pipeline`を宣言します。

| 登録するfamily | 呼び出すメソッド | この例の処理 |
|---|---|---|
| `DispatchFamily.SendSync` | `Send` | 20と22を足す |
| `DispatchFamily.SendAsync` | `SendAsync` | 名前の空白を除き、応答の末尾に`!`を付ける |
| `DispatchFamily.Stream` | `StreamAsync` | 偶数を選び、10倍し、40以下を残す |

Send系の`Before`はリストの先頭から、`After`は末尾から呼びます。この例では`TrimName.Before`、Handler、`AddExclamation.After`の順に動きます。Streamの各項目は`KeepEven`、`TimesTen`、`KeepAtMostForty`の順に処理します。

`[ZendiatorComposition]`を付けた`App`から、`GettingStarted.Generated.App`名前空間の登録メソッドと呼び出しメソッドを生成します。属性の文字列はcompositionの識別子です。

## 例外と取消

`Send`の例外は呼出し元へ伝わります。`SendAsync`の処理中の例外は`await`で、StreamのHandlerやHookの例外は列挙時に受け取ります。`request`がnullの場合は呼出し時に例外が出るため、`try`には呼出しと待機・列挙を含めてください。

取消tokenは各入口の`CancellationToken`引数で渡します。この例で取消が検出されると、`OperationCanceledException`が伝わります。Streamは列挙時のtokenも受け取り、呼出し時・列挙時のどちらの取消も反映します。既に取消済みの場合も、Streamでの通知は最初の項目を要求したときです。

## Streamの開始と再列挙

`StreamAsync`の呼出しや列挙子の取得だけでは、HandlerやHookは動きません。`await foreach`が最初の項目を要求したときに処理を始めます。

同じ返り値をもう一度列挙すると、新しい列挙子でHandlerとHookの処理をやり直します。結果のキャッシュとしては使えません。

## DI scopeの所有

標準DIが、この例のHandlerとHookの生成・破棄を受け持ちます。呼び出し側はscopeを作り、そのscopeから`IZendiator`を取得します。非同期処理の完了とStreamの列挙終了までscopeを保持し、その後に破棄します。HandlerやHookを呼び出し側で個別に破棄する必要はありません。

Streamの返り値をscopeの外へ持ち出し、scopeを破棄してから列挙を始めないでください。途中で`break`した場合も、`await foreach`による列挙子の後始末が終わるまでscopeを保持します。

`SendAsync`が返す`ValueTask`は、例のように1回だけ`await`します。同じ`ValueTask`を保存して複数回消費しないでください。

## 0.4.0から移るとき

0.5.0候補では、Handlerの属性と明示的な登録を使います。0.4.0のinterface実装やアセンブリ指定だけに依存した登録は、そのまま移せません。

Behaviorの並びは`Pipeline`の順序付きリストで指定します。この例のHookは、要求の変換、応答の変換、Streamの項目処理という専用の契約を実装しています。既存Behaviorのretryやcacheなどの意味を、自動でHookへ置き換えるものではありません。継続の呼び出しを制御するBehaviorを移す際は、`Stage.Wrapper<T>()`の契約と対象経路を確認してください。

この例は、型引数を持たないclassのrequest、`int`または`string`の応答、`int`のStream、DIが所有するHandlerとHookを使います。ほかの型や経路を網羅する例ではありません。0.5.0候補のAPIと公開範囲は開発中です。

### ジェネリック要求のWrapperと非ジェネリック要求を併用する

0.4.0の`AddOpenBehavior`から移る場合は、Wrapperの契約を明示的に実装し、継続の呼出しを制御する既存の処理を保ちます。以下は既存アプリケーションへ組み込むための断片です。`OpenSendWrapper`名前空間に2つの要求を定義します。

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace OpenSendWrapper;

public sealed record Query<TMarker>(int Value) : IRequest<int>;
public sealed record ClosedQuery(int Value) : IRequest<int>;
```

`[ZendiatorHandler]`を付けた`Handler<TMarker>`と`ClosedHandler`を用意します。それぞれ`IRequestHandler<Query<TMarker>, int>`と`IRequestHandler<ClosedQuery, int>`を実装し、処理本体と必要なDI依存関係は既存のものを使います。次のWrapperは継続を1回呼び、`finally`で終了を記録します。

```csharp
public sealed class Logging<TRequest, TResponse> : IAsyncSendWrapper<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    public async ValueTask<TResponse> InvokeAsync<TNext>(
        TRequest request, TNext next, CancellationToken token)
        where TNext : IAsyncSendContinuation<TRequest, TResponse>
    {
        Console.WriteLine("Before send");
        try
        {
            return await next.InvokeAsync(request, token).ConfigureAwait(false);
        }
        finally
        {
            Console.WriteLine("Send finished");
        }
    }
}
```

両方のHandlerを選び、要求ごとに`Pipeline`を宣言します。非ジェネリック要求のPipelineは空で、Wrapperは`Query<>`だけに適用します。

```csharp
[ZendiatorComposition("openSendWrapperPair")]
public sealed class Pair;

public static class RegisterPair
{
    public static IServiceCollection Add(IServiceCollection services) =>
        services.AddZendiator<Pair>(c =>
        {
            c.Handlers<ClosedQuery>(DispatchFamily.SendAsync, typeof(ClosedHandler));
            c.Pipeline(typeof(Query<>), DispatchFamily.SendAsync,
                Stage.Wrapper(typeof(Logging<,>)));
            c.Handlers(typeof(Query<>), DispatchFamily.SendAsync, typeof(Handler<>));
            c.Pipeline<ClosedQuery>(DispatchFamily.SendAsync);
        });
}
```

同じ名前空間の非同期メソッドから、scope内の生成されたfacadeを取得して両方のoverloadを呼びます。`IZendiator`自体はジェネリックではなく、open側の`SendAsync`は`Query<int>`から型引数を推論します。

```csharp
IServiceCollection services = new ServiceCollection();
RegisterPair.Add(services);
using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var mediator = scope.ServiceProvider.GetRequiredService<Generated.Pair.IZendiator>();

var openResult = await mediator.SendAsync(new Query<int>(42), CancellationToken.None);
var closedResult = await mediator.SendAsync(new ClosedQuery(7), CancellationToken.None);
```

両方の非同期処理が完了するまでscopeを保持します。PipelineはDIが所有するHandlerとWrapperを借りるため、Wrapper内で個別に破棄しないでください。返された`ValueTask`はそれぞれ1回だけ`await`します。この例の範囲はclassの要求に対する1個のopen response Wrapperと、別のclosed response routeです。他のdispatch family、session factory、HookとWrapperの追加の組合せにはそれぞれの契約があり、この例では登録方法を示していません。
### Handler属性の付け忘れ

プロジェクト内で定義したHandlerを属性なしで明示登録すると、ZEN0035で登録箇所が示されます。ZEN0035はほかの生成条件の不備にも使います。診断文が属性不足を示している場合は、上の`AddNumbersHandler`のようにクラス宣言へ`[ZendiatorHandler]`を付けてください。

`Zendiator.CodeFixes`は任意で追加するパッケージです。利用する場合は、`Zendiator`と同じ候補バージョンを用意し、プロジェクトの`ZendiatorPackageVersion`をそのバージョンに合わせてください。[導入例](../src/Zendiator.CodeFixes/README.md)のように`PrivateAssets="all"`を指定します。CodeFixの候補パッケージはNuGet.orgには公開していません。

CodeFixの対象は属性不足と判断できるZEN0035だけです。Fix Allの対象は現在の文書だけです。

現行のVisual Studioで利用できます。
