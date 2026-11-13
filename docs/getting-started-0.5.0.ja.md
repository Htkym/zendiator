# 0.7.0開発版の使い方

0.7.0は未公開です。このガイドは[ルート単位の依存取得の変更](release/0.7.0-release-notes.md)を含む開発版の契約を説明します。公開済みの0.6.0パッケージでは従来の動作を保ちます。[0.6.0のリリースノート](release/0.6.0-release-notes.ja.md)も確認してください。

以下の例は公開済みの0.6.0パッケージで実行でき、新しい依存取得の動作は含みません。0.7.0を試す場合は、このソースからZendiator.AbstractionsとZendiatorの同じバージョンのパッケージを作り、ローカルNuGetフィードに置いてください。そのフィードとNuGet.orgからサンプルを復元し、復元と実行の両方に`-p:ZendiatorPackageVersion=0.7.0`を指定します。NuGet.orgに0.7.0が公開されていることを前提にはしません。

## 公開済み0.6.0の例を実行する

.NET 10 SDKとC# 14を使います。NuGet.orgから0.6.0のパッケージを取得します。

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
    <ZendiatorPackageVersion Condition="'$(ZendiatorPackageVersion)' == ''">0.6.0</ZendiatorPackageVersion>
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
        await mediator.StreamSource(new ReadNumbers(6)).ConsumeAsync(new NumberSink(numbers));

        Console.WriteLine($"Send: {sum}");
        Console.WriteLine($"SendAsync: {greeting}");
        Console.WriteLine($"StreamAsync: {string.Join(", ", numbers)}");
    }
}

[ZendiatorComposition("gettingStarted")]
public sealed class App;

public sealed record AddNumbers(int Left, int Right) : ISyncRequest<int>;

[Handler]
public sealed class AddNumbersHandler : ISyncRequestHandler<AddNumbers, int>
{
    public int Handle(AddNumbers request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return request.Left + request.Right;
    }
}

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

public sealed class TrimName : ISendRequestTransform<Greet>
{
    public Greet Before(Greet request, CancellationToken token) => request with { Name = request.Name.Trim() };
}

public sealed class AddExclamation : ISendResponseTransform<Greet, string>
{
    public string After(Greet request, string response, CancellationToken token) => response + "!";
}

public sealed record ReadNumbers(int Count) : IStreamRequest<int>;

[Handler]
public sealed class ReadNumbersHandler : IStreamSourceHandler<ReadNumbers, int, NumberCursor>
{
    public ValueTask<NumberCursor> OpenAsync(ReadNumbers request, CancellationToken token) =>
        new(new NumberCursor(request.Count, token));
}

public sealed class NumberCursor(int count, CancellationToken token) : IAsyncEnumerator<int>
{
    private int _index;
    private bool _started;
    public int Current { get; private set; }
    public ValueTask<bool> MoveNextAsync() => _started ? new(Move()) : StartAsync();

    private async ValueTask<bool> StartAsync()
    {
        _started = true;
        await Task.Yield();
        return Move();
    }

    private bool Move()
    {
        token.ThrowIfCancellationRequested();
        if (_index >= count) return false;
        Current = ++_index;
        return true;
    }

    public ValueTask DisposeAsync() { _started = true; _index = count; return default; }
}

// Value-type sinks are copied; the list is the shared result owner.
public readonly struct NumberSink(List<int> numbers) : IStreamSink<int>
{
    public ValueTask<bool> OnNextAsync(int item, CancellationToken token)
    {
        numbers.Add(item);
        return new(true);
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

PowerShellから実行します。

```powershell
$version = '0.6.0'
dotnet run --project .\Zendiator.Sample.GettingStarted.csproj -c Release -p:ZendiatorPackageVersion=$version
```

実行すると、次の3行を出力します。

```text
Send: 42
SendAsync: Hello, Ada!
StreamAsync: 20, 40
```

## 登録と実行順序

`[Handler]`はHandlerを明示する属性です。`Handlers<TRequest>`で使うHandlerを選び、`Pipeline<TRequest>`でHookの順序を指定します。この例ではHookがない同期処理にも、空の`Pipeline`を明示しています。対応するHandlerのみの`SendSync`と`SendAsync`では、`Pipeline`を省略できます。stageがないStreamには、空の`Pipeline`を`DispatchFamily.Stream`で宣言します。

| 登録するfamily | 呼び出すメソッド | この例の処理 |
|---|---|---|
| `DispatchFamily.SendSync` | `Send` | 20と22を足す |
| `DispatchFamily.SendAsync` | `SendAsync` | 名前の空白を除き、応答の末尾に`!`を付ける |
| `DispatchFamily.Stream` | `StreamSource(...).ConsumeAsync(...)` | 偶数を選び、10倍し、40以下を残す |

Send系の`Before`はリストの先頭から、`After`は末尾から呼びます。この例では`TrimName.Before`、Handler、`AddExclamation.After`の順に動きます。Streamの各項目は`KeepEven`、`TimesTen`、`KeepAtMostForty`の順に処理します。

`[ZendiatorComposition]`を付けた`App`から、`GettingStarted.Generated.App`名前空間の登録メソッドと呼び出しメソッドを生成します。属性の文字列はcompositionの識別子です。

## 例外と取消

`Send`の例外は呼出し元へ伝わります。Hookがないか同期Beforeだけを使い、factory・Wrapper・sessionを使わないplain `SendAsync`では、依存解決と同期callbackが呼出し中に動きます。その例外や取消は、`ValueTask`が返る前に同期throwとなることがあります。同期部分で行った`AsyncLocal`、`CultureInfo`、`SynchronizationContext`の変更も呼出し側に見えます。返された`ValueTask`の非同期の失敗は`await`で受け取ります。`try`には呼出しと待機を含めてください。

この例のStreamではConsumeAsyncによる消費時にHandlerやHookが動きます。`request`がnullの場合は入口の呼出し時に例外が出るため、これらの失敗を扱う`try`には呼出しと待機を含めてください。

取消tokenは各入口の`CancellationToken`引数で渡します。この例で取消が検出されると、`OperationCanceledException`が伝わります。この例のStreamでは列挙時のtokenも受け取り、呼出し時・列挙時のどちらの取消も反映します。既に取消済みの場合も、取消の検出は最初の項目を要求したときです。

## Streamの開始と再列挙

名前付きNumberCursorをOpenAsyncで開きます。標準列挙にはStreamAsyncが返すsource classを使います。この例はStreamSourceのreadonly計画値とConsumeAsync、readonly sinkと共有Listで結果を集めます。計画値はIAsyncEnumerableではなく、ConsumeAsyncごとに非公開の専用ownerを作ります。入口の呼出しやcursor取得だけではuser DIやI/Oを始めず、各ownerの最初のMoveで開始します。標準source objectはStreamAsyncの呼出し時に割り当てます。

pending Moveの完了前に別MoveやDisposeを始めると拒否します。Moveを一度awaitしてからDisposeし、scopeは後始末まで保持してください。false Filterは同じMove内で次itemへ進み、terminalのfalseは正常な全体停止です。sink/serializer例外はsource middlewareへ注入しません。

Current、pending Dispose、取消し、session所有権、context、HTTPと移行条件は[Streamの移行・実行契約](streaming-0.5.0.ja.md)を参照してください。この例は時間や割当の優位を保証しません。標準列挙、typed terminal、HTTPは対象の版・条件・実測結果で別々に評価してください。

## DI scopeの所有

標準DIがこの例のHandlerとHookの生成・破棄を受け持ちます。呼び出し側はscopeを作り、そのscopeから`IZendiator`を取得します。非同期処理の完了とStreamの列挙終了までscopeを保持し、その後に破棄します。HandlerやHookを呼び出し側で個別に破棄する必要はありません。

生成登録の既定は `IZendiator` が Scoped、Handler・Hook・session factory が Transient です。Mediator は取得した依存を再利用します。条件を満たす閉じた `SendAsync` ルートでは、同期 Before Hook だけの経路も含め、ルート単位の依存セットを使います。対象は Handler と全 Hook が DI から借用する参照型で、異なる受け手の型が 2 種類以上あり、Zendiator の session/factory 契約や Wrapper を使わないルートです。open generic のルート定義、値型の受け手、Handler だけのルートなどは従来の取得方法を保ちます。通常の DI の実装ファクトリはこの除外条件の factory 契約には当たりません。

初回の Send では、引数と取消を最初に検査した後、最初の Hook が動く前に DI が全受け手を取得します。新しいインスタンスが必要な場合は、この時点で構築します。外側の Hook が失敗した場合も、内側の Hook と Handler は構築済みになることがあります。依存セットの構築に失敗した場合は Hook を呼び出さず、失敗した取得結果は保存しません。DI はコンストラクタの引数を解決する途中で取消を検査しません。必須の受け手が null ならセットを保存せず、後で取得を再試行できます。コンストラクタが null を検出する前に、後続の引数が取得済みになる場合があります。

同期 Before だけの経路では、依存解決とコールバックが呼び出し中に動き、同期例外が発生することや、Handler の元の `ValueTask` をそのまま返すことは変わりません。ほかの非同期 Hook 経路の実行境界も変わりません。

Mediator は取得したセットをそのルートで再利用します。同じ型でも、Transient の受け手はルートごとに別のインスタンスになる場合があります。ルート間で同じインスタンスを使う必要があれば、Scoped または Singleton として登録してください。未使用の受け手や、並行した初回取得で採用されなかったインスタンスも、DI が生成と破棄を担当します。配送と後始末が終わるまで、その DI scope を保持してください。

依存セットだけで使う型を Mediator の明示的な `IServiceProvider` 経由で解決すると、元のプロバイダーへ委ねます。その結果は Mediator の型別キャッシュに保存せず、Transient なら解決のたびに新しいインスタンスになる場合があります。同じ型を直接使う別ルートがある場合は、その型別の捕捉を保ちます。Scoped と Singleton の同一性は DI の登録に従います。

`EagerDependencies` を明示した場合の取得時期と同一性は保ちます。同期 Before だけのルートの受け手を指定すると、そのルート全体は従来の型別捕捉を使い、DI から注入されたインスタンスを再利用します。指定したサービスは Mediator の解決時に取得し、指定していない受け手には従来の取得規則を適用します。指定したサービスのコンストラクタが依存する型は、DI が先に取得する場合があります。関係しないルートは条件を満たせば依存セットを使います。従来対応していなかった非同期 Hook 経路で `EagerDependencies` が使えるようになる変更ではありません。

生成された構成の登録を使ってください。独自のプロバイダーは受け手だけでなく、生成された依存セットも解決できる必要があります。生成される型名やメンバー、配置は内部実装であり、手動で構築するための公開 API ではありません。

Microsoft.Extensions.DependencyInjection の `ValidateOnBuild` は登録された依存セットのコンストラクタが宣言する依存関係を検査できます。ただし、すべての配送経路、DI ファクトリ内部の処理、open generic を閉じた構成や、実行中の操作まで検証するものではありません。`ValidateScopes` も有効にし、実際に使うルートで構築、状態の共有、破棄を確認してください。Singleton の Mediator に Scoped のサービスを捕捉させないでください。

同じ依存または依存セットを並行して解決した呼出しは、公開された同じ捕捉instanceを使います。ある解決の例外はほかの成功結果で隠しません。生成・破棄は標準DIの責任です。[詳細な条件と制約](getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)を参照してください。

Streamの返り値をscopeの外へ持ち出し、scopeを破棄してから列挙を始めないでください。途中で`break`した場合も、`await foreach`による列挙子の後始末が終わるまでscopeを保持します。

`SendAsync`が返す`ValueTask`は例のように1回だけ`await`します。同じ`ValueTask`を保存して複数回消費しないでください。

## 型付きqueryと状態更新commandを同じAppで使う

[GenericLookupサンプル](../samples/Zendiator.Sample.GenericLookup/)では`Lookup<T>`の結果を`T`、`StatusQuery`の結果を`InventoryStatus`で受け取ります。`RefreshInventory`の完了を`await`してからqueryを送り、更新後の状態を取得します。

この例はclassのrequestと、DIが所有するclassのHandlerを使います。これらのHandlerのみの`SendAsync`経路では、空の`Pipeline`を追加する必要はありません。返された`ValueTask`はそれぞれ1回だけ`await`し、非同期処理が完了するまでscopeを保持してください。

## 複数の配送方法を一つのAppで使う

[ComposedAppサンプル](../samples/Zendiator.Sample.ComposedApp/)では型付きquery、更新command、同期Send、SendAllとStreamの7つのrouteを同じAppとDI scopeで使います。`Pipeline`はrouteごとに指定し、Hookの順序とWrapper自身のcache・retry処理を保ちます。

各routeはその配送方法で対応している構成に従います。構成が不適合なrouteが一つでもあれば、App全体の生成を止めます。scoped ref-like requestを含む複数routeの合成は今回の対応範囲外です。

この例はclassのrequestと、DIが所有するclassのHandler・Hook・Wrapperを使います。返された`ValueTask`はそれぞれ1回だけ`await`し、非同期処理とStreamの列挙・後始末が終わるまでscopeを保持してください。

### Handler属性を補って再buildする

CodeFixを使う場合は、`Zendiator`と同じバージョンの`Zendiator.CodeFixes`を`PrivateAssets="all"`で追加し、NuGet.orgからrestoreしてください。現在のVisual Studioでプロジェクトを開き、ZEN0035の修正候補から`Add Handler attribute`を選びます。

```xml
<PackageReference Include="Zendiator.CodeFixes"
                  Version="$(ZendiatorPackageVersion)" PrivateAssets="all" />
```

```powershell
$project = 'samples/Zendiator.Sample.ComposedApp/Zendiator.Sample.ComposedApp.csproj'
dotnet restore $project -p:ZendiatorPackageVersion=$version --source https://api.nuget.org/v3/index.json
```

たとえばComposedAppの`RefreshInventoryHandler`に属性が欠けている場合、候補を一意に特定でき、属性を追加した後のApp全体を生成できるときに修正できます。同じAppのType登録も保持します。変更する利用者コードはHandler属性だけです。曖昧な候補、不正な契約、未採用の構成を推測して直すことはありません。候補が出ない場合は、generatorの読み込みと契約を確認してください。

修正後はComposedAppを再buildします。登録順、wrapperのretryやcache、scopeの境界は手動で移行してください。既存のwrapper本文は意図を確認せずhookへ変換しません。

```powershell
dotnet build $project -c Release --no-restore -p:ZendiatorPackageVersion=$version
```

## 0.4.0から移るとき

0.5.0では、Handlerの属性と明示的な登録を使います。0.4.0のinterface実装やアセンブリ指定だけに依存した登録は、そのまま移せません。

Behaviorの並びは`Pipeline`の順序付きリストで指定します。この例のHookは要求の変換、応答の変換、Streamの項目処理という専用の契約を実装しています。既存Behaviorのretryやcacheなどの意味を、自動でHookへ置き換えるものではありません。継続の呼び出しを制御するBehaviorを移す際は、`Stage.Wrapper<T>()`の契約と対象経路を確認してください。

この例は型引数を持たないclassのrequest、`int`または`string`の応答、`int`のStream、DIが所有するHandlerとHookを使います。ほかの型や経路を網羅する例ではありません。更新前に[リリースノート](release/0.5.0-release-notes.ja.md)と既知の制限を確認してください。

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

`[Handler]`を付けた`Handler<TMarker>`と`ClosedHandler`を用意します。それぞれ`IRequestHandler<Query<TMarker>, int>`と`IRequestHandler<ClosedQuery, int>`を実装し、処理本体と必要なDI依存関係は既存のものを使います。次のWrapperは継続を1回呼び、`finally`で終了を記録します。

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

両方のHandlerを選びます。この例では非ジェネリック要求にも空の`Pipeline`を明示し、Wrapperは`Query<>`だけに適用します。

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

プロジェクト内で定義したHandlerを属性なしで明示登録すると、ZEN0035で登録箇所が示されます。ZEN0035はほかの生成条件の不備にも使います。診断文が属性不足を示している場合は、上の`AddNumbersHandler`のようにクラス宣言へ`[Handler]`を付けてください。

`Zendiator.CodeFixes`は任意で追加するパッケージです。利用する場合は、`Zendiator`と同じ候補バージョンを用意し、プロジェクトの`ZendiatorPackageVersion`をそのバージョンに合わせてください。[導入例](../src/Zendiator.CodeFixes/README.md)のように`PrivateAssets="all"`を指定します。NuGet.orgで公開されるまでは、同じバージョンのローカルビルド済みpackageを使ってください。

CodeFixの対象は属性不足と判断できるZEN0035だけです。Fix Allの対象は現在の文書だけです。

現行のVisual Studioで利用できます。
