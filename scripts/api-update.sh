#!/usr/bin/env bash
# Updates the public API files (PublicAPI.Shipped.txt / PublicAPI.Unshipped.txt) from the
# code, the way the PublicApiAnalyzers code fixes do:
#
#   RS0016, a public symbol missing from the files: `dotnet format` adds its line to
#           PublicAPI.Unshipped.txt.
#   RS0017, a line for a symbol that is no longer public: `dotnet format` does not fix
#           this one (it is reported on the text file, not on code), so this script
#           builds the libraries, reads each RS0017 and applies the fix itself. A line in
#           PublicAPI.Unshipped.txt is deleted; a line in PublicAPI.Shipped.txt stays and
#           a "*REMOVED*" line for it is added to PublicAPI.Unshipped.txt.
#
# Review the diff and commit it with the change.
#
# Usage: scripts/api-update.sh      (make api-update; make api-check runs it too)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
SOLUTION="LaunchDarkly.AI.Server.sln"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

cd "$ROOT"
"$DOTNET" format analyzers "$SOLUTION" --no-restore --diagnostics RS0016 RS0017 --severity hidden

# The projects that have public API files.
mapfile -t projects < <(find src -name PublicAPI.Unshipped.txt -exec dirname {} \; | sort |
  while read -r dir; do ls "$dir"/*.csproj; done)
[ "${#projects[@]}" -ne 0 ] || exit 0

log="$(mktemp "${TMPDIR:-/tmp}/api-update.XXXXXX")"
trap 'rm -f "$log"' EXIT
for project in "${projects[@]}"; do
  # Fails while an RS0017 is left, so the status is not used; the output is.
  "$DOTNET" build "$project" --no-restore -c Release -nologo -clp:NoSummary >>"$log" 2>&1 || true
done

python3 -I - "$log" <<'PY'
import os, re, sys

# <file>(<line>,<column>): error RS0017: Symbol '<symbol>' is part of the declared API, ...
pattern = re.compile(r"^\s*(?P<file>.+?PublicAPI\.(?:Shipped|Unshipped)\.txt)\((?P<line>\d+),\d+\): "
                     r"(?:error|warning) RS0017: Symbol '(?P<symbol>.*)' is part of the declared API")
found = {}
with open(sys.argv[1], encoding="utf-8", errors="replace") as log:
    for text in log:
        m = pattern.match(text)
        if m:  # One per target framework: keep each (file, symbol) once.
            found.setdefault(os.path.realpath(m["file"]), set()).add(m["symbol"])

def read_lines(path):
    with open(path, encoding="utf-8", newline="") as f:
        return f.read().splitlines(keepends=True)

def write_lines(path, lines):
    if lines and not lines[-1].endswith(("\n", "\r")):
        lines[-1] += "\n"
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.writelines(lines)

for path, symbols in sorted(found.items()):
    folder, name = os.path.split(path)
    if name == "PublicAPI.Unshipped.txt":
        lines = read_lines(path)
        write_lines(path, [l for l in lines if l.rstrip("\r\n") not in symbols])
    else:
        unshipped = os.path.join(folder, "PublicAPI.Unshipped.txt")
        lines = read_lines(unshipped)
        present = {l.rstrip("\r\n") for l in lines}
        lines += [f"*REMOVED*{s}\n" for s in sorted(symbols) if f"*REMOVED*{s}" not in present]
        write_lines(unshipped, lines)
    for symbol in sorted(symbols):
        print(f"api-update: removed {symbol} ({os.path.relpath(path)})")
PY
