# Stream で結果を順に返す

[English](streaming.md)

ハンドラーがすべての結果を待たずに項目を返せるときは、`StreamAsync` を使います。呼び出し側は `await foreach` で各項目を受け取り、必要な分だけ読んだら `break` で終了できます。

## ハンドラーを定義する

次のハンドラーは、1 から指定した個数までの整数を順番に返します。非同期イテレーターの引数に `[EnumeratorCancellation]` を付けると、呼び出し側からキャンセルを伝えられます。[サンプル全体](../samples/Zendiator.Sample.GettingStarted/Program.cs)

```csharp
using System.Runtime.CompilerServices;
using Zendiator;

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
```

`AddZendiator<App>` のラムダ式でハンドラーとパイプラインを登録します。整数をそのまま返すには、`c.Pipeline<ReadNumbers>(DispatchFamily.Stream)` で空のパイプラインを指定します。ここでは偶数を選び、10 倍してから、40 以下の数だけを残します。

```csharp
c.Handlers<ReadNumbers>(DispatchFamily.Stream, typeof(ReadNumbersHandler));
c.Pipeline<ReadNumbers>(DispatchFamily.Stream,
    Stage.Hook<KeepEven>(), Stage.Hook<TimesTen>(), Stage.Hook<KeepAtMostForty>());
```

次のループは `20` と `40` を順に表示します。サンプルでは `IStreamItemFilter` と `IStreamItemMap` で各フックを定義しています。途中で項目の型を変える場合は、`StreamPipeline<TRequest, TFinal>` で最後の型を指定します。[ComposedApp の例](../samples/Zendiator.Sample.ComposedApp/Program.cs) では、末尾で `int` を `string` に変換し、`StreamPipeline<ReadNumbers, string>` と登録しています。

```csharp
await foreach (var number in mediator.StreamAsync(new ReadNumbers(6), token))
{
    Console.WriteLine(number);
}
```

## 入力の型と読み始めるタイミング

`IAsyncEnumerable<TItem>` を返すハンドラーは `IStreamHandler<TRequest, TItem>.Handle` を実装します。入力元がすでに独自のカーソルを持つ場合は、`IStreamSourceHandler<TRequest, TItem, TCursor>.OpenAsync` が使えます。カーソルの型にはクラスと構造体の両方を使えます。リクエストごとにどちらかのインターフェイスを選び、列挙のたびに独立したカーソルを返してください。DI がハンドラーを再利用する場合も同じです。

`StreamAsync` を呼んだ時点では、入力を開きません。入力を開くタイミングは、登録した処理とキャンセルトークンによって決まります。

| 条件 | 入力を開くタイミング | 後始末 |
|---|---|---|
| `IStreamHandler` のパイプラインが空で、Before 処理も所有セッションもなく、呼び出しと列挙の両方のトークンでキャンセルできない場合 | `GetAsyncEnumerator` がハンドラーを取得して `Handle` を呼び、カーソルを取得します。最初の項目は最初の移動で読みます。 | 末尾に達した場合や読み取りに失敗した場合も、呼び出し側は破棄の完了を待ちます。移動や破棄で発生する例外は入力元の実装に従います。 |
| それ以外の `StreamAsync` | 最初の移動で Before 処理とパイプラインを開始します。ミドルウェアを使う場合は、その実装が入力を開くタイミングを決めます。 | Zendiator は取得したカーソルと所有セッションを破棄してから、入力の終了や失敗を呼び出し側に伝えます。`await foreach` を途中で抜けた場合も破棄の完了を待ちます。 |
| `StreamSource(request, token).ConsumeAsync(sink)` | `ConsumeAsync` を呼ぶたびに項目の読み取りを開始します。 | 処理が終わると、Zendiator が取得したカーソルと所有セッションを破棄します。sink の所有者は呼び出し側です。 |

呼び出し元のキャンセルトークンを `StreamAsync` に渡してください。`WithCancellation` で渡した列挙用のトークンも使えます。Zendiator がカーソルの寿命を管理する場合は、両方のトークンを組み合わせ、入力を読む前にキャンセルを確認します。列挙子を直接使うときは、`MoveNextAsync` の完了を待ってから次の移動や破棄を始めます。`Current` を読めるのは、移動が成功してから次の操作を始めるまでです。Zendiator は所有するリソースを取得と逆の順序で一度ずつ破棄します。後始末で例外が発生した場合は、最後の例外が呼び出し側に届きます。

## 最初の項目を読む前に検証する

列挙ごとに一度だけリクエストを検証するには、`IAsyncStreamBefore<TRequest>.BeforeAsync` を実装します。`c.StreamBefore<ReadNumbers>(typeof(ValidateReadNumbers))` と登録すると、最初の移動で入力を開く前に実行します。複数の処理は登録順に呼び出します。ミドルウェアが入力を開き直しても、検証は繰り返しません。リクエストの差し替え、空のストリームの返却、準備と後始末をひと組にした処理には、ミドルウェアを使います。

## ミドルウェアでカーソルを扱う

`IStreamMiddleware<TRequest, TItem>` を実装し、`Stage.Wrapper<T>()` で登録します。`OpenAsync<TNext>` では引数に `TNext next` を取り、制約に `where TNext : struct, IStreamUpstream<TRequest, TItem>` を指定します。`IStreamUpstream<TRequest, TItem>` を直接受け取るオーバーロードも使えます。

項目の読み取りと破棄を上流に任せるには、`next.OpenAsync(request, token)` で取得したカーソルをそのまま返します。Zendiator は列挙時に、返ったカーソルとキャンセルトークンを確認します。ローカル変数や非同期の補助メソッドを経由してカーソルを返しても、この判定は同じです。項目の読み方や破棄時の処理を変えるには、独自のカーソルで上流のカーソルを包みます。その場合は、独自のカーソルが上流のカーソルも破棄します。[ログを記録するミドルウェアの例](../samples/Zendiator.Sample.Application/Behaviors/StreamLoggingBehavior.cs)

ミドルウェアが同時に開ける上流のカーソルは一つです。入力を末尾まで読み、後始末が正常に完了すると、入力を開き直せます。その際、すでに届けた項目がもう一度届く場合があります。途中で列挙を打ち切った場合や失敗した場合は、同じ列挙で入力を開き直せません。Zendiator は失敗した読み取りを自動で再試行しません。

## 項目を sink に渡す

項目を一つずつ処理するコンポーネントには `IStreamSink<TItem>` を実装し、`await mediator.StreamSource(request, token).ConsumeAsync(sink)` で渡せます。Zendiator は `OnNextAsync(item, token)` の完了を待ってから次の項目を読みます。続けるときは `true`、正常終了するときは `false` を返してください。構造体の sink は値渡しです。呼び出し側で処理結果を使うには、双方が参照できるオブジェクトに結果を保存します。

## HTTP エンドポイントから返す

`StreamAsync` にリクエストのキャンセルトークンを渡し、戻り値の `IAsyncEnumerable<TItem>` をエンドポイントから返します。シリアル化とカーソルの破棄が終わるまで、DI スコープを維持してください。ASP.NET Core は設定済みのシリアライザーで応答を書き込みます。書き込みを始めてからエラーが起きると、応答が途中で途切れることがあります。[DI スコープの寿命](getting-started.ja.md)
