# Zendiator

[English](README.md)

.NET 10 向けの、ソース生成を使う Mediator です。Handler と処理順を定義し、生成された
`IZendiator` からリクエストごとのメソッドを呼び出します。型付きの配送には生成コードを使い、
実行時のアセンブリ走査やリフレクション呼び出しは使いません。

## インストール

.NET 10 と C# 14 が必要です。[NuGet の Zendiator](https://www.nuget.org/packages/Zendiator) を追加します。

```shell
dotnet add package Zendiator --version 0.6.0
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

`Zendiator` には Source Generator が含まれ、`Zendiator.Abstractions` に依存します。
契約だけを定義するプロジェクトでは `Zendiator.Abstractions` 単体を参照できます。
Zendiator の各パッケージは同じバージョンに揃えてください。ライセンスは MIT です。

## 最初のリクエスト

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
            configuration.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler));
            configuration.Pipeline<Greet>(DispatchFamily.SendAsync);
        });

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        var greeting = await mediator.SendAsync(new Greet("Ada"));
        Console.WriteLine(greeting);
    }
}

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

`[Handler]` で Handler を示し、`Handlers<TRequest>` で実装を選びます。
Hook と middleware は `Pipeline<TRequest>` の順序付きリストに登録します。
Handler だけを使う Send の経路では空の Pipeline を省略できます。Stream の経路では明示してください。
Wrapper の retry、cache、継続呼び出しは、アプリケーションで定義した動作を維持します。
設定全体は[構成ガイド](docs/getting-started-0.5.0.ja.md)と
[ComposedApp サンプル](samples/Zendiator.Sample.ComposedApp/)を参照してください。

## 配送メソッド

| 操作 | 生成される入口 |
|---|---|
| 同期リクエスト | `Send(request, token)` |
| 非同期リクエスト | `SendAsync(request, token)` |
| 同期の複数結果 | `SendAll(request, token)` |
| 非同期の複数結果 | `SendAllAsync(request, token)` |
| 通知の逐次配送 | `PublishAsync(notification, token)` |
| 標準の非同期列挙 | `StreamAsync(request, token)` |
| sink による逐次消費 | `StreamSource(request, token).ConsumeAsync(sink)` |

呼び出し側では具体的なリクエスト型を使います。実行時の object によるリクエスト配送、
Publish の並列実行、fire-and-forget、永続化や outbox は提供していません。
通知の登録は Send と Stream の経路とは別です。

## 依存のライフタイムと例外

生成登録の既定は `IZendiator` が Scoped、Handler・Hook・session factory が Transient です。
上書きする登録は `AddZendiator` より先に追加してください。各 Mediator は取得した依存を再利用します。
条件を満たす非同期 Hook の経路では、外側の失敗で未使用になる receiver も含め、経路ごとの依存セットを取得します。
Transient の同一性やコンストラクタの実行時期に依存する場合は、
[依存の取得とライフタイム](docs/getting-started-0.5.0.md#route-local-capture-for-asynchronous-hooks)を確認してください。

生成と破棄は DI が担当します。配送・列挙・後始末が終わるまで scope を保持し、
破棄後に入口を使わないでください。再利用する Handler と Hook は並行呼び出しに対応する必要があります。
`ValidateOnBuild` だけでは配送時に初めて取得する依存を検証できません。

返された `ValueTask` は一度だけ await します。直接呼び出す経路では、`ValueTask` を返す前に
例外が発生することがあります。`SendAsync` の呼び出しと await の両方を `try` の中に置いてください。

依存の保持領域は既定でインラインに置きます。`ClosedServiceCacheMode.LazyBlocks` は、
一部の依存だけを使う構成で保持領域の費用を変える設定です。
`EagerDependencies` は依存を生成する時期を明示的に変えるため、未使用のサービスを生成する場合があります。
使用状況の推測による自動選択は行いません。[保持領域の設定](docs/getting-started-0.5.0.md#closed-dependency-storage)も参照してください。

## Stream

経路ごとに、次のどちらかの Handler を選びます。

- `IStreamHandler<TRequest, TItem>` は `Handle` から `IAsyncEnumerable<TItem>` を返します。
- `IStreamSourceHandler<TRequest, TItem, TCursor>` は `OpenAsync` で、名前付きの class または struct の cursor を開きます。

`StreamAsync` は `await foreach` で列挙でき、ASP.NET Core の action から返して通常の非同期シリアライズにも使えます。
シリアライズと破棄が終わるまで、リクエストの scope を保持してください。
`StreamSource(...).ConsumeAsync(...)` は任意の sink 用メソッドで、`IAsyncEnumerable<T>` ではありません。
時間や割当量が必ず減るものでもありません。値型の sink は値渡しになるため、
集計結果は class の sink または共有する参照先に保存してください。

標準の middleware で、上流を呼び出す port を値型のまま保持できます。

```csharp
public sealed class Relay<TRequest, TItem> : IStreamMiddleware<TRequest, TItem>
{
    public ValueTask<IAsyncEnumerator<TItem>> OpenAsync<TNext>(
        TRequest request, TNext next, CancellationToken cancellationToken)
        where TNext : struct, IStreamUpstream<TRequest, TItem>
        => next.OpenAsync(request, cancellationToken);
}
```

ジェネレーターはアクセス可能な sealed class に、正確な制約を持つ public・非 virtual の generic 実装が
直接宣言されている場合に、この具体型の経路を選びます。ほかの対応する型では interface 経由の呼び出しを維持します。
[型付き middleware の移行ガイド](docs/migrating-stream-upstream.md)に詳しい条件をまとめています。

列挙子は逐次操作し、処理中の Move を await してから破棄してください。EOF・失敗・途中終了の後も破棄が必要です。
guard 付きの経路は最初の Move で入力を取得します。取消不可の token を使う、条件を満たす単純な iterator 経路は
`GetAsyncEnumerator` で取得します。経路を変えるために token を差し替えず、実際の取消 token を渡してください。
UI context への復帰や ambient state の分離は保証しません。
[入力と所有権](docs/streaming-iterators.md)、[実行上の制約](docs/release/known-limitations.md)も参照してください。

## 移行と IDE 支援

0.6.0 では `ZendiatorHandlerAttribute` を `HandlerAttribute` に変更しました。
`[ZendiatorHandler]` を `[Handler]` に変更し、パッケージのバージョンを揃えて利用側を再ビルドしてください。
ほかの名前と曖昧になる場合は `[global::Zendiator.HandlerAttribute]` と書けます。
標準の Stream middleware は `OpenAsync<TNext>` を直接公開します。
旧実装との adapter や、具体型を直接呼び出すコードの変更は[移行ガイド](docs/migrating-stream-upstream.md)を確認してください。

任意の [Zendiator.CodeFixes](src/Zendiator.CodeFixes/README.md) は、対応する ZEN0035 の診断に対して
不足している `[Handler]` を追加します。バージョン 0.6.0 を `PrivateAssets="all"` 付きで導入してください。
Fix All の対象は現在の文書だけで、処理順・Wrapper の動作・ライフタイムは書き換えません。
コマンドラインのコンパイルでは IDE の修正は適用されません。

[0.6.0 リリースノート](docs/release/0.6.0-release-notes.ja.md)と
[MediatR からの移行ガイド](docs/migrating-from-mediatr.ja.md)も参照してください。1.0 より前の API は変更される場合があります。

## 性能比較

2026-11-11 に一部の利用場面を測定しました。各セルは **平均時間 / 割当量（B/op）** です。
時間とメモリは別の指標です。

|ケース|Zendiator|Immediate.Handlers 4.2.0|MediatR 14.2.0|Mediator 3.0.2|DispatchR 2.3.1|Foundatio.Mediator 1.3.3|
|---|---:|---:|---:|---:|---:|---:|
|Send / Behavior 0|1.21 ns / 0 B|7.18 ns / 0 B|35.18 ns / 200 B|35.84 ns / 64 B|25.84 ns / 0 B|28.20 ns / 0 B|
|Send / Behavior 5|9.38 ns / 0 B|16.78 ns / 0 B|130.72 ns / 1,016 B|79.88 ns / 544 B|34.16 ns / 0 B|155.00 ns / 0 B|
|同一scopeで再取得＋Send / Behavior 0|24.51 ns / 0 B|35.45 ns / 0 B|54.81 ns / 200 B|77.00 ns / 64 B|53.51 ns / 0 B|63.15 ns / 0 B|
|同一scopeで再取得＋Send / Behavior 5|40.05 ns / 0 B|44.60 ns / 0 B|148.61 ns / 1,016 B|115.75 ns / 544 B|57.53 ns / 0 B|185.89 ns / 0 B|
|新規scope＋1 Send＋破棄 / Behavior 5|148.91 ns / 704 B|113.39 ns / 568 B|208.90 ns / 1,400 B|151.09 ns / 920 B|406.93 ns / 1,712 B|413.65 ns / 856 B|
|Notification / 16 subscribers / 同期|107.85 ns / 0 B|N/A|900.10 ns / 4,784 B|188.95 ns / 0 B|146.04 ns / 0 B|685.74 ns / 0 B|
|Stream 1024件 / Behavior 5 / 同期|12.13 μs / 1,016 B|89.41 μs / 1,106 B|182.81 μs / 3,593 B|90.55 μs / 1,688 B|89.69 μs / 1,104 B|106.04 μs / 1,808 B|
|Stream 1024件 / Behavior 5 / 非同期|147.51 μs / 116,362 B|229.28 μs / 1,753 B|448.74 μs / 4,724 B|227.71 μs / 2,338 B|226.34 μs / 1,755 B|280.33 μs / 2,538 B|
|Stream途中終了 / 16件入力 / Behavior 5 / 同期|365.93 ns / 1,016 B|254.44 ns / 1,104 B|717.95 ns / 3,592 B|340.93 ns / 1,688 B|282.08 ns / 1,104 B|572.81 ns / 1,808 B|
|Stream途中終了 / 16件入力 / Behavior 5 / 非同期|1.84 μs / 1,456 B|1.96 μs / 1,752 B|3.03 μs / 4,720 B|2.13 μs / 2,336 B|2.02 μs / 1,752 B|2.47 μs / 2,536 B|
|Behavior短絡 / 3段 / 繰り返し|4.49 ns / 0 B|4.77 ns / 0 B|77.26 ns / 704 B|53.36 ns / 496 B|27.00 ns / 0 B|55.23 ns / 384 B|
|非同期Hook成功 / 初回|0.791 μs / 480 B|1.199 μs / 504 B|1.194 μs / 1,352 B|1.006 μs / 808 B|1.042 μs / 560 B|1.733 μs / 856 B|
|非同期Hook成功 / 繰り返し|0.030 μs / 0 B|0.049 μs / 0 B|0.557 μs / 944 B|0.515 μs / 496 B|0.067 μs / 0 B|0.488 μs / 480 B|
|非同期Hook失敗 / 初回・外側|3.621 μs / 1,712 B|8.124 μs / 3,808 B|8.635 μs / 3,384 B|6.155 μs / 3,216 B|6.445 μs / 2,968 B|9.820 μs / 4,872 B|

新規 scope の値は作成・初回解決・Send・破棄を含みます。Stream は操作全体の値で、
途中終了は 16 件の入力から 1 件だけ消費します。Hook の初回・繰り返し・外側失敗は測定範囲が異なります。
N/A は適用できる比較結果がないことを示します。

Windows 11 x64、Intel Core Ultra 7 258V、SDK 10.0.403 / runtime 10.0.14、
BenchmarkDotNet 0.15.8、Release、affinity 1。ウォームアップ 20 回、実測 12 回、
要求測定時間 500 ms、各ケース 1 launch です。初回・cold のケースは 16384 invocations です。
1 launch の観測から、一般的な順位や条件を揃えた変更前後の改善は確定しません。
Foundatio は intercepted 経路を使い、API と DI 登録の形もライブラリ間で異なります。

[丸め前の結果](benchmarks/results/20261111-8848306-summary.csv)と
[全数値観測](benchmarks/results/20261111-8848306-readme.json)に、
測定対象のソース・表外の利用場面・ばらつきを保存しています。
[測定範囲](benchmarks/README.md#read-the-comparison)も参照してください。

## Native AOT

パッケージを使うアプリケーションは、標準の型付き Stream 経路を含め、win-x64 の native executable として
publish・実行を確認しています。値型の型引数で open-generic DI Handler を実行時に閉じる構成には、
既知の制約があります。struct の cursor や上流 port 全般が未対応という意味ではありません。
ほかの RID は未確認です。利用前に[既知の制約](docs/release/known-limitations.md)を確認してください。
