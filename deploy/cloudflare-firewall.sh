#!/usr/bin/env bash
# Restrict inbound 80/443 to Cloudflare's published ranges.
#
#   sudo bash deploy/cloudflare-firewall.sh          # apply
#   sudo bash deploy/cloudflare-firewall.sh --open   # revert to open (before disabling the proxy)
#
# Without this, Cloudflare is decorative: the origin IP leaks through certificate
# transparency logs, historical DNS, or any outbound connection, and an attacker
# simply targets the server directly. Run it again whenever Cloudflare updates its
# ranges — they change occasionally, and a stale list locks out real traffic.
#
# SSH is untouched, so a mistake here does not lock you out of the box.

set -euo pipefail

if [[ $EUID -ne 0 ]]; then
	echo "Run as root (or with sudo)." >&2
	exit 1
fi

if ! command -v ufw >/dev/null 2>&1; then
	echo "ufw not installed — run deploy/setup.sh first." >&2
	exit 1
fi

# Drop any rules this script previously added, so re-running replaces rather than stacks.
purge_existing() {
	# Delete by rule text, repeatedly, since numbering shifts as rules are removed.
	while ufw status numbered | grep -qE '(80|443)/tcp.*# cloudflare'; do
		num=$(ufw status numbered | grep -E '(80|443)/tcp.*# cloudflare' | head -1 |
			sed -E 's/^\[[[:space:]]*([0-9]+)\].*/\1/')
		ufw --force delete "$num" >/dev/null
	done
	ufw delete allow 80/tcp >/dev/null 2>&1 || true
	ufw delete allow 443/tcp >/dev/null 2>&1 || true
}

if [[ "${1:-}" == "--open" ]]; then
	echo "Reverting to open 80/443 (use before turning Cloudflare's proxy off)."
	purge_existing
	ufw allow 80/tcp >/dev/null
	ufw allow 443/tcp >/dev/null
	ufw status verbose | sed 's/^/    /'
	exit 0
fi

echo "Fetching Cloudflare ranges..."
v4=$(curl -fsS --max-time 20 https://www.cloudflare.com/ips-v4)
v6=$(curl -fsS --max-time 20 https://www.cloudflare.com/ips-v6)

# Refuse to proceed on a suspiciously short list: applying a truncated list would
# firewall out most of Cloudflare and take the site down.
count=$(printf '%s\n%s\n' "$v4" "$v6" | grep -c '/' || true)
if (( count < 10 )); then
	echo "Only $count ranges returned — refusing to apply a partial list." >&2
	exit 1
fi
echo "  got $count ranges"

purge_existing

while read -r cidr; do
	[[ -z "$cidr" ]] && continue
	ufw allow proto tcp from "$cidr" to any port 80 comment 'cloudflare' >/dev/null
	ufw allow proto tcp from "$cidr" to any port 443 comment 'cloudflare' >/dev/null
done < <(printf '%s\n%s\n' "$v4" "$v6")

echo "Applied. Only Cloudflare can now reach 80/443; SSH is unchanged."
ufw status | grep -c cloudflare | xargs printf '    %s cloudflare rules active\n'
