# Zendiator

[English](README.md)

Zendiatorは.NET 10向けの高速なMediatorです。**同期Beforeフックを5つ通したリクエスト送信は平均6.4～6.7 nsで、1回あたりのメモリ割り当ては0 Bでした。** この測定ではMediatorとリクエストを計測前に用意しています。ハンドラーを定義して `SendAsync` を呼ぶと、ビルド時に生成したコードがリクエストをハンドラーに渡します。

## 性能

リクエスト、通知、ストリーム、フックを含む代表14ケースで比較しました。すべてのケースでZendiatorの平均時間が最も短く、測定順を逆にしても同じ結果でした。次の表はWindows x64、.NET 10.0.14でコミット `0d53c53` を測定した正順の結果です。

| 処理 | Zendiator | 次に速かったライブラリ | Zendiatorの割り当て量（1回あたり） |
|---|---:|---:|---:|
| 同期Beforeフックを5つ通してリクエストを送信 | 6.71 ns | Immediate.Handlers（20.95 ns） | 0 B |
| 同期処理を行う16個のハンドラーに通知 | 140.12 ns | Mediator（229.14 ns） | 0 B |
| 5段のミドルウェアを通る同期ストリームを1件読んで破棄 | 317.31 ns | Immediate.Handlers（385.47 ns） | 976 B |

測定は `1.0.0-rc.1` の作成前に行いました。非同期ストリームを1件で打ち切るケースだけは、どちらの測定順でも競合と信頼区間が重なりました。[全14ケースの結果、割り当て量と測定条件](benchmarks/README.ja.md)

## インストール

.NET 10とC# 14を使います。リリース候補を試すには、`1.0.0-rc.1` を入手できるNuGetフィードを選び、次のコマンドを実行してください。

```shell
dotnet add package Zendiator --version 1.0.0-rc.1
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

[リリースノート（英語）](docs/release/1.0.0-rc.1-release-notes.md)・[MITライセンス](LICENSE)
