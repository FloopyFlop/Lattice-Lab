#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

export NUGET_PACKAGES="$ROOT_DIR/.nuget/packages"
mkdir -p "$NUGET_PACKAGES"

exec dotnet run --project "$ROOT_DIR/GateSimMac/GateSimMac.csproj" -- "$@"
