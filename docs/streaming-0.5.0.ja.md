# 0.5.0 Streamの移行と実行契約

0.5.0では名前付きcursorを開くHandlerを使います。MediatorのStreamAsync(request, apiToken)は標準IAsyncEnumerableとしてsource objectを返します。typed terminalにはStreamSource(request, apiToken).ConsumeAsync(sink, enumerationToken)を使います。StreamSourceの返り値はreadonlyの計画値で、IAsyncEnumerableではありません。既定のChannelや別producer taskは置きません。入口の形だけで時間や割当の優位は保証しません。標準列挙、typed terminal、HTTPは対象の版・条件・実測結果を分けて評価します。

## yield Handlerから名前付きcursorへ

HandlerはIStreamSourceHandler<TRequest, TItem, TCursor>を実装し、OpenAsyncで名前付きIAsyncEnumeratorを返します。privateなcompiler iterator型を公開signatureへ書くことはできません。生成コードからアクセスできるclass cursorも使え、structを必須にはしません。

~~~csharp
public sealed record ReadNames(int Count) : IStreamRequest<string>;

[ZendiatorHandler]
public sealed class ReadNamesHandler : IStreamSourceHandler<ReadNames, string, NameCursor>
{
    public ValueTask<NameCursor> OpenAsync(ReadNames request, CancellationToken token) =>
        new(new NameCursor(request.Count, token));
}

public sealed class NameCursor(int count, CancellationToken token) : IAsyncEnumerator<string>
{
    private int index;
    public string Current { get; private set; } = null!;
    public async ValueTask<bool> MoveNextAsync()
    {
        if (index >= count) return false;
        token.ThrowIfCancellationRequested();
        await Task.Yield();
        token.ThrowIfCancellationRequested();
        Current = $"name-{index++}";
        return true;
    }
    public ValueTask DisposeAsync() { index = count; return default; }
}
~~~

Task.Yieldはデモ用の非同期境界です。実アプリのread、write、DB、network等のI/Oとfinallyの後始末は、OpenAsyncやcursorへ移してください。Task.Yieldや全件bufferで代用しません。既存IAsyncEnumerableをtyped外装に包むだけでは、内側のinterface呼出しやiterator処理は消えません。

~~~csharp
[ZendiatorComposition("names")]
public sealed class App;

services.AddZendiator<App>(configuration =>
{
    configuration.Handlers<ReadNames>(DispatchFamily.Stream, typeof(ReadNamesHandler));
    configuration.Pipeline<ReadNames>(DispatchFamily.Stream);
});
~~~

要求と順序はHandlers/Pipelineで明示します。Filter/Mapは専用hookとStage.Hook、所有sessionはHandlerEntry.Sessionやfactory付きstageを使います。任意middlewareはIStreamMiddleware.OpenAsyncとStage.Wrapperで登録し、nextはIStreamUpstreamです。この箇所はIAsyncEnumeratorへの明示的な型消去境界であり、typed hookと同じ費用とは扱いません。旧AddOpenStreamBehavior、Validator、OwnsStreamによるbackend選択は使用しません。

## 標準列挙とtyped terminal

~~~csharp
var source = mediator.StreamAsync(new ReadNames(3), token);
await foreach (var name in source.WithCancellation(enumerationToken))
    Console.WriteLine(name);
~~~

typed terminalではIStreamSinkのOnNextAsync(item, token)を実装します。trueは継続、falseは正常停止です。

~~~csharp
var names = new List<string>();
await mediator.StreamSource(new ReadNames(3), token).ConsumeAsync(new NameSink(names), enumerationToken);

public readonly struct NameSink(List<string> names) : IStreamSink<string>
{
    public ValueTask<bool> OnNextAsync(string item, CancellationToken token)
    {
        names.Add(item);
        return new(true);
    }
}
~~~

sinkは値渡しなので、struct sinkの結果はList等の共有ownerへ保存します。class sinkの状態も共有されます。同じsinkを複数の列挙で共有する場合、状態の分離や同期は利用者の責任です。生成コードは借用sinkをDisposeしません。

StreamSourceの計画値は標準列挙用sourceとは別です。ConsumeAsyncごとに非公開の専用ownerを新しく作り、標準列挙のownerとは共有しません。計画値を再利用しても、各消費の停止状態とcleanupは独立します。標準列挙やHTTPへ返す場合はStreamAsyncを使ってください。

## 開始・取消し・操作の重複

opaque middlewareを挟まない単一segmentの経路（standalone）では、StreamAsyncの呼出し時に割り当てたsource objectを最初の列挙のownerとして使います。後続のGetAsyncEnumeratorは独立したownerを作ります。このsourceはIAsyncEnumeratorも実装し、そのinterface経由でDisposeしても閉じるのは最初のownerだけです。その後のGetAsyncEnumeratorは独立したcloneを返します。このcastは全StreamAsyncに共通する契約ではありません。

最初のGetAsyncEnumeratorより前のsource aliasからのMoveは拒否します。初回Getがtokenを設定している間のDisposeもInvalidOperationExceptionとなり、設定中のownerを閉じたり資源を解放したりしません。

opaque middlewareを含む経路は、immutableな計画を保持するclassのenumerable adapterを返し、GetAsyncEnumeratorごとに新しいguarded cursorを作ります。standaloneでは列挙しない場合も最初のownerを入口で割り当てる一方、この経路ではsource adapterと列挙ownerが別です。

従来の値型sourceだけを作って列挙せずに捨てる使い方と比べ、standaloneの入口でowner objectを割り当てる費用が増えます。typed処理ではStreamSourceの値型計画を使い、標準source objectを作らずに消費できます。列挙しないsourceの生成・保持と、標準列挙やtyped terminalでの消費は別のworkloadです。割当量と時間をそれぞれ実測して評価します。

StreamAsync、StreamSource、GetAsyncEnumeratorだけでは、user DI、HandlerのOpenAsync、owned session、I/Oを開始しません。各ownerの最初のMoveで開始します。API tokenと列挙tokenは、両方が取消可能で異なる場合だけ初回Moveで連結します。ConsumeAsyncのtokenも列挙tokenと同様です。開始前のDisposeはuser DIやlinked CTSを作りません。

pending Move中の別MoveとDisposeは、同期的にInvalidOperationExceptionとなります。停止を予約したり、自動joinしたりしません。先のMoveを一度awaitしてからDisposeしてください。Disposeがpendingの間は新しいMoveを開始できません。その間の重複Disposeは同じTask-backed完了を共有します。返された各ValueTaskは一度だけ消費します。

Currentは成功したMoveの後から、次の受理されたMove/Disposeまで有効です。EOF、失敗、Disposeの後は無効です。Current getter自体の例外にもfinally/await usingで対処してください。false Filterは公開itemを返さず、同じMoveの中で取消しを再確認して次のsource itemへ進みます。公開したitemより先を先読みしません。

terminalはcallbackを逐次完了までawaitします。falseを受理すると全体の正常停止が確定し、後からtokenが取り消されても遡って取消し例外にしません。trueの後は次itemの取得前にtokenを検査します。callbackにはAPI tokenを含む実効tokenを渡します。

## middleware・失敗・所有権

middlewareは同時に一つのupstream cursorだけを持ち、各cursorを破棄します。内側の正常EOFとcleanupが完了した場合だけnextを再度開けます。early Dispose、失敗、外側の終了、terminalのfalseで全体が閉じた後は再開できません。失敗した同じupstreamを再度開くretryはできません。正常完了した内側のcursorを明示的に再度開く場合、既に返したitemが重複し得ます。自動retryは行いません。

sourceの読取例外はsource-side middlewareが扱えます。sink、await foreach本体、serializerの例外はsourceのMoveへ注入せず、middlewareで回復する契約にはしません。pending callbackを残してcleanupしません。callbackの終了後にcursorとowned sessionを一度、所有順の逆に破棄します。cleanup自体の失敗は、通常のtry/finallyと同様に先の失敗へ優先します。

DIから借用したHandler、hook、factory、providerとsinkは生成cursorの所有物ではありません。DI scopeは利用者/hostが、列挙とcleanupの完了まで保持します。action内のusing scopeを抜けてからsourceをserializerへ返してはいけません。

内部awaitはConfigureAwait(false)を使い、UI SynchronizationContextへの復帰を保証しません。ExecutionContextは抑制しません。以前のbackendと同期AsyncLocal変更の見え方が同じとは約束しません。

## HTTPから自然に返す

Controllerは生成sourceを標準interfaceとして返せます。

~~~csharp
[HttpGet]
public IAsyncEnumerable<string> GetNames(CancellationToken cancellationToken) =>
    mediator.StreamAsync(new ReadNames(3), cancellationToken);
~~~

ASP.NET Coreの標準System.Text.Json formatterを使う場合、JSON配列のframing、options/metadata、列挙とDisposeは標準serializerへ任せます。request scopeはserializationとcursor cleanupまで保持し、RequestAbortedを読取へ伝えます。最初や途中のpending、client切断、source/serializer失敗は独立したHTTP検証対象です。書込み開始後の失敗は正常な未送信配列へ差し替えられるとは限らず、成功を装う閉じ括弧を追加しません。直接HTTP writer sinkは別のintegrationで、ここでは実装していません。

標準IAsyncEnumerable、typed terminal、HTTP serializationは別の評価経路です。一つだけの改善を他の改善と呼びません。外部MoveNext制御やpull zipは標準列挙境界を使います。旧API互換backendや既定Channel bridgeは追加しません。

## sampleと測定資料

[GettingStarted](../samples/Zendiator.Sample.GettingStarted/Program.cs)はreadonly sink、[ComposedApp](../samples/Zendiator.Sample.ComposedApp/Program.cs)は標準列挙を示します。Application/Hostは既存Sendの登録を変えず、Stream専用named compositionを追加します。入口はいずれもMediatorです。

ベンチのpeer側Work.Streamと登録条件は保持し、Z側は名前付きcursorとcanonical Stream compositionを使います。このDI/configとprotocolの違いを記録し、保存済み0.4.0/w64等のrawを上書き・再解釈しません。Full/EarlyBreakは標準列挙、FullTyped/EarlyBreakTypedはtyped terminalです。後者のsinkはfixture内のclass状態を逐次再利用します。HTTPを含む測定ではありません。protocolやDI/configを含む製品経路全体の比較として、標準列挙とtyped terminalの実測結果を分けて評価します。dispatcher単独の改善や、測定していないstage・実I/O・HTTPの性能へは一般化しません。API移行やsource変更だけでは改善の根拠になりません。
