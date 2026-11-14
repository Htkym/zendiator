"""Strict source binding for the four fixed PERF03 candidate counted maps.

This validates source provenance, not runtime behavior or timing denominators.
V0 continues to use the accepted artifact_gate.py schema-v1 validator.
"""
import hashlib
import json
import re
from pathlib import Path, PurePosixPath


PREFIX = "perf03-sources/src/Perf03CauseDiagnostics/"
ORIGINS = {
    "Counted.Stream0.g.cs": "Zendiator.StreamCanonical.DF7872ACF770B8DDDA6D6A61054B21A2251A08597B558387231219CCDDFB0B4B.g.cs",
    "Counted.Relay5.g.cs": "Zendiator.StreamCanonical.B80A3F15A7016AF93086943F607702393A9513056C3C751D172215C32167550B.g.cs",
    "CountedStreamCursorState.cs": "StreamCursorState.cs",
}
REFERENCES = {"VEC": "V0", "VEntry": "V0", "VRoot": "V0", "VWord": "VRoot"}
REMOVED = {
    "VEC": {"S0.L0267.context-capture", "S0.L0307.context-run", "S0.L0383.context-capture",
            "R5.L2865.context-capture", "R5.L2904.context-run", "R5.L2969.context-capture", "R5.L3096.context-capture"},
    "VEntry": {"S0.L0159.boundary", "R5.L2652.boundary", "R5.L2660.boundary"},
}
HASH = re.compile(r"[0-9A-F]{64}\Z")


def require(value, message):
    if not value:
        raise ValueError("PERF03 candidate counted binding: " + message)


def _path(value):
    require(isinstance(value, str) and value and "\\" not in value and
            not value.startswith("/") and all(part not in {"", ".", ".."} and
            re.fullmatch(r"[A-Za-z0-9_.-]+", part) for part in value.split("/")), "unsafe archive path")
    return value


def _lf(data):
    return data.decode("utf-8").replace("\r\n", "\n")


def apply_source_patch(patch, originals):
    """Replay complete unified text diff; never execute a supplied transformation."""
    lines = patch.splitlines(keepends=True)
    outputs, edits = {}, {}
    index = 0
    while index < len(lines):
        if lines[index].startswith(("diff --git ", "index ")) or lines[index] == "\n":
            index += 1
            continue
        require(lines[index].startswith("--- ") and index + 1 < len(lines) and
                lines[index + 1].startswith("+++ "), "invalid unified patch header")
        old_path = lines[index][4:].strip()
        new_path = lines[index + 1][4:].strip()
        old_path = old_path[2:] if old_path.startswith("a/") else old_path
        new_path = new_path[2:] if new_path.startswith("b/") else new_path
        old_name = PurePosixPath(_path(old_path)).name
        new_name = PurePosixPath(_path(new_path)).name
        require(old_path in {old_name, "src/Perf03CauseDiagnostics/" + old_name} and
                new_path in {new_name, "src/Perf03CauseDiagnostics/" + new_name}, "patch path is outside diagnostic sources")
        require(old_name in originals and new_name not in outputs, "unknown/duplicate patch file")
        old_lines = originals[old_name].splitlines(keepends=True)
        result, changes = [], []
        position, hunks = 0, 0
        index += 2
        while index < len(lines) and lines[index].startswith("@@ "):
            match = re.fullmatch(r"@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@[^\n]*\n?", lines[index])
            require(match is not None, "invalid patch hunk")
            old_start, old_count, new_start, new_count = (int(match[1]), int(match[2] or 1), int(match[3]), int(match[4] or 1))
            target = old_start - 1 if old_count else old_start
            require(position <= target <= len(old_lines), "overlapping/out-of-range patch hunk")
            result.extend(old_lines[position:target])
            position = target
            require((new_start - 1 if new_count else new_start) == len(result), "patch new-file offset changed")
            consumed = produced = 0
            index += 1
            while index < len(lines) and not lines[index].startswith(("@@ ", "--- ", "diff --git ")):
                line = lines[index]
                require(line[:1] in {" ", "+", "-"}, "unsupported unified patch line")
                no_newline = index + 1 < len(lines) and lines[index + 1].rstrip("\n") == "\\ No newline at end of file"
                if no_newline:
                    require(line.endswith("\n"), "malformed no-newline marker")
                    line = line[:-1]
                if line[0] in " -":
                    require(position < len(old_lines) and old_lines[position] == line[1:],
                            f"patch context/source mismatch: {old_name}:{position + 1} expected {old_lines[position] if position < len(old_lines) else None!r}, patch {line[1:]!r}")
                    position += 1
                    consumed += 1
                if line[0] in " +":
                    result.append(line[1:])
                    produced += 1
                if line[0] in "+-":
                    changes.append(line)
                index += 2 if no_newline else 1
            require((consumed, produced) == (old_count, new_count), "patch hunk counts changed")
            hunks += 1
        require(hunks > 0, "patch file contains no hunks")
        result.extend(old_lines[position:])
        outputs[new_name] = "".join(result)
        edits[new_name] = changes
    require(outputs, "empty patch")
    return outputs, edits


def verify_variant_counted_sources(bundle_root, sources, variant, binding, *, archived=True):
    """Return the exact site map after byte hashes, patch replay and catalog checks.

    binding has mapPath/mapSha256/catalogPath/catalogSha256, all archive-relative.
    Map nested patch/archive objects contain path and sha256; paths are relative to
    src/Perf03CauseDiagnostics, except binding paths which include PREFIX.
    """
    require(variant in REFERENCES, "unknown candidate")
    require(variant != "VWord", "VWord remains unranked until its VRoot reference chain and atomic transition proof are implemented")
    require(isinstance(binding, dict) and set(binding) == {"mapPath", "mapSha256", "catalogPath", "catalogSha256"}, "map/catalog binding shape")
    root = Path(bundle_root)

    def read_archive(name, digest):
        _path(name)
        require(name.startswith(PREFIX) and isinstance(digest, str) and HASH.fullmatch(digest) and
                sources.get(name) == digest, "archive hash is not source-bound")
        target = root / (name if archived else name.removeprefix("perf03-sources/"))
        current = target
        while current != root:
            require(not current.is_symlink() and not (hasattr(current, "is_junction") and current.is_junction()), "reparse archive path")
            current = current.parent
        data = target.read_bytes()
        require(hashlib.sha256(data).hexdigest().upper() == digest, "archive bytes changed")
        return data

    def artifact(value):
        require(isinstance(value, dict) and set(value) == {"path", "sha256"}, "artifact identity shape")
        return read_archive(PREFIX + _path(value["path"]), value["sha256"])

    map_bytes = read_archive(binding["mapPath"], binding["mapSha256"])
    data = json.loads(map_bytes)
    require(data.get("schemaVersion") == 2 and type(data.get("schemaVersion")) is int and
            data.get("protocol") == "zendiator-perf03-variant-counted-sites-v2" and
            data.get("variant") == variant and data.get("referenceVariant") == REFERENCES[variant], "map protocol/variant/reference")
    require(isinstance(data.get("baseRevision"), str) and re.fullmatch(r"[0-9a-f]{40}", data["baseRevision"]), "comparison base revision")
    baseline_bytes = artifact(data["baselineMap"])
    baseline = json.loads(baseline_bytes)
    require(baseline.get("schemaVersion") == 1 and baseline.get("protocol") == "zendiator-perf03-counted-sites-v1" and
            baseline.get("variant") == "V0", "immutable V0 map identity")
    provenance = json.loads(artifact(data["snapshotProvenance"]))
    require(provenance.get("schemaVersion") == 1 and provenance.get("protocol") == "zendiator-perf03-snapshot-v1" and
            provenance.get("variant") == "V0", "immutable V0 snapshot identity")
    trace = data.get("traceBinding")
    require(isinstance(trace, dict) and set(trace) == {"path", "sha256", "referencePath", "referenceSha256"} and
            trace["path"] == "Perf03CountTrace.cs", "trace helper binding missing/substituted")
    read_archive(PREFIX + _path(trace["path"]), trace["sha256"])
    read_archive(PREFIX + _path(trace["referencePath"]), trace["referenceSha256"])
    baseline_hashes = {row["originFile"]: row["originSha256"] for row in baseline["files"]}
    provenance_hashes = {row["diagnosticFile"]: row["diagnosticSha256"] for row in provenance["files"]}
    require(baseline_hashes == provenance_hashes and set(baseline_hashes) == set(ORIGINS.values()), "V0 snapshot/map origin mismatch")
    rows = data.get("files")
    require(isinstance(rows, list) and len(rows) == 3, "exactly three counted source files required")
    origins, counted, references, pairs = {}, {}, {}, {}
    for row in rows:
        require(isinstance(row, dict) and row.get("file") in ORIGINS and row["file"] not in pairs and
                row.get("originFile") == ORIGINS[row["file"]], "counted/origin file substitution")
        name, origin = row["file"], row["originFile"]
        origin_bytes = read_archive(PREFIX + _path(row["originPath"]), row["originSha256"])
        reference_bytes = read_archive(PREFIX + _path(row["referencePath"]), row["referenceSha256"])
        require(read_archive(PREFIX + origin, row["originSha256"]) == origin_bytes, "current timing source differs from candidate origin")
        if REFERENCES[variant] == "V0":
            require(row["referenceSha256"] == baseline_hashes[origin], "treatment reference is not immutable V0")
        else:
            # VWord requires the already source-validated VRoot map chain, not a free-form label.
            require(isinstance(data.get("referenceSourceHashes"), dict) and
                    data["referenceSourceHashes"].get(origin) == row["referenceSha256"], "VWord/VRoot reference hashes")
        origins[origin] = _lf(origin_bytes)
        references[origin] = _lf(reference_bytes)
        counted[name] = _lf(read_archive(PREFIX + name, row["sha256"]))
        pairs[name] = row
    require(set(pairs) == set(ORIGINS), "incomplete counted source set")

    # Id is checked separately against the compiled consumer; only its declared line may differ.
    references["Perf03CauseVariant.cs"] = 'namespace Competitive.Perf03.Diagnostics;\n\ninternal static class Perf03CauseVariant\n{\n    internal const string Id = "' + REFERENCES[variant] + '";\n}\n'
    treated, edits = apply_source_patch(_lf(artifact(data["treatmentPatch"])), references)
    if "Perf03CauseVariant.cs" in treated:
        changed = edits.pop("Perf03CauseVariant.cs")
        require(changed == ['-    internal const string Id = "' + REFERENCES[variant] + '";\n',
                            '+    internal const string Id = "' + variant + '";\n'], "treatment changed more than variant Id")
        treated.pop("Perf03CauseVariant.cs")
    require(set(treated) <= set(origins), "treatment patch changed an unrelated file")
    for name, text in origins.items():
        actual = treated.get(name, references[name])
        require(actual == text, f"treatment patch does not reproduce candidate {name}: replay length {len(actual)}, candidate length {len(text)}; starts {actual[:12]!r}/{text[:12]!r}; ends {actual[-12:]!r}/{text[-12:]!r}")
    instrumented, _ = apply_source_patch(_lf(artifact(data["instrumentationPatch"])), origins)
    require(instrumented == counted, "instrumentation patch does not reproduce all counted copies")

    sites, mapped = data.get("sites"), {}
    require(isinstance(sites, list) and sites, "site list missing")
    for site in sites:
        require(isinstance(site, dict) and isinstance(site.get("id"), str) and site["id"] not in mapped and
                isinstance(site.get("kind"), str) and site["kind"] and site.get("file") in pairs and
                site.get("originFile") == pairs[site["file"]]["originFile"] and
                type(site.get("line")) is int and site["line"] > 0 and
                type(site.get("originLine")) is int and site["originLine"] > 0 and
                isinstance(site.get("expression"), str) and site["expression"], "invalid/duplicate site")
        clone_lines = counted[site["file"]].splitlines()
        origin_lines = origins[site["originFile"]].splitlines()
        require(site["line"] <= len(clone_lines) and site["originLine"] <= len(origin_lines), "site line out of range")
        line = clone_lines[site["line"] - 1]
        api = re.search(r"(?:Perf03CountTrace\.(Status|Result|Hit)|Perf03CountSites\.(Observe))\(", line)
        require(api is not None and json.dumps(site["id"]) in line and json.dumps(site["kind"]) in line and
                site["expression"] in origin_lines[site["originLine"] - 1], "site expression/call differs")
        mapped[site["id"]] = dict(site, api=api[1] or api[2])
    actual_calls = {(name, number) for name, text in counted.items() for number, line in enumerate(text.splitlines(), 1)
                    if re.search(r"(?:Perf03CountTrace\.(?:Status|Result|Hit)|Perf03CountSites\.Observe)\(", line)}
    require(actual_calls == {(site["file"], site["line"]) for site in mapped.values()}, "unmapped/phantom counted trace calls")
    if variant in REMOVED:
        baseline_sites = {site["id"]: site["kind"] for site in baseline["sites"]}
        require(set(mapped) == set(baseline_sites) - REMOVED[variant] and
                all(site["kind"] == baseline_sites[name] for name, site in mapped.items()), "factor removed or changed unrelated sites")
    catalog = _lf(read_archive(binding["catalogPath"], binding["catalogSha256"]))
    hashes = re.findall(r'\bMapSha256\s*=\s*"([0-9A-F]{64})"', catalog)
    catalog_sites = re.findall(r'\("([^"]+)",\s*"([^"]+)"\)', catalog)
    require(hashes == [binding["mapSha256"]] and len(catalog_sites) == len(mapped) and
            len(dict(catalog_sites)) == len(mapped) and dict(catalog_sites) == {name: site["kind"] for name, site in mapped.items()}, "catalog/map mismatch")
    return dict(variant=variant, map=data, sites=mapped, timingSourceHashes={row["originFile"]: row["originSha256"] for row in rows})
