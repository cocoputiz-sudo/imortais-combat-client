#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
PACKAGE_LABEL="${2:-$VERSION}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Imortais.LinuxClient/Imortais.LinuxClient.csproj"
PUBLISH="$ROOT/dist/linux/publish"
PKGROOT="$ROOT/dist/linux/pkg"
OUT="$ROOT/dist/linux/IMORTAIS-Combat-Client-v${PACKAGE_LABEL}-linux-x64.deb"

rm -rf "$PUBLISH" "$PKGROOT"
mkdir -p "$PUBLISH" "$PKGROOT/DEBIAN"   "$PKGROOT/opt/imortais-combat-client"   "$PKGROOT/usr/bin"   "$PKGROOT/usr/share/applications"   "$PKGROOT/usr/share/icons/hicolor/256x256/apps"

dotnet publish "$PROJECT" -c Release -r linux-x64 --self-contained true   -p:PublishSingleFile=false   -p:DebugType=None   -p:DebugSymbols=false   -o "$PUBLISH"

# The installed version comes from the app itself (MainWindow.Version); the updater compares
# semantic versions of tags vX.Y.Z-linux. Same Debian package name as the alpha, so installing
# v1 replaces the alpha in place.
printf '{"version":"%s"}\n' "$PACKAGE_LABEL" > "$PUBLISH/linux-build.json"
cp -a "$PUBLISH/." "$PKGROOT/opt/imortais-combat-client/"
cp "$ROOT/upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/Assets/imortais-icon.png"   "$PKGROOT/usr/share/icons/hicolor/256x256/apps/imortais-combat-client.png"

cat > "$PKGROOT/DEBIAN/control" <<EOF
Package: imortais-combat-client
Version: $VERSION
Section: games
Priority: optional
Architecture: amd64
Depends: libpcap0.8 | libpcap0.8t64, libcap2-bin, pkexec
Maintainer: Guild Combat Client
Description: Guild Combat Client for Albion Online (Linux)
 Cliente Linux do War Room: captura libpcap, mesmo núcleo de envio do
 Windows (pareamento por guilda), dano PvP/PvE, mortes, party e mapa.
EOF

cat > "$PKGROOT/usr/bin/imortais-combat-client" <<'EOF'
#!/usr/bin/env bash
exec /opt/imortais-combat-client/IMORTAIS-Combat-Client "$@"
EOF
chmod 0755 "$PKGROOT/usr/bin/imortais-combat-client"
chmod 0755 "$PKGROOT/opt/imortais-combat-client/IMORTAIS-Combat-Client"

cat > "$PKGROOT/usr/share/applications/imortais-combat-client.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=Guild Combat Client
Comment=Observador de combate do War Room da guilda
Exec=imortais-combat-client
Icon=imortais-combat-client
Terminal=false
Categories=Game;Utility;
StartupNotify=true
EOF

cat > "$PKGROOT/DEBIAN/postinst" <<'EOF'
#!/usr/bin/env bash
set -e
BIN=/opt/imortais-combat-client/IMORTAIS-Combat-Client
if ! command -v setcap >/dev/null 2>&1; then
  echo "IMORTAIS: libcap2-bin/setcap não está disponível." >&2
  exit 1
fi
if [ ! -f "$BIN" ]; then
  echo "IMORTAIS: executável não encontrado em $BIN." >&2
  exit 1
fi
setcap cap_net_raw,cap_net_admin=eip "$BIN"
if command -v getcap >/dev/null 2>&1; then
  CAPS="$(getcap "$BIN" || true)"
  echo "$CAPS" | grep -q "cap_net_raw" || { echo "IMORTAIS: cap_net_raw não foi aplicada." >&2; exit 1; }
  echo "$CAPS" | grep -q "cap_net_admin" || { echo "IMORTAIS: cap_net_admin não foi aplicada." >&2; exit 1; }
fi
if command -v update-desktop-database >/dev/null 2>&1; then
  update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
fi
exit 0
EOF
chmod 0755 "$PKGROOT/DEBIAN/postinst"

cat > "$PKGROOT/DEBIAN/prerm" <<'EOF'
#!/usr/bin/env bash
set -e
BIN=/opt/imortais-combat-client/IMORTAIS-Combat-Client
if command -v setcap >/dev/null 2>&1 && [ -f "$BIN" ]; then
  setcap -r "$BIN" >/dev/null 2>&1 || true
fi
exit 0
EOF
chmod 0755 "$PKGROOT/DEBIAN/prerm"

mkdir -p "$(dirname "$OUT")"
dpkg-deb --root-owner-group --build "$PKGROOT" "$OUT"
( cd "$(dirname "$OUT")" && sha256sum "$(basename "$OUT")" > "$(basename "$OUT").sha256" )

echo
echo "Linux package created:"
echo "  $OUT"
echo "  $OUT.sha256"
