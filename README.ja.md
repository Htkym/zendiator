# Zendiator

[English](README.md)

コンパイル時に型付きディスパッチを生成する、.NET 10 向けの小さな Mediator です。
Roslyn Incremental Source Generator がリクエストごとの `SendAsync` オーバーロード、
構造体の継続ノード、DI 登録を生成します。型付きの要求配送に、実行時のアセンブリ走査、
リフレクション呼び出し、`dynamic` は使いません。

- 対象: .NET 10（C# 14、nullable 有効）
- 配布: `Zendiator.Abstractions` と `Zendiator` の 2 パッケージ（同バージョン管理）
- 状態: V1 前のプレビュー。破壊的変更を許容します
- リポジトリ: https://github.com/Htkym/zendiator
- ライセンス: MIT

## インストール

```shell
dotnet add package Zendiator --version 0.4.0
```

`Zendiator` は Abstractions への依存と Source Generator を含みます。
契約だけを置くプロジェクトは `Zendiator.Abstractions` のみを参照できます。
アプリケーションで使うパッケージのバージョンは固定し、両パッケージでそろえてください。

このREADMEは0.4.0の利用方法を説明しています。更新時のAPIと移行上の変更は、
[リリースノート](docs/release/0.4.0-release-notes.ja.md)を参照してください。

役割分担の目安です。

| プロジェクト | 参照 |
|---|---|
| Contracts（メッセージ定義） | `Zendiator.Abstractions` のみ |
| Application（ハンドラー・Behavior・DI 構成） | `Zendiator`（Abstractions は推移的に参照） |
| Host（起動側・構成用） | Application（`AddApplication()` 形式のラッパーを呼ぶ） |

構成はハンドラー側から逆参照を作らない場所に置きます。Roslyn は実行時依存になりません。Generator 自体は `Zendiator` パッケージに `analyzers/dotnet/cs` として同梱されます。

## 使い方

現在のコンパイルに対する登録は、次の呼び出し 1 つで行えます。
別の初期化や独自 Provider は不要です。

```csharp
using Zendiator.DependencyInjection;

services.AddZendiator();
```

ホストは通常の `builder.Build()`、DI コンテナは標準の構築方法を使います。
別アセンブリ、Behavior、生成先 namespace を指定する場合は、後述の構成ラムダを使います。

メッセージとハンドラーを定義します。`class`、`record`、`struct`、`record struct` を使えます。
応答には利用側の `Result` 型や null 許容型も使えます。

```csharp
using Zendiator;

public sealed record MemorialTargetDto(int Year, IReadOnlyList<string> Names);

public readonly record struct GetTargetYearQuery(int Year) : IQuery<MemorialTargetDto>;

public sealed class GetTargetYearQueryHandler : IQueryHandler<GetTargetYearQuery, MemorialTargetDto>
{
    public ValueTask<MemorialTargetDto> HandleAsync(GetTargetYearQuery query, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new(new MemorialTargetDto(query.Year, [$"Household-{query.Year}-1"]));
    }
}
```

複数プロジェクトのアプリケーションでは、構成ルートから設定します。
属性や空の Mediator クラスは要りません。

```csharp
using Microsoft.Extensions.DependencyInjection;
using Zendiator.DependencyInjection;

namespace MyApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddZendiator(static configuration =>
        {
            configuration.Namespace = "MyApp.Application.Generated";
            configuration.RegisterServicesFromAssemblyContaining<ApplicationAssemblyMarker>();
            configuration.RegisterServicesFromAssemblyContaining<ContractsAssemblyMarker>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
        });

        return services;
    }
}
```

```csharp
services.AddApplication();
```

- `IZendiator` は設定した namespace に生成されます。
- `SendAsync` はリクエスト型に対応するオーバーロードを生成します。対応するジェネリック要求の形も含みます。
  `IRequest<T>` や `object` を受ける汎用送信 API はありません。
  派生契約（`ICommand<T>` など）で宣言した変数からの送信はできません。静的な型が具体型である呼び出しだけが対象です。
- 属性による構成（`[GenerateZendiator]` のクラス／アセンブリ属性）は
  別の宣言方法として利用できます。同一コンパイルで DI 構成ラムダとは併用できません。

呼び出し側です。

```csharp
using MyApp.Application.Generated;

var services = new ServiceCollection();
services.AddApplication();
await using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var scope = provider.CreateAsyncScope();
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var result = await zendiator.SendAsync(new GetTargetYearQuery(2026));
```

`services.AddZendiator()` は `TryAdd` で登録します。事前登録があれば置き換えません。
事前登録の有効期間は維持しますが、Mediator は `Transient` を含めて最初に取得した Handler・Behavior を自身の有効期間中に再利用します。
登録を繰り返しても重複しません。Mediator は `IZendiator` から `Zendiator` への型登録を
1 つ持ち、具体型の `Zendiator` は別途登録しません。解決・注入には `IZendiator` を使います。
生成方法を差し替える場合は、Factory の登録先を `Zendiator` から `IZendiator` に変えてください。

```csharp
services.AddScoped<IZendiator>(provider => new Zendiator(provider));
services.AddZendiator();
```

直接 `new Zendiator(provider)` で生成する方法も引き続き使えます。
具体型を個別に登録しても、`IZendiator` の生成方法は変わりません。

## ストリーミング

ストリーム要求とハンドラーを定義します。1 つの要求には 1 つのハンドラーが対応します。

```csharp
public sealed record GetHouseholdNames(int Count) : IStreamRequest<string>;

public sealed class GetHouseholdNamesHandler : IStreamRequestHandler<GetHouseholdNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(
        GetHouseholdNames request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        for (var i = 0; i < request.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return $"Household-{i}";
        }
    }
}
```

通常の Behavior と並べてストリーム用の Behavior を登録します。

```csharp
configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
configuration.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>), order: 2);
```

軽い同期の入力検証には、Validator の閉じた具象型を明示的に登録します。

```csharp
public sealed class HouseholdNameValidator : IStreamRequestValidator<GetHouseholdNames>
{
    public void Validate(GetHouseholdNames request) => ArgumentOutOfRangeException.ThrowIfNegative(request.Count);
}

configuration.AddStreamRequestValidator(typeof(HouseholdNameValidator), order: 0);
```

Validator はアセンブリ走査では自動登録されません。型はアクセス可能で抽象でない、閉じたクラスに限ります。実装するすべての `IStreamRequestValidator<TRequest>` が、生成対象の閉じた Stream 経路と要求型まで完全に一致する必要があります。オープンジェネリックの Validator や基底要求型への一致には対応していません。登録型と Validator の order は構成内で重複できず、Behavior の order とは独立しています。

検証は `StreamAsync` を呼び出すたびに order の昇順で一度だけ同期実行され、成功してから列挙用オブジェクトを返します。同じ列挙用オブジェクトの列挙では再検証しません。Validator がある参照型要求の null は呼び出し時に拒否し、Validator がない経路の null 検査は従来どおり最初の `MoveNextAsync` で行います。キャンセルの検査は列挙時に行うため、取り消し済みの API トークンでも入口の検証は実行されます。検証が失敗すると、後続の Validator とパイプラインの依存解決を止めます。

属性方式では、生成 Mediator またはアセンブリに `[StreamRequestValidator(typeof(HouseholdNameValidator), Order = 0)]` を付けます。構成ラムダとの併用はできません。Validator は入力だけを検証し、並行呼び出しに対応させてください。非同期処理や I/O はハンドラーまたは Stream Behavior に置いてください。検証した入力を列挙終了まで変更せず、DI スコープも維持してください。

取得した Validator は Transient 登録でも Mediator 内で再利用されます。同じ型がハンドラーや Behavior でもある場合は、入口でそのインスタンスを構築することがあります。パイプラインのメソッドは列挙時に実行されます。[既知の制限](docs/release/known-limitations.md)も参照してください。

列挙は遅延実行です。ハンドラーは `StreamAsync` 呼び出し時ではなく、最初の `MoveNextAsync` で開始します。API トークンと `WithCancellation` のどちらでも取り消しできます。両方が取り消し可能かつ異なるトークンである場合のみ連結します。

初回の初期化に失敗した列挙器は再開せず、それ以降の `MoveNextAsync()` は `false` を返します。失敗した場合も `await using` などで破棄してください。再試行するときは `StreamAsync` から新しい列挙を作ります。

```csharp
await foreach (var name in zendiator.StreamAsync(new GetHouseholdNames(3), cancellationToken))
{
    Console.WriteLine(name);
}
```

`DisposeAsync()` が返す `ValueTask` は一度だけ消費してください。通常の `await using` はこの条件を満たします。プール化された終了処理では、完了後も返値を消費するまで列挙器への参照が保持されることがあります。

`ref struct` の要求は同期契約（`ISyncRequest` ＋ `SendSync`）を使います。非同期経路とストリーム要素は診断します（ZEN0012）。再列挙は保証しないため、新しい列挙には `StreamAsync` を呼び直します。値型で閉じたオープンジェネリックは Native AOT では動作しません（[既知の制限](docs/release/known-limitations.md)）。

## 同期の複数結果

`ISyncMultiRequest<T>` は `SendAllSync(request, cancellationToken)` で複数のハンドラーの結果を順番に返します。
戻り値は `IReadOnlyList<T>` です。具体的なコレクション型へのキャストには依存しないでください。

結果を保持するバッファがある場合は、`Span<T>` を受け取るオーバーロードを使えます。
次は `GetValues` に `int` を返すハンドラーが 2 つ登録されている場合の例です。

```csharp
Span<int> results = stackalloc int[2];
int written = zendiator.SendAllSync(new GetValues(), results, cancellationToken);
// results[..written] に、ハンドラー順の結果が入ります。
```

このオーバーロードは 3 引数とも必須です。要求の null、事前キャンセル、バッファ容量の順に確認し、
容量不足の場合はハンドラーや依存サービスを取得する前に `ArgumentException` を送出します。
全ハンドラーが成功してから結果を書き込み、余った領域には触れません。
途中で失敗した場合も dispatcher はバッファに書き込みませんが、ハンドラー自身の副作用は取り消しません。
入力の `ReadOnlySpan<T>` と出力領域が重なっていても、結果の書き込みは全ハンドラーの実行後です。

参照型の結果には、呼び出し元で保持する配列の Span を渡してください。
既存バッファを使うことで結果コンテナーの確保を省けますが、初回の DI 解決やハンドラー自身の確保は別です。
非同期の `SendAllAsync` に Span を渡す API はありません。

## ライフタイム

既定は Scoped です。登録ごとに Singleton と Transient も選べます。

```csharp
services.AddZendiator(static configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetTargetYearQueryHandler>();
    configuration.ServiceLifetime = ServiceLifetime.Singleton;
});
```

規則です。

- `ServiceLifetime` の既定は Scoped です。この値は実行時に流れるだけで、
  生成構造は変わらないため、Provider ごとに変えられます。
- `ServiceLifetime` は Mediator の有効期間です。Mediator が Scoped の場合、生成される
  ハンドラーと Behavior は既定で Transient として登録されます。ただし、同じ Mediator は
  初回に取得したインスタンスを再利用します。Singleton と Transient の Mediator では、
  生成される依存も指定した有効期間で登録されます。既存登録の有効期間は変わりません。
- 同じ Scope 内の別の利用者ともインスタンスを共有する場合は、
  `configuration.DependencyLifetime = ServiceLifetime.Scoped` を指定するか、
  対象の依存を `AddZendiator()` より前に Scoped で登録してください。
  スコープ検証で Singleton が Scoped の依存を保持していないことを確認してください。
- 先に行われた登録が優先されます。後からの登録によって既存の登録が置き換わることはありません。
- 範囲外の値は、定数なら生成時（ZEN0018）に診断されます。
- Handler・Behavior は初めて必要になったときに取得し、同一 Mediator インスタンス内で再利用します。Transient も同じです。既定の Scoped Mediator で新しい構成が必要なら新しいスコープを作り、解決するたびに新しい構成が必要なら Mediator を Transient に設定します。
  早期終了での未構築保証（後続 Behavior とハンドラーを解決しない）も変わりません。

Singleton は、ハンドラー、Behavior、その依存サービスを並行した呼び出しで安全に共有できる場合に使います。
リクエストのスコープに依存するサービスには Scoped を使ってください。

## Behavior

Behavior は DI で解決する `class`、継続処理は生成した `readonly struct` です。
継続をデリゲートやインターフェース変数に変換しません。

```csharp
public interface IRequestContinuation<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> InvokeAsync(TRequest request, CancellationToken cancellationToken);
}

public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    ValueTask<TResponse> HandleAsync<TNext>(
        TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>;
}
```

規則です。

- `Order` の値が小さい Behavior ほど外側に適用されます。
- 同じ Behavior 型や同じ `Order` 値の重複はエラー（ZEN0004）です。
- 閉じた Behavior と `Behavior<TRequest, TResponse>` の 2 引数オープン Behavior に対応します。
  オープン Behavior は制約（`struct`、`class`、`notnull`、`new()`、基底・インターフェイス、
  入れ子のジェネリックを含む）を満たすリクエストにだけ適用されます。
- 早期終了では後続 Behavior とハンドラーを解決しません。未構築のハンドラーを参照しても作られません。
- リクエストと `CancellationToken` を差し替えて次へ渡せます。
- 逐次複数回呼び出しによる再試行ができます。並列呼び出しや処理終了後の保持は対象外です。
- null の参照型リクエストは拒否し、各ノードの実行前にキャンセルを確認します。例外は変換しません。

実行例は `samples/` にあります。Contracts・Application・Host の 3 プロジェクト構成で、
ログ Behavior と利用者独自の `Result<T, E>` による成功・失敗の扱いを確認できます。

```powershell
dotnet run --project samples/Zendiator.Sample.Host -c Release
```

## 複数アセンブリ

現在のコンパイルと構成で指定したアセンブリだけを Roslyn シンボルで調べます
（`RegisterServicesFromAssemblyContaining<T>()`、`typeof(X).Assembly` 形式）。
アセンブリ全体の走査とは別に、ハンドラー契約が参照する正確なリクエスト型も取り込みます。
そのため、リクエスト型の入ったアセンブリを走査対象に入れ忘れても、ハンドラーと同じか
ハンドラーから到達できるリクエストは経路になります。意図しない取り込みを避けたい場合は、
ハンドラーの配置とアセンブリ登録の指定を見直してください。
記述が違っても同じアセンブリ集合になる構成は、同じ生成単位を共有します。

## 診断

| ID | 条件 |
|---|---|
| ZEN0001 | 対象リクエストのハンドラーがない |
| ZEN0002 | 単一要求に複数のハンドラー実装がある |
| ZEN0003 | 複数応答契約、未対応の型（非公開など）、アクセス不能、応答の不一致 |
| ZEN0004 | Behavior の契約不一致、重複、順序競合、どの経路にも一致しない閉じた Behavior |
| ZEN0005 | 生成先の名前・宣言不備、重複、メンバー衝突、予約衝突 |
| ZEN0006 | 生成方式の競合（クラス属性＋アセンブリ属性） |
| ZEN0007 | 生成 namespace の不正・生成名の衝突 |
| ZEN0008 | 種類の競合（NativeVoid／Unit、単一／複数、同期／非同期、応答／応答なし複数） |
| ZEN0009 | 要求から推論できないジェネリック束縛 |
| ZEN0010 | 閉じた／オープン束縛の曖昧さ（単一路。複数配送はファンアウトする） |
| ZEN0011 | 要求・ハンドラー・Behavior 間の制約不整合 |
| ZEN0012 | 不正な ref 経路（非同期での boxing、ref 応答、同期への誘導） |
| ZEN0013 | 不正な購読者・登録対象 |
| ZEN0014 | 予約（未発行。通知型消去経路は対応済み） |
| ZEN0015 | 属性構成と DI 構成ラムダの併用 |
| ZEN0016 | 構造の異なる複数の DI 構成 |
| ZEN0017 | 未対応の構成式・コールバック形状 |
| ZEN0018 | 不正な構成値（namespace、重複、lifetime 範囲、順序競合） |
| ZEN0019 | 曖昧な登録束縛（予約） |
| ZEN0020 | 生成登録へ接続できない `AddZendiator` 呼び出し |
| ZEN0021 | 生成 Mediator を明示的に `IDisposable` として扱う |
| ZEN0022 | `using` スコープから取得した Mediator をそのメソッドから返す |
| ZEN0023 | 同じメソッド内でスコープを明示的に破棄した後に送信する |
| ZEN0024 | 閉じた Stream Validator の型が不正、または契約に一致する経路がない |

有効期間を調べる解析器の ZEN0021～ZEN0023 は警告です。静的に確定できる形だけを検出し、スコープの安全性を網羅的に証明するものではありません。修正方法は[移行ガイド](docs/migrating-from-mediatr.ja.md)を参照してください。

## 公開API

Source Generatorが、設定した経路ごとの具体的なオーバーロードを生成します。生成 `IZendiator` に、具体的な要求型の変数を渡します。

|契約|生成される操作|
|---|---|
|`IRequest<T>`、`ICommand<T>`、`IQuery<T>`|`SendAsync(request, cancellationToken)`|
|`IRequest`、`ICommand`|`ValueTask` を返す `SendAsync`|
|`ISyncRequest<T>`、`ISyncRequest`、`ISyncCommand`|`SendSync(request, cancellationToken)`|
|`IMultiRequest<T>`、`IMultiRequest`|`SendAllAsync(request, cancellationToken)`|
|`ISyncMultiRequest<T>`、`ISyncMultiRequest`|`SendAllSync(request, cancellationToken)`|
|`INotification`|逐次の `PublishAsync`。`Publish` も `ValueTask` を返す別名|
|`IStreamRequest<T>`|`IAsyncEnumerable<T>` を返す `StreamAsync`|

要求とStreamは、任意のruntime objectや契約インターフェース型だけの変数から送信できません。Notificationの型消去は別の対応経路です。アセンブリ、Behaviorの順序、有効期間は `AddZendiator` で設定します。属性による宣言も使えますが、同じコンパイル単位で設定lambdaと併用しません。本体とGeneratorのパッケージ版は揃えてください。

## 性能比較

取得済みの入口、初回取得、Scopeの作成、Streamの消費を分けて比べた観測値です。時間と管理ヒープの割り当ては別の指標として選定に使ってください。

|条件|Zendiator Mean ns|B/op|比較先|Mean ns|B/op|
|---|---:|---:|---|---:|---:|
|既存Scopeで入口を再取得してSend・Behavior 5|32.25|0|Immediate|46.07|0|
|新Scope＋初回取得＋Send＋破棄・Behavior 0|79.43|376|Immediate|96.47|368|
|新Scope＋初回取得＋Send＋破棄・Behavior 5|169.08|568|Immediate|121.61|568|
|事前作成Scopeで初回取得＋Send・Behavior 5|807.98|440|Immediate|605.65|440|
|同期Notification・購読者16|121.77|0|DispatchR|175.40|0|
|同期Stream・全1024項目・Behavior 0|14,190.35|216|Immediate|14,222.54|144|
|同期Stream・16項目の途中終了・Behavior 0|62.57|216|Immediate|42.84|144|
|同期Relay・全1024項目・前処理5段|15,028.75|216|DispatchR|13,610.87|144|

上表の既存ScopeのSendはImmediateより平均時間が低い一方、Behaviorを含む初回取得や新Scopeで1回送る条件は高い値でした。Stream0の全1024項目では時間差が約0.23%と小さく、順位を確定できません。割り当てはZendiatorが72 B多く、途中で列挙を終える条件やRelayにも不利な例があります。

2026-10-08にソース版 `1c41d2b105073d2dc9be2c0e8684fdce2fa11f55` を測定しました。Windows 11 x64、Intel Core Ultra 7 258V、SDK 10.0.401、.NET 10.0.12、BenchmarkDotNet 0.15.8、Release、affinity mask 1、warmup20、measurement12、要求iteration 500 ms、各ケース1 launchです。表の比較先はImmediate.Handlers 4.2.0とDispatchR.Mediator 2.3.1、DIは10.0.12です。

MeanはBDNのoverhead補正と外れ値処理後の平均で、Nは9〜12でした。[丸めていない抜粋CSV](benchmarks/results/20261008-1c41d2b-summary.csv)にはMean、信頼区間の半幅であるError、StdDev、N、割り当てを別列で載せています。全体比較は74比較キー・全360件です。1 launchの比較から、独立実行での順位再現、全条件の非劣化、変更前後の因果的な改善は認定しません。

Handlerは軽い処理で、ライブラリ間のAPI形状とDI登録も同一ではありません。ScopeK1はScopeの生成・破棄を含み、FirstSendは含みません。StreamとRelayは1項目でなく、列挙・破棄を含む操作全体の値です。Relayの前処理は同期的に実行し、次のEnumerableを直接返します。アプリ起動、HTTP全体や非同期I/Oへの予測には使いません。[ベンチマークの説明](benchmarks/README.ja.md)に実行条件と再現手順があります。

## AOT とトリミング

`IsAotCompatible` を設定し、生成コードはリフレクションを使いません。
AOT・トリミング警告は抑制せず、エラーとして扱います。
完全な Native AOT リンクにはネイティブのビルドツールが必要です。
NuGet パッケージを参照する自分のアプリで `PublishAot` を有効にします。
次のパスは実際のプロジェクトに読み替えてください。

```powershell
dotnet publish MyApp/MyApp.csproj -c Release -r win-x64 -p:PublishAot=true
```

CI はパッケージを参照する consumer で smoke test と Native AOT の行列を検証します。
リポジトリ内の ProjectReference のサンプルだけで、パッケージ利用時の対応を保証しません。
確認項目は [CI ワークフロー](.github/workflows/ci.yml)、各リリースの検証記録と
値型で閉じるオープンジェネリックの境界は [既知の制限](docs/release/known-limitations.md) を参照してください。

## 対応しない操作

並列配信、fire-and-forget、永続化や outbox、要求の `Send(object)`、循環検出、CodeFix、CodeLens、組み込みの `Result` パイプライン変換、組み込みログ、Send の入力検証は提供しません。Stream の同期入力検証は、上記の明示登録した Validator で行います。逐次 `PublishAsync` による通知、`StreamAsync` によるストリーム、通常の `TResponse` 値としての利用者独自 `Result` 型は対応しています。

MediatR からの移行は [移行ガイド](docs/migrating-from-mediatr.ja.md) を参照してください。
