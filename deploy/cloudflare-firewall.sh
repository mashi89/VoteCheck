#!/usr/bin/env bash
# Restrict inbound 80/443 to Cloudflare's published ranges.
#
#   sudo bash deploy/cloudflare-firewall.sh          # apply
#   sudo bash deploy/cloudflare-firewall.sh --open   # revert to open (before disabling the proxy)
#
# Two enforcement points, because ufw alone does nothing here:
#
#   DOCKER-USER  the one that matters. Docker publishes 80/443 by writing its own
#                iptables rules, and those are evaluated before ufw's INPUT chain —
#                so a published container port answers the whole internet no matter
#                what `ufw status` claims. DOCKER-USER is the hook Docker provides
#                for exactly this, evaluated ahead of its own rules.
#   ufw          still applied, for anything listening on the host rather than in a
#                container. Cheap, and it keeps `ufw status` honest.
#
# Without this, Cloudflare is decorative: the origin IP leaks through certificate
# transparency logs, historical DNS, or any outbound connection, and an attacker
# simply targets the server directly. Run it again whenever Cloudflare updates its
# ranges — they change occasionally, and a stale list locks out real traffic.
#
# SSH is untouched, so a mistake here does not lock you out of the box.
#
# Rules live in iptables, which a reboot clears, so this installs a systemd unit
# that re-runs the script after Docker starts.

set -euo pipefail

if [[ $EUID -ne 0 ]]; then
	echo "Run as root (or with sudo)." >&2
	exit 1
fi

for cmd in ufw iptables ip6tables; do
	command -v "$cmd" >/dev/null 2>&1 || { echo "$cmd not installed — run deploy/setup.sh first." >&2; exit 1; }
done

CHAIN=CF-ONLY
UNIT=/etc/systemd/system/cloudflare-firewall.service
SCRIPT_PATH=$(readlink -f "$0")

# The public interface. Scoping the rule to it matters: DOCKER-USER also sees traffic
# the containers originate, and the sync's own calls to api.eduskunta.fi are tcp/443.
# Without -i they would match the drop and the mirror would silently stop updating.
IFACE=$(ip route show default | awk '{print $5; exit}')
[[ -n "$IFACE" ]] || { echo "Could not determine the default-route interface." >&2; exit 1; }

# --- helpers ---------------------------------------------------------------

jump_args() { printf '%s' "-i $IFACE -p tcp -m multiport --dports 80,443 -j $CHAIN"; }

drop_jump() {
	local ipt=$1
	# shellcheck disable=SC2046
	while $ipt -C DOCKER-USER $(jump_args) >/dev/null 2>&1; do
		# shellcheck disable=SC2046
		$ipt -D DOCKER-USER $(jump_args)
	done
}

clear_chain() {
	local ipt=$1
	drop_jump "$ipt"
	$ipt -F "$CHAIN" >/dev/null 2>&1 || true
	$ipt -X "$CHAIN" >/dev/null 2>&1 || true
}

apply_chain() {
	local ipt=$1 ranges=$2
	$ipt -N "$CHAIN" >/dev/null 2>&1 || $ipt -F "$CHAIN"
	while read -r cidr; do
		[[ -z "$cidr" ]] && continue
		$ipt -A "$CHAIN" -s "$cidr" -j RETURN
	done <<<"$ranges"
	$ipt -A "$CHAIN" -j DROP

	drop_jump "$ipt"
	# shellcheck disable=SC2046
	$ipt -I DOCKER-USER 1 $(jump_args)
}

purge_ufw() {
	while ufw status numbered | grep -qE '(80|443)/tcp.*# cloudflare'; do
		num=$(ufw status numbered | grep -E '(80|443)/tcp.*# cloudflare' | head -1 |
			sed -E 's/^\[[[:space:]]*([0-9]+)\].*/\1/')
		ufw --force delete "$num" >/dev/null
	done
	ufw delete allow 80/tcp >/dev/null 2>&1 || true
	ufw delete allow 443/tcp >/dev/null 2>&1 || true
}

# --- revert ----------------------------------------------------------------

if [[ "${1:-}" == "--open" ]]; then
	echo "Reverting to open 80/443 (use before turning Cloudflare's proxy off)."
	clear_chain iptables
	clear_chain ip6tables
	systemctl disable --now cloudflare-firewall.service >/dev/null 2>&1 || true
	rm -f "$UNIT"
	systemctl daemon-reload
	purge_ufw
	ufw allow 80/tcp >/dev/null
	ufw allow 443/tcp >/dev/null
	echo "  DOCKER-USER rules removed, ufw reopened, boot unit uninstalled."
	exit 0
fi

# --- apply -----------------------------------------------------------------

iptables -L DOCKER-USER -n >/dev/null 2>&1 || {
	echo "No DOCKER-USER chain — is Docker running? Start it, then re-run." >&2
	exit 1
}

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

echo "Filtering published container ports (DOCKER-USER on $IFACE)"
apply_chain iptables "$v4"
apply_chain ip6tables "$v6"

echo "Mirroring the rules in ufw, for anything listening on the host"
purge_ufw
while read -r cidr; do
	[[ -z "$cidr" ]] && continue
	ufw allow proto tcp from "$cidr" to any port 80 comment 'cloudflare' >/dev/null
	ufw allow proto tcp from "$cidr" to any port 443 comment 'cloudflare' >/dev/null
done < <(printf '%s\n%s\n' "$v4" "$v6")

echo "Installing the boot unit (iptables does not survive a reboot)"
cat >"$UNIT" <<UNITFILE
[Unit]
Description=Restrict inbound 80/443 to Cloudflare's published ranges
After=docker.service network-online.target
Requires=docker.service
Wants=network-online.target

[Service]
Type=oneshot
RemainAfterExit=yes
ExecStart=/usr/bin/env bash $SCRIPT_PATH

[Install]
WantedBy=multi-user.target
UNITFILE
systemctl daemon-reload
systemctl enable cloudflare-firewall.service >/dev/null 2>&1

echo
echo "Applied. Only Cloudflare can now reach 80/443; SSH is unchanged."
iptables -S "$CHAIN" | grep -c RETURN | xargs printf '    %s IPv4 ranges allowed\n'
ip6tables -S "$CHAIN" | grep -c RETURN | xargs printf '    %s IPv6 ranges allowed\n'
echo "    verify from another machine: curl -sI --max-time 5 http://<server-ip>/"
