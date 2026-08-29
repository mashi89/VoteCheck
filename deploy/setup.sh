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

log "Adding swap if memory is tight"
# Building the app compiles .NET (and, with the Cloudflare overlay, Caddy from Go source)
# on this machine. Both want more than 1 GB. Swap makes a 1 GB server survive the build;
# it is slow but it finishes, and it is never touched at runtime.
mem_mb=$(awk '/MemTotal/ {print int($2/1024)}' /proc/meminfo)
if (( mem_mb < 2048 )) && [[ ! -f /swapfile ]]; then
	echo "  ${mem_mb} MB RAM detected — creating a 2G swapfile"
	fallocate -l 2G /swapfile || dd if=/dev/zero of=/swapfile bs=1M count=2048 status=none
	chmod 600 /swapfile
	mkswap /swapfile >/dev/null
	swapon /swapfile
	grep -q '^/swapfile' /etc/fstab || echo '/swapfile none swap sw 0 0' >>/etc/fstab
	# Prefer RAM; this exists for build spikes, not steady-state paging.
	sysctl -qw vm.swappiness=10
	grep -q '^vm.swappiness' /etc/sysctl.conf || echo 'vm.swappiness=10' >>/etc/sysctl.conf
else
	echo "  ${mem_mb} MB RAM — no swapfile needed (or one already exists)"
fi

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
