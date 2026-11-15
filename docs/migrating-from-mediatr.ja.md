# MediatRからの移行

[English](migrating-from-mediatr.md)

このガイドは`IMediator`、`ISender`、`IPublisher`を使うMediatR 12のアプリケーションを、.NET 10・C# 14上のZendiator 0.6.0へ移行する手順です。後のMediatRでは登録APIが異なる場合があります。[README](../README.ja.md)と[現行の構成ガイド](getting-started-0.5.0.ja.md)も参照してください。構成ガイドは既存リンクを保つためファイル名を維持していますが、内容は0.6.0向けです。

新しいSend・Streamルートでは、`[Handler]`を付けたHandlerを`Handlers`で選び、`Pipeline`に段を順番に並べる明示的な構成を使います。以前のアセンブリ探索、`AddOpenBehavior`、`Order`のAPIは互換経路として残っています。既存のZendiatorコードから移行するときは、[0.6.0のHandler・Stream変更](release/0.6.0-release-notes.ja.md)も適用してください。

## APIの対応

| MediatR 12 | Zendiator 0.6.0 | 移行する内容 |
|---|---|---|
| `IRequest<TResponse>` | `IRequest<TResponse>` | 名前空間を変更する |
| `IRequest` | `IRequest`または`ICommand` | 1型引数のHandlerから`ValueTask`を返す |
| `IRequestHandler<TRequest,TResponse>.Handle` | `IRequestHandler<TRequest,TResponse>.HandleAsync` | `ValueTask<TResponse>`を返し、`[Handler]`を付ける |
| `IRequestHandler<TRequest>.Handle` | `IRequestHandler<TRequest>.HandleAsync` | `ValueTask`を返し、`[Handler]`を付ける |
| 実行時のアセンブリ登録 | `AddZendiator<App>`と`Handlers`／`Pipeline` | コンパイル時に対象ルートを選ぶ |
| `IMediator`／`ISender` | 生成された`IZendiator` | 構成の生成名前空間を参照する |
| `Send(request)` | `SendAsync(request)` | 1回だけawaitする |
| `IPipelineBehavior<TRequest,TResponse>` | SendのHookまたは`IAsyncSendWrapper<TRequest,TResponse>` | 継続の制御方法で選ぶ |
| Behaviorの登録順 | `Pipeline`に並べた段の順序 | 先に書いた段が外側になる |
| `INotification`／`Publish` | `INotification`／`PublishAsync`または`Publish` | 通知を別に登録し、順次配送する |
| `IStreamRequest<T>`／`CreateStream` | `IStreamRequest<T>`／`StreamAsync` | 入力契約を1つ選ぶ |
| `Send(object)` | 対応なし | 呼び出し側で具体的な要求型を保つ |

`ICommand<T>`と`IQuery<T>`はアプリケーションのCommand・Queryの分類に使えるZendiatorの選択肢です。MediatRのインターフェースではありません。実行時型による要求の配送と、通知の型消去経路では対応範囲が異なります。

## パッケージとプロジェクト構成

構成を宣言するプロジェクトへインストールします。

```shell
dotnet add package Zendiator --version 0.6.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

`Zendiator`にはGeneratorが含まれ、`Zendiator.Abstractions`へ依存します。契約だけを定義するプロジェクトは`Zendiator.Abstractions`だけを参照できます。Zendiatorの全パッケージを同じバージョンにそろえてください。任意で追加する`Zendiator.CodeFixes`も0.6.0とし、`PrivateAssets="all"`を指定します。

生成時に調べるのは、構成を置いたコンパイルと明示的に指定したアセンブリです。実行時に任意のアセンブリを探索しません。複数プロジェクトでは契約・実装・構成の参照を明示してください。[ComposedAppサンプル](../samples/Zendiator.Sample.ComposedApp/)と[構成の診断](diagnostics/configuration.md)に例があります。

## 要求・Handler・登録

MediatRのHandlerから名前空間、メソッド名、戻り値の型を変更し、ZendiatorのHandlerに属性を付けて選択します。次は[GettingStartedサンプル](../samples/Zendiator.Sample.GettingStarted/Program.cs)と同じ登録・Scopeの使い方による、実行可能な例です。

```csharp
using Migration.Generated.App;
using Microsoft.Extensions.DependencyInjection;
using Zendiator;
using Zendiator.DependencyInjection;

namespace Migration;

public static class Program
{
    public static async Task Main()
    {
        var services = new ServiceCollection();
        services.AddZendiator<App>(configuration =>
        {
            configuration.Handlers<GetUserQuery>(DispatchFamily.SendAsync, typeof(GetUserQueryHandler));
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var user = await mediator.SendAsync(new GetUserQuery(1));
        Console.WriteLine(user.Id);
    }
}

[ZendiatorComposition("migration")]
public sealed class App;

public sealed record UserDto(int Id);
public sealed record GetUserQuery(int Id) : IRequest<UserDto>;

[Handler]
public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public ValueTask<UserDto> HandleAsync(GetUserQuery request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return new(new UserDto(request.Id));
    }
}
```

期待する出力は`1`です。生成名前空間にはアプリケーションの名前空間と構成マーカーが入ります。この例では`Migration.Generated.App`です。HandlerだけのSendルートは空の`Pipeline`を省略できます。登録を`AddApplication`などのメソッドでまとめても、追加の初期化は不要です。

応答なしの要求は`IRequestHandler<TRequest>`を実装し、`HandleAsync`から`ValueTask`を返します。同様に`DispatchFamily.SendAsync`でHandlerを選びます。旧`IRequestHandler<TRequest,Unit>`も互換経路として残っていますが、1つのルートで両方のHandler契約を混在させないでください。

オープンジェネリックのHandlerは、要求から型引数が決まり、型制約を満たす場合に使えます。定義の登録は`Handlers(typeof(Lookup<>), DispatchFamily.SendAsync, typeof(LookupHandler<>))`と書きます。実行できる例は[GenericLookup](../samples/Zendiator.Sample.GenericLookup/)にあります。閉じた型とオープンな型の曖昧な一致や、要求から決まらない型引数は診断されます。

## 送信側・Lifetime・例外

生成された`IZendiator`を解決または注入します。呼び出し側の要求型は具体型を保ってください。`object`や`IRequest<T>`の変数からは生成された要求ごとのオーバーロードを選べません。同期ルートは`Send`、非同期ルートは`SendAsync`、複数Handlerを明示したルートは`SendAll`／`SendAllAsync`を使います。旧`SendSync`／`SendAllSync`は互換APIの名前です。

生成された登録ではMediatorがScoped、Handler・Hook・Session FactoryがTransientになります。MediatR 12のMediatorの既定値はTransientです。登録を上書きするときは`AddZendiator`より前にサービスを登録してください。`TryAdd`による生成登録は先行する登録を保ちます。再利用するサービスはアプリケーションの並行利用に対応する必要があります。

各MediatorはTransientを含む取得済みの依存を再利用します。条件を満たす非同期Hookルートは最初のHookより前にルート単位の依存セットを取得します。それ以外のルートにはそれぞれの捕捉規則が適用されます。送信や再試行のたびに新しい依存が作られるとは限りません。構築時期やインスタンスの共有に依存する前に、[現行の捕捉規則](getting-started-0.5.0.ja.md#di-scopeの所有)を確認してください。

構築と破棄はDIが所有します。送信、列挙、後始末がすべて終わってからScopeを破棄してください。生成されたMediatorは`IDisposable`の所有者ではありません。ScopeやProviderの破棄後の利用は未対応です。`ValidateScopes`と`ValidateOnBuild`を有効にし、遅延解決されるルートも実際に呼び出して確認します。

返された`ValueTask`は1回だけawaitしてください。条件を満たす直接の`SendAsync`経路では、依存の解決、キャンセル検査、同期コールバックが`ValueTask`を返す前に例外を投げる場合があります。呼び出しとawaitの両方を`try`の中へ置いてください。例外を自動で回復する方針はありません。Lifetime Analyzerは証明できる誤用をZEN0021〜ZEN0023で報告しますが、警告がないことは複雑な非同期処理でのScopeの安全性を保証しません。

## Behavior・Hook・Wrapper

要求・応答の変換や、成功・例外・後始末の観測には専用Hookを使い、`Stage.Hook<T>()`で選びます。Retry・Cache・Short-circuitなど、継続を呼ぶかどうか、何回呼ぶかをBehaviorが決める場合は`IAsyncSendWrapper<TRequest,TResponse>`を使います。

次のWrapperは1回の呼び出しの前後にログを書く動作を保ちます。

```csharp
public sealed class Logging<TRequest, TResponse> : IAsyncSendWrapper<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    public async ValueTask<TResponse> InvokeAsync<TNext>(
        TRequest request, TNext next, CancellationToken token)
        where TNext : IAsyncSendContinuation<TRequest, TResponse>
    {
        Console.WriteLine($"Handling {typeof(TRequest).Name}");
        try
        {
            return await next.InvokeAsync(request, token).ConfigureAwait(false);
        }
        finally
        {
            Console.WriteLine($"Handled {typeof(TRequest).Name}");
        }
    }
}
```

先ほどの例の既存の`AddZendiator<App>`設定へ段を追加します。

```csharp
configuration.Pipeline<GetUserQuery>(DispatchFamily.SendAsync,
    Stage.Wrapper<Logging<GetUserQuery, UserDto>>());
```

MediatRの登録順を`Pipeline`に書く順序へ移します。元のBehaviorの意図を保ち、テストで確認してください。Wrapperは`next`を呼ばずに終了したり、順次複数回呼んだりできます。各結果は1回だけ消費します。継続を保持したり、処理の完了後に並行して呼び出したりしないでください。取得済みの後続サービスは再試行でも再利用されます。応答なしのルートは`IAsyncSendWrapper<TRequest>`、同期ルートは`ISendWrapper`を使います。

[ComposedAppサンプル](../samples/Zendiator.Sample.ComposedApp/Program.cs)にはHookとCache・RetryのWrapperがあります。旧`IPipelineBehavior`、`AddOpenBehavior`、数値の`Order`は別の互換APIとして残っています。既存のZendiatorの探索構成をCompositionへ移すときは、明示的なWrapper契約と順序付きの`Pipeline`へ移してください。

## 通知と複数Handler

通知のHandlerは`INotificationHandler<TNotification>.HandleAsync`を実装し、`ValueTask`を返します。通知の登録はCompositionのSend・Streamの選択とは別です。既存のアセンブリ探索構成は通知に対応しています。[Sample.Applicationの登録](../samples/Zendiator.Sample.Application/DependencyInjection.cs)と[Sample.Host](../samples/Zendiator.Sample.Host/Program.cs)で、生成される`PublishAsync`と呼び出し方を確認できます。

`PublishAsync`と別名の`Publish`は購読者を1つずつawaitして順次配送します。fire-and-forgetではありません。購読者の順序は`HandlerOrder`で決まり、同順位には安定した決定的な順序があります。最初の失敗で配送を止めます。既知の通知型に購読者がいない場合は正常終了します。通知の型消去経路は登録済みの閉じた型に対応し、正確な実行時型をキーにします。CommandのHandlerがない場合は、空の購読者一覧として扱わず構成エラーになります。

MediatR 12の通常の`Send`には、複数Handlerから応答を受ける契約がありません。ZendiatorのCompositionでは`DispatchFamily.SendAllAsync`または`DispatchFamily.SendAllSync`を使ってHandlerを明示的に選びます。応答は選択したHandlerの順に並び、各分岐にPipelineが適用されます。応答なしの複数配送で`Unit`の一覧は作りません。旧探索経路は`IMultiRequest`／`IMultiRequest<T>`と`HandlerOrder`の規則を使います。Compositionの形はComposedAppを参照してください。

## Streamと同期要求

消費側は標準の`IAsyncEnumerable<T>`を維持できます。通常の非同期入力では`IStreamHandler<TRequest,TItem>`の実装に`[Handler]`を付け、`Handle`から`IAsyncEnumerable<TItem>`を返します。コンパイラ生成の非同期Iteratorを使うのが基本です。名前付きのclassまたはstruct Cursorが必要なら`IStreamSourceHandler<TRequest,TItem,TCursor>.OpenAsync`を使います。`Handlers`で入力契約を1つだけ選び、Streamの`Pipeline`を宣言します。段がないStreamでも空の一覧を宣言してください。

上流を独自に制御するときは`IStreamMiddleware`と`Stage.Wrapper`、型付きの項目処理にはFilter・Map契約と`Stage.Hook`を使います。出力型が変わるPipelineは`StreamPipeline`で最終型を宣言します。要求の`IStreamRequest<T>`は引き続き入力の項目型を表します。[Iteratorガイド](streaming-iterators.md)は両方の入力、キャンセル、直接経路とガード付き経路を説明しています。[0.6.0のStream移行](migrating-stream-upstream.md)では型付き`OpenAsync<TNext>`のMiddlewareを説明しています。

`StreamAsync`は標準のEnumerableを返します。通常は`await foreach`を使い、APIと列挙のキャンセルトークンを渡してください。早期終了や失敗も含め、破棄までScopeを維持します。取得時期と後始末は直接経路かガード付き経路かで異なり、すべての経路が最初のMoveで始まるわけではありません。`StreamSource(...).ConsumeAsync(...)`は別の型付き終端です。readonlyの計画値はEnumerableではなく、値型Sinkの結果は参照状態で共有します。標準列挙、型付き終端、HTTPの性能は別々に測定する必要があります。

通常の同期要求は`ISyncRequest`と`ISyncRequestHandler`を使い、`DispatchFamily.SendSync`で構成して生成された`Send`を呼びます。ref-like要求には追加の構成制限があり、通常のclassを使ったCompositionの例だけでは対応を判断できません。[既知の制限](release/known-limitations.md)も確認してください。

## 非対応機能と検証

| 機能 | 対応 |
|---|---|
| 実行時の`Send(object)`、実行時型によるStream配送 | 呼び出し側で具体的な要求型を保つ |
| 並列Publish、並列SendAll、fire-and-forget | 順次実行の契約を使う |
| MediatRの実行前後プロセッサー、例外Handler | 処理内容をHookまたはWrapperへ移す |
| `ref struct`の応答、`ref return` | 非対応 |
| Native AOTでの任意の実行時ジェネリック構築 | 閉じた型とRIDの制限を確認する |
| 永続化、Outbox、永続メッセージング | アプリケーション側で基盤を用意する |

文書に記載したwin-x64の構成では、パッケージ利用によるNative AOTの公開と実行を検証しています。他のRIDは未検証です。値型引数で閉じるopen-generic DIには、Native AOTの別の制限があります。structのStream Cursorが使えることとは区別してください。

移行は次の順で進めます。

1. Zendiatorのパッケージを0.6.0にそろえ、構成を置くプロジェクトを選ぶ。
2. 要求・Handlerの契約を変更し、`[Handler]`と`Handlers`を宣言する。
3. Behaviorの意図をHookまたはWrapperへ移し、`Pipeline`で順序を保つ。
4. 生成された入口の具体的なオーバーロードへ呼び出し側を移す。通知の登録は別に扱う。
5. Streamの入力とMiddlewareを移し、キャンセルを渡し、Scopeと破棄の境界を保つ。
6. DI検証を有効にし、対象ルートを呼び出す。応答・順序の既存テストを使い、構築回数、反復送信時の状態、破棄を実際の捕捉規則に照らして確認する。

エラーの場所と修正方法は[構成の診断](diagnostics/configuration.md)と[CodeFixの対応範囲](../src/Zendiator.CodeFixes/README.md)を参照してください。CodeFixは、対象が一意に決まる場合に不足したHandler属性を追加できます。Pipelineの順序、Lifetimeの境界、Wrapperの動作は自動で移行しません。
