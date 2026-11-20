#!/usr/bin/env bash
# API compatibility check: packs the packages at a base git ref (in a temporary
# worktree) and in the current working tree, then runs Microsoft.DotNet.ApiCompat
# (pinned in .config/dotnet-tools.json) on each package against its base version.
#
# A breaking change to the public API (a removed or changed type or member, a
# renamed parameter, a dropped target framework) fails the check. An addition is not
# breaking. A breaking change passes when it is declared:
#
#   - PR_TITLE is a conventional-commit title that marks a breaking change with `!`
#     (`feat!: ...`, `fix(client)!: ...`); in CI, the pull request's title;
#   - API_COMPATIBLE=true: in CI, the `api-compatible` pull-request label, which an owner
#     applies to a flagged change they judge compatible.
#
# The check still runs and reports what it found.
#
# Usage: scripts/api-compat.sh <base-ref>     (for example origin/main, or a commit)
#        APICOMPAT_ARGS="--suppression-file x.xml" passes extra arguments to ApiCompat.
set -euo pipefail

BASE="${1:?usage: scripts/api-compat.sh <base-ref>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
SOLUTION="LaunchDarkly.AI.Server.sln"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

if ! BASE_SHA="$(git -C "$ROOT" rev-parse --verify --quiet "$BASE^{commit}")"; then
  echo "api-compat: the base ref '$BASE' does not exist (yet; local main stays empty until the first reviewed step is fast-forwarded into it)." >&2
  echo "api-compat: pass an existing ref or commit: make api-compat BASE=<ref>" >&2
  exit 1
fi
WORK="$(mktemp -d "${TMPDIR:-/tmp}/api-compat.XXXXXX")"
cleanup() {
  git -C "$ROOT" worktree remove --force "$WORK/base" >/dev/null 2>&1 || true
  rm -rf "$WORK"
}
trap cleanup EXIT

echo "api-compat: base $BASE ($BASE_SHA) against the working tree"
git -C "$ROOT" worktree add --detach --quiet "$WORK/base" "$BASE_SHA"

if [ ! -f "$WORK/base/$SOLUTION" ]; then
  echo "api-compat: the base has no $SOLUTION, so there is no API to compare against."
  exit 0
fi

"$DOTNET" tool restore --tool-manifest "$ROOT/.config/dotnet-tools.json" >/dev/null

echo "api-compat: packing the base"
"$DOTNET" pack "$WORK/base/$SOLUTION" -c Release -o "$WORK/base-nupkgs" -nologo -v quiet
echo "api-compat: packing the working tree"
"$DOTNET" pack "$ROOT/$SOLUTION" -c Release -o "$WORK/head-nupkgs" -nologo -v quiet

# ApiCompat needs every assembly the package's assemblies were compiled against
# (framework reference assemblies and package dependencies) to resolve the types in
# signatures. MSBuild reports them per target framework as ReferencePath.
# The option is given once, followed by one "<moniker>|<file>,<file>,..." value per
# target framework, where <moniker> is the full form, for example .NETCoreApp,Version=v8.0.
references() { # <option> <project file>
  local option="$1" project="$2" bin tfm moniker files
  bin="$(dirname "$project")/bin/Release"
  printf '%s\n' "$option"
  for tfm in "$bin"/*/; do
    tfm="$(basename "$tfm")"
    json="$("$DOTNET" msbuild "$project" -nologo -t:ResolveReferences -p:Configuration=Release \
      -p:TargetFramework="$tfm" -getProperty:TargetFrameworkMoniker -getItem:ReferencePath)"
    moniker="$(python3 -I -c 'import sys,json; print(json.load(sys.stdin)["Properties"]["TargetFrameworkMoniker"])' <<<"$json")"
    files="$(python3 -I -c 'import sys,json; print(",".join(i["FullPath"] for i in json.load(sys.stdin)["Items"]["ReferencePath"]))' <<<"$json")"
    printf '%s\n' "$moniker|$files"
  done
}

# Lists the packages in a folder as "<id>|<nupkg path>|<1 if it ships an assembly, else 0>",
# reading the ID from each package's nuspec.
list_packages() { # <folder>
  python3 -I - "$1" <<'PY'
import os, sys, zipfile
import xml.etree.ElementTree as ET
folder = sys.argv[1]
for name in sorted(os.listdir(folder)):
    if not name.endswith(".nupkg") or name.endswith(".symbols.nupkg"):
        continue
    path = os.path.join(folder, name)
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        nuspec = ET.fromstring(z.read(next(n for n in names if n.endswith(".nuspec"))))
    ns = nuspec.tag.split("}")[0].strip("{")
    pid = nuspec.find(f"{{{ns}}}metadata/{{{ns}}}id").text
    ships = any(n.startswith("lib/") and n.endswith(".dll") for n in names)
    print(f"{pid}|{path}|{int(ships)}")
PY
}

declare -A HEAD_PKG BASE_PKG SHIPS
while IFS='|' read -r id path ships; do HEAD_PKG[$id]="$path"; SHIPS[$id]="$ships"; done < <(list_packages "$WORK/head-nupkgs")
while IFS='|' read -r id path ships; do
  BASE_PKG[$id]="$path"
  [ "${SHIPS[$id]:-0}" = "1" ] || SHIPS[$id]="$ships"
done < <(list_packages "$WORK/base-nupkgs")
mapfile -t PACKAGE_IDS < <(printf '%s\n' "${!HEAD_PKG[@]}" "${!BASE_PKG[@]}" | sort -u)
echo "api-compat: packages: ${PACKAGE_IDS[*]}"

# Breaks that ApiCompat does not flag, because the metadata signature stays the same.
# Each function prints one "error CPXnnn" line per break and returns 1 if there is one,
# or 2 if it could not run.
#
# A parameter default removed (an optional parameter becomes required) or changed: from
# the PublicAPI reports, which record each default (`int factor = 2`).
report_breaks() { # <base project folder> <head project folder>
  local out
  out="$(python3 -I - "$1" "$2" <<'PY'
import os, sys

def report(folder):
    lines = set()
    for name in ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"):
        path = os.path.join(folder, name)
        if os.path.exists(path):
            for line in open(path, encoding="utf-8").read().splitlines():
                if line.startswith("*REMOVED*"):
                    lines.discard(line[len("*REMOVED*"):])
                elif line.strip() and not line.startswith("#"):
                    lines.add(line)
    return lines

def value_end(line, j):
    """Where a default value that starts at j ends: the next , or ) outside brackets and quotes,
    so default(KeyValuePair<int, string>) is one value."""
    quote, depth = None, 0
    while j < len(line):
        c = line[j]
        if quote:
            if c == "\\":
                j += 1
            elif c == quote:
                quote = None
        elif c in "\"'":
            quote = c
        elif c in "(<[":
            depth += 1
        elif c in ")>]" and depth:
            depth -= 1
        elif c in ",)" and depth == 0:
            break
        j += 1
    return j

def split(line):
    """(the line without its parameter defaults, [each parameter's default, or None])."""
    start = line.find("(")
    if start < 0:
        return line, []
    out, defaults, depth, quote, i = [line[: start + 1]], [None], 1, None, start + 1
    while i < len(line):
        c = line[i]
        if quote:
            if c == "\\":
                out.append(line[i : i + 2])
                i += 2
                continue
            quote = None if c == quote else quote
        elif depth == 1 and line.startswith(" = ", i):
            j = value_end(line, i + 3)
            defaults[-1] = line[i + 3 : j]
            i = j
            continue
        elif c in "\"'":
            quote = c
        elif c in "(<[":
            depth += 1
        elif c in ")>]":
            depth -= 1
            if depth == 0:
                out.append(line[i:])
                break
        elif c == "," and depth == 1:
            defaults.append(None)
        out.append(c)
        i += 1
    return "".join(out), defaults

base, head = report(sys.argv[1]), report(sys.argv[2])
added = {}
for line in head - base:
    added.setdefault(split(line)[0], []).append(line)
for old in sorted(base - head):
    key, old_defaults = split(old)
    for new in added.get(key, []):
        for old_default, new_default in zip(old_defaults, split(new)[1]):
            if old_default is not None and old_default != new_default:
                kind = "an optional parameter becomes required" if new_default is None else "a parameter default changes"
                print(f"error CPX001: {kind}: '{old}' is now '{new}'")
PY
)" || return 2
  [ -z "$out" ] && return 0
  printf '%s\n' "$out"
  return 1
}

# A member that becomes required, on a type that callers construct: from the assemblies'
# RequiredMemberAttribute (scripts/required-members.cs), since the report does not record
# `required`. Compares the base and head builds of each target framework they share.
required_breaks() { # <base project folder> <head project folder> <assembly name>
  local base_bin="$1/bin/Release" head_bin="$2/bin/Release" tfm status=0 member type
  for tfm in "$head_bin"/*/; do
    tfm="$(basename "$tfm")"
    [ -f "$base_bin/$tfm/$3.dll" ] && [ -f "$head_bin/$tfm/$3.dll" ] || continue
    "$DOTNET" run "$ROOT/scripts/required-members.cs" -- "$base_bin/$tfm/$3.dll" >"$WORK/required-base.txt" || return 2
    "$DOTNET" run "$ROOT/scripts/required-members.cs" -- "$head_bin/$tfm/$3.dll" >"$WORK/required-head.txt" || return 2
    while read -r member; do
      type="${member%.*}"
      if grep -qxF "required $member" "$WORK/required-base.txt" ||
        ! grep -qxF "type $type" "$WORK/required-base.txt" ||
        ! grep -qxF "constructible $type" "$WORK/required-head.txt"; then
        continue
      fi
      echo "error CPX002: $tfm: $member becomes required, and callers construct $type"
      status=1
    done < <(sed -n 's/^required //p' "$WORK/required-head.txt")
  done
  return "$status"
}

failed=()
tool_errors=()
for id in "${PACKAGE_IDS[@]}"; do
  head_pkg="${HEAD_PKG[$id]:-}"
  base_pkg="${BASE_PKG[$id]:-}"
  if [ -z "$head_pkg" ]; then
    echo "api-compat: $id: no longer packed; that is a breaking change."
    failed+=("$id")
    continue
  fi
  if [ -z "$base_pkg" ]; then
    echo "api-compat: $id: new package, nothing to compare against."
    continue
  fi
  if [ "${SHIPS[$id]}" != "1" ]; then
    echo "api-compat: $id: ships no assembly (a metapackage), no API to compare."
    continue
  fi
  args=()
  while IFS= read -r line; do args+=("$line"); done < <(
    references --package-assembly-references "$ROOT/src/$id/$id.csproj"
    references --baseline-package-assembly-references "$WORK/base/src/$id/$id.csproj")
  echo "api-compat: $id"
  log="$WORK/$id.log"
  if (cd "$ROOT" && "$DOTNET" tool run apicompat -- package "$head_pkg" \
        --baseline-package "$base_pkg" \
        --run-api-compat \
        --enable-rule-cannot-change-parameter-name \
        ${APICOMPAT_ARGS:-} \
        "${args[@]}") >"$log" 2>&1; then
    cat "$log"
  else
    cat "$log"
    # A compatibility finding is an error with a CP0nnn diagnostic ID. Anything else
    # (CP1nnn reference warnings, usage errors) is the tool failing to run properly,
    # which no label can accept.
    if grep -Eq '\berror CP0[0-9]{3}\b|^CP0[0-9]{3}:' "$log"; then
      failed+=("$id")
    else
      tool_errors+=("$id")
      continue
    fi
  fi
  # CPX: this script's own findings, for breaks that ApiCompat does not see.
  for check in report_breaks required_breaks; do
    status=0
    "$check" "$WORK/base/src/$id" "$ROOT/src/$id" "$id" || status=$?
    if [ "$status" -eq 2 ]; then
      echo "api-compat: $id: $check could not run."
      tool_errors+=("$id")
    elif [ "$status" -eq 1 ] && [[ " ${failed[*]} " != *" $id "* ]]; then
      failed+=("$id")
    fi
  done
done

if [ "${#tool_errors[@]}" -ne 0 ]; then
  echo "api-compat: ApiCompat did not run correctly for: ${tool_errors[*]} (see the output above)."
  exit 2
fi

if [ "${#failed[@]}" -eq 0 ]; then
  echo "api-compat: no breaking changes."
  exit 0
fi

echo "api-compat: breaking changes in: ${failed[*]}"
# A conventional-commit type, an optional scope, then `!` before the colon.
title_pattern='^[a-z]+(\([^)]*\))?!: '
accepted_by=""
if [[ "${PR_TITLE:-}" =~ $title_pattern ]]; then
  accepted_by="the pull request title, which declares a breaking change (!)"
elif [ "${API_COMPATIBLE:-false}" = "true" ]; then
  accepted_by="the api-compatible label (API_COMPATIBLE=true)"
fi
if [ -n "$accepted_by" ]; then
  echo "api-compat: accepted, because the break is declared by $accepted_by."
  if [ -n "${GITHUB_ACTIONS:-}" ]; then
    echo "::warning title=Breaking API change accepted::Breaking changes in ${failed[*]} were accepted by $accepted_by."
  fi
  exit 0
fi
echo "api-compat: if the break is intended, mark the pull request title with ! (feat!: ..., fix(scope)!: ...; locally: PR_TITLE='feat!: ...')."
echo "api-compat: if an owner judges the flagged change compatible, they add the api-compatible label (locally: API_COMPATIBLE=true)."
exit 1
