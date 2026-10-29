#!/bin/sh
# Installs FanPilot from this folder (the release's .tar.gz, extracted) as a systemd service, item 28c:
#   sudo ./install.sh                            installs or updates, and starts it
#   sudo ./install.sh --import-data <folder>     the same, bringing curves, pairings and names from a portable run
#   sudo ./install.sh --remove                   stops and removes it; the data stays in /var/lib/fanpilot
# The program goes to /opt/fanpilot, the data to /var/lib/fanpilot, the secrets' key to /etc/fanpilot.
set -eu

PREFIX=/opt/fanpilot
DATA=/var/lib/fanpilot
KEYS=/etc/fanpilot
UNIT=/etc/systemd/system/fanpilot.service
HERE=$(cd "$(dirname "$0")" && pwd)

fail() { echo "install.sh: $*" >&2; exit 1; }

[ "$(id -u)" -eq 0 ] || fail "rode como root: sudo $0 $*"
command -v systemctl > /dev/null || fail "o systemd é necessário (systemctl não encontrado)"

if [ "${1:-}" = "--remove" ]; then
    systemctl disable --now fanpilot.service 2> /dev/null || true
    rm -f "$UNIT"
    systemctl daemon-reload
    rm -rf "$PREFIX"
    echo "FanPilot removido. Os dados ficam em $DATA e a chave dos segredos em $KEYS (apague-os se quiser)."
    exit 0
fi

IMPORT=""
if [ "${1:-}" = "--import-data" ]; then
    [ -n "${2:-}" ] || fail "--import-data precisa da pasta de dados antiga"
    IMPORT=$(cd "$2" && pwd) || fail "pasta não encontrada: $2"
elif [ -n "${1:-}" ]; then
    fail "opção desconhecida: $1 (use --import-data <pasta> ou --remove)"
fi

[ "$(uname -m)" = "x86_64" ] || fail "este pacote é para Linux x86_64; esta máquina é $(uname -m)"
[ -x "$HERE/FanPilot" ] || fail "FanPilot não encontrado em $HERE: rode o install.sh de dentro da pasta extraída"
# The .NET runtime inside FanPilot needs the system's ICU for languages and number formats.
if ! ldconfig -p 2> /dev/null | grep -q libicuuc; then
    echo "Aviso: a biblioteca ICU (libicu) não foi encontrada; o FanPilot não abre sem ela." >&2
    echo "       Ubuntu/Debian: sudo apt install libicu-dev   Fedora: sudo dnf install libicu" >&2
fi

# An update replaces the program with the service stopped (it hands the fans back to the BIOS meanwhile).
systemctl stop fanpilot.service 2> /dev/null || true

mkdir -p "$PREFIX"
rm -rf "$PREFIX/wwwroot"
cp -R "$HERE/FanPilot" "$HERE/wwwroot" "$PREFIX/"
for f in FanPilot.pdb LICENSE THIRD_PARTY_NOTICES.md README.md README.pt-BR.md CHANGELOG.md install.sh fanpilot.service; do
    [ -e "$HERE/$f" ] && cp "$HERE/$f" "$PREFIX/"
done
chmod 755 "$PREFIX/FanPilot" "$PREFIX/install.sh"
mkdir -p "$KEYS" && chmod 700 "$KEYS"

if [ -n "$IMPORT" ]; then
    "$PREFIX/FanPilot" --import-data "$IMPORT" --data "$DATA"
fi

cp "$HERE/fanpilot.service" "$UNIT"
chmod 644 "$UNIT"
systemctl daemon-reload
systemctl enable --now fanpilot.service

PORT=8787
if [ -f "$DATA/config.json" ]; then
    PORT=$(sed -n 's/.*"port"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$DATA/config.json" | head -n 1)
    [ -n "$PORT" ] || PORT=8787
fi

echo "FanPilot instalado e rodando como serviço (systemctl status fanpilot; journalctl -u fanpilot)."
echo "Painel neste PC: http://localhost:$PORT/pair  ·  no terminal: $PREFIX/FanPilot --tui"
echo "Para o celular na rede local, abra a porta $PORT só para a rede de casa, por exemplo:"
echo "  ufw:       sudo ufw allow from 192.168.0.0/16 to any port $PORT proto tcp"
echo "  firewalld: sudo firewall-cmd --permanent --zone=home --add-port=$PORT/tcp && sudo firewall-cmd --reload"
