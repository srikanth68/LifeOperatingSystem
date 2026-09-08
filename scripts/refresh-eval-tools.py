#!/usr/bin/env python3
"""Refresh the tool DESCRIPTIONS in the eval catalogue from the MCP source.

The catalogue at san/San.Evals/prompts/tools-live.json is a frozen export: an eval
whose tool list moves underneath it cannot compare two runs. But when a description
is deliberately reworded -- and descriptions are the thing tool-selection cases
actually measure -- the export has to follow, or the next run scores a catalogue
that no longer exists.

It was first extracted by hand, and that went badly. One regex could not see past
`async Task<string>` and silently matched a later method's parameters, leaving
twelve tools with none; another paired the wrong quotes around an escaped `\\"` and
ate the text between them. Hence this: descriptions only, matched to the tool name
they sit under, with the count asserted at both ends.

Parameters are NOT touched. Re-deriving them is what broke last time, and a
signature change is rare enough to be worth doing deliberately.

    python scripts/refresh-eval-tools.py            # rewrites, prints what changed
    python scripts/refresh-eval-tools.py --check    # exits 1 if stale, writes nothing
    python scripts/refresh-eval-tools.py --add-new  # also append newly declared tools

--add-new is separate because appending changes the CATALOGUE SIZE, and catalogue size
is one of the things the eval measures: tool selection got measurably worse going from
eleven tools to forty-eight. A run against a bigger catalogue is not comparable to the
baseline before it, so growing it is a decision rather than a refresh. Parameters are
left empty for appended tools -- fill them in by hand if the tool takes any.
"""

import glob
import io
import json
import re
import sys

BS = chr(92)
CATALOGUE = "san/San.Evals/prompts/tools-live.json"

# [McpServerTool(Name = "x")] immediately followed by its [Description("...")].
# Anchoring on the attribute pair, rather than scanning forward to the method,
# is what keeps this from wandering into the next tool.
PAIR = re.compile(
    r'\[McpServerTool\(Name\s*=\s*"([a-z0-9_]+)"\)\]\s*\r?\n\s*'
    r'\[Description\("((?:[^"\\]|\\.)*)"\)\]'
)


def descriptions_from_source():
    found = {}
    for path in sorted(glob.glob("mcp/Maaya.Mcp/Tools/*.cs")):
        src = io.open(path, encoding="utf-8").read()
        for name, body in PAIR.findall(src):
            if name in found:
                sys.exit(f"{name} is declared twice in mcp/Maaya.Mcp/Tools -- fix that first.")
            found[name] = body.replace(BS + '"', '"').replace(BS + BS, BS)
    return found


def main():
    check_only = "--check" in sys.argv

    source = descriptions_from_source()
    tools = json.load(io.open(CATALOGUE, encoding="utf-8"))

    # A tool in the catalogue with no counterpart in source means one was renamed or
    # removed, which is a parameter-level change this script deliberately will not
    # guess at. Say so rather than silently leaving a stale entry behind.
    missing = [t["name"] for t in tools if t["name"] not in source]
    if missing:
        sys.exit(
            "No source found for: " + ", ".join(missing) + "\n"
            "A tool was renamed or removed. Re-export the catalogue deliberately."
        )

    added = [n for n in source if not any(t["name"] == n for t in tools)]

    if added and "--add-new" in sys.argv and not check_only:
        for name in added:
            tools.append({"name": name, "description": source[name], "parameters": []})
        print("appended: " + ", ".join(added))
        print("NOTE: parameters left empty -- fill them in by hand for any tool that takes some.")
        added = []

    changed = []
    for t in tools:
        fresh = source[t["name"]]
        if fresh != t["description"]:
            changed.append(t["name"])
            t["description"] = fresh

    if check_only:
        if changed or added:
            print("STALE. Descriptions differ for: " + ", ".join(changed or ["-"]))
            if added:
                print("Not in the catalogue at all: " + ", ".join(added))
            return 1
        print(f"up to date ({len(tools)} tools)")
        return 0

    io.open(CATALOGUE, "w", encoding="utf-8", newline="\n").write(
        json.dumps(tools, indent=2, ensure_ascii=False) + "\n"
    )

    print(f"{len(tools)} tools, {len(source)} in source")
    print("changed: " + (", ".join(changed) if changed else "nothing"))
    if added:
        print("in source but NOT in the catalogue (add deliberately): " + ", ".join(added))
    return 0


if __name__ == "__main__":
    sys.exit(main())
