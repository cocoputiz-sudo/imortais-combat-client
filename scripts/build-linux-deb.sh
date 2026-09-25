#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.1.0}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Imortais.LinuxClient/Imortais.LinuxClient.csproj"
PUBLISH="$ROOT/dist/linux/publish"
PKGROOT="$ROOT/dist/linux/pkg"
OUT="$ROOT/dist/linux/IMORTAIS-Combat-Client-v${VERSION}-linux-x64.deb"

rm -rf "$PUBLISH" "$PKGROOT"
mkdir -p "$PUBLISH" "$PKGROOT/DEBIAN"   "$PKGROOT/opt/imortais-combat-client"   "$PKGROOT/usr/bin"   "$PKGROOT/usr/share/applications"   "$PKGROOT/usr/share/icons/hicolor/256x256/apps"

dotnet publish "$PROJECT" -c Release -r linux-x64 --self-contained true   -p:PublishSingleFile=false   -p:DebugType=None   -p:DebugSymbols=false   -o "$PUBLISH"

cp -a "$PUBLISH/." "$PKGROOT/opt/imortais-combat-client/"
cp "$ROOT/upstream/AlbionOnline-StatisticsAnalysis/src/StatisticsAnalysisTool/Assets/imortais-icon.png"   "$PKGROOT/usr/share/icons/hicolor/256x256/apps/imortais-combat-client.png"

cat > "$PKGROOT/DEBIAN/control" <<EOF
Package: imortais-combat-client
Version: $VERSION
Section: games
Priority: optional
Architecture: amd64
Depends: libpcap0.8 | libpcap0.8t64, libcap2-bin
Maintainer: IMORTAIS
Description: IMORTAIS Combat Client for Albion Online
 Cliente Linux da guilda IMORTAIS com captura libpcap, parser Photon,
 medidor de dano, party, registro e telemetria para o War Room.
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
Name=IMORTAIS Combat Client
Comment=Combat observer e Battle Report da guilda IMORTAIS
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
if command -v setcap >/dev/null 2>&1 && [ -f "$BIN" ]; then
  setcap cap_net_raw,cap_net_admin=eip "$BIN" || true
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
sha256sum "$OUT" > "$OUT.sha256"

echo
echo "Linux package created:"
echo "  $OUT"
echo "  $OUT.sha256"
