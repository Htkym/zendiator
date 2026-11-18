# 同期Streamを1件で打ち切るときの改善案

READMEの代表ケースで明確に差があるのは、16件の入力に5段のBehaviorを通し、最初の1件を読んで破棄する `stream5-early-sync` である。Zendiatorの393.74 nsに対し、最速のImmediate.Handlersは286.42 nsだった。丸め前の平均値で計算すると、並ぶには1回あたり107.33 ns、Zendiatorの所要時間の約27.3%を削る必要がある。まず、上流を切り離した後の中間段の破棄を軽くする。そこで足りない分は、初回に5個の子カーソルを開く費用から探す。[READMEの性能比較](../../README.ja.md#性能比較)・[丸め前の測定値](../results/0.7.0-2bc3726-summary.csv)

## 現在の差

| ライブラリ | 平均時間（ns/op） | 信頼区間（ns/op） | 割当量（B/op） |
|---|---:|---:|---:|
| Immediate.Handlers 4.3.0 | 286.42 | 278.33–294.50 | 1,104 |
| DispatchR 2.3.1 | 312.13 | 302.80–321.46 | 1,104 |
| Mediator 3.0.2 | 380.91 | 372.82–389.01 | 1,688 |
| Zendiator 0.7.0 | 393.74 | 383.28–404.20 | 1,016 |
| Foundatio.Mediator 1.3.3 | 625.86 | 611.23–640.49 | 1,808 |
| MediatR 15.0.0 | 806.12 | 784.35–827.90 | 3,592 |

Zendiatorの割当量はすでに6社で最小で、Immediateより88 B少ない。表はコミット `2bc3726` のソースを1台のマシンで、各ケース1 launchずつ測った値である。[測定値と条件](../results/0.7.0-2bc3726-readme.json)

代表14行の平均時間でZendiatorが首位なのは12行。もう1行の「Behavior短絡・3段・繰り返し」は、Zendiatorが4.666 ns［4.501–4.831］、Immediateが4.633 ns［4.483–4.784］で、どちらも0 Bだった。こちらは信頼区間が重なるため、再測定で順位を確認する。[順位の判定基準](README.md#run-retain-and-decide)

## 1件を返すまでと、その後にかかる費用

`ZendiatorStream5.EarlyBreak` は、準備済みのscope、mediator、requestを使う。計時区間には `StreamAsync`、列挙子の取得、最初の `MoveNextAsync`、`Current` の読み出し、`await foreach` を抜けるときの `DisposeAsync` が入る。Zendiatorの5段はそれぞれ前処理を行って同じ要求とトークンを上流に渡し、受け取ったカーソルをそのまま返す。入力は名前付きの `WorkStreamCursor`。Immediateなどの競合はコンパイラ生成のiteratorを使い、Streamの各段で再列挙して `yield` する。順位を判定するときは、この公開表と同じ入力、段の処理、消費方法を使う。[ベンチマーク本体](../Zendiator.UseCaseBenchmarks/GeneratedCases.cs#L1057)・[各段の実装](../Zendiator.UseCaseBenchmarks/GeneratedCases.cs#L112)・[比較条件](README.md#public-row-mapping-and-comparison-boundaries)

同期の途中終了を0段にすると、Zendiatorは71.98 ns、Immediateは42.94 nsになる。Zendiatorの0段と5段の差は321.76 nsある。段の処理を前処理だけに揃えた別ケースのRelay5でも、Zendiatorは395.15 nsかかっている。長いStreamの1件あたりの処理より、段を開いて1件で閉じる費用を先に調べる理由である。0段と5段では生成経路も異なるため、差額を5で割って各段の原価とはみなせない。[0段とRelayの測定値](../results/0.7.0-2bc3726-summary.csv)

生成コードはすでに型付きの上流を使い、各段が同じカーソルを返してトークンも一致すると、最初の1件から末端の `MoveNextAsync` と `Current` を直接呼ぶ。初回のscope検査を根元に集約したPERF-02aも採用済みで、当時の順方向と逆方向の試験で約3〜4%短縮した。[末端に接続する生成処理](../../src/Zendiator.SourceGenerator/Emission/CanonicalStreamIdentityLeaseEmitter.cs#L115)・[PERF-02aの結果](../../docs/release/0.7.0-release-notes.md#stream-scope-check-verification-and-performance-decision)

初回列挙用オブジェクトの再利用、所有状態の埋め込み、Openと破棄の同期化をまとめたPERF-02b/cでは、割当量が1,016から952 B/opに減った。ところが時間は正順で365.64から489.15 ns、逆順で386.76から511.72 nsに延びた。これは別のA/B測定の値で、各変更の寄与は未測定である。数値は保存済みの `p2bc-summary02/DELIVERY.md`（対照 `6fbca3a`、候補 `0b5561e`）による。[過去の採否](README.md#disposition-as-of-2026-11-15)

## 試す変更と順序

最初に、既存の0・1・3・5段で `Creation`、`First`、`EarlyBreak` を同じ同期入力で測る。`Creation` は列挙前まで、`First` は最初のMoveと破棄までを含む。開始と破棄を分ける診断では、各列挙子を一度だけ使い、「取得済みで未開始の列挙子の最初のMove」と「1件読み終えた列挙子の破棄」を別々のバッチで測る。準備と計測後の後始末を計時区間の外に出す。この内訳で試作の順番を決め、採否には元の `EarlyBreak` 全体を使う。[既存の測定メソッド](../Zendiator.UseCaseBenchmarks/GeneratedCases.cs#L1072)

### 破棄するものが残っていない中間段を、その場で完了させる

5段すべてが上流カーソルをそのまま返すと、根元の `IdentityLease` は親子間の参照を外してから、末端から順に子を閉じる。このとき4つの中間段では、入力も使用中の上流もすでに空だが、各段は生成された `async DisposeCoreAsync` に入る。[破棄順序](../../src/Zendiator.SourceGenerator/Emission/CanonicalStreamIdentityLeaseEmitter.cs#L194)・[破棄処理の生成](../../src/Zendiator.SourceGenerator/Emission/CanonicalStreamFallbackEmitter.cs#L487)

最初の試作では、項目フックも所有セッションもなく、切り離しが済み、解放する参照が空の場合だけ、生成メソッドの入口で完了済みの `ValueTask` を返す。実際の入力を持つ末端と、全体を閉じる根元は従来の処理を通す。各段のトークン解放、状態更新、完了通知は引き続き `CloseClaimedAsync` と `RetireClaimedClose` が担う。これでユーザーの破棄処理を呼ぶ可能性がない段だけの効果を測れる。[実行時の状態更新](../../src/Zendiator/Streaming/StreamCursorState.cs#L339)

### 新しい子カーソルの初期化で取り直す排他を減らす

開始の費用が大きければ、こちらを先に試す。各段の `OpenUpstreamAsync` は子カーソルを作って親に登録し、直後の `OpenForMiddlewareAsync` が `TryBeginMove` で排他を取得して保留操作を確認する。初期化の所有権を確保した状態で新しい子を親に公開すれば、この取得を省ける可能性がある。公開後のMove、上流の再オープン、親が先に閉じる場合、保留・失敗時の解放を含め、公開時点から排他が有効であることを確かめる。破棄の変更とは別に測る。[子を生成する箇所](../../src/Zendiator.SourceGenerator/Emission/CanonicalStructStreamUpstreamEmitter.cs#L15)・[初期化の入口](../../src/Zendiator.SourceGenerator/Emission/CanonicalStreamFallbackEmitter.cs#L333)・[排他と保留操作の検査](../../src/Zendiator/Streaming/StreamCursorState.cs#L86)

両案を測っても差が残り、子の生成・所有権の引き渡しが主因と確認できた場合は、中間カーソル自体を省ける条件を調べる。候補は、ソースを確認できるsealedな実装が、前処理の後で `return next.OpenAsync(request, token)` を一度だけ行う連鎖である。元のmiddlewareは順番どおりに呼ぶ。全段で `next` やカーソルが外部に保存されず、要求・トークン・実行コンテキスト・例外の伝わり方まで保てると確認できるルートに限る。ほかのルートには現行のカーソルを使う。この案は生成の可否を調べる段階で、短縮幅は未測定である。[型付き呼び出しの選択条件](../../src/Zendiator.SourceGenerator/Analysis/Configuration/CanonicalStructStreamContractBinding.cs)・[上流の所有権と再オープンの契約](../../src/Zendiator.Abstractions/Streams/IStreamUpstream.cs)

## 最速になったかを判定する

- **正しさ。** 5段の呼び出し順、入力を1件だけ読むこと、途中終了でも入力を1回だけ破棄することを既存の正当性検査で確かめる。トークンが異なる段や別のカーソルを返す段、保留中のOpen、破棄の競合、後始末が失敗したときに届く例外と `AsyncLocal` の見え方も検証する。部分的にしか所有権を取得できなかった場合は特に重要である。[所有権のテスト](../../tests/Zendiator.Generator.Tests/CanonicalNativeStreamIdentityLeaseTests.cs#L309)・[保留するOpen](../../tests/Zendiator.Generator.Tests/CanonicalNativeStreamOpenFusionTests.cs#L10)・[破棄時のコンテキストと例外](../../tests/Zendiator.Generator.Tests/CanonicalNativeStreamTerminalOwnershipTests.cs#L158)
- **変更ごとの採否。** 現行版と候補版を、同じベンチマークとpackage lockを持つ別々のクリーンなworktreeに固定する。まず31セルの `perf-02a.json` を正順と逆順で実行する。対象行で現行版を上回るか、同じ回のImmediateにどこまで近づいたかを分けて記録する。割当量は現行の1,016 B/opを目安に同じ回の現行版と比較し、同期・非同期の全件列挙、非同期の途中終了、typed終端への反復可能な悪化も確認する。未列挙の `Creation` は別に確認する。[31セルの定義](perf-02a.json)・[変更前後を交互に測る手順](README.md#controlled-revision-comparison)
- **公開表の順位。** 候補を絞ったら122セルの公開表用matrixを使い、ほかの4社も含めて同条件で測る。正順・逆順の両方で、この行のZendiatorの平均が6社中最小になり、信頼区間の上限が同じ回の次点の下限を下回れば「この条件で最速」とする。区間が重なった場合や順序で勝敗が変わった場合は順位を保留し、各回の時間、信頼区間、B/opを掲載する。短絡と非同期Stream途中終了も、現在はImmediateと区間が重なるため、この回で再確認する。[122セルの定義](post-060-full-baseline.json)・[READMEの測定条件](../README.ja.md#現在のreadmeの比較)

リポジトリのルートで、`$pythonExe` にPython 3の実行ファイル、`$baselineTree` と `$candidateTree` に各worktreeの絶対パスを指定する。出力先には両ツリーの外にある未使用のパスを使う。公開表の確認時はmatrixを `post-060-full-baseline.json` に替え、出力先を新しくする。SDK 10.0.404、.NET 10.0.14 x64、Release、workstation GC、affinity 1、warmup 20回、計測12回、要求500 ms、各ケース1 launchを揃える。

```powershell
$driver = 'benchmarks/Zendiator.UseCaseBenchmarks/run_ab.py'
$matrix = 'benchmarks/performance-plan/perf-02a.json'
$pairArgs = @(
    '--baseline', $baselineTree, '--baseline-sha', (git -C $baselineTree rev-parse HEAD),
    '--candidate', $candidateTree, '--candidate-sha', (git -C $candidateTree rev-parse HEAD),
    '--matrix', $matrix, '--expect-sdk', '10.0.404'
)
& $pythonExe $driver @pairArgs --output D:/z3/o/stream5-early-forward
& $pythonExe $driver @pairArgs --output D:/z3/o/stream5-early-reverse --reverse
```
