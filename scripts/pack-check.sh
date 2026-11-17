#!/usr/bin/env bash
# Packaging check: pack every package into a local folder feed, then install each one
# into a fresh console project outside the repository, from that feed plus nuget.org,
# and build and run it. The packages checked are whatever the solution packs, so a new
# package is covered without changing this script.
#
# It fails when:
#   - a package can't be installed, or its dependencies can't be restored;
#   - a package assembly references an assembly that the package's declared
#     dependencies don't bring in (an undeclared dependency), checked at run time on
#     net8.0 and net10.0;
#   - the consumer can't compile against a public type a package records in its
#     PublicAPI files, or a type comes from the wrong assembly. A package with no
#     assembly of its own (a metapackage) is checked against the public types of the
#     packages it depends on;
#   - a package ships an assembly but has no PublicAPI files, or has neither an
#     assembly nor a dependency on another package in the feed;
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
PLAN="$WORK/plan"
cleanup() { if [ "${KEEP_WORK:-}" = "1" ]; then echo "pack-check: kept $WORK"; else rm -rf "$WORK"; fi; }
trap cleanup EXIT

# A private package cache, so a package repacked under the same version is never
# served from an older copy in the user's global cache.
export NUGET_PACKAGES="$WORK/nuget-packages"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

echo "pack-check: packing into $FEED"
"$DOTNET" pack "$ROOT/LaunchDarkly.AI.Server.sln" -c Release -o "$FEED" -warnaserror -nologo -v quiet

# Reads every package in the feed and writes, for each one, $PLAN/<id>/version,
# $PLAN/<id>/assemblies (C# string literals of the assemblies to load) and
# $PLAN/<id>/types (C# tuples of a public type and the assembly it must come from).
python3 -I - "$FEED" "$ROOT" "$PLAN" <<'PY'
import os, re, sys, zipfile
import xml.etree.ElementTree as ET

feed, root, plan = sys.argv[1:4]

def fail(message):
    sys.exit(f"pack-check: {message}")

packages = {}
for name in sorted(os.listdir(feed)):
    if not name.endswith(".nupkg") or name.endswith(".symbols.nupkg"):
        continue
    with zipfile.ZipFile(os.path.join(feed, name)) as z:
        names = z.namelist()
        nuspec = ET.fromstring(z.read(next(n for n in names if n.endswith(".nuspec"))))
    ns = {"n": nuspec.tag.split("}")[0].strip("{")}
    meta = nuspec.find("n:metadata", ns)
    pid = meta.find("n:id", ns).text
    version = meta.find("n:version", ns).text
    deps = {d.get("id") for d in meta.iter(f"{{{ns['n']}}}dependency")}
    assemblies = sorted({os.path.basename(n)[:-4] for n in names if n.startswith("lib/") and n.endswith(".dll")})
    has_payload = any(n.startswith("lib/") or n.endswith(".dll") for n in names)
    packages[pid] = {"version": version, "deps": deps, "assemblies": assemblies, "has_payload": has_payload}

if not packages:
    fail(f"no packages were packed into {feed}")

# The metapackage's own contract.
meta_pkg = packages.get("LaunchDarkly.AI")
if meta_pkg is None:
    fail("LaunchDarkly.AI was not packed")
if meta_pkg["has_payload"]:
    fail("LaunchDarkly.AI must not contain an assembly")
if "LaunchDarkly.AI.Server" not in meta_pkg["deps"]:
    fail("LaunchDarkly.AI does not depend on LaunchDarkly.AI.Server")
print("pack-check: LaunchDarkly.AI has no assembly and depends on LaunchDarkly.AI.Server", file=sys.stderr)

def public_types(package_id):
    folder = os.path.join(root, "src", package_id)
    files = [os.path.join(folder, f) for f in ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt")]
    if not all(os.path.isfile(f) for f in files):
        fail(f"{package_id} ships an assembly but src/{package_id} has no PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt")
    types = set()
    for path in files:
        for line in open(path, encoding="utf-8"):
            line = line.strip()
            # "~" marks an entry without nullable annotations, and "*REMOVED*" an entry
            # no longer in the API: neither is a type to compile against here.
            if not line or line.startswith(("#", "~", "*REMOVED*")):
                continue
            if "(" in line or "->" in line or " = " in line:
                continue  # a member, not a type
            name = line.split()[-1]  # drop modifiers such as abstract, sealed, static
            name = re.sub(r"<([^<>]*)>", lambda m: "<" + "," * m.group(1).count(",") + ">", name)
            types.add(name)
    return sorted(types)

def own_assembly(package_id):
    assemblies = packages[package_id]["assemblies"]
    return package_id if package_id in assemblies else assemblies[0]

def load_closure(package_id, seen=None):
    """The assemblies of a package and of the feed packages it depends on, recursively."""
    seen = set() if seen is None else seen
    if package_id in seen:
        return set()
    seen.add(package_id)
    result = set(packages[package_id]["assemblies"])
    for dep in packages[package_id]["deps"] & packages.keys():
        result |= load_closure(dep, seen)
    return result

for pid, info in sorted(packages.items()):
    feed_deps = sorted(info["deps"] & packages.keys())
    if info["assemblies"]:
        type_sources = [pid]
    elif feed_deps:
        type_sources = [d for d in feed_deps if packages[d]["assemblies"]]
    else:
        fail(f"{pid} has no assembly and no dependency on another package in the feed")
    entries = []
    for source in type_sources:
        assembly = own_assembly(source)
        entries += [f'            (typeof(global::{t}), "{assembly}"),' for t in public_types(source)]
    out = os.path.join(plan, pid)
    os.makedirs(out)
    with open(os.path.join(out, "version"), "w") as f:
        f.write(info["version"])
    with open(os.path.join(out, "assemblies"), "w") as f:
        f.write(", ".join(f'"{a}"' for a in sorted(load_closure(pid))))
    with open(os.path.join(out, "types"), "w") as f:
        f.write("\n".join(entries))
PY
mapfile -t PACKAGE_IDS < <(ls "$PLAN")
echo "pack-check: packages: ${PACKAGE_IDS[*]}"

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

for id in "${PACKAGE_IDS[@]}"; do
  version="$(cat "$PLAN/$id/version")"
  dir="$WORK/consumers/$id"
  echo "pack-check: $id $version: fresh consumer in $dir"
  "$DOTNET" new console -o "$dir" -n PackCheckConsumer --no-restore >/dev/null
  cp "$WORK/nuget.config" "$dir/nuget.config"
  sed -i.bak 's:<TargetFramework>[^<]*</TargetFramework>:<TargetFrameworks>net8.0;net10.0</TargetFrameworks><TreatWarningsAsErrors>true</TreatWarningsAsErrors>:' "$dir/PackCheckConsumer.csproj"
  rm -f "$dir/PackCheckConsumer.csproj.bak"
  # Sources: the consumer's nuget.config, which lists only the local feed and nuget.org.
  "$DOTNET" add "$dir/PackCheckConsumer.csproj" package "$id" --version "$version" >/dev/null

  cat > "$dir/Program.cs" <<EOF
using System;
using System.Collections.Generic;
using System.Reflection;

internal static class PackCheck
{
    private static int Main()
    {
        var failures = new List<string>();
        foreach (var name in new string[] { $(cat "$PLAN/$id/assemblies") })
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
        var publicTypes = new (Type Type, string Assembly)[]
        {
$(cat "$PLAN/$id/types")
        };
        foreach (var (type, expected) in publicTypes)
        {
            if (type.Assembly.GetName().Name != expected)
            {
                failures.Add(\$"{type.FullName} comes from {type.Assembly.GetName().Name}, expected {expected}");
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
