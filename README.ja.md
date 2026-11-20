# Zendiator

[English](README.md)

Zendiatorは、.NET 10とC# 14向けのプロセス内メディエーターです。ソースジェネレーターがビルド時にリクエストとハンドラーを結び付け、`Microsoft.Extensions.DependencyInjection` の登録コードを生成します。`SendAsync` でリクエストを送り、`PublishAsync` で購読者に通知し、`StreamAsync` で非同期ストリームを読み取れます。

[最初のリクエストを送る](#最初のリクエストを送る)・[MediatRの代替として使う際の移行手順](docs/migrating-from-mediatr.ja.md)・[Windows x64で検証したNative AOT構成（英語）](docs/release/known-limitations.md#native-aot-and-dispatch-boundaries)

## 性能

Windows 11 x64上で、.NET 10.0.14のReleaseビルドをBenchmarkDotNet 0.15.8で測定しました。表は1回あたりの「処理時間 / 割り当て量」で、正順と逆順の平均です。

| ケース | Zendiator 1.0.1 | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send / Behavior 0 | 1.84 ns / 0 B | 11.20 ns / 0 B | 57.30 ns / 200 B | 58.15 ns / 64 B | 39.16 ns / 0 B | 40.85 ns / 0 B |
| Send / Behavior 5 | 8.29 ns / 0 B | 23.26 ns / 0 B | 201.46 ns / 1,016 B | 129.84 ns / 544 B | 47.19 ns / 0 B | 209.79 ns / 0 B |
| 同一スコープで再取得してSend / Behavior 0 | 32.23 ns / 0 B | 49.77 ns / 0 B | 80.44 ns / 200 B | 97.64 ns / 64 B | 70.73 ns / 0 B | 82.08 ns / 0 B |
| 同一スコープで再取得してSend / Behavior 5 | 41.24 ns / 0 B | 56.67 ns / 0 B | 196.21 ns / 1,016 B | 146.43 ns / 544 B | 80.05 ns / 0 B | 224.51 ns / 0 B |
| 新規スコープで1回Sendして破棄 / Behavior 5 | 126.58 ns / 560 B | 152.24 ns / 568 B | 260.63 ns / 1,400 B | 219.39 ns / 920 B | 652.91 ns / 1,712 B | 384.20 ns / 856 B |
| Notification / 16 subscribers / 同期 | 144.53 ns / 0 B | N/A | 1,207.51 ns / 4,784 B | 229.39 ns / 0 B | 209.52 ns / 0 B | 1,005.43 ns / 0 B |
| Stream 1,024件 / Behavior 5 / 同期 | 15.013 µs / 928 B | 107.542 µs / 1,104 B | 214.211 µs / 3,592.5 B | 108.836 µs / 1,688.5 B | 106.151 µs / 1,104.5 B | 127.107 µs / 1,808 B |
| Stream 1,024件 / Behavior 5 / 非同期 | 166.127 µs / 1,586 B | 313.403 µs / 1,755 B | 613.929 µs / 4,726 B | 320.580 µs / 2,339.5 B | 314.207 µs / 1,756 B | 353.049 µs / 2,539 B |
| Stream途中終了 / 16件入力 / Behavior 5 / 同期 | 306.31 ns / 928 B | 347.98 ns / 1,104 B | 977.11 ns / 3,592 B | 450.96 ns / 1,688 B | 377.69 ns / 1,104 B | 744.31 ns / 1,808 B |
| Stream途中終了 / 16件入力 / Behavior 5 / 非同期 | 2.105 µs / 1,256 B | 2.436 µs / 1,752 B | 4.843 µs / 4,720 B | 2.735 µs / 2,336 B | 2.426 µs / 1,752 B | 3.022 µs / 2,536 B |
| Behavior短絡 / 3段 / 繰り返し | 5.64 ns / 0 B | 6.48 ns / 0 B | 116.34 ns / 704 B | 75.54 ns / 496 B | 41.13 ns / 0 B | 77.22 ns / 384 B |
| 非同期Hook成功 / 初回 | 1.023 µs / 448 B | 1.711 µs / 504 B | 1.560 µs / 1,352 B | 1.349 µs / 808 B | 1.338 µs / 560 B | 2.154 µs / 856 B |
| 非同期Hook成功 / 繰り返し | 41.48 ns / 0 B | 61.46 ns / 0 B | 780.85 ns / 944 B | 612.56 ns / 2,900.5 B | 82.21 ns / 0 B | 600.23 ns / 480 B |
| 非同期Hook失敗 / 初回・外側 | 5.492 µs / 1,712 B | 10.858 µs / 3,808 B | 10.142 µs / 3,416 B | 9.032 µs / 3,216 B | 8.069 µs / 2,968 B | 13.138 µs / 4,872 B |

[測定条件と実行手順](benchmarks/README.ja.md)・[測定結果（CSV）](benchmarks/results/f2973af-representative-summary.csv)

## インストール

.NET 10とC# 14を使うプロジェクトに、次のNuGetパッケージを追加してください。

```shell
dotnet add package Zendiator --version 1.0.1
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0
```

`Zendiator` パッケージにはソースジェネレーターが含まれます。リクエストやハンドラーのインターフェイスだけを共有するプロジェクトでは、`Zendiator.Abstractions` を単独で参照できます。アプリ全体で両パッケージのバージョンを揃えてください。

## 最初のリクエストを送る

次のコードをコンソールアプリの `Program.cs` に書いて実行すると、`Hello, Ada` と表示されます。

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
        services.AddZendiator<App>(c =>
            c.Handlers<Greet>(DispatchFamily.SendAsync, typeof(GreetHandler)));

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IZendiator>();
        Console.WriteLine(await mediator.SendAsync(new Greet("Ada")));
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

生成された `IZendiator` には、リクエストの型に対応したメソッドがあります。フックやミドルウェアを特定の順序で使うときは `Pipeline` に登録します。DIのスコープはリクエストの完了まで、ストリームの場合は列挙子を破棄するまで保持してください。[導入ガイド](docs/getting-started.ja.md)・[実行できるサンプル](samples/Zendiator.Sample.GettingStarted/)

## 用途に合うメソッドを選ぶ

| 用途 | 呼び出し方 |
|---|---|
| 結果を1つ受け取る | `Send` または `SendAsync` |
| 複数のハンドラーから結果を集める | `SendAll` または `SendAllAsync` |
| 購読者に順番に通知する | アセンブリ単位で登録して `PublishAsync` を呼ぶ |
| ストリームから1件ずつ読み取る | `StreamAsync` の結果を `await foreach` で列挙する |

`AddZendiator<App>` のように構成に名前を付けると、SendやStreamのパイプラインを明示できます。一つのMediatorでリクエスト送信と通知の両方を使うには、メッセージとハンドラーを含むアセンブリを `AddZendiator` に登録します。[リクエストを送って通知する例](docs/getting-started.ja.md#リクエストを送って通知する)

## ガイド

- [ストリームの実装、キャンセル、途中終了](docs/streaming.ja.md)
- [MediatRからの移行](docs/migrating-from-mediatr.ja.md)・[Zendiatorの更新](docs/upgrading.ja.md)
- [DIのライフタイム、互換性、Native AOTの対応範囲（英語）](docs/release/known-limitations.md)
- [構成の診断（英語）](docs/diagnostics/configuration.md)・[ハンドラー属性を補うIDEの修正機能（英語）](src/Zendiator.CodeFixes/README.md)

[リリースノート（英語）](docs/release/1.0.1-release-notes.md)・[MITライセンス](LICENSE)
