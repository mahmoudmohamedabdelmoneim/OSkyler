#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 ]]; then
  echo "Usage: sudo ./install.sh <fqdn> <release-directory>" >&2
  exit 2
fi

SKYLER_FQDN="$1"
RELEASE_SOURCE="$2"
RELEASE_ID="$(date -u +%Y%m%dT%H%M%SZ)"
RELEASE_TARGET="/opt/skyler/releases/${RELEASE_ID}"

if [[ ! -f "${RELEASE_SOURCE}/api/Skyler.Api" || ! -f "${RELEASE_SOURCE}/portal/Skyler.Portal" ]]; then
  echo "The release directory must contain api/Skyler.Api and portal/Skyler.Portal files." >&2
  exit 3
fi

export DEBIAN_FRONTEND=noninteractive
apt-get update
apt-get install -y ca-certificates curl debian-keyring debian-archive-keyring apt-transport-https gnupg

if ! command -v ollama >/dev/null 2>&1; then
  curl -fsSL https://ollama.com/install.sh | sh
fi

if ! command -v caddy >/dev/null 2>&1; then
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
    | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
    | tee /etc/apt/sources.list.d/caddy-stable.list >/dev/null
  chmod o+r /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  chmod o+r /etc/apt/sources.list.d/caddy-stable.list
  apt-get update
  apt-get install -y caddy
fi

if ! id skyler >/dev/null 2>&1; then
  useradd --system --create-home --home-dir /home/skyler --shell /usr/sbin/nologin skyler
fi

install -d -o skyler -g skyler -m 0750 /opt/skyler/data
install -d -o skyler -g skyler -m 0750 /home/skyler/.local/share/Skyler/Authentication
install -d -o root -g root -m 0755 /opt/skyler/releases
cp -a "${RELEASE_SOURCE}" "${RELEASE_TARGET}"
chown -R root:root "${RELEASE_TARGET}"
find "${RELEASE_TARGET}" -type d -exec chmod 0755 {} +
chmod +x "${RELEASE_TARGET}/api/Skyler.Api" "${RELEASE_TARGET}/portal/Skyler.Portal"
ln -sfn "${RELEASE_TARGET}" /opt/skyler/current

if [[ ! -f /opt/skyler/data/skyler.db && -f "${RELEASE_SOURCE}/data/skyler.db" ]]; then
  install -o skyler -g skyler -m 0640 "${RELEASE_SOURCE}/data/skyler.db" /opt/skyler/data/skyler.db
fi

install -o root -g root -m 0644 "${RELEASE_SOURCE}/deploy/skyler-api.service" /etc/systemd/system/skyler-api.service
install -o root -g root -m 0644 "${RELEASE_SOURCE}/deploy/skyler-portal.service" /etc/systemd/system/skyler-portal.service
sed "s/__SKYLER_FQDN__/${SKYLER_FQDN}/g" "${RELEASE_SOURCE}/deploy/Caddyfile.template" > /etc/caddy/Caddyfile
caddy validate --config /etc/caddy/Caddyfile

systemctl daemon-reload
systemctl enable --now ollama

echo "Pulling the exact configured model tag. This can take several minutes."
sudo -u ollama -H ollama pull mistral

systemctl enable --now skyler-api skyler-portal caddy
systemctl restart skyler-api skyler-portal caddy

echo "Installed Skyler release ${RELEASE_ID}"
echo "Model: $(sudo -u ollama -H ollama list | sed -n '2p')"
echo "URL: https://${SKYLER_FQDN}/"
