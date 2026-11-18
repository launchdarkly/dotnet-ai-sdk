#!/usr/bin/env bash
# Self-test of the public API checks, against a fixture repository instead of the real
# packages. The fixture is a git repository in a temporary folder with one small library
# (Fixture.Api), its PublicAPI files, and copies of this repository's Makefile,
# scripts/api-compat.sh, scripts/api-update.sh, dotnet tool manifest and global.json, so
# it runs the same commands CI runs:
#
#   check 1, the report is current: `make build` (RS0016/RS0017 as errors) and
#            `make api-check`, with `make api-update` as the update command;
#   check 2, a breaking change is declared: scripts/api-compat.sh against the fixture's
#            base commit, with API_COMPATIBLE=true standing in for the api-compatible
#            label.
#
# Each case starts from the base commit, changes the fixture, runs a check and compares
# the outcome with the expected one. The script prints one "ok" or "not ok" line per case
# and fails if any case is "not ok".
#
# Usage: scripts/api-check-selftest.sh      (make api-check-selftest)
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
unset API_COMPATIBLE GITHUB_ACTIONS

WORK="$(mktemp -d "${TMPDIR:-/tmp}/api-check-selftest.XXXXXX")"
FIX="$WORK/repo"
LIB="$FIX/src/Fixture.Api"
LOG="$WORK/out.log"
cleanup() {
  chmod -R u+rw "$WORK" 2>/dev/null || true
  rm -rf "$WORK"
}
trap cleanup EXIT

passed=0
failed=()
ok() { echo "ok - $1"; passed=$((passed + 1)); }
not_ok() { # <case> <reason>
  echo "not ok - $1: $2"
  sed 's/^/    | /' "$LOG" | tail -n 25
  failed+=("$1")
}

# Runs a command in the fixture, with its output in $LOG; returns its exit status.
run() { (cd "$FIX" && "$@") >"$LOG" 2>&1; }

# Replaces one exact piece of text in the fixture's Api.cs; fails the case if absent.
mutate() { # <old> <new>
  python3 -I - "$LIB/Api.cs" "$1" "$2" <<'PY'
import sys
path, old, new = sys.argv[1:4]
text = open(path, encoding="utf-8").read()
if text.count(old) != 1:
    sys.exit(f"fixture text not found exactly once: {old!r}")
open(path, "w", encoding="utf-8").write(text.replace(old, new))
PY
}

# Rewrites PublicAPI.Unshipped.txt from the code, as a developer would after a change, so
# that check 1 passes and only check 2 is under test. Starting from an empty file means a
# removed symbol cannot survive (the RS0017 fix is not applied by dotnet format).
update_report() {
  printf '#nullable enable\n' >"$LIB/PublicAPI.Unshipped.txt"
  (cd "$FIX" && "$DOTNET" format analyzers LaunchDarkly.AI.Server.sln --diagnostics RS0016 --severity hidden) >/dev/null 2>&1 || true
}

reset_fixture() {
  chmod -R u+rw "$LIB" 2>/dev/null || true
  git -C "$FIX" reset -q --hard base
  git -C "$FIX" clean -qfdx -e bin -e obj
}

# ---- The fixture --------------------------------------------------------------------

mkdir -p "$LIB" "$FIX/scripts" "$FIX/.config"
cp "$ROOT/Makefile" "$ROOT/global.json" "$FIX/"
cp "$ROOT/scripts/api-compat.sh" "$ROOT/scripts/api-update.sh" "$FIX/scripts/"
cp "$ROOT/.config/dotnet-tools.json" "$FIX/.config/"
analyzers_version="$(python3 -I - "$ROOT/Directory.Packages.props" <<'PY'
import sys, xml.etree.ElementTree as ET
for item in ET.parse(sys.argv[1]).getroot().iter("PackageVersion"):
    if item.get("Include") == "Microsoft.CodeAnalysis.PublicApiAnalyzers":
        print(item.get("Version"))
PY
)"
cat >"$LIB/Fixture.Api.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsPackable>true</IsPackable>
    <Version>0.1.0</Version>
    <Authors>fixture</Authors>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="$analyzers_version" PrivateAssets="all" />
  </ItemGroup>
</Project>
EOF
cat >"$LIB/Api.cs" <<'EOF'
namespace Fixture.Api
{
    public static class Calc
    {
        public static int Add(int a, int b) => a + b;
        public static int Sub(int a, int b) => a - b;
        public static int Drop(int x, int y) => x + y;
        public static string Move(string first, int second) => first + second;
        public static int Twice(int x) => 2 * x;
        public static int Scale(int x, int factor = 2) => x * factor;
        public static string Describe(object value) => value.ToString() ?? "";
        public static string Label(string text) => text;
        public static string Greet(string name, string greeting = "Hello") => greeting + " " + name;
        public static string Echo(string text) => text;
    }

    public class Widget
    {
        public string Name { get; init; } = "";

        public int Size { get; set; }
    }
}
EOF
printf '#nullable enable\n' >"$LIB/PublicAPI.Shipped.txt"
printf '#nullable enable\n' >"$LIB/PublicAPI.Unshipped.txt"
printf 'bin/\nobj/\n' >"$FIX/.gitignore"
(
  cd "$FIX" &&
    git init -q -b main &&
    "$DOTNET" new sln -n LaunchDarkly.AI.Server --format sln -o . &&
    "$DOTNET" sln LaunchDarkly.AI.Server.sln add src/Fixture.Api/Fixture.Api.csproj
) >"$LOG" 2>&1 || { cat "$LOG"; echo "api-check-selftest: could not create the fixture"; exit 2; }
update_report
git -C "$FIX" add -A
git -C "$FIX" -c user.name=fixture -c user.email=fixture@example.invalid commit -qm base
git -C "$FIX" tag base
if ! run make build; then
  cat "$LOG"
  echo "api-check-selftest: the fixture's base does not build"
  exit 2
fi
if ! run make api-check; then
  cat "$LOG"
  echo "api-check-selftest: the fixture's base report is not current"
  exit 2
fi
echo "api-check-selftest: fixture at $FIX (PublicApiAnalyzers $analyzers_version)"

# ---- Check 1: the report is current -------------------------------------------------

c="check 1: an added public member with a stale report fails the build with RS0016"
reset_fixture
mutate '        public static string Echo(string text) => text;' \
  $'        public static string Echo(string text) => text;\n        public static int Extra() => 0;'
if run make build; then not_ok "$c" "make build passed"
elif ! grep -q 'RS0016' "$LOG"; then not_ok "$c" "no RS0016 in the output"
else ok "$c"; fi

c="check 1: a removed public member with a stale report fails the build with RS0017"
reset_fixture
mutate '        public static int Sub(int a, int b) => a - b;' ''
if run make build; then not_ok "$c" "make build passed"
elif ! grep -q 'RS0017' "$LOG"; then not_ok "$c" "no RS0017 in the output"
else ok "$c"; fi

c="check 1: make api-check fails on a stale report and names the report"
reset_fixture
mutate '        public static string Echo(string text) => text;' \
  $'        public static string Echo(string text) => text;\n        public static int Extra() => 0;'
if run make api-check; then not_ok "$c" "make api-check passed"
elif ! grep -q 'PublicAPI.Unshipped.txt' "$LOG"; then not_ok "$c" "the output does not name PublicAPI.Unshipped.txt"
else ok "$c"; fi

c="check 1: the stale-report message names the update command (a make target)"
reset_fixture
mutate '        public static string Echo(string text) => text;' \
  $'        public static string Echo(string text) => text;\n        public static int Extra() => 0;'
run make build
cp "$LOG" "$WORK/build.log"
run make api-check
cat "$WORK/build.log" >>"$LOG"
# Lines that start with "make" are make's own status lines, not the checks' messages.
if grep -v '^make' "$LOG" | grep -Eq '\bmake [a-z][a-z-]*'; then
  ok "$c"
else
  not_ok "$c" "neither the build nor make api-check names a make target to run"
fi

c="check 1: the update command removes a deleted member from the report"
reset_fixture
mutate '        public static int Sub(int a, int b) => a - b;' ''
run make api-update
if ! run make build; then not_ok "$c" "make build still fails after make api-update"
elif grep -q 'Sub(int a, int b)' "$LIB/PublicAPI.Unshipped.txt"; then
  not_ok "$c" "PublicAPI.Unshipped.txt still lists Sub"
else ok "$c"; fi

c="check 1: the update command records the removal of a shipped member (*REMOVED*)"
reset_fixture
grep 'Calc.Sub(' "$LIB/PublicAPI.Unshipped.txt" >>"$LIB/PublicAPI.Shipped.txt"
sed -i '/Calc\.Sub(/d' "$LIB/PublicAPI.Unshipped.txt"
mutate '        public static int Sub(int a, int b) => a - b;' ''
run make api-update
if ! run make build; then not_ok "$c" "make build still fails after make api-update"
elif ! grep -q 'Calc.Sub(' "$LIB/PublicAPI.Shipped.txt"; then not_ok "$c" "the shipped line was deleted"
elif ! grep -q '^\*REMOVED\*static Fixture.Api.Calc.Sub(int a, int b) -> int$' "$LIB/PublicAPI.Unshipped.txt"; then
  cp "$LIB/PublicAPI.Unshipped.txt" "$LOG"
  not_ok "$c" "PublicAPI.Unshipped.txt has no *REMOVED* line for Sub"
else ok "$c"; fi

c="check 1: a nullability change (string to string?) shows in the report"
reset_fixture
mutate 'public static string Echo(string text) => text;' 'public static string? Echo(string? text) => text;'
if run make build; then
  not_ok "$c" "make build passed with the old signature in the report"
else
  run make api-check
  if git -C "$FIX" diff -- src/Fixture.Api/PublicAPI.Unshipped.txt | grep -q '^+.*Echo(string? text) -> string?'; then
    ok "$c"
  else
    git -C "$FIX" diff -- src/Fixture.Api/PublicAPI.Unshipped.txt >"$LOG"
    not_ok "$c" "the regenerated report has no 'Echo(string? text) -> string?' line"
  fi
fi

c="check 1: a missing report fails"
reset_fixture
rm "$LIB/PublicAPI.Shipped.txt" "$LIB/PublicAPI.Unshipped.txt"
if run make build && run make api-check; then not_ok "$c" "make build and make api-check both passed"
else ok "$c"; fi

c="check 1: an unreadable report fails"
reset_fixture
chmod 000 "$LIB/PublicAPI.Unshipped.txt"
if run make build && run make api-check; then not_ok "$c" "make build and make api-check both passed"
else ok "$c"; fi
chmod u+rw "$LIB/PublicAPI.Unshipped.txt"

# ---- Check 2: a breaking change is declared -----------------------------------------

compat() { run env "$@" scripts/api-compat.sh base; }

# <kind> <old text> <new text>: undeclared it fails with a compatibility finding; with
# API_COMPATIBLE=true it passes and says it accepted the break.
breaking() {
  local kind="$1"
  reset_fixture
  mutate "$2" "$3"
  update_report
  c="check 2: $kind fails undeclared"
  compat API_COMPATIBLE=
  status=$?
  if [ "$status" -ne 1 ]; then not_ok "$c" "exit status $status, expected 1"
  elif ! grep -q 'breaking changes in: Fixture.Api' "$LOG"; then not_ok "$c" "Fixture.Api is not reported as breaking"
  else ok "$c"; fi
  c="check 2: $kind passes when declared (API_COMPATIBLE=true)"
  if [ "$status" -ne 1 ]; then
    # Nothing was detected, so there is no break to accept: one failure, not two.
    echo "skip - $c: the break was not detected undeclared"
    return
  fi
  compat API_COMPATIBLE=true
  status=$?
  if [ "$status" -ne 0 ]; then not_ok "$c" "exit status $status, expected 0"
  elif ! grep -q 'accepted' "$LOG"; then not_ok "$c" "the output does not say the break was accepted"
  else ok "$c"; fi
}

breaking "a removed name" \
  '        public static int Sub(int a, int b) => a - b;' ''
breaking "a renamed name" \
  'public class Widget' 'public class Gadget'
breaking "a removed parameter" \
  'Drop(int x, int y) => x + y;' 'Drop(int x) => x;'
breaking "a renamed parameter" \
  'Add(int a, int b) => a + b;' 'Add(int left, int b) => left + b;'
breaking "a moved parameter" \
  'Move(string first, int second) => first + second;' 'Move(int second, string first) => first + second;'
breaking "an added required parameter" \
  'Twice(int x) => 2 * x;' 'Twice(int x, int y) => (2 * x) + y;'
breaking "an optional parameter that becomes required" \
  'Scale(int x, int factor = 2)' 'Scale(int x, int factor)'
breaking "a parameter type that accepts less" \
  'Describe(object value)' 'Describe(string value)'
breaking "a return type that can hold more" \
  'public static string Label(string text) => text;' 'public static object Label(string text) => text;'
breaking "a removed member of a public class" \
  $'\n        public int Size { get; set; }\n' $'\n'
breaking "a member that becomes required" \
  'public string Name { get; init; } = "";' 'public required string Name { get; init; }'
breaking "a changed parameter default" \
  'string greeting = "Hello"' 'string greeting = "Hi"'

c="check 2: an added name passes undeclared"
reset_fixture
mutate '        public static string Echo(string text) => text;' \
  $'        public static string Echo(string text) => text;\n        public static int Extra() => 0;'
update_report
if ! compat API_COMPATIBLE=; then not_ok "$c" "api-compat.sh failed"
elif ! grep -q 'no breaking changes' "$LOG"; then not_ok "$c" "the output does not say 'no breaking changes'"
else ok "$c"; fi

c="check 2: a value other than true (another label) does not pass a break"
reset_fixture
mutate '        public static int Sub(int a, int b) => a - b;' ''
update_report
compat API_COMPATIBLE=false
status=$?
if [ "$status" -ne 1 ]; then not_ok "$c" "exit status $status, expected 1"
else ok "$c"; fi

c="check 2: a package that is no longer packed fails"
reset_fixture
sed -i 's|<IsPackable>true</IsPackable>|<IsPackable>false</IsPackable>|' "$LIB/Fixture.Api.csproj"
compat API_COMPATIBLE=
status=$?
if [ "$status" -ne 1 ]; then not_ok "$c" "exit status $status, expected 1"
elif ! grep -q 'Fixture.Api: no longer packed' "$LOG"; then not_ok "$c" "the output does not say the package is no longer packed"
else ok "$c"; fi

# ---- The CI wiring of check 2 -------------------------------------------------------

WORKFLOW="$ROOT/.github/workflows/api-compatibility.yml"
c="check 2 in CI: only the api-compatible label sets API_COMPATIBLE"
grep -n 'API_COMPATIBLE' "$WORKFLOW" >"$LOG"
if grep -Fq "API_COMPATIBLE: \${{ contains(github.event.pull_request.labels.*.name, 'api-compatible') }}" "$WORKFLOW"; then ok "$c"
else not_ok "$c" "the workflow does not set API_COMPATIBLE from the api-compatible label"; fi

c="check 2 in CI: a PR title that declares a breaking change (!) passes the check"
grep -n 'title' "$WORKFLOW" >"$LOG" || true
if grep -q 'github.event.pull_request.title' "$WORKFLOW"; then ok "$c"
else not_ok "$c" "the workflow never reads the PR title, so a declared break (feat!:) still fails"; fi

echo
echo "api-check-selftest: ${passed} passed, ${#failed[@]} failed"
[ "${#failed[@]}" -eq 0 ]
