#!/usr/bin/env bash
# Packaging check: pack every package into a local folder feed, then install each one
# into a fresh console project outside the repository, from that feed plus nuget.org,
# and build and run it.
#
# It fails when:
#   - a package can't be installed, or its dependencies can't be restored;
#   - a package assembly references an assembly that the package's declared
#     dependencies don't bring in (an undeclared dependency), checked at run time on
#     net8.0 and net10.0;
#   - the consumer can't compile against a public type the package records in its
#     PublicAPI files (for LaunchDarkly.AI, the core package's types), or a type comes
#     from the wrong assembly;
#   - the LaunchDarkly.AI metapackage contains an assembly, or doesn't depend on
#     LaunchDarkly.AI.Server.
#
# Usage: scripts/pack-check.sh            (KEEP_WORK=1 keeps the temporary folder)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET="${DOTNET:-dotnet}"
NUGET_ORG="https://api.nuget.org/v3/index.json"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/pack-check.XXXXXX")"
FEED="$WORK/feed"
cleanup() { if [ "${KEEP_WORK:-}" = "1" ]; then echo "pack-check: kept $WORK"; else rm -rf "$WORK"; fi; }
trap cleanup EXIT

# A private package cache, so a package repacked under the same version is never
# served from an older copy in the user's global cache.
export NUGET_PACKAGES="$WORK/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/LaunchDarkly.AI.Server/LaunchDarkly.AI.Server.csproj")"
[ -n "$VERSION" ] || { echo "pack-check: could not read the version" >&2; exit 1; }

echo "pack-check: packing $VERSION into $FEED"
"$DOTNET" pack "$ROOT/LaunchDarkly.AI.Server.sln" -c Release -o "$FEED" -warnaserror -nologo -v quiet

# package id | assemblies the package must make loadable | PublicAPI file folder for its types
PACKAGES=(
  "LaunchDarkly.AI.Server|LaunchDarkly.AI.Server|src/LaunchDarkly.AI.Server"
  "LaunchDarkly.AI.Server.ExtensionsAI|LaunchDarkly.AI.Server.ExtensionsAI,LaunchDarkly.AI.Server|src/LaunchDarkly.AI.Server.ExtensionsAI"
  "LaunchDarkly.AI|LaunchDarkly.AI.Server|src/LaunchDarkly.AI.Server"
)

python3 -I - "$FEED/LaunchDarkly.AI.$VERSION.nupkg" <<'PY'
import sys, zipfile, re
path = sys.argv[1]
with zipfile.ZipFile(path) as z:
    names = z.namelist()
    libs = [n for n in names if n.startswith("lib/") or n.endswith(".dll")]
    if libs:
        sys.exit(f"pack-check: LaunchDarkly.AI must not contain an assembly, found: {libs}")
    nuspec = z.read(next(n for n in names if n.endswith(".nuspec"))).decode()
    if not re.search(r'<dependency id="LaunchDarkly\.AI\.Server"', nuspec):
        sys.exit("pack-check: LaunchDarkly.AI does not depend on LaunchDarkly.AI.Server")
print("pack-check: LaunchDarkly.AI has no assembly and depends on LaunchDarkly.AI.Server")
PY

# Writes C# `typeof(...)` entries for each public type recorded in a package's PublicAPI files.
public_types() {
  python3 -I - "$1"/PublicAPI.Shipped.txt "$1"/PublicAPI.Unshipped.txt <<'PY'
import sys, re
types = set()
for path in sys.argv[1:]:
    for line in open(path, encoding="utf-8"):
        line = line.strip().lstrip("~")
        if not line or line.startswith("#") or line.startswith("*REMOVED*"):
            continue
        if "(" in line or "->" in line or " = " in line:
            continue  # a member, not a type
        name = line.split()[-1]  # drop modifiers such as abstract, sealed, static
        name = re.sub(r"<([^<>]*)>", lambda m: "<" + "," * m.group(1).count(",") + ">", name)
        types.add(name)
for t in sorted(types):
    print(f"            typeof(global::{t}),")
PY
}

cat > "$WORK/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-feed" value="$FEED" />
    <add key="nuget.org" value="$NUGET_ORG" />
  </packageSources>
</configuration>
EOF

for entry in "${PACKAGES[@]}"; do
  IFS='|' read -r id assemblies apidir <<<"$entry"
  dir="$WORK/consumers/$id"
  echo "pack-check: $id: fresh consumer in $dir"
  "$DOTNET" new console -o "$dir" -n PackCheckConsumer --no-restore >/dev/null
  cp "$WORK/nuget.config" "$dir/nuget.config"
  sed -i.bak 's:<TargetFramework>[^<]*</TargetFramework>:<TargetFrameworks>net8.0;net10.0</TargetFrameworks><TreatWarningsAsErrors>true</TreatWarningsAsErrors>:' "$dir/PackCheckConsumer.csproj"
  rm -f "$dir/PackCheckConsumer.csproj.bak"
  # Sources: the consumer's nuget.config, which lists only the local feed and nuget.org.
  "$DOTNET" add "$dir/PackCheckConsumer.csproj" package "$id" --version "$VERSION" >/dev/null

  asm_list="$(printf '"%s", ' ${assemblies//,/ })"
  expected="${assemblies%%,*}"
  [ "$id" = "LaunchDarkly.AI" ] && expected="LaunchDarkly.AI.Server"
  cat > "$dir/Program.cs" <<EOF
using System;
using System.Collections.Generic;
using System.Reflection;

internal static class PackCheck
{
    private static int Main()
    {
        var failures = new List<string>();
        foreach (var name in new[] { $asm_list })
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(new AssemblyName(name));
            }
            catch (Exception e)
            {
                failures.Add(\$"cannot load {name}: {e.GetType().Name}: {e.Message}");
                continue;
            }
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                try
                {
                    Assembly.Load(reference);
                }
                catch (Exception e)
                {
                    failures.Add(\$"{name} references {reference.Name} {reference.Version}, which no declared dependency provides: {e.GetType().Name}");
                }
            }
        }
        var publicTypes = new Type[]
        {
$(public_types "$ROOT/$apidir")
        };
        foreach (var type in publicTypes)
        {
            if (type.Assembly.GetName().Name != "$expected")
            {
                failures.Add(\$"{type.FullName} comes from {type.Assembly.GetName().Name}, expected $expected");
            }
        }
        foreach (var failure in failures)
        {
            Console.Error.WriteLine("pack-check: $id: " + failure);
        }
        Console.WriteLine(\$"pack-check: $id: {publicTypes.Length} public type(s) checked, {failures.Count} failure(s) on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        return failures.Count == 0 ? 0 : 1;
    }
}
EOF
  "$DOTNET" build "$dir/PackCheckConsumer.csproj" -c Release -nologo -v quiet
  for tfm in net8.0 net10.0; do
    "$DOTNET" run --project "$dir/PackCheckConsumer.csproj" -c Release -f "$tfm" --no-build
  done
done

echo "pack-check: all packages install, restore, build and load cleanly"
