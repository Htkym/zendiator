# MediatR からの移行方法

[English](migrating-from-mediatr.md)

MediatR 12 を使ったコードを Zendiator へ移す手順です。API の対応関係と、
書き換えが必要な箇所、対応していない機能をまとめています。

RC2では、呼び出すハンドラーとBehaviorを標準DIで解決します。Scoped／Singletonの再利用、
Transientの生成、検証、破棄はProviderが担当します。削除した生成内部型 `CachePolicy`、
route holder、Mediatorの `IDisposable` には依存しないでください。
以前のSend／Stream数値は過去の記録です。現在の測定結果と制限は
[0.1.0 release notes](release/0.1.0-release-notes.md)と
[既知の制限](release/known-limitations.md)にまとめています。

前提は次のとおりです。

- 移行元は MediatR 12（`IMediator`、`ISender`、`IPublisher` の構成）を想定しています。
- 移行先は .NET 10（C# 14、nullable 有効）が必要です。
- MediatR の新しいメジャーバージョンには商用ライセンスが必要です。Zendiator は競合順位を主張しません。
  現行の性能値は[0.1.0 release notes](release/0.1.0-release-notes.md)を見てください。

## 対応関係の概要

| MediatR 12 | Zendiator | 備考 |
|---|---|---|
| `IRequest<TResponse>` | `IRequest<TResponse>` | そのまま置き換え |
| `IRequest`（`Unit` を返す） | `IRequest`（戻り値なし、`ValueTask`） | `Unit` を書かない形が標準。旧 `Unit` 形式は互換経路として残る |
| `ICommand<T>`、`IQuery<T>` | `ICommand<T>`、`IQuery<T>` | そのまま置き換え |
| `IRequestHandler<T, R>`（`Task<R> Handle`） | `IRequestHandler<T, R>`（`ValueTask<R> HandleAsync`） | 戻り値とメソッド名が変わる |
| `IRequestHandler<T>`（`Task Handle`） | `IRequestHandler<T>`（`ValueTask HandleAsync`） | 戻り値なし用。`Unit` 不要 |
| `ICommandHandler`、`IQueryHandler` | 同名あり（応答あり）。戻り値なしコマンドは `IRequestHandler<T>` を使う | 旧 `ICommandHandler<T>`（`Unit` 返し）は互換経路 |
| `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` | `AddZendiator(configuration => ...)` | 通常の DI 構成で生成まで行う。手書きの生成先クラスは不要 |
| `IMediator` / `ISender` | `IZendiator` | 解決して使う点は同じ |
| `Send(request)`（`Task<R>`） | `SendAsync(request)`（`ValueTask<R>`） | `await` が必要 |
| 戻り値なし `Send`（`Task`） | `SendAsync`（`ValueTask`） | `await` が必要 |
| `IPipelineBehavior<T, R>`（`next()` デリゲート） | `IPipelineBehavior<T, R>`（struct の継続） | 書き換えが必要 |
| 戻り値なし Behavior | `IPipelineBehavior<T>`（struct の継続） | 1 型引数で書く |
| 実行順 | 登録順序に依存するため、`Order`（小さいほど外側）で明示し直す | テストで順序を確認する |
| `INotification`、`Publish` | `INotification`、`PublishAsync`／`Publish` | 順次配送。購読者なしは正常終了 |
| 1 リクエスト複数ハンドラー | `IMultiRequest<T>`／`IMultiRequest` + `SendAllAsync` | 明示した要求だけ複数配送する |
| ジェネリック要求 | 対応パターンのオープンジェネリックに対応 | 閉じた型との重複は診断（ZEN0010） |
| `ref struct` 要求 | 同期要求（`ISyncRequest`）+ `SendSync` | 非同期経路では使えない |
| `Send(object)` | なし（通知の型消去経路を除く） | 具体型での送信に直す |
| `IStreamRequest<T>` / `CreateStream` | `IStreamRequest<TItem>`＋`StreamAsync` | 1要求1ハンドラー、遅延・取消・破棄対応。`CreateStream`別名なし |

通知とコマンドを混同しないでください。コマンドはハンドラー不在を構成エラーとし、
通知は既知の型に購読者がいなければ正常終了します。

## パッケージとプロジェクト構成

MediatR のパッケージ参照を外し、2 つのパッケージを参照します。

```xml
<ItemGroup>
  <PackageReference Include="Zendiator.Abstractions" Version="0.1.0" />
  <PackageReference Include="Zendiator" Version="0.1.0" />
</ItemGroup>
```

役割分担の目安は README のとおりです。メッセージ定義とハンドラーは
`Zendiator.Abstractions` だけを参照し、生成設定を置くプロジェクトが
`Zendiator` を参照します。

標準の構成は DI 構成コードです。生成先のためのプロジェクトや空のクラス、
構成属性は要りません。

```csharp
// 通常の Application プロジェクトの構成ルート
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
            configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
        });

        return services;
    }
}
```

```csharp
// Host 側はラッパーを呼ぶだけ
services.AddApplication();
```

注意点です。

- 同じアセンブリだけなら `RegisterServicesFromAssemblyContaining` は省略できます。
- `Namespace` を省略すると `{AssemblyName}.Generated` になります。
- 旧来の空 partial クラス方式とアセンブリ属性方式も引き続き使えます。
  DI 構成ラムダとの併用は診断（ZEN0015）になります。
- 生成先のコンパイルと明示したアセンブリだけを調べます。
  ハンドラーが参照するリクエスト型は自動で取り込みます。
- `AddZendiator()` は `TryAdd` で登録します。事前登録があれば置き換えません。
- ライフタイムの既定が違います。MediatR の既定は Transient、
  Zendiator の既定は Scoped です。引数なしは Scoped と同じです。
  Singleton と Transient は構成で選びます。

```csharp
services.AddZendiator(static configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
    configuration.ServiceLifetime = ServiceLifetime.Singleton;
});
```
- 指定したライフタイムは Zendiator、ハンドラー、Behavior のすべてに適用されます。
  混在しないため、捕捉的依存は生じません。
- 状態を持つハンドラー（呼び出し回数のカウンターなど）は、
  Transient から Scoped に変わると使い回されるため、動作が変わります。
  必要なら事前に `AddTransient` で登録しておくと、その指定が残ります。
- 移行直後の検証では `ValidateScopes` と `ValidateOnBuild` の有効化を推奨します。

## リクエストとハンドラーの書き換え

リクエスト型の宣言はほぼそのまま使えます。`class`、`record`、`struct`、
`record struct` が使えます。

```csharp
// 移行前 (MediatR)
using MediatR;

public sealed record GetUserQuery(int Id) : IRequest<UserDto>;

public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
        => Task.FromResult(new UserDto(request.Id));
}
```

```csharp
// 移行後 (Zendiator)
using Zendiator;

public sealed record GetUserQuery(int Id) : IQuery<UserDto>;

public sealed class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> HandleAsync(GetUserQuery request, CancellationToken cancellationToken)
        => new(new UserDto(request.Id));
}
```

変更点は 3 つです。

1. 名前空間を `MediatR` から `Zendiator` に変える。
2. メソッド名を `Handle` から `HandleAsync` に変える。
3. 戻り値を `Task<T>` から `ValueTask<T>` に変える。完了済みの結果は
   `new(...)` で包むか、`async ValueTask<T>` メソッドにします。
   `Task.FromResult` は使いません。

制約として、リクエストは公開型で、応答契約は 1 つだけです。
非ジェネリックの要求にハンドラーが 2 つ以上あると生成時エラー（ZEN0002）になります。
条件に合わない型は ZEN0003 で報告されます。

ジェネリック要求は、ハンドラーの型引数が要求の型引数から一意に決まる
パターンに対応します。型引数の順序変更、一部の固定型、応答型への代入、
`class`／`struct`／`new()` などの制約に対応します。

```csharp
public sealed record GetById<T>(int Id) : IRequest<T> where T : class;

public sealed class GetByIdHandler<T>(IRepository<T> repository) : IRequestHandler<GetById<T>, T>
    where T : class
{
    public ValueTask<T> HandleAsync(GetById<T> request, CancellationToken cancellationToken)
        => repository.GetAsync(request.Id, cancellationToken);
}

// 呼び出し元の型引数が未確定でもよい
ValueTask<T> Load<T>(IZendiator sender, int id, CancellationToken cancellationToken)
    where T : class
    => sender.SendAsync(new GetById<T>(id), cancellationToken);
```

閉じたハンドラーとオープンなハンドラーが同じ閉じた要求に重なると、
曖昧さの診断（ZEN0010）になります。要求から決まらない余分な型引数は
診断（ZEN0009）になり、勝手な既定型では埋めません。

## 戻り値なしのリクエスト

新しい標準形では `Unit` を書きません。MediatR の `Task Handle` に対応するのは
Zendiator の `ValueTask HandleAsync` です。

```csharp
// 移行前
using MediatR;

public sealed record Ping : IRequest;
public sealed class PingHandler : IRequestHandler<Ping>
{
    public Task Handle(Ping request, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
```

```csharp
// 移行後
using Zendiator;

public sealed record DeleteUser(int UserId) : ICommand;

public sealed class DeleteUserHandler : IRequestHandler<DeleteUser>
{
    public ValueTask HandleAsync(DeleteUser command, CancellationToken cancellationToken)
        => default;
}
```

```csharp
// 呼び出し側
await zendiator.SendAsync(new DeleteUser(userId), cancellationToken);
```

注意点です。

- 戻り値なし要求の Handler・Behavior・継続は 1 型引数で書きます。
  旧来の `IRequestHandler<T, Unit>`／`ValueTask<Unit>` の形も互換経路として
  そのままビルドできます。新旧の両 Handler を同じ要求に置くと診断（ZEN0008）です。
- 新形式へ移した要求の送信結果は `ValueTask<Unit>` ではなく `ValueTask` になります。
- 旧 2 型引数 Behavior は新 1 型引数 Behavior へ移す必要があります。

## 登録の書き換え

MediatR のアセンブリスキャン登録をやめ、通常の DI 構成コードで生成設定を書きます。
推奨経路では空の partial クラスや構成属性は要りません（旧クラス・旧アセンブリ属性経路も
互換のために残っています。同一コンパイルで DI 構成と混ぜないでください）。

```csharp
// 移行前
services.AddMediatR(static configuration =>
{
    configuration.RegisterServicesFromAssembly(typeof(GetUserQuery).Assembly);
});
```

```csharp
// 移行後: Application プロジェクトの構成ルート
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
            configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
            configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
        });

        return services;
    }
}
```

```csharp
// 移行後: Host 側はラッパーを呼ぶだけ
services.AddApplication();
```

```csharp
// 移行後: 生成された mediator の解決（生成 namespace を using）
using MyApp.Application.Generated;

var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
```

旧来の空 partial クラス方式とアセンブリ属性方式は、移行期間の互換経路として残ります。

## 送信側の書き換え

`IMediator` / `ISender` の解決を `IZendiator` に変えます。
用途に応じて次の送信 API を使い分けます。

| 用途 | API | 戻り値 |
|---|---|---|
| 通常の単一応答 | `SendAsync` | `ValueTask<TResponse>` |
| 応答なしコマンド | `SendAsync` | `ValueTask` |
| 通知 | `PublishAsync`／`Publish` | `ValueTask` |
| 複数応答 | `SendAllAsync` | `ValueTask<IReadOnlyList<TResponse>>` |
| 応答なし複数配送 | `SendAllAsync` | `ValueTask` |
| 同期単一応答 | `SendSync` | `TResponse` |
| 同期応答なし | `SendSync` | `void` |
| 同期複数応答／応答なし | `SendAllSync` | `IReadOnlyList<TResponse>`／`void` |

```csharp
// 移行前
var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
var user = await mediator.Send(new GetUserQuery(1), cancellationToken);
```

```csharp
// 移行後
var zendiator = scope.ServiceProvider.GetRequiredService<IZendiator>();
var user = await zendiator.SendAsync(new GetUserQuery(1), cancellationToken);
```

注意点です。

- 非同期の戻り値は `ValueTask` 系です。必ず `await` します。
  戻り値の再利用（複数回 await）はしません。
- `SendAsync` は具体的なリクエスト型ごとのオーバーロードだけを生成します。
  `object` や `IRequest<T>` 型の変数からの送信はできません。
  変数の静的な型が具体型になるよう呼び出し側を直します。
- `CancellationToken` は省略できます。渡したトークンは各ノードの実行前に確認され、
  取り消し時は `OperationCanceledException` になります。
- null の参照型リクエストは拒否されます。例外は変換されずに伝播します。

## 通知の書き換え

MediatR の通知は Zendiator の通知に移せます。購読者の手動ファンアウトは要りません。

```csharp
using Zendiator;

public sealed record UserCreated(int UserId) : INotification;

public sealed class WelcomeEmail : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
        => default;
}

public sealed class AuditLog : INotificationHandler<UserCreated>
{
    public ValueTask HandleAsync(UserCreated notification, CancellationToken cancellationToken)
        => default;
}
```

```csharp
await zendiator.PublishAsync(new UserCreated(1), cancellationToken);
```

注意点です。

- 配送は順次です。前の購読者の完了後に次を開始します。
- 順序は `[HandlerOrder(Order = ...)]` の昇順です。未指定は 0 で、
  同順位は安定した決定的順序になります。
- 最初の失敗で停止し、後続の購読者は構築されません。
- 既知の通知型に購読者がいなければ正常終了します。構成にない未知の型は例外です。
- `INotification` 型の変数からの送信もできます（登録済みの閉じた型が対象）。
  型付き経路は静的な通知型、型消去経路は正確な実行時型を配送キーとします。
- `Publish` は `PublishAsync` と同じ契約の別名です。fire-and-forget にはなりません。

## 複数ハンドラーの書き換え

MediatR 12 の通常 `Send` に複数ハンドラー機能はありません。
Zendiator では明示した要求だけが複数配送します。

```csharp
using Zendiator;

public sealed record GetQuotes(string ProductCode) : IMultiRequest<Quote>;

// 呼び出し側
IReadOnlyList<Quote> quotes = await zendiator.SendAllAsync(new GetQuotes("P1"), cancellationToken);
```

注意点です。

- 単一要求のままハンドラーを複数にすると従来どおり生成時エラー（ZEN0002）です。
- 各ハンドラーに Pipeline が適用され、応答は実行順に集めます。
- 応答なしの複数配送は `ValueTask` だけを返し、`Unit` の一覧は作りません。
- 順序は通知と同じ規則（`HandlerOrder` 昇順＋決定的な同順位規則）です。

## 同期要求の書き換え

`ref struct` 要求は同期契約で扱います。非同期経路への送信はできません。

```csharp
using Zendiator;

public readonly ref struct ParseYear : ISyncRequest<int>
{
    public ParseYear(ReadOnlySpan<byte> data) => Data = data;
    public ReadOnlySpan<byte> Data { get; }
}

public sealed class ParseYearHandler : ISyncRequestHandler<ParseYear, int>
{
    public int Handle(scoped ParseYear request, CancellationToken cancellationToken)
        => request.Data[0] - (byte)'0';
}
```

```csharp
// 非 async の呼び出しスコープ内で利用する
int year = zendiator.SendSync(new ParseYear(buffer), cancellationToken);
```

注意点です。

- 要求を `object` や interface 型へ変換する書き方はコンパイルできません。
- 要求を保持する（field、lambda 捕捉、`await` 跨ぎ）書き方もコンパイルできません。
- ジェネリックな `ref struct` 要求は `allows ref struct` の境界で書きます。
- 応答型は通常の型に限ります。`ref` 構造体の応答や `ref return` は未対応です。

## Behavior の書き換え

`next()` デリゲート呼び出しを、struct の継続への `InvokeAsync` に変えます。
実行順は登録順ではなく `Order` で決まります。小さいほど外側です。

```csharp
// 移行前 (MediatR)
using MediatR;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        try
        {
            return await next();
        }
        finally
        {
            Console.WriteLine($"Handled {typeof(TRequest).Name}");
        }
    }
}
```

```csharp
// 移行後 (Zendiator)
using Zendiator;

public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> HandleAsync<TNext>(
        TRequest request,
        TNext next,
        CancellationToken cancellationToken)
        where TNext : struct, IRequestContinuation<TRequest, TResponse>
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        try
        {
            return await next.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Console.WriteLine($"Handled {typeof(TRequest).Name}");
        }
    }
}
```

```csharp
// 移行後: DI 構成への登録（AddApplication 内）
services.AddZendiator(static configuration =>
{
    configuration.RegisterServicesFromAssemblyContaining<GetUserQueryHandler>();
    configuration.AddOpenBehavior(typeof(LoggingBehavior<,>), order: 0);
});
```

Behavior の意味はそのまま移せます。

- 早期終了は `next` を呼ばないだけです。後続 Behavior とハンドラーは解決されません。
- 再試行は `next.InvokeAsync` の逐次複数回呼び出しで書けます。並列呼び出しは対象外です。
- リクエストやトークンの差し替えは、差し替えた値で `InvokeAsync` を呼びます。
- オープン Behavior（2 引数ジェネリック）は制約を満たすリクエストにだけ適用されます。
  閉じた Behavior は一致する経路にだけ適用されます。
- 同じ Behavior 型や同じ `Order` の重複はエラー（ZEN0004）です。
  MediatR での登録順に頼っていた順序は、`Order` で明示し直します。
- DI コンストラクター注入はそのまま使えます。
- 戻り値なし要求には 1 型引数の `IPipelineBehavior<T>` を使います。
  同期要求には `ISyncPipelineBehavior` を使います。
- 複数配送では各ハンドラーの分岐に Pipeline が適用されます。

## 対応していない機能

次は Zendiator に相当機能がありません。移行前に置き換え方を決めます。

| MediatR の機能 | 対応 |
|---|---|
| `Send(object)` による実行時配送 | なし（通知の型消去経路を除く）。具体型での送信に直します |
| 実行前後プロセッサー、例外ハンドラー | 専用機構なし。Behavior として実装します |
| 並列 Publish、並列 SendAll、fire-and-forget | なし（順次配送のみ） |
| `ref` 構造体の応答、`ref return` | なし |
| 未使用の全閉型を列挙するような AOT 設定 | なし。使用する閉じた型を明示します |

## 移行チェックリスト

1. パッケージ参照を 2 つに置き換える。
2. `using MediatR;` を `using Zendiator;` に置き換える。
3. `Handle` を `HandleAsync` に、`Task<T>` を `ValueTask<T>` に直す。
4. 戻り値なしは `Unit` をやめ、1 型引数 Handler と `ValueTask` に直す
   （旧 `Unit` 形式は残せるが、新旧の混在は診断される）。
5. 生成設定を DI 構成ラムダに置き、必要な対象・Behavior・順序を移す。
6. `AddMediatR` を `AddApplication` 等のラッパー経由の `AddZendiator` に直し、ライフタイム既定値の差を確認する。
7. 送信側を用途別の API（`SendAsync`／`PublishAsync`／`SendAllAsync`／`SendSync`／`StreamAsync`）に直す。
   ストリームは遅延実行（初回 `MoveNextAsync` で開始）、API トークンか `WithCancellation` で取消し、
   `await using` で破棄し、再列挙は呼び直す。
8. 変数が `object` や `IRequest<T>` 型になっていないか探す。
9. Behavior を継続呼び出しに書き換え、`Order` を付ける
   （同期は `ISyncPipelineBehavior`、ストリームは `IStreamPipelineBehavior`＋`AddOpenStreamBehavior`）。
10. 通知の購読者を `INotificationHandler` に直し、順序が必要なら `HandlerOrder` を付ける。
11. 複数配送が必要な要求に `IMultiRequest` を付け、`SendAllAsync` に直す。
12. `ref struct` 要求を同期契約（`ISyncRequest` + `SendSync`）に直す。
13. `ValidateScopes` と `ValidateOnBuild` を有効にしてテストを通す。
14. 生成時エラー（`ZEN0001`〜`ZEN0020`）が出たら、型の公開範囲・重複・制約・構成式を見直す。

テストは既存のものを流用できます。戻り値の期待値、Behavior の呼び出し回数、
呼び出し順序が変わっていないことを確認してください。
