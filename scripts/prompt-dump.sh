#!/usr/bin/env bash
# Show exactly what San last sent the model, with a token breakdown.
#
#   ./scripts/prompt-dump.sh            # the most recent request
#   ./scripts/prompt-dump.sh -n 3       # the 3 most recent
#   ./scripts/prompt-dump.sh --full     # include every message, not just the system prompt
#   ./scripts/prompt-dump.sh --tools    # per-tool token costs, largest first
#
# Requires LLM_LOG_WIRE=true in deploy/env/san.env — San only dumps the wire when
# asked, because the prompt carries finances, health and correspondence and this
# writes it into the container log:
#
#   printf '\nLLM_LOG_WIRE=true\n' >> deploy/env/san.env && docker compose up -d san
#   ...take a turn...
#   sed -i '' '/^LLM_LOG_WIRE=true$/d' deploy/env/san.env && docker compose up -d san
#
# Token counts are chars/4, the same estimate San logs. Gemma's tokenizer runs
# roughly 40% higher on JSON tool schemas, so treat these as relative weights for
# deciding what to trim, not as the real prompt size. The true number is in the
# response usage: docker compose logs san | grep prompt_tokens

set -uo pipefail
cd "$(dirname "$0")/.."

COUNT=1; FULL=0; TOOLS=0; SINCE="${SINCE:-60m}"
while [ $# -gt 0 ]; do
  case "$1" in
    -n) COUNT="$2"; shift 2 ;;
    --full) FULL=1; shift ;;
    --tools) TOOLS=1; shift ;;
    --since) SINCE="$2"; shift 2 ;;
    *) echo "unknown option: $1"; exit 2 ;;
  esac
done

docker compose logs --since "$SINCE" san 2>/dev/null \
  | sed 's/^san-1 *| *//' \
  | COUNT="$COUNT" FULL="$FULL" TOOLS="$TOOLS" python3 -c '
import sys, os, re, json

txt = sys.stdin.read()
count = int(os.environ["COUNT"]); full = os.environ["FULL"] == "1"; tools_only = os.environ["TOOLS"] == "1"

# Each dump starts "LLM REQUEST (...):" followed by pretty-printed JSON. Walk the
# braces rather than regexing the body — the prompt contains braces of its own.
reqs = []; i = 0
while True:
    m = re.search(r"LLM REQUEST \(", txt[i:])
    if not m: break
    s = txt.find("{", i + m.end()); d = 0; j = s
    while j < len(txt):
        if txt[j] == "{": d += 1
        elif txt[j] == "}":
            d -= 1
            if d == 0: break
        j += 1
    try: reqs.append(json.loads(txt[s:j+1]))
    except Exception: pass
    i = i + m.end()

if not reqs:
    print("No LLM REQUEST found. Is LLM_LOG_WIRE=true set, and has a turn happened since?")
    sys.exit(1)

tok = lambda s: len(s) // 4 if s else 0

for r in reqs[-count:]:
    msgs = r.get("messages", [])
    tools = r.get("tools", []) or []
    sysmsg = next((m.get("content", "") for m in msgs if m.get("role") == "system"), "")
    tool_tok = tok(json.dumps(tools))
    hist = [m for m in msgs if m.get("role") != "system"]
    hist_tok = sum(tok(m.get("content") if isinstance(m.get("content"), str) else json.dumps(m.get("content"))) for m in hist)

    print("=" * 72)
    print(f"  system prompt  {tok(sysmsg):>6} est tokens   ({len(sysmsg)} chars)")
    print(f"  tools          {tool_tok:>6} est tokens   ({len(tools)} tools)")
    print(f"  history        {hist_tok:>6} est tokens   ({len(hist)} messages)")
    print(f"  TOTAL          {tok(sysmsg)+tool_tok+hist_tok:>6} est tokens")
    print("=" * 72)

    if tools_only:
        rows = sorted(((t["function"]["name"], tok(json.dumps(t))) for t in tools), key=lambda x: -x[1])
        print()
        for n, c in rows: print(f"  {n:<28}{c:>6}")
        print("  " + "(total)".ljust(28) + str(tool_tok).rjust(6))
        continue

    print()
    print("--- SYSTEM PROMPT (verbatim) " + "-" * 43)
    print(sysmsg)

    if full:
        print()
        print("--- MESSAGES " + "-" * 59)
        for m in hist:
            c = m.get("content")
            if not isinstance(c, str): c = json.dumps(c)[:200] + " ...(non-text content)"
            print("")
            print("[" + str(m.get("role")) + "] " + str(tok(c)) + " est tokens")
            print(c)
    else:
        print()
        print(f"--- {len(hist)} history message(s) omitted; pass --full to see them ---")
    print()
'
