#!/bin/sh
# Builds the Linux release package (item 28c): dist/FanPilot-<version>-linux-x64.tar.gz, with FanPilot published as one
# self-contained file (no .NET needed on the PC), the panel, the systemd unit, install.sh and the licenses. Runs on
# Linux (the CI; on Windows, WSL or Docker: NTFS keeps no executable bit, and the package needs FanPilot and install.sh
# executable); the .NET SDK of global.json is enough.
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
VERSION=$(sed -n 's:.*<VersionPrefix>\(.*\)</VersionPrefix>.*:\1:p' "$ROOT/Directory.Build.props")
NAME="FanPilot-$VERSION-linux-x64"
WORK="$ROOT/dist/$NAME"

rm -rf "$WORK" "$ROOT/dist/$NAME.tar.gz"
mkdir -p "$WORK"

# Both frameworks first: "dotnet publish -f net10.0" restoring by itself would write a lock file without net472. The test
# project brings FanPilot with it; the solution would bring the tray too, a Windows-only project.
dotnet restore "$ROOT/tests/FanPilot.Tests/FanPilot.Tests.csproj" --nologo
dotnet publish "$ROOT/src/FanPilot.csproj" -c Release -f net10.0 -r linux-x64 --self-contained \
    -p:PublishSingleFile=true --no-restore --nologo -o "$WORK"

cp "$ROOT/packaging/linux/fanpilot.service" "$ROOT/packaging/linux/fanpilot.desktop" "$ROOT/packaging/linux/install.sh" "$WORK/"
cp "$ROOT/LICENSE" "$ROOT/THIRD_PARTY_NOTICES.md" "$ROOT/README.md" "$ROOT/README.pt-BR.md" "$ROOT/CHANGELOG.md" "$WORK/"
# The same modes whoever builds it: only FanPilot and install.sh run.
find "$WORK" -type d -exec chmod 755 {} +
find "$WORK" -type f -exec chmod 644 {} +
chmod 755 "$WORK/FanPilot" "$WORK/install.sh"

# Owned by root, whoever built it.
tar -C "$ROOT/dist" --owner=0 --group=0 --numeric-owner -czf "$ROOT/dist/$NAME.tar.gz" "$NAME"
rm -rf "$WORK"
echo "OK -> dist/$NAME.tar.gz"
