# Zendiator

[English](README.md)

Zendiatorは.NET 10向けの高速なMediatorです。**同期Beforeフックを5つ通したリクエスト送信は平均6.40～6.71 nsで、1回あたりのメモリ割り当ては0 Bでした。** この測定ではMediatorとリクエストを計測前に用意しています。ハンドラーを定義して `SendAsync` を呼ぶと、ビルド時に生成したコードがリクエストをハンドラーに渡します。

## 性能

Windows x64、.NET 10.0.14で、1.0.0のリリース前のコミット `0d53c53` を測定しました。各セルは1回あたりの **平均処理時間 / メモリ割り当て量** です。正順と逆順では、同じ83通りの組み合わせを逆の順序で実行しました。Immediate.Handlersの通知には対応する測定ケースがないため、`N/A` としています。

どちらの測定順でも、Zendiatorの平均時間は14ケースすべてで最短でした。割り当て量が最小だったのは、同率を含めてどちらの順でも12ケースでした。

### 正順

| 処理 | Zendiator | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send（0段） | 1.65 ns / 0 B | 11.82 ns / 0 B | 51.49 ns / 200 B | 64.79 ns / 64 B | 43.08 ns / 0 B | 40.25 ns / 0 B |
| Send（5段） | 6.71 ns / 0 B | 20.95 ns / 0 B | 184.12 ns / 1,016 B | 113.78 ns / 544 B | 49.77 ns / 0 B | 188.89 ns / 0 B |
| 同じスコープで再取得してSend（0段） | 36.69 ns / 0 B | 47.57 ns / 0 B | 79.66 ns / 200 B | 97.67 ns / 64 B | 63.36 ns / 0 B | 80.63 ns / 0 B |
| 同じスコープで再取得してSend（5段） | 36.98 ns / 0 B | 58.94 ns / 0 B | 197.74 ns / 1,016 B | 154.22 ns / 544 B | 80.28 ns / 0 B | 257.67 ns / 0 B |
| 新しいスコープで1回Sendして破棄（5段） | 126.38 ns / 624 B | 173.62 ns / 568 B | 259.77 ns / 1,400 B | 213.77 ns / 920 B | 519.08 ns / 1,712 B | 400.80 ns / 856 B |
| 同期処理を行う16個のハンドラーに通知 | 140.12 ns / 0 B | N/A | 1,191.93 ns / 4,784 B | 229.14 ns / 0 B | 234.45 ns / 0 B | 898.58 ns / 0 B |
| 同期ストリームを全1,024件読む（5段） | 15.219 µs / 976 B | 110.913 µs / 1,104 B | 221.922 µs / 3,594 B | 113.622 µs / 1,689 B | 111.175 µs / 1,105 B | 130.435 µs / 1,809 B |
| 非同期ストリームを全1,024件読む（5段） | 201.291 µs / 116,322 B | 344.973 µs / 1,755 B | 640.243 µs / 4,726 B | 328.871 µs / 2,339 B | 317.359 µs / 1,756 B | 376.680 µs / 2,540 B |
| 同期ストリームを16件中1件で打ち切る（5段） | 317.31 ns / 976 B | 385.47 ns / 1,104 B | 1,021.16 ns / 3,592 B | 479.10 ns / 1,688 B | 396.62 ns / 1,104 B | 777.55 ns / 1,808 B |
| 非同期ストリームを16件中1件で打ち切る（5段） | 2.260 µs / 1,416 B | 2.508 µs / 1,752 B | 3.926 µs / 4,720 B | 2.621 µs / 2,336 B | 2.609 µs / 1,752 B | 3.265 µs / 2,536 B |
| 3段の先頭で短絡（繰り返し） | 4.83 ns / 0 B | 6.01 ns / 0 B | 108.08 ns / 704 B | 72.31 ns / 496 B | 36.04 ns / 0 B | 75.60 ns / 384 B |
| 非同期フックの成功（初回） | 1.023 µs / 448 B | 1.744 µs / 504 B | 1.629 µs / 1,352 B | 1.393 µs / 808 B | 1.335 µs / 560 B | 2.192 µs / 856 B |
| 非同期フックの成功（繰り返し） | 37.93 ns / 0 B | 58.05 ns / 0 B | 701.63 ns / 944 B | 553.04 ns / 496 B | 77.71 ns / 0 B | 563.80 ns / 480 B |
| 外側の非同期フックで失敗（初回） | 4.422 µs / 1,680 B | 10.076 µs / 3,808 B | 10.259 µs / 3,384 B | 7.776 µs / 3,216 B | 7.779 µs / 2,968 B | 11.838 µs / 4,872 B |

### 逆順

| 処理 | Zendiator | Immediate.Handlers 4.3.0 | MediatR 15.0.0 | Mediator 3.0.2 | DispatchR.Mediator 2.3.1 | Foundatio.Mediator 1.3.3 |
|---|---:|---:|---:|---:|---:|---:|
| Send（0段） | 1.74 ns / 0 B | 11.41 ns / 0 B | 45.66 ns / 200 B | 46.86 ns / 64 B | 37.79 ns / 0 B | 35.72 ns / 0 B |
| Send（5段） | 6.40 ns / 0 B | 20.25 ns / 0 B | 166.35 ns / 1,016 B | 104.04 ns / 544 B | 42.12 ns / 0 B | 184.23 ns / 0 B |
| 同じスコープで再取得してSend（0段） | 35.25 ns / 0 B | 44.22 ns / 0 B | 77.91 ns / 200 B | 85.60 ns / 64 B | 62.72 ns / 0 B | 73.56 ns / 0 B |
| 同じスコープで再取得してSend（5段） | 35.57 ns / 0 B | 59.94 ns / 0 B | 195.30 ns / 1,016 B | 148.86 ns / 544 B | 73.49 ns / 0 B | 221.97 ns / 0 B |
| 新しいスコープで1回Sendして破棄（5段） | 135.44 ns / 624 B | 159.43 ns / 568 B | 266.79 ns / 1,400 B | 208.16 ns / 920 B | 555.78 ns / 1,712 B | 383.04 ns / 856 B |
| 同期処理を行う16個のハンドラーに通知 | 145.94 ns / 0 B | N/A | 1,221.92 ns / 4,784 B | 239.62 ns / 0 B | 204.91 ns / 0 B | 871.66 ns / 0 B |
| 同期ストリームを全1,024件読む（5段） | 15.125 µs / 976 B | 110.807 µs / 1,104 B | 222.092 µs / 3,592 B | 110.216 µs / 1,688 B | 117.380 µs / 1,104 B | 128.873 µs / 1,808 B |
| 非同期ストリームを全1,024件読む（5段） | 192.900 µs / 116,320 B | 325.066 µs / 1,752 B | 628.665 µs / 4,720 B | 324.403 µs / 2,336 B | 323.658 µs / 1,752 B | 366.102 µs / 2,536 B |
| 同期ストリームを16件中1件で打ち切る（5段） | 296.50 ns / 976 B | 385.53 ns / 1,104 B | 904.29 ns / 3,592 B | 525.73 ns / 1,688 B | 379.99 ns / 1,104 B | 797.66 ns / 1,808 B |
| 非同期ストリームを16件中1件で打ち切る（5段） | 2.048 µs / 1,416 B | 2.283 µs / 1,752 B | 3.612 µs / 4,720 B | 2.638 µs / 2,336 B | 2.314 µs / 1,752 B | 2.884 µs / 2,536 B |
| 3段の先頭で短絡（繰り返し） | 4.63 ns / 0 B | 5.44 ns / 0 B | 98.72 ns / 704 B | 69.38 ns / 496 B | 34.87 ns / 0 B | 67.59 ns / 384 B |
| 非同期フックの成功（初回） | 0.995 µs / 448 B | 1.456 µs / 504 B | 1.466 µs / 1,352 B | 1.306 µs / 808 B | 1.231 µs / 560 B | 2.083 µs / 856 B |
| 非同期フックの成功（繰り返し） | 36.08 ns / 0 B | 57.34 ns / 0 B | 596.42 ns / 944 B | 546.56 ns / 496 B | 82.08 ns / 0 B | 558.79 ns / 2,902 B |
| 外側の非同期フックで失敗（初回） | 4.578 µs / 1,680 B | 10.301 µs / 3,808 B | 9.910 µs / 3,384 B | 9.699 µs / 3,216 B | 7.846 µs / 2,968 B | 11.936 µs / 4,872 B |

Sendの5段は、Zendiatorでは同期Beforeフック5つです。Streamの途中終了では、1件を読んで列挙子を破棄するまでを測ります。Zendiatorの各段は入力のカーソルをそのまま返し、比較先の各段は上流を列挙して `yield` します。

Zendiatorの平均時間の99.9%信頼区間の上限が全比較先の下限を下回ったのは、正順で13ケース、逆順で13ケースでした。[測定条件](benchmarks/README.ja.md)・[全166件の測定結果（CSV）](benchmarks/results/0d53c53-representative-summary.csv)

## インストール

.NET 10とC# 14を使うプロジェクトに、次のNuGetパッケージを追加してください。

```shell
dotnet add package Zendiator --version 1.0.0
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

[リリースノート（英語）](docs/release/1.0.0-release-notes.md)・[MITライセンス](LICENSE)
