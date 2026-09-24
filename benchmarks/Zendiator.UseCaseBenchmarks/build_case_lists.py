import json
from pathlib import Path

root = Path(__file__).resolve().parent
libraries = ("Zendiator", "MediatRHistorical", "Mediator", "DispatchR", "Immediate")
depths = (0, 1, 3, 5)

send = [
    {"Type": f"{library}Send{depth}", "Method": method, "Lifetime": "Scoped"}
    for depth in depths
    for method in ("Typed", "ResolveSend", "ScopeK1", "ScopeK10")
    for library in libraries
]

features = [
    {"Type": f"{library}Void{depth}", "Method": "Dispatch"}
    for depth in depths for library in libraries
]
features += [
    {"Type": f"{library}Closed", "Method": "Dispatch"}
    for library in ("Zendiator", "MediatRHistorical", "DispatchR", "Immediate")
]
features += [
    {"Type": f"{library}Open", "Method": "Dispatch"}
    for library in ("Zendiator", "MediatRHistorical", "DispatchR")
]
features += [
    {"Type": f"{library}Notification{handlers}", "Method": method}
    for handlers in (0, 1, 4, 16)
    for library in ("Zendiator", "MediatRHistorical", "Mediator", "DispatchR")
    for method in (("Dispatch", "DispatchAsync") if handlers else ("Dispatch",))
]

streams = []
for depth in depths:
    for library in libraries:
        name = f"{library}Stream{depth}"
        for asynchronous in (False, True):
            streams.append({"Type": name, "Method": "Full", "Count": 16, "Asynchronous": asynchronous})
        if depth in (0, 5):
            for asynchronous in (False, True):
                streams.append({"Type": name, "Method": "Full", "Count": 1024, "Asynchronous": asynchronous})
                for method in ("First", "EarlyBreak"):
                    streams.append({"Type": name, "Method": method, "Count": 16, "Asynchronous": asynchronous})
            streams.append({"Type": name, "Method": "Creation", "Count": 16, "Asynchronous": False})
            streams.append({"Type": name, "Method": "Cancellation", "Count": 16, "Asynchronous": True})

for group, cases in (("send", send), ("features", features), ("streams", streams)):
    assert len(cases) == len({json.dumps(case, sort_keys=True) for case in cases})
    (root / f"{group}.json").write_text(json.dumps(cases, indent=2) + "\n")
    print(f"{group}: {len(cases)} cases")
