"""Render the checked BDN matrix without interpreting one-launch differences."""

import csv
import sys

from artifact_gate import filesystem_path, resolve_path, read


if len(sys.argv) != 2:
    raise SystemExit("Usage: python make_report.py OUTPUT_ROOT")
OUTPUT = resolve_path(sys.argv[1])
ROWS = list(csv.DictReader(filesystem_path(OUTPUT / "all-results.csv").open(encoding="utf-8")))
LIBS = {
    "Zendiator": "Zendiator",
    "Immediate": "Immediate.Handlers 4.2.0",
    "MediatRHistorical": "MediatR 14.2.0",
    "Mediator": "Mediator.SourceGenerator 3.0.2",
    "DispatchR": "DispatchR.Mediator 2.3.1",
}


def result(prefix, kind, depth, method, count="", asynchronous=""):
    type_name = f"{prefix}{kind}{depth}"
    matches = [
        row for row in ROWS
        if row["Type"] == type_name
        and row["Method"] == method
        and str(row["Count"]) == str(count)
        and str(row["Asynchronous"]) == str(asynchronous)
    ]
    if not matches:
        return "対象外"
    if len(matches) != 1:
        raise ValueError(f"Ambiguous result: {type_name}.{method}")
    row = matches[0]
    mean = float(row["MeanNs"])
    allocated = float(row["AllocatedBytes"])
    number = f"{mean:.1f}" if mean < 100 else f"{mean:.0f}"
    return f"{number} ns / {allocated:.0f} B"


def table(cases):
    names = list(LIBS.values())
    lines = ["| ケース | " + " | ".join(names) + " |", "|---|" + "---:|" * len(names)]
    for label, kind, depth, method, count, asynchronous in cases:
        cells = [result(prefix, kind, depth, method, count, asynchronous) for prefix in LIBS]
        lines.append("| " + label + " | " + " | ".join(cells) + " |")
    return "\n".join(lines)


metadata = read(OUTPUT / "runs" / "send" / "run-info.json")
execution_order_note = (
    "ライブラリ横断の比較キー順に各ライブラリを単一ケースで起動し、先頭ライブラリを交代した。実行ログと計画の完全一致を確認した。"
    if metadata.get("executionOrder") == "cross-library-interleaved-verified" else
    "BDN 0.15.8 は benchmark type ごとに実行したため、ライブラリ別の連続ブロックとなった。同じケースのライブラリの交互実行や先頭ライブラリの交代は行われておらず、小さな時間差には長時間の状態変化の影響があり得る。"
)
revision = metadata.get("revision", metadata.get("commit"))
if not revision:
    raise ValueError("The send run has no source revision")
source_digest = metadata.get("sourceDigest")
manifest = read(OUTPUT / "runs" / "send" / "manifest.json")
generated = next((item["sha256"] for item in manifest.get("generated", []) if item["path"].endswith("Zendiator.g.cs")), None)
product = (metadata.get("productHashes") or [None])[0]
environment = "、".join(part for part in (
    f"SDK {metadata['sdk']}" if metadata.get("sdk") else "",
    manifest.get("runtime", ""),
    f"製品 DLL `{product}`" if product else "",
    f"生成コード `{generated}`" if generated else "",
) if part)
parts = [
    "# ユースケース別の競合ライブラリ比較",
    "",
    f"Zendiator revision `{revision}`" + (f"、ソース digest `{source_digest}`" if source_digest else "") + "。数値は BenchmarkDotNet の mean / allocated bytes。`all-results.csv` に median、標準偏差、有効 iteration 数も収録した。各 `runs/<group>/results/*-full.json` に生データ、`run.log` に警告、`run-info.json` に成功件数と子プロセスの DLL hash を残した。",
    "",
    f"測定環境: {environment}。" if environment else "",
    "",
    "この表は 1 launch の探索比較。API と業務コードの形が異なるため、小差やライブラリ全体の順位を確定しない。`対象外` は本ハーネスに同等ケースがないことを示し、速度 0 や機能の不在を意味しない。",
]

for method, title in (
    ("Typed", "取得済み入口からの Send"),
    ("ResolveSend", "同じ Scope で入口を再取得して Send"),
    ("ScopeK1", "新しい Scope で 1 回 Send"),
    ("ScopeK10", "新しい Scope で 10 回 Send"),
):
    parts += ["", f"## {title}", "", table([(f"Behavior {depth} 段", "Send", depth, method, "", "") for depth in (0, 1, 3, 5)])]

parts += [
    "",
    "## 新しい Scope での入口の初回取得",
    "",
    "Controller に Scoped の入口を注入して 1 回 Send する用途に近い形を、段階ごとに別ケースとして測る。Provider は事前に構築した。`初回取得＋Send` は計測外で作成した Scope を 1 回の操作ごとに 1 つ使い、同じ Scope での再取得にならないようにした（BDN の invocation 16384 回、unroll 1、Scope の作成と破棄は iteration 単位の setup／cleanup）。行どうしの単純な差を構築・解決のコストとはみなさない。HTTP や MVC のパイプライン全体は含まない。",
    "",
    table([("Scope 作成・破棄のみ", "Send", 0, "ScopeOnly", "", "")]
          + [(f"Behavior {depth} 段、Scope 作成・入口の初回取得・破棄", "Send", depth, "ScopeResolve", "", "") for depth in (0, 1, 3, 5)]
          + [(f"Behavior {depth} 段、用意済み Scope で入口の初回取得＋Send", "Send", depth, "FirstSend", "", "") for depth in (0, 1, 3, 5)]),
]

parts += [
    "",
    "## 戻り値のない要求（取得済み入口）",
    "",
    table([(f"Behavior {depth} 段", "Void", depth, "Dispatch", "", "") for depth in (0, 1, 3, 5)]),
    "",
    "## Generic 要求（取得済み入口）",
    "",
    table([(name, name, "", "Dispatch", "", "") for name in ("Closed", "Open")]),
    "",
    "## Notification（取得済み入口）",
    "",
    table([(f"購読者 {count}、同期完了", "Notification", count, "Dispatch", "", "") for count in (0, 1, 4, 16)]
          + [(f"購読者 {count}、Task.Yield で中断", "Notification", count, "DispatchAsync", "", "") for count in (1, 4, 16)]),
]

parts += [
    "",
    "## Stream 全列挙、16 項目",
    "",
    table([(f"Behavior {depth} 段、{'非同期中断' if asynchronous else '同期完了'}", "Stream", depth, "Full", 16, asynchronous) for depth in (0, 1, 3, 5) for asynchronous in (False, True)]),
    "",
    "## Stream 全列挙、1024 項目",
    "",
    table([(f"Behavior {depth} 段、{'非同期中断' if asynchronous else '同期完了'}", "Stream", depth, "Full", 1024, asynchronous) for depth in (0, 5) for asynchronous in (False, True)]),
    "",
    "## Stream の生成・部分列挙・キャンセル（16 項目）",
    "",
    table([(f"Behavior {depth} 段、{label}", "Stream", depth, method, 16, asynchronous)
           for depth in (0, 5)
           for label, method, asynchronous in (
               ("生成のみ", "Creation", False),
               ("先頭 1 件、同期完了", "First", False),
               ("先頭 1 件、非同期中断", "First", True),
               ("早期終了、同期完了", "EarlyBreak", False),
               ("早期終了、非同期中断", "EarlyBreak", True),
               ("途中キャンセル、非同期中断", "Cancellation", True),
           )]),
]

RELAY_STAGE = {
    "creation": "Stream 生成時に同期 throw",
    "enumerator": "GetAsyncEnumerator で throw",
    "move-call": "MoveNextAsync の呼び出しで同期 throw",
    "move-await": "初回 MoveNextAsync の完了で throw",
}


def relay_timing():
    path = OUTPUT / "runs" / "relay" / "correctness.json"
    if not filesystem_path(path).exists():
        return []
    rows = [row for row in read(path) if row.get("suite") == "RelayTiming"]
    lines = ["| ライブラリ | Behavior 段数 | 前処理の実行時点 | 前処理の例外 |", "|---|---:|---|---|"]
    for row in rows:
        when = ("Stream 生成時" if row["eventsAtCreation"] else
                "GetAsyncEnumerator 時" if row["eventsAtEnumerator"] else "初回 MoveNextAsync 時")
        lines.append(f"| {LIBS[row['library']]} | {row['behaviors']} | {when} | {RELAY_STAGE[row['failureStage']]} |")
    return ["", "前処理の実行時点と例外の出方（測定前の確認で記録）:", "", "\n".join(lines)]


if any(row["Group"] == "relay" for row in ROWS):
    parts += [
        "",
        "## 前処理のみの Stream Behavior、全列挙 16 項目",
        "",
        "各ライブラリの Behavior を、前処理の後に next の列挙元をそのまま返す非 async 実装にそろえた独立ケース。上の Stream 表（async iterator で中継する Behavior）とは処理内容が異なるため、表をまたいで比較しない。",
        "",
        table([(f"Behavior {depth} 段、{'非同期中断' if asynchronous else '同期完了'}", "Relay", depth, "Full", 16, asynchronous) for depth in (1, 5) for asynchronous in (False, True)]),
        "",
        "## 前処理のみの Stream Behavior、全列挙 1024 項目",
        "",
        table([(f"Behavior 5 段、{'非同期中断' if asynchronous else '同期完了'}", "Relay", 5, "Full", 1024, asynchronous) for asynchronous in (False, True)]),
        "",
        "## 前処理のみの Stream Behavior、生成・部分列挙・キャンセル（16 項目）",
        "",
        table([(f"Behavior 5 段、{label}", "Relay", 5, method, 16, asynchronous)
               for label, method, asynchronous in (
                   ("生成のみ", "Creation", False),
                   ("先頭 1 件、同期完了", "First", False),
                   ("先頭 1 件、非同期中断", "First", True),
                   ("早期終了、同期完了", "EarlyBreak", False),
                   ("早期終了、非同期中断", "EarlyBreak", True),
                   ("途中キャンセル、非同期中断", "Cancellation", True),
               )]),
    ] + relay_timing()

parts += [
    "",
    "## 測定条件と読み方",
    "",
    f"- Release、CPU affinity 1。BDN の子プロセスをケースごとに分離し、warmup 20、測定 12、指定 iteration time 500 ms、1 launch。{execution_order_note}実行時の CPU・OS・SDK・ライブラリ版は `run.log` と `manifest.json` を参照。全ケースの独立セッション再現や Tier1 JIT 分析まで済んだ正式な最速認定ではない。",
    "- Send と Void のハンドラは同期完了する軽い計算で、要求オブジェクトは事前に作成した。`Typed` の 1～2 ns 付近は測定限界に近く、実業務の I/O や複雑な Handler の所要時間を表さない。Notification の非同期ケースは各ハンドラで `Task.Yield()`、Stream の非同期ケースは列挙中に実際に中断する。入力、業務結果、Behavior 順、キャンセル、例外、通知の購読者数、列挙結果を測定前に確認した。",
    "- Zendiator と DispatchR は共通 Mediator 入口。Immediate は要求ごとの生成入口。MediatR は `Task`、他は主に `ValueTask` を返す。Mediator.SourceGenerator は具体的 Mediator 入口。これらの API 差が実測値に含まれる。",
    "- DI lifetime は各ライブラリの公式登録で Scoped を指定できる範囲に合わせた。Immediate の入口は生成器の形、DispatchR の入口 lifetime は登録に従う。`ScopeK1` は Scope の生成と破棄を含む。`ScopeK10` は 10 件の合計値であり、1 件あたりに割らない。",
    "- Notification の Immediate、Generic の一部は本ハーネスで同等のケースを構成していないため対象外。異なる API を無理に共通化した値や推定値は載せない。",
]

parts += [
    "- 測定前に Send/Void の例外とキャンセル、通知の購読順と例外、Stream の破棄などを確認する。時間測定には Stream 途中キャンセルを含めるが、Send の例外経路や非同期中断する Send handler の時間はこのマトリクスの対象外。",
]

filesystem_path(OUTPUT / "RESULT.ja.md").write_text("\n".join(parts) + "\n", encoding="utf-8")
print(OUTPUT / "RESULT.ja.md")
