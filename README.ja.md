# Zendiator

[English](README.md)

コンパイル時に型付きディスパッチを生成する、.NET 10 向けの小さな Mediator です。
Roslyn Incremental Source Generator がリクエストごとの `SendAsync` オーバーロード、
構造体の継続ノード、DI 登録を生成します。実行時の型 switch、リフレクション呼び出し、
`dynamic` は使いません。

- 対象: .NET 10（C# 14、nullable 有効）
- 配布: `Zendiator.Abstractions` と `Zendiator` の 2 パッケージ（同バージョン管理）
- 現行版: [0.1.1](docs/release/0.1.1-release-notes.md)
- リポジトリ: https://github.com/Htkym/zendiator
- ライセンス: MIT

## インストール

```xml
<ItemGroup>
  <PackageReference Include="Zendiator.Abstractions" Version="0.1.1" />
  <PackageReference Include="Zendiator" Version="0.1.1" />
</ItemGroup>
```

役割分担の目安です。

| プロジェクト | 参照 |
|---|---|
| Contracts（メッセージ定義） | `Zendiator.Abstractions` のみ |
| Application（ハンドラー・Behavior・DI 構成） | `Zendiator.Abstractions` + `Zendiator`（`AddZendiator` 用） |
| Host（起動側・構成用） | Application（`AddApplication()` 形式のラッパーを呼ぶ） |

構成はハンドラー側から逆参照を作らない場所に置きます。Roslyn は実行時依存になりません。Generator 自体は `Zendiator` パッケージに `analyzers/dotnet/cs` として同梱されます。

## 使い方

メッセージとハンドラーを定義します。`class`、`record`、`struct`、`record struct` を使えます。
応答には利用側の `Result` 型や null 許容型も使えます。

```csharp
using Zendiator;

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

構成ルートから設定します。属性も空クラスも要りません。

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
- `SendAsync` は具体的なリクエスト型ごとのオーバーロードだけを生成します。
  `IRequest<T>` や `object` を受ける汎用送信 API はありません。
  派生契約（`ICommand<T>` など）で宣言した変数からの送信はできません。静的な型が具体型である呼び出しだけが対象です。
- 属性による構成（`[GenerateZendiator]` のクラス／アセンブリ属性）は
  互換経路として残ります。同一コンパイルで DI 構成ラムダとは併用できません。

呼び出し側です。

```csharp
services.AddApplication();
await using var scope = provider.CreateAsyncScope();
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var result = await zendiator.SendAsync(new GetTargetYearQuery(2026));
```

`services.AddZendiator()` は `TryAdd` で登録します。事前登録があれば置き換えません。
`Transient` で事前登録したハンドラーは `Transient` のまま使われます。
登録を繰り返しても重複しません。`IZendiator` は同じ有効期間の `Zendiator` を返します。

## ストリーミング

ストリーム要求とハンドラーを定義します。1要求には1ハンドラーが対応します。

```csharp
public sealed record GetHouseholdNames(int Count) : IStreamRequest<string>;

public sealed class GetHouseholdNamesHandler : IStreamRequestHandler<GetHouseholdNames, string>
{
    public async IAsyncEnumerable<string> HandleAsync(GetHouseholdNames request, CancellationToken ct)
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

通常のBehaviorと並べてstream用Behaviorを登録します。

```csharp
configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
configuration.AddOpenStreamBehavior(typeof(StreamLoggingBehavior<,>), order: 2);
```

列挙は遅延実行です。ハンドラーは`StreamAsync`呼び出し時ではなく、最初の`MoveNextAsync`で開始します。APIトークンと`WithCancellation`のどちらでも取り消しできます。異なる取り消し可能トークンのときだけ連結します。

```csharp
await foreach (var name in zendiator.StreamAsync(new GetHouseholdNames(3), cancellationToken))
{
    Console.WriteLine(name);
}
```

`ref struct`の要求は同期契約（`ISyncRequest`＋`SendSync`）を使います。非同期経路とストリーム要素は診断します（ZEN0012）。再列挙は保証しないため、新しい列挙には`StreamAsync`を呼び直します。値型で閉じたオープンジェネリックはNativeAOTで不可です（[既知の制限](docs/release/known-limitations.md)）。

## ライフタイム

既定は Scoped です。登録ごとに Singleton と Transient も選べます。
MediatR の `Lifetime` 設定に相当します（MediatR の既定は Transient）。

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
- 指定した有効期間は、新しく追加する Zendiator、ハンドラー、Behavior の登録に適用されます。
  既存登録とその依存サービスの有効期間は変わりません。スコープ検証を有効にして、
  Singleton が Scoped の依存を保持していないことを確認してください。
- 先に登録した方が勝ちます。後からの登録は既存登録を置き換えません。
- 範囲外の値は、定数なら生成時（ZEN0018）に診断されます。
- 解決の意味は DI どおりです。Transient では送信ごとに新しいハンドラーが作られます。
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

- 小さい `Order` が外側に巻きます。
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

## 公開 API

`Zendiator.Abstractions`（net10.0、外部依存なし）が契約を持ちます。

- 要求: `IRequest<TResponse>`、`ICommand<TResponse>`、`IQuery<TResponse>`、
  `IRequest`、`ICommand`、`IMultiRequest<TResponse>`、`IMultiRequest`、
  `ISyncRequest<TResponse>`、`ISyncRequest`、`ISyncCommand`、
  `ISyncMultiRequest<TResponse>`、`ISyncMultiRequest`、`INotification`、`Unit`、
  `IStreamRequest<TItem>`
- ハンドラー: `IRequestHandler<TRequest, TResponse>`、`IRequestHandler<TRequest>`、
  `ICommandHandler<TCommand, TResponse>`、`ICommandHandler<TCommand>`、
  `IQueryHandler<TQuery, TResponse>`、`INotificationHandler<TNotification>`、
  `ISyncRequestHandler<TRequest, TResponse>`、`ISyncRequestHandler<TRequest>`、
  `IStreamRequestHandler<TRequest, TItem>`
- パイプライン: `IRequestContinuation<TRequest, TResponse>`、
  `IRequestContinuation<TRequest>`、`IPipelineBehavior<TRequest, TResponse>`、
  `IPipelineBehavior<TRequest>`、`ISyncRequestContinuation<TRequest, TResponse>`、
  `ISyncRequestContinuation<TRequest>`、`ISyncPipelineBehavior<TRequest, TResponse>`、
  `ISyncPipelineBehavior<TRequest>`、`IStreamContinuation<TRequest, TItem>`、
  `IStreamPipelineBehavior<TRequest, TItem>`
- 属性: `GenerateZendiatorAttribute`、`IncludeAssemblyAttribute`、
  `PipelineBehaviorAttribute`、`HandlerOrderAttribute`、`NotificationAttribute`

`Zendiator`（net10.0）が DI 入口を持ちます。

- `ZendiatorConfiguration`: `Namespace`、`ServiceLifetime`、
  `RegisterServicesFromAssemblyContaining<T>()`、
  `RegisterServicesFromAssembly(Assembly)`、`AddOpenBehavior(Type, int)`、
  `AddOpenStreamBehavior(Type, int)`、
  `AddNotification<T>()`、`ConfigureHandlerOrder(Type, int)`、`Snapshot()`
- `ZendiatorConfigurationSnapshot`: 凍結済みの記録値と `GetFingerprint()`
- `ZendiatorServiceCollectionExtensions.AddZendiator`（引数なしと構成ラムダ付き。
  未接続の呼び出しは診断付き例外で失敗する）

利用側コンパイルごとの生成コード（`IZendiator`、`Zendiator`、属性方式の登録拡張
または DI 方式の registrar＋interceptor）は製品の一部として扱います。
安定版の公開後は直前の安定版を基準にパッケージ互換性を検証します。
`0.1.1` は `0.x` の公開であり、API を永久に固定するものではなく、`1.0.0` の前に変更される場合があります。
契約・ハンドラー・管路・属性の一覧は、上記の Public API 節のとおりです。

## 性能

`benchmarks/Zendiator.Benchmarks` で直接呼び出しと型付き送信を比べられます。
ウォームアップ済みスコープと同期完了する無割り当てハンドラー／Behavior で、
送信あたりの追加割り当て 0 B を確認しています（0 段・1 段の同期経路はテストでも固定）。
初回DI解決、ログ出力、非同期中断は、この0 Bの主張に含めません。レイテンシの数値保証はしません。

ハンドラーとBehaviorは呼び出すたびに、紐付いたDI Providerから解決します。
ScopedとSingletonの再利用はDIコンテナーが担当します。同じ登録一覧から複数のProviderを
作った場合にも有効期間を守るため、Mediator側の独自キャッシュを削除しました。
以前の約1〜4 nsという数値は、現在の送信経路には適用できません。

現在の正式測定（BenchmarkDotNet、Release、Throughput、3回起動、MemoryDiagnoser）は
[0.1.0 release notes](docs/release/0.1.0-release-notes.md)に概要を、
[正式測定（英語）](docs/performance.md)に全表をまとめています。
結果は測定した経路と環境に限定します。generic応答の生成、スコープ全体の作成・破棄、
非同期中断を伴うStreamの割り当ては、それぞれ分けて記録しています。
競合順位や一般的なallocation-freeは主張しません。
`OPT-stream-async` を含む[既知の制限](docs/release/known-limitations.md)も参照してください。

## AOT とトリミング

`IsAotCompatible` を設定し、生成コードはリフレクションを使いません。
AOT・トリミング警告は抑制せず、エラーとして扱います。
完全な Native AOT リンクには C++ ワークロードが必要です。アプリを Native AOT で発行する例
（プロジェクトのパスは自分のアプリに読み替えてください）:

```powershell
dotnet publish samples/Zendiator.Sample.Host -c Release -r win-x64 `
  -p:PublishTrimmed=true -p:TrimMode=full --self-contained
```

検証済みの行列（AOT01-AOT12。型付き、void、管路、汎用、通知、複数、同期と ref、
ストリーム、ストリーム管路、汎用ストリーム、DI-first、Assembly 互換）と既知の境界
（値型で閉じたオープンジェネリック）の概要は
[既知の制限](docs/release/known-limitations.md)を見てください。

## 対象外（後続）

並列配信、fire-and-forget、永続化やoutbox、要求の `Send(object)`、循環検出、CodeFix、CodeLens、組み込み `Result` 管路、組み込みログ・検証は初版の対象外です。逐次 `PublishAsync` による通知、`StreamAsync` によるストリーム、通常の `TResponse` 値としての利用者独自 `Result` 型は対応済みです。

MediatR からの移行は `docs/migrating-from-mediatr.ja.md` を見てください。
