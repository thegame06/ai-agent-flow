#!/usr/bin/env bash
set -euo pipefail

if [[ -n "${DOTNET_CMD:-}" ]]; then
  echo "$DOTNET_CMD"
  exit 0
fi

for candidate in \
  dotnet \
  dotnet.exe \
  "/mnt/c/Program Files/dotnet/dotnet.exe" \
  "/c/Program Files/dotnet/dotnet.exe" \
  "/cygdrive/c/Program Files/dotnet/dotnet.exe"; do
  if command -v "$candidate" >/dev/null 2>&1 || [[ -x "$candidate" ]]; then
    echo "$candidate"
    exit 0
  fi
done

echo "dotnet was not found. Install .NET SDK or set DOTNET_CMD=/path/to/dotnet." >&2
exit 127
