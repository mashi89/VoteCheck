#!/usr/bin/env bash
# Provision a fresh Ubuntu server to run VoteCheck. Idempotent: safe to re-run.
#
#   ssh root@<server-ip>
#   curl -fsSL https://raw.githubusercontent.com/mashi89/VoteCheck/master/deploy/setup.sh | bash
#
# or, from a clone on the server:  sudo bash deploy/setup.sh
#
# Installs Docker, locks the firewall down to SSH/HTTP/HTTPS, and turns on automatic
# security updates. It does NOT deploy the app — see deploy/README.md for that.

set -euo pipefail

if [[ $EUID -ne 0 ]]; then
	echo "Run as root (or with sudo)." >&2
	exit 1
fi

log() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }

log "Updating package lists"
export DEBIAN_FRONTEND=noninteractive
apt-get update -qq

log "Installing Docker"
if ! command -v docker >/dev/null 2>&1; then
	apt-get install -y -qq ca-certificates curl gnupg
	install -m 0755 -d /etc/apt/keyrings
	curl -fsSL https://download.docker.com/linux/ubuntu/gpg |
		gpg --dearmor -o /etc/apt/keyrings/docker.gpg
	chmod a+r /etc/apt/keyrings/docker.gpg
	echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
		>/etc/apt/sources.list.d/docker.list
	apt-get update -qq
	apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
else
	echo "Docker already present: $(docker --version)"
fi

# Containers carry restart: unless-stopped, so enabling the daemon at boot is all that
# is needed to bring the stack back after a reboot — no systemd unit of our own.
log "Enabling Docker at boot"
systemctl enable --now docker

log "Configuring the firewall"
apt-get install -y -qq ufw
ufw allow OpenSSH >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw --force enable >/dev/null
ufw status verbose | sed 's/^/    /'

log "Enabling automatic security updates"
# This is the part that decides whether an unattended box stays safe. Security updates
# apply on their own; reboots are left to you, so a kernel update needs a manual reboot.
apt-get install -y -qq unattended-upgrades
cat >/etc/apt/apt.conf.d/20auto-upgrades <<'CONF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
CONF
systemctl enable --now unattended-upgrades

log "Capping Docker log growth"
# Without this, container logs grow until the disk is full — the most common way a small
# box like this falls over months after anyone last looked at it.
mkdir -p /etc/docker
if [[ ! -f /etc/docker/daemon.json ]]; then
	cat >/etc/docker/daemon.json <<'CONF'
{
  "log-driver": "json-file",
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
CONF
	systemctl restart docker
else
	echo "/etc/docker/daemon.json exists; leaving it alone."
	echo "Ensure it caps log size, or logs will eventually fill the disk."
fi

log "Done"
cat <<'NEXT'
Next:
  1. Point an A record at this server's IPv4 address (and AAAA at its IPv6).
  2. Clone the repo here, then from its root:

       DOMAIN=your.domain [email protected] \
         docker compose -f docker-compose.prod.yml up -d --build

  3. Watch the first backfill:  docker compose -f docker-compose.prod.yml logs -f votecheck

See deploy/README.md for detail.
NEXT
