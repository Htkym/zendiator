# Zendiator

[English](README.md)

Zendiatorは、.NET 10とC# 14向けのプロセス内メディエーターです。ソースジェネレーターがビルド時にリクエストとハンドラーを結び付け、`Microsoft.Extensions.DependencyInjection` の登録コードを生成します。`SendAsync` でリクエストを送り、`PublishAsync` で購読者に通知し、`StreamAsync` で非同期ストリームを読み取れます。

**同期Beforeフックを5つ通したリクエスト送信は平均7.01～9.58 nsで、1回あたりのメモリ割り当ては0 Bでした。** この測定ではMediatorとリクエストを計測前に用意しています。

[最初のリクエストを送る](#最初のリクエストを送る)・[MediatRの代替として使う際の移行手順](docs/migrating-from-mediatr.ja.md)・[Windows x64で検証したNative AOT構成（英語）](docs/release/known-limitations.md#native-aot-and-dispatch-boundaries)

## 性能

Windows x64、.NET 10.0.14で、Zendiator 1.0.1、Immediate.Handlers 4.3.0、MediatR 15.0.0、Mediator 3.0.2、DispatchR.Mediator 2.3.1、Foundatio.Mediator 1.3.3の6ライブラリを比較しました。代表14ケースのうち、Immediate.Handlersには通知に相当するケースがないため、表では `N/A` としています。測定対象の83通りを正順と逆順で実行しました。

どちらの測定順でも、Zendiatorの平均処理時間は14ケースすべてで最短でした。割り当て量は7ケースで単独最小、残り7ケースで0 Bの同率最小でした。

[ケース別に6ライブラリの時間と割り当て量を見る](benchmarks/README.ja.md#ケース別の測定結果)

### 平均処理時間（正順）

| ケース | Zendiator 1.0.1 | Immediate 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR 2.3.1 | Foundatio 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send（0段） | 2.04 ns | 13.19 ns | 70.45 ns | 70.59 ns | 45.65 ns | 44.90 ns |
| Send（5段） | 9.58 ns | 26.84 ns | 239.07 ns | 153.75 ns | 52.36 ns | 235.80 ns |
| 同じスコープで再取得してSend（0段） | 33.52 ns | 54.41 ns | 82.66 ns | 114.87 ns | 79.80 ns | 86.41 ns |
| 同じスコープで再取得してSend（5段） | 35.75 ns | 57.85 ns | 197.13 ns | 147.42 ns | 83.03 ns | 226.87 ns |
| 新しいスコープで1回Sendして破棄（5段） | 131.93 ns | 155.14 ns | 268.97 ns | 227.14 ns | 774.62 ns | 389.28 ns |
| 同期処理を行う16個のハンドラーに通知 | 143.94 ns | N/A | 1,249.78 ns | 230.08 ns | 209.27 ns | 1,006.80 ns |
| 同期ストリームを全1,024件読む（5段） | 15.242 µs | 108.246 µs | 216.464 µs | 108.292 µs | 106.468 µs | 127.324 µs |
| 非同期ストリームを全1,024件読む（5段） | 167.413 µs | 311.844 µs | 624.129 µs | 329.834 µs | 316.544 µs | 352.939 µs |
| 同期ストリームを16件中1件で打ち切る（5段） | 306.07 ns | 348.98 ns | 951.59 ns | 453.85 ns | 377.63 ns | 741.37 ns |
| 非同期ストリームを16件中1件で打ち切る（5段） | 1.985 µs | 2.477 µs | 3.764 µs | 2.655 µs | 2.426 µs | 2.979 µs |
| 3段の先頭で短絡（繰り返し） | 4.88 ns | 5.72 ns | 111.61 ns | 72.43 ns | 36.11 ns | 72.04 ns |
| 非同期フックの成功（初回） | 1.050 µs | 1.518 µs | 1.570 µs | 1.341 µs | 1.296 µs | 2.182 µs |
| 非同期フックの成功（繰り返し） | 37.28 ns | 60.46 ns | 626.77 ns | 582.95 ns | 82.83 ns | 600.53 ns |
| 外側の非同期フックで失敗（初回） | 5.755 µs | 10.522 µs | 10.140 µs | 9.811 µs | 7.679 µs | 12.730 µs |

### 平均処理時間（逆順）

| ケース | Zendiator 1.0.1 | Immediate 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR 2.3.1 | Foundatio 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send（0段） | 1.65 ns | 9.22 ns | 44.16 ns | 45.71 ns | 32.68 ns | 36.79 ns |
| Send（5段） | 7.01 ns | 19.67 ns | 163.85 ns | 105.94 ns | 42.02 ns | 183.77 ns |
| 同じスコープで再取得してSend（0段） | 30.94 ns | 45.13 ns | 78.22 ns | 80.41 ns | 61.66 ns | 77.75 ns |
| 同じスコープで再取得してSend（5段） | 46.74 ns | 55.50 ns | 195.29 ns | 145.44 ns | 77.08 ns | 222.15 ns |
| 新しいスコープで1回Sendして破棄（5段） | 121.22 ns | 149.34 ns | 252.29 ns | 211.64 ns | 531.21 ns | 379.11 ns |
| 同期処理を行う16個のハンドラーに通知 | 145.13 ns | N/A | 1,165.25 ns | 228.70 ns | 209.78 ns | 1,004.07 ns |
| 同期ストリームを全1,024件読む（5段） | 14.783 µs | 106.837 µs | 211.958 µs | 109.380 µs | 105.834 µs | 126.890 µs |
| 非同期ストリームを全1,024件読む（5段） | 164.840 µs | 314.962 µs | 603.728 µs | 311.326 µs | 311.871 µs | 353.159 µs |
| 同期ストリームを16件中1件で打ち切る（5段） | 306.55 ns | 346.99 ns | 1,002.63 ns | 448.07 ns | 377.74 ns | 747.24 ns |
| 非同期ストリームを16件中1件で打ち切る（5段） | 2.224 µs | 2.395 µs | 5.922 µs | 2.814 µs | 2.427 µs | 3.066 µs |
| 3段の先頭で短絡（繰り返し） | 6.39 ns | 7.23 ns | 121.07 ns | 78.65 ns | 46.14 ns | 82.39 ns |
| 非同期フックの成功（初回） | 0.995 µs | 1.905 µs | 1.550 µs | 1.357 µs | 1.379 µs | 2.126 µs |
| 非同期フックの成功（繰り返し） | 45.68 ns | 62.46 ns | 934.94 ns | 642.18 ns | 81.59 ns | 599.94 ns |
| 外側の非同期フックで失敗（初回） | 5.229 µs | 11.194 µs | 10.144 µs | 8.254 µs | 8.459 µs | 13.546 µs |

### 1回あたりのメモリ割り当て量

単位はBです。正順と逆順で値が異なるセルには「正順 / 逆順」の順に両方を載せています。

| ケース | Zendiator 1.0.1 | Immediate 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR 2.3.1 | Foundatio 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send（0段） | 0 | 0 | 200 | 64 | 0 | 0 |
| Send（5段） | 0 | 0 | 1,016 | 544 | 0 | 0 |
| 同じスコープで再取得してSend（0段） | 0 | 0 | 200 | 64 | 0 | 0 |
| 同じスコープで再取得してSend（5段） | 0 | 0 | 1,016 | 544 | 0 | 0 |
| 新しいスコープで1回Sendして破棄（5段） | 560 | 568 | 1,400 | 920 | 1,712 | 856 |
| 同期処理を行う16個のハンドラーに通知 | 0 | N/A | 4,784 | 0 | 0 | 0 |
| 同期ストリームを全1,024件読む（5段） | 928 | 1,104 | 3,593 / 3,592 | 1,688 / 1,689 | 1,104 / 1,105 | 1,808 |
| 非同期ストリームを全1,024件読む（5段） | 1,586 | 1,755 | 4,726 | 2,340 / 2,339 | 1,756 | 2,539 |
| 同期ストリームを16件中1件で打ち切る（5段） | 928 | 1,104 | 3,592 | 1,688 | 1,104 | 1,808 |
| 非同期ストリームを16件中1件で打ち切る（5段） | 1,256 | 1,752 | 4,720 | 2,336 | 1,752 | 2,536 |
| 3段の先頭で短絡（繰り返し） | 0 | 0 | 704 | 496 | 0 | 384 |
| 非同期フックの成功（初回） | 448 | 504 | 1,352 | 808 | 560 | 856 |
| 非同期フックの成功（繰り返し） | 0 | 0 | 944 | 2,909 / 2,892 | 0 | 480 |
| 外側の非同期フックで失敗（初回） | 1,744 / 1,680 | 3,808 | 3,384 / 3,448 | 3,216 | 2,968 | 4,872 |

Sendの5段は、Zendiatorでは同期Beforeフック5つです。Streamの途中終了では、1件を読んで列挙子を破棄するまでを測ります。Zendiatorの各段は入力のカーソルをそのまま返し、比較先の各段は上流を列挙して `yield` します。今回のZendiatorの入力カーソルでは、非同期メソッドに1.0.0から利用できる `[GeneratePooledProducer]` を指定しています。

Zendiatorの平均時間の99.9%信頼区間の上限が全比較先の下限を下回ったのは、正順で14ケース、逆順で13ケースでした。非同期ストリームを1件で打ち切るケースでは、逆順で競合と信頼区間が重なりました。[測定条件](benchmarks/README.ja.md)・[全166件の測定結果（CSV）](benchmarks/results/f2973af-representative-summary.csv)

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
