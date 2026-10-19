#!/usr/bin/env bash
# tools/release.sh OWNER/REPO VERSION ["changelog text"]
# Builds the plugin, copies the Dalamud zip to dist/latest.zip and rewrites pluginmaster.json for that version.
# Upload dist/latest.zip as an asset of the GitHub release v<VERSION>, then commit pluginmaster.json (see README, "Releasing").
set -euo pipefail
[ $# -ge 2 ] || { sed -n 2,4p "$0"; exit 1; }
repo=$1 version=$2 changelog=${3:-}
cd "$(dirname "$0")/.."
if [ -x "$PWD/.tools/dotnet/dotnet" ]; then export DOTNET_ROOT="$PWD/.tools/dotnet" PATH="$PWD/.tools/dotnet:$PATH"; fi
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export DALAMUD_HOME="${DALAMUD_HOME:-${XLCORE:-$HOME/.xlcore}/dalamud/Hooks/dev}"
dotnet build plugin/SpherePlayer -c Release -p:Version="$version"
out=plugin/SpherePlayer/bin/Release/SpherePlayer
mkdir -p dist
cp "$out/latest.zip" dist/latest.zip
python3 - "$repo" "$version" "$changelog" "$out/SpherePlayer.json" <<'PY'
import json, sys, time
repo, version, changelog, manifest = sys.argv[1:5]
m = json.load(open(manifest, encoding="utf-8"))
url = f"https://github.com/{repo}/releases/download/v{version}/latest.zip"
m.update({
    "RepoUrl": f"https://github.com/{repo}",
    "IsHide": False, "IsTestingExclusive": False,
    "DownloadCount": 0, "LastUpdate": int(time.time()),
    "DownloadLinkInstall": url, "DownloadLinkTesting": url, "DownloadLinkUpdate": url,
    "Changelog": changelog,
})
json.dump([m], open("pluginmaster.json", "w", encoding="utf-8"), indent=2, ensure_ascii=False)
open("pluginmaster.json", "a").write("\n")
PY
echo "dist/latest.zip and pluginmaster.json are ready for v$version."
echo "Next: gh release create v$version dist/latest.zip -R $repo --title $version   (then commit pluginmaster.json)"
