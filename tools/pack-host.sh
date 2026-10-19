#!/usr/bin/env bash
# Packs the player host (Chromium, about 360 MB) into dist/ as a zip and writes its URL, size and SHA-256 into host-manifest.json.
# Usage: tools/pack-host.sh OWNER/REPO [REVISION]
# Upload dist/*.zip as a GitHub release asset under the tag printed at the end, then build and release the plugin (it embeds host-manifest.json).
set -euo pipefail
cd "$(dirname "$0")/.."
repo="${1:?usage: tools/pack-host.sh OWNER/REPO [REVISION]}"
rev="${2:-1}"
cef="$(sed -n 's/.*CefSharp.OffScreen.NETCore" Version="\([^"]*\)".*/\1/p' player-host/PlayerHost.csproj)"
[ -n "$cef" ] || { echo "CefSharp version not found in player-host/PlayerHost.csproj"; exit 1; }
version="$cef-$rev"
export DOTNET_ROOT="$PWD/.tools/dotnet" PATH="$PWD/.tools/dotnet:$PATH" DOTNET_CLI_HOME="$PWD/.tools/home" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build player-host -c Release -o player-host/out >/dev/null
name="PlayerHost-$version.zip"
tag="host-$version"
mkdir -p dist
python3 - "$name" "$version" "$repo" "$tag" <<'PY'
import hashlib, json, os, sys, zipfile
name, version, repo, tag = sys.argv[1:5]
src = "player-host/out"
skip = lambda f: f.endswith(".pdb") or f == "host-trace.log"
out = os.path.join("dist", name)
nuget = os.path.join(os.environ.get("DOTNET_CLI_HOME", ".tools/home"), ".nuget", "packages")
licenses = [os.path.join(nuget, p) for p in (
    "cefsharp.common.netcore/%s/LICENSE" % version.split("-")[0],
    "chromiumembeddedframework.runtime.win-x64/%s/LICENSE.txt" % "143.0.9")]
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for root, _, files in sorted(os.walk(src)):
        for f in sorted(files):
            if skip(f): continue
            p = os.path.join(root, f)
            z.write(p, os.path.relpath(p, src))
    for i, l in enumerate(licenses):
        if os.path.exists(l): z.write(l, "licenses/%s-%s" % (["CefSharp", "Chromium-CEF"][i], os.path.basename(l)))
h = hashlib.sha256()
with open(out, "rb") as f:
    for chunk in iter(lambda: f.read(1 << 20), b""): h.update(chunk)
manifest = {"version": version, "url": "https://github.com/%s/releases/download/%s/%s" % (repo, tag, name),
            "sha256": h.hexdigest().upper(), "size": os.path.getsize(out)}
json.dump(manifest, open("host-manifest.json", "w"), indent=2); open("host-manifest.json", "a").write("\n")
print(json.dumps(manifest, indent=2))
PY
echo
echo "Next: upload dist/$name to a GitHub release tagged $tag, then rebuild the plugin (./build.sh)."
