"""Render the checked BDN matrix without interpreting one-launch differences."""

import csv
import json
import sys
from pathlib import Path


if len(sys.argv) != 2:
    raise SystemExit("Usage: python make_report.py OUTPUT_ROOT")
OUTPUT = Path(sys.argv[1]).resolve()
ROWS = list(csv.DictReader((OUTPUT / "all-results.csv").open(encoding="utf-8")))
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


metadata = json.loads((OUTPUT / "runs" / "send" / "run-info.json").read_text(encoding="utf-8"))
revision = metadata.get("revision", metadata.get("commit"))
if not revision:
    raise ValueError("The send run has no source revision")
source_digest = metadata.get("sourceDigest")
parts = [
    "# ユースケース別の競合ライブラリ比較",
    "",
    f"Zendiator revision `{revision}`" + (f"、ソース digest `{source_digest}`" if source_digest else "") + "。数値は BenchmarkDotNet の mean / allocated bytes。`all-results.csv` に median、標準偏差、有効 iteration 数も収録した。各 `runs/<group>/results/*-full.json` に生データ、`run.log` に警告、`run-info.json` に成功件数と子プロセスの DLL hash を残した。",
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
    "",
    "## 測定条件と読み方",
    "",
    "- Release、CPU affinity 1。BDN の子プロセスをケースごとに分離し、warmup 20、測定 12、指定 iteration time 500 ms、1 launch。実行時の CPU・OS・SDK・ライブラリ版は `run.log` と `manifest.json` を参照。全ケースの独立セッション再現や Tier1 JIT 分析まで済んだ正式な最速認定ではない。",
    "- Send と Void のハンドラは同期完了する軽い計算で、要求オブジェクトは事前に作成した。`Typed` の 1～2 ns 付近は測定限界に近く、実業務の I/O や複雑な Handler の所要時間を表さない。Notification の非同期ケースは各ハンドラで `Task.Yield()`、Stream の非同期ケースは列挙中に実際に中断する。入力、業務結果、Behavior 順、キャンセル、例外、通知の購読者数、列挙結果を測定前に確認した。",
    "- Zendiator と DispatchR は共通 Mediator 入口。Immediate は要求ごとの生成入口。MediatR は `Task`、他は主に `ValueTask` を返す。Mediator.SourceGenerator は具体的 Mediator 入口。これらの API 差が実測値に含まれる。",
    "- DI lifetime は各ライブラリの公式登録で Scoped を指定できる範囲に合わせた。Immediate の入口は生成器の形、DispatchR の入口 lifetime は登録に従う。`ScopeK1` は Scope の生成と破棄を含む。`ScopeK10` は 10 件の合計値であり、1 件あたりに割らない。",
    "- Notification の Immediate、Generic の一部は本ハーネスで同等のケースを構成していないため対象外。異なる API を無理に共通化した値や推定値は載せない。",
]

parts += [
    "- 測定前に Send/Void の例外とキャンセル、通知の購読順と例外、Stream の破棄などを確認する。時間測定には Stream 途中キャンセルを含めるが、Send の例外経路や非同期中断する Send handler の時間はこのマトリクスの対象外。",
]

(OUTPUT / "RESULT.ja.md").write_text("\n".join(parts) + "\n", encoding="utf-8")
print(OUTPUT / "RESULT.ja.md")
