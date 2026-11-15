# 0.6.0の使い方

[English](getting-started-0.5.0.md)

0.6.0のpackageで同期処理・非同期処理・Streamを使う例です。移行時は[リリースノート](release/0.6.0-release-notes.ja.md)を確認してください。

## 準備と実行

.NET 10 SDKとC# 14を使います。NuGet.orgから0.6.0のpackageを取得します。

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
using System.Runtime.CompilerServices;
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
            numbers.Add(number);

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
public sealed class ReadNumbersHandler : IStreamHandler<ReadNumbers, int>
{
    public async IAsyncEnumerable<int> Handle(ReadNumbers request,
        [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        for (var index = 0; index < request.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return index + 1;
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
| `DispatchFamily.Stream` | `StreamAsync`を`await foreach`で列挙 | 偶数を選び、10倍し、40以下を残す |

Send系の`Before`はリストの先頭から、`After`は末尾から呼びます。この例では`TrimName.Before`、Handler、`AddExclamation.After`の順に動きます。Streamの各項目は`KeepEven`、`TimesTen`、`KeepAtMostForty`の順に処理します。

`[ZendiatorComposition]`を付けた`App`から、`GettingStarted.Generated.App`名前空間の登録メソッドと呼び出しメソッドを生成します。属性の文字列はcompositionの識別子です。

## 例外と取消

`Send`の例外は呼出し元へ伝わります。Hookがないか同期Beforeだけを使い、factory・Wrapper・sessionを使わないplain `SendAsync`では、依存解決と同期callbackが呼出し中に動きます。その例外や取消は`ValueTask`が返る前に同期throwとなることがあります。同期部分で行った`AsyncLocal`、`CultureInfo`、`SynchronizationContext`の変更も呼出し側に見えます。返された`ValueTask`の非同期の失敗は`await`で受け取ります。`try`には呼出しと待機を含めてください。

この例のStreamでは`await foreach`で最初の項目を要求するとHandlerやHookが動きます。`request`がnullの場合は入口の呼出し時に例外が出るため、これらの失敗を扱う`try`には`StreamAsync`の呼出しと列挙を含めてください。

取消tokenは各入口の`CancellationToken`引数で渡します。この例で取消が検出されると、`OperationCanceledException`が伝わります。この例のStreamでは列挙時のtokenも受け取り、呼出し時・列挙時のどちらの取消も反映します。既に取消済みの場合も、取消の検出は最初の項目を要求したときです。

## Streamの開始と再列挙

`ReadNumbersHandler`は`IStreamHandler<ReadNumbers, int>`を実装し、コンパイラが生成する非同期Iteratorを返します。`StreamAsync`が返す標準の`IAsyncEnumerable<int>`を`await foreach`で列挙し、Filter・MapのHookを順に適用して結果を集めます。この例はHookが3段あるため、ガード付きの経路を使います。`StreamAsync`の呼出しや列挙子の取得だけでは利用者の依存解決や入力処理を始めず、列挙子の最初のMoveで開始します。標準のsource objectは`StreamAsync`の呼出し時に割り当てます。

pending Moveの完了前に別のMoveやDisposeを始めると拒否します。Moveを一度awaitしてからDisposeし、scopeは後始末まで保持してください。Filterがfalseを返すと、同じMove内で次の項目へ進みます。`await foreach`は全件の列挙、失敗、`break`による早期終了のいずれでも、列挙子の破棄を待ちます。

名前付きcursorの例は[ComposedAppサンプル](../samples/Zendiator.Sample.ComposedApp/)を参照してください。任意のsink終端は[標準列挙とtyped terminal](streaming-0.5.0.ja.md#標準列挙とtyped-terminal)で説明しています。`StreamSource(...).ConsumeAsync(...)`のreadonly計画値は`IAsyncEnumerable<T>`ではありません。値型のsinkは値渡しになるため、結果は共有する参照先に保持します。

Current、pending Dispose、取消し、session所有権、context、HTTPと移行条件は[Streamの移行・実行契約](streaming-0.5.0.ja.md)を参照してください。この例は時間や割当の優位を保証しません。標準列挙、typed terminal、HTTPは対象のバージョン・条件・実測結果で別々に評価してください。

## DI scopeの所有

標準DIがこの例のHandlerとHookの生成・破棄を受け持ちます。呼び出し側はscopeを作り、そのscopeから`IZendiator`を取得します。非同期処理の完了とStreamの列挙終了までscopeを保持し、その後に破棄します。HandlerやHookを呼び出し側で個別に破棄する必要はありません。

生成登録の既定は、`IZendiator`がScoped、Handler・Hook・session factoryがTransientです。通常は各Mediatorが依存の型ごとに最初に公開された成功結果を捕捉し、その後の呼出しでも使います。

非同期Hookの実行経路を使う閉じた`SendAsync` routeでは、借用するHandlerと全Hookが参照型で、Zendiatorのsession/factory契約やWrapperを使わず、異なるreceiver型が2個以上ある場合、route単位の依存セットを捕捉します。このセットは初回dispatchの取消確認後、既存の非同期実行内で最初のHookより前に取得します。外側Hookの失敗で未使用になるHandlerも含め、DIが全receiverを取得します。Transientのreceiverは、ほかのrouteやMediatorの明示的な`IServiceProvider`経由で取得するものと異なる場合があります。

同じ依存または依存セットを並行して解決した呼出しは、公開された同じ捕捉instanceを使います。ある解決の例外はほかの成功結果で隠しません。生成・破棄は標準DIの責任です。[詳細な条件と制約](getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)を参照してください。

Streamの返り値をscopeの外へ持ち出し、scopeを破棄してから列挙を始めないでください。途中で`break`した場合も、`await foreach`による列挙子の後始末が終わるまでscopeを保持します。

`SendAsync`が返す`ValueTask`は例のように1回だけ`await`します。同じ`ValueTask`を保存して複数回消費しないでください。

## 閉じた依存の保持領域

閉じた依存の保持領域はcompositionの大きさによらず既定でインラインに置きます。この設定が変えるのは保持領域だけです。依存とroute単位の依存セットは最初の使用時に取得し、Mediatorは最初に公開された成功結果を再利用します。破棄はDIが担当します。条件を満たすrouteの依存セットでは、上で説明したとおりreceiverをまとめて取得します。

短命のscopeで一部の依存だけを使うcompositionでは、markerにlazy blocksを指定できます。

```csharp
[ZendiatorComposition("reads", ClosedServiceCache = ClosedServiceCacheMode.LazyBlocks)]
public sealed class Reads;
```

`ClosedServiceCacheMode.Inline`は既定のインライン保持を明示します。`LazyBlocks`は閉じた依存を最大16個ずつのblockに分け、block内の依存を標準DIから初めて取得できた後に、そのblockを割り当てます。小さなcompositionにも適用され、依存が1〜16個なら1個のblockを使い、依存がなければblockを作りません。保持方法は生成時に決まり、Sendに保持方法を選ぶ分岐は追加しません。定義されていない列挙値はZEN0035になります。

以前は依存の数だけでblockを選んでいたcompositionも、現在はインライン保持になります。処理の条件に合う場合に`LazyBlocks`を明示してください。この設定を追加する前に、Windows x64で64個の依存を使い、lazy blocksとインライン保持を比較した測定があります。初回に1個のblockだけを使う疎なケースでは、操作ごとの割当量が336 B減りました。63回のSendで多くの依存を使うケースでは96 B増え、初期化後に少数の依存へ配送するケースでは平均時間が29.2%増えました（+0.258 ns）。使う依存が複数のblockに分散する場合や、初期化後の呼出しを繰り返す場合にも改善するという保証はありません。blockの大きさがあらゆる処理の条件に最適ということでもありません。

open genericの依存はこの設定とは別のcacheに保持します。閉じた型を4種類まで保持し、それを超えるとdictionaryへ移します。以前のdictionary保持との比較では、1種類の型を使う初回scopeの割当量が488 B減りました。5種類目の型でdictionaryへ移す初回scopeでは160 B増え、平均時間は8.35%増えました。4種類という境界が常に最適なわけではなく、これらの結果はすべてのscopeでの改善を示しません。

## 型付きqueryと状態更新commandを同じAppで使う

[GenericLookupサンプル](../samples/Zendiator.Sample.GenericLookup/)では`Lookup<T>`の結果を`T`、`StatusQuery`の結果を`InventoryStatus`で受け取ります。`RefreshInventory`の完了を`await`してからqueryを送り、更新後の状態を取得します。

この例はclassのrequestと、DIが所有するclassのHandlerを使います。これらのHandlerのみの`SendAsync`経路では、空の`Pipeline`を追加する必要はありません。返された`ValueTask`はそれぞれ1回だけ`await`し、非同期処理が完了するまでscopeを保持してください。

## 複数の配送方法を一つのAppで使う

[ComposedAppサンプル](../samples/Zendiator.Sample.ComposedApp/)では型付きquery、更新command、同期Send、SendAllとStreamの7つのrouteを同じAppとDI scopeで使います。`Pipeline`はrouteごとに指定し、Hookの順序とWrapper自身のcache・retry処理を保ちます。

最初の`CachedCount<Product>("current")`は在庫を読み、Handlerが意図的に1回だけ投げる`TransientCountException`を受けて、Wrapperの1回のretryで成功します。2回目はHandlerを呼ばずにcacheの値を返します。在庫の読み取り回数はどちらの時点でも`2`です。この回数は、後続のSendAllが同じ在庫を読む前に記録します。意図的な失敗はこのHandlerだけで発生します。取消済みの場合はWrapperがretryしません。

Streamの入力は名前付きcursorが返す`int`です。`KeepEven`で絞り、`TimesTen`で10倍し、`KeepAtMostForty`で絞り、最後に`AsText`で`int`から`string`へ変換します。`StreamPipeline<ReadNumbers, string>`は最終型を宣言します。Handlerが整数を返すため、`ReadNumbers`は`IStreamRequest<int>`のままです。生成される`StreamAsync`の戻り値は`IAsyncEnumerable<string>`になります。

リポジトリのルートから、packageを使う例を実行します。

```powershell
$version = '0.6.0'
$project = 'samples/Zendiator.Sample.ComposedApp/Zendiator.Sample.ComposedApp.csproj'

dotnet restore $project -p:ZendiatorPackageVersion=$version --source https://api.nuget.org/v3/index.json
dotnet run --project $project -c Release --no-restore -p:ZendiatorPackageVersion=$version
```

期待する出力は次のとおりです。

```text
Product: Keyboard
Sum: 42
Cached count: 3
Read attempts after retry: 2
Cache hit: 3
Read attempts after cache hit: 2
SendAll: 15, 16
SendAllAsync: 25, 26
StreamAsync: number=20, number=40
```

各routeはその配送方法で対応している構成に従います。構成が不適合なrouteが一つでもあれば、App全体の生成を止めます。scoped ref-like requestを含む複数routeの合成は今回の対応範囲外です。

この例はclassのrequestと、DIが所有するclassのHandler・Hook・Wrapperを使います。返された`ValueTask`はそれぞれ1回だけ`await`し、非同期処理とStreamの列挙・後始末が終わるまでscopeを保持してください。

### 目的から既存の例を選ぶ

| 目的 | 例と契約 |
|---|---|
| 要求を変換し、結果を観測・変換する | [GettingStarted](../samples/Zendiator.Sample.GettingStarted/Program.cs)の`TrimName`と`AddExclamation`は専用のSend Hookを使う |
| retry・cache・short-circuitを行う | [ComposedApp](../samples/Zendiator.Sample.ComposedApp/Program.cs)の`CountCache`が継続を呼ばずに終了したり、複数回呼んだりする。`CachedCountHandler`が意図的な1回の失敗を起こす |
| Streamを絞り、出力型を変える | ComposedAppの順序付きFilter・Map Hookと`StreamPipeline<ReadNumbers, string>`を使う |
| moduleで公開するrouteを限定する | [Sample.Application](../samples/Zendiator.Sample.Application/DependencyInjection.cs)の`ApplicationStreams`は宣言したStream HandlerとPipelineだけを選ぶ |
| 入力契約を選ぶ | GettingStartedはコンパイラ生成のIterator、ComposedAppは名前付きcursorを使う。[入力とLifetimeの選び方](streaming-iterators.md#choose-an-input-contract)も参照する |

いずれの例も生成された`IZendiator`を呼び出します。Hookは専用の処理を表し、継続を独自に制御する処理はWrapperに残します。借用するHandlerと段はDIが所有し、各例では完了と破棄までscopeを維持します。サンプルの性能は保証していません。

### 生成された構成を調べる

上の`$project`と`$version`を使い、コンパイラの生成ファイルをプロジェクトの`obj`内へ出力します。

```powershell
dotnet build $project -c Release --no-restore -p:ZendiatorPackageVersion=$version -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=obj/generated
```

プロジェクトの`obj/generated`以下を再帰的に調べ、今回のbuildで生成したファイルを確認します。固定のhashではなく、`ComposedApp.Generated.App`やHandler・段の型名を検索してください。`obj`には古いbuildの出力が残る場合があるため、今実行したbuildのファイルであることを確かめます。

| 生成ファイル名の規則 | 確認する内容 |
|---|---|
| `Zendiator.Composition.*.g.cs` | 選んだ`Handlers`、`Pipeline`／`StreamPipeline`の順序、DI登録と捕捉するサービスの保持領域 |
| `Zendiator.Route.*.g.cs` | 生成された呼び出し側の要求別overloadと、Streamの最終的な項目型 |
| `Zendiator.StreamCanonical.*.g.cs` | cursorの所有と、順序付きのStream項目変換 |

生成された`Service_`、`GenericService`、`RouteDependencies`のmemberがあれば、そこから捕捉するreceiverを調べ、DI登録と[捕捉規則](#di-scopeの所有)を照らします。コンストラクターの依存と破棄は引き続き標準DIが所有します。これらのファイル名と生成memberは、調べるための内部実装であり、呼び出したり保持したりするためのAPIではありません。

### Handler属性を補って再buildする

CodeFixを使う場合は、`Zendiator`と同じバージョンの`Zendiator.CodeFixes`を`PrivateAssets="all"`で追加し、NuGet.orgからrestoreしてください。現在のVisual Studioでプロジェクトを開き、ZEN0035の修正候補から`Add Handler attribute`を選びます。

```xml
<PackageReference Include="Zendiator.CodeFixes"
                  Version="$(ZendiatorPackageVersion)" PrivateAssets="all" />
```

```powershell
$version = '0.6.0'
$project = 'samples/Zendiator.Sample.ComposedApp/Zendiator.Sample.ComposedApp.csproj'
dotnet restore $project -p:ZendiatorPackageVersion=$version --source https://api.nuget.org/v3/index.json
```

たとえばComposedAppの`RefreshInventoryHandler`に属性が欠けている場合、候補を一意に特定でき、属性を追加した後のApp全体を生成できるときに修正できます。同じAppのType登録も保持します。変更する利用者コードはHandler属性だけです。曖昧な候補、不正な契約、未採用の構成を推測して直すことはありません。候補が出ない場合は、generatorの読み込みと契約を確認してください。

修正後はComposedAppを再buildします。登録順、wrapperのretryやcache、scopeの境界は手動で移行してください。既存のwrapper本文は意図を確認せずhookへ変換しません。

```powershell
dotnet build $project -c Release --no-restore -p:ZendiatorPackageVersion=$version
```

## 0.4.0から移るとき

0.6.0ではHandlerの属性と明示的な登録を使います。0.4.0のinterface実装やアセンブリ指定だけに依存した登録は、そのまま移せません。

Behaviorの並びは`Pipeline`の順序付きリストで指定します。この例のHookは要求の変換、応答の変換、Streamの項目処理という専用の契約を実装しています。既存Behaviorのretryやcacheなどの意味を、自動でHookへ置き換えるものではありません。継続の呼び出しを制御するBehaviorを移す際は、`Stage.Wrapper<T>()`の契約と対象経路を確認してください。

この例は型引数を持たないclassのrequest、`int`または`string`の応答、`int`のStream、DIが所有するHandlerとHookを使います。ほかの型や経路を網羅する例ではありません。更新前に[リリースノート](release/0.6.0-release-notes.ja.md)と[既知の制限](release/known-limitations.md)を確認してください。

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

`Zendiator.CodeFixes`は任意で追加するパッケージです。利用する場合は、`Zendiator`と同じ0.6.0をNuGet.orgから取得し、プロジェクトの`ZendiatorPackageVersion`を`0.6.0`に合わせてください。[導入例](../src/Zendiator.CodeFixes/README.md)のように`PrivateAssets="all"`を指定します。

CodeFixの対象は属性不足と判断できるZEN0035だけです。Fix Allの対象は現在の文書だけです。

現行のVisual Studioで利用できます。
