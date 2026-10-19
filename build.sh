#!/usr/bin/env bash
# Builds the plugin and the player host. Uses the .NET 10 SDK in .tools/dotnet if present, else the one on PATH.
# DALAMUD_HOME must point to a Dalamud build (default: ~/.xlcore/dalamud/Hooks/dev).
set -e
cd "$(dirname "$0")"
if [ -x "$PWD/.tools/dotnet/dotnet" ]; then
  export DOTNET_ROOT="$PWD/.tools/dotnet" PATH="$PWD/.tools/dotnet:$PATH" DOTNET_CLI_HOME="$PWD/.tools/home"
fi
export DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export DALAMUD_HOME="${DALAMUD_HOME:-${XLCORE:-$HOME/.xlcore}/dalamud/Hooks/dev}"
dotnet build plugin/SpherePlayer -c Release
[ "$1" = "--no-host" ] || dotnet build player-host -c Release -o player-host/out
echo "Plugin: $PWD/plugin/SpherePlayer/bin/Release/SpherePlayer.dll"
