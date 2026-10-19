#!/usr/bin/env bash
# Runs the plugin logic checks (host download, YouTube-terms rules) with the project-local .NET SDK.
set -e
cd "$(dirname "$0")/.."
export DOTNET_ROOT="$PWD/.tools/dotnet" PATH="$PWD/.tools/dotnet:$PATH" DOTNET_CLI_HOME="$PWD/.tools/home" DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
export DALAMUD_HOME="${DALAMUD_HOME:-${XLCORE:-$HOME/.xlcore}/dalamud/Hooks/dev}"
dotnet run --project tests/PluginTests -c Release
