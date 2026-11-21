"""Summarize selected PPMAP lines; skips unrelated Player.log entries."""
import argparse
from collections import defaultdict
import math
from pathlib import Path

def number(value):
    try:
        n = float(value)
        return n if math.isfinite(n) else None
    except (ValueError, TypeError):
        return None

def parse(lines):
    runs = defaultdict(lambda: {"meta": {}, "samples": defaultdict(dict), "errors": [], "end": {}})
    for line in lines:
        offset = line.find("PPMAP|")
        if offset < 0:
            continue
        line = line[offset:]
        data = dict(token.split("=", 1) for token in line.strip().split("|")[1:] if "=" in token)
        if not data.get("run"):
            continue
        result = runs[data["run"]]
        kind = data.get("kind")
        if kind == "begin":
            result["meta"] = data
        elif kind == "end":
            result["end"] = data
        elif kind == "error":
            result["errors"].append(data.get("error", "unspecified"))
        elif kind in {"sample", "cell", "sampleEnd"}:
            try:
                seq = int(data["seq"])
            except (ValueError, KeyError):
                result["errors"].append("Bad sample number")
                continue
            snapshot = result["samples"].setdefault(seq, {"end": False, "header": {}, "cells": {}})
            if kind == "cell":
                try:
                    key = (data["watch"], int(data["dx"]), int(data["dy"]))
                except (ValueError, KeyError):
                    result["errors"].append("Bad cell position")
                    continue
                if key in snapshot["cells"]:
                    result["errors"].append(f"Duplicate cell {key!r} in sample {seq}")
                snapshot["cells"][key] = data
            elif kind == "sample":
                snapshot["header"] = data
            else:
                snapshot["end"] = True
    return runs

def center(sample, watch="target"):
    return sample["cells"].get((watch, 0, 0), {})

def summarize(run, result):
    meta, snapshots, end = result["meta"], result["samples"], result["end"]
    print(f"Run {run}, build {meta.get('buildLabel','?')}, {meta.get('label','?')}, route={meta.get('route','?')}")
    watches = {"target"} | ({"source"} if meta.get("source") not in (None, "-1") else set())
    expected = {(watch, x, y) for watch in watches for x in range(-2,3) for y in range(-2,3)}
    complete = sorted(n for n, s in snapshots.items() if s["header"] and s["end"] and set(s["cells"]) == expected)
    print(f"  Complete managed samples: {len(complete)}/{len(snapshots)}; stop={end.get('reason','missing')}")
    if complete != list(range(len(complete))) or len(snapshots) != len(complete) or not end or not meta or end.get("samples") != str(len(snapshots)):
        print("  WARN: incomplete/inconsistent trace. Do not infer that absent changes did not occur.")
    for error in result["errors"]:
        print(f"  WARN: {error}")
    if 0 not in complete:
        return
    baseline = snapshots[0]
    for watch in sorted(watches):
        first = center(baseline, watch)
        timeseries = [center(snapshots[n], watch) for n in complete]
        last = timeseries[-1]
        a, b = number(first.get("kg")), number(last.get("kg"))
        change = f"{(b-a)*1e9:+.8g} µg" if a is not None and b is not None else "?"
        temps = [k for value in timeseries if (k := number(value.get("kelvin"))) is not None]
        thermal = f"{min(temps)-273.15:.4f} to {max(temps)-273.15:.4f} °C" if temps else "?"
        n_changed = {key[1:] for n in complete for key, v in snapshots[n]["cells"].items() if
                     key[0] == watch and key[1:] != (0,0) and (v.get("valid"), v.get("element"), v.get("kgBits"))
                     != tuple(baseline["cells"][key].get(k) for k in ("valid","element","kgBits"))}
        print(f"  {watch}: {first.get('element','?')} -> {last.get('element','?')}; delta {change}; center temperatures {thermal}")
        print(f"    Neighbor mass/element or visibility changed at {len(n_changed)} positions (not evidence of cause).")
    print("  Descriptive Grid readback only; does not isolate command effects or verify food status.")

def main():
    p = argparse.ArgumentParser()
    p.add_argument("log", type=Path)
    args = p.parse_args()
    with args.log.open(encoding="utf-8-sig", errors="replace") as f:
        runs = parse(f)
    if not runs:
        p.exit(2, "No PPMAP capture lines found.\n")
    for run, result in runs.items():
        summarize(run, result)

if __name__ == "__main__":
    main()
