# Deploying VoteCheck — edustajavahti.fi

The concrete path for this deployment: domain at **Domainkeskus**, server at **UpCloud
Helsinki**, **Cloudflare** in front. Reliability and DDoS resistance drive the ordering
below, and the ordering is most of the protection:

> **The origin IP must never appear in public DNS, and the firewall must be locked down
> before the first visitor arrives.** Certificates are obtained via DNS-01, which needs no
> inbound traffic — so the server can be fully provisioned, certified and firewalled while
> still invisible, and only then receive traffic, always through Cloudflare. There is no
> exposed window for an attacker to record the IP and bypass the proxy later.

Do the phases in order. A and B can run in parallel (nameserver changes take a while to
propagate); everything else is sequential.

---

## Phase A — Cloudflare zone (start immediately, propagation takes time)

1. Add `edustajavahti.fi` to a Cloudflare account, **Free plan**. Cloudflare answers with
   a pair of nameservers.
2. At **Domainkeskus**, set the domain's nameservers to that pair (this is what the *Omat
   nimipalvelimet* product is for — Domainkeskus's own DNS is not used at all). Propagation
   for `.fi` is usually under an hour, occasionally longer.
3. **Do not create any DNS records yet.** No record pointing at the server exists until
   Phase E, and then only proxied.
4. Create the API token Caddy will use for DNS-01: **My Profile → API Tokens → Create
   Token**, permission **Zone → DNS → Edit**, scoped to this one zone. Copy it; it goes
   into a `.env` file on the server in Phase D and nowhere else.
5. Zone settings, before any traffic:
   - **SSL/TLS → Full (strict).** The origin will hold a real Let's Encrypt certificate,
     so strict works, and anything less lets Cloudflare accept a spoofed origin.
   - **SSL/TLS → Edge Certificates → Always Use HTTPS: on.** (Harmless with DNS-01 —
     this is exactly the setting that silently breaks HTTP-01 renewals, which is why the
     stack does not use HTTP-01.)
   - **Security → Bots → Bot Fight Mode: on.** Free, blocks the dumb-scraper tier.
   - **Security → Settings → Security Level: Medium.** *Under Attack* mode is for
     attacks, not steady state — it interstitials every visitor.
   - **Rate limiting** (the Free plan includes one rule): match `/search*`, threshold
     ~30 requests per 10 seconds per IP, action Block. `/search` is the only endpoint
     where every request does real per-query work; everything else is cacheable.
   - **Cache Rules:** `/vote/*` and `/mp/*` → *Eligible for cache*, edge TTL 1 hour or
     more. Historical divisions are immutable; this keeps a traffic spike — malicious or
     viral — away from the origin entirely. Leave `/api/v1/*` and `/search` uncached.

## Phase B — Create the server

UpCloud control panel → **Deploy a server**:

- **Location:** Helsinki (`fi-hel1`/`fi-hel2`). The audience is Finnish, and SQLite wants
  the local block storage both UpCloud tiers provide — never a network filesystem.
- **Plan:** Starter, **1 vCPU / 2 GB**, standard SSD. Runtime needs only ~300–400 MB, but
  `docker compose up --build` compiles .NET with the SDK *and* Caddy from Go source, and
  a 1 GB box fails or crawls during that build. (`setup.sh` adds a 2 GB swapfile when
  MemTotal is under 2048 MB. A 2 GB server reports ~1950 MB once the kernel takes its
  share, so it gets one too — harmless at `vm.swappiness=10`, and useful headroom for
  the Go compile. On 1 GB the swapfile is what makes the build finish at all, slowly.)
- **Operating system:** Ubuntu LTS.
- **SSH keys:** add yours **at deploy time** — Linux servers are key-only, and retrofitting
  through the console is the hard way.
- **Storage:** **virtio**, and tick **encryption at rest** — both are creation-time
  choices that cost a rebuild to change later. virtio is the paravirtualised interface;
  IDE and SCSI are emulation, and Ubuntu has virtio drivers in-kernel. The mirror is
  public data, but the same disk holds Caddy's TLS private key and the Cloudflare API
  token, and encryption is what keeps those off a decommissioned disk.
- **Backups:** the **Day** tier, ~€0.60/month. The mirror is rebuildable and not worth
  backing up, but `caddy-data` is not — losing it re-issues against Let's Encrypt rate
  limits — and a whole-server snapshot undoes "deleted the wrong thing" in minutes
  rather than a rebuild plus a full re-backfill.
- **Firewall:** included in the plan; configured in Phase F, not skipped.

Note the IPv4 (and IPv6) address. **It never goes in an unproxied DNS record.**

Do **not** run two instances. The sync is the single writer to one SQLite file.

## Phase C — Provision

```
ssh root@<server-ip>
git clone https://github.com/mashi89/VoteCheck.git
cd VoteCheck
bash deploy/setup.sh
```

The script is idempotent and does: Docker, `ufw` limited to SSH/80/443, unattended
security upgrades, container log caps, and a swapfile when RAM < 2 GB.

## Phase D — Deploy (still invisible to the internet)

Put the API token from Phase A beside the compose files, then bring the stack up with the
Cloudflare overlay:

```
printf 'CLOUDFLARE_API_TOKEN=%s\n' '<token>' > .env
chmod 600 .env
DOMAIN=edustajavahti.fi ACME_EMAIL=<your-email> \
  docker compose -f docker-compose.prod.yml -f docker-compose.cloudflare.yml up -d --build
```

`.env` is gitignored; the token exists only in Cloudflare and in this file. The overlay
builds Caddy with the `caddy-dns/cloudflare` module and issues the certificate via
**DNS-01** — Caddy writes a TXT record through the API, so the certificate arrives even
though nothing can reach the server yet. Watch it succeed:

```
docker compose -f docker-compose.prod.yml -f docker-compose.cloudflare.yml logs -f caddy
```

Meanwhile the backfill starts: ~2,800 divisions from 2023 onward, fetched ~50 at a time
with a politeness delay — a few minutes. `docker compose ... logs -f votecheck` until it
says `Backfill complete`.

Why DNS-01 and not HTTP-01: behind a proxy, HTTP-01 depends on an inbound challenge
arriving untouched, and proxy settings like *Always Use HTTPS* can break that months later,
silently, at renewal time. DNS-01 does not care what the proxy does — and it is what makes
this phase possible before any DNS record exists.

## Phase E — Point DNS, proxied only

In Cloudflare DNS, create for the apex:

- `A` → the server's IPv4, **Proxied** (orange cloud)
- `AAAA` → the IPv6, **Proxied**, if the server has one

Never create a grey-cloud (DNS-only) record for this server, not even temporarily "to
test" — historical-DNS services archive it permanently, and the origin lockdown is then
worth much less. Test through the proxy or via `curl --resolve` locally.

## Phase F — Lock the origin down

Do this **before** Phase E, not after. The opening principle of this runbook is that the
firewall is closed before the first visitor arrives, and a proxied `A` record makes
visitors possible the moment it propagates. Locking first costs nothing: Cloudflare can
already reach the origin, there is simply nothing pointing at it yet.

**1. On the host — the layer that actually enforces this:**

```
sudo bash deploy/cloudflare-firewall.sh
```

`ufw` on its own does **nothing** for this stack, and `ufw status` showing a tidy list of
Cloudflare ranges is not evidence of anything. Docker publishes 80/443 by writing its own
iptables rules, and those are evaluated before ufw's INPUT chain — so a published
container port answers the entire internet however ufw is configured. The script therefore
filters in `DOCKER-USER`, the hook Docker provides for exactly this, and mirrors the rules
into ufw only to cover things listening on the host rather than in a container.

It scopes the rule to the default-route interface, because `DOCKER-USER` also carries
traffic the containers originate: the sync's own calls to api.eduskunta.fi are tcp/443,
and an unscoped drop would stop the mirror updating without a word. It installs a systemd
unit as well, since iptables does not survive a reboot. SSH is never touched, so a mistake
here cannot lock you out, and it refuses to apply a suspiciously short range list.

Re-run it when Cloudflare updates its ranges.

**2. UpCloud firewall (control panel → the server → Firewall)** — an outer layer, and
**not available on a trial account**; it needs a verified/paid one. It sits *before* the
network interface, so flood traffic aimed at the raw IP is dropped upstream and never
consumes the server's bandwidth, which nothing on the host can do.

UpCloud's firewall is **stateless**, and that shapes the inbound rules as much as the
outbound ones. It does not know that a packet is the reply to a connection the server
opened, so replies are inbound traffic like any other: a rule set of "22, plus 443 from
Cloudflare, drop the rest" keeps the site serving perfectly while the sync to
api.eduskunta.fi, Caddy's DNS-01 renewals, the Cloudflare range refresh, apt and NTP all
stop — failures that surface weeks later. Outbound stays **Accept all** (UpCloud's own
SMTP block can stay); a blocked outbound rule fails the same way.

Three things about the control panel that the rules below are written around:

- **A rule with an empty source covers IPv4 only.** Every "any source" rule needs a twin
  with source `::/0`. There is no ICMPv6 protocol: an ICMP rule with an IPv6 source is one.
- **Tick *Auto-add DNS rules*.** It accepts replies from UpCloud's resolvers, which the
  server uses.
- **The target port does not take a range**, so the reply rules cannot be confined to the
  ephemeral ports (32768–60999). Rules 4 and 5 make up for it: without them, a packet
  carrying source port 443 would reach the origin's 443 from anywhere and skip Cloudflare.

Inbound, in this order — first match wins:

| # | Action | Protocol | Source address | Source port | Target port | For |
|---|---|---|---|---|---|---|
| 1 | Accept | TCP | any | — | 22 | SSH — first, so nothing below can lock you out. From your own IP(s) if static; key-only auth is the real gate. |
| 2 | Accept | ICMP | any | — | — | Path-MTU discovery; without it some connections stall silently. Its `::/0` twin is ICMPv6, which IPv6 does not work without. |
| 3 | Accept | TCP | each [Cloudflare range](https://www.cloudflare.com/ips/), one rule per range, IPv4 and IPv6 | — | 443 | Visitors. Only 443: under Full (strict) and Always Use HTTPS, Cloudflare never connects to 80. |
| 4 | **Drop** | TCP | any | — | 443 | Everything else aimed at 443 — must come before the reply rules |
| 5 | **Drop** | TCP | any | — | 80 | The same for 80 |
| 6 | Accept | TCP | any | 443 | — | Replies: the sync, Let's Encrypt, the Cloudflare API, image pulls |
| 7 | Accept | TCP | any | 80 | — | Replies from apt — Ubuntu's mirrors are plain HTTP |
| 8 | Accept | UDP | any | 123 | — | NTP replies |
| 9 | Accept | TCP | any | 4460 | — | NTS key exchange — chrony on Ubuntu 26.04 prefers Ubuntu's NTS servers, and falls back to plain NTP without this |
| — | Drop | | | | | Default rule |

"Any" means two rules, the empty-source one and its `::/0` twin. Rules 6–9 let replies
through to every port, which is harmless: 22 is open regardless, nothing else listens, and
the host's `DOCKER-USER` and `ufw` rules still apply behind it.

Build the rules while the firewall is still **Disabled**, then enable it and check both
directions: the direct-IP probe below must still time out, and the server must still
reach what it depends on —

```
curl -s -o /dev/null -w '%{http_code}\n' https://api.eduskunta.fi/                      # 200
curl -s -o /dev/null -w '%{http_code}\n' https://api.cloudflare.com/client/v4/ips      # 200
curl -s -o /dev/null -w '%{http_code}\n' https://acme-v02.api.letsencrypt.org/directory # 200
apt-get update && chronyc sources
```

The UpCloud web console still reaches the server if SSH ever stops answering.

Deploying with only step 1 is reasonable: it is what stops anyone bypassing the proxy to
reach the app. What step 2 adds is volumetric-flood absorption on the raw IP, and that
attack needs the IP first — which nobody has as long as no grey-cloud record is ever
created.

Without this phase Cloudflare is decorative: the origin IP leaks eventually (certificate
transparency logs, any outbound connection) and an attacker simply targets the server
directly.

**Verify from another machine, never from the server.** Traffic from the host to its own
public IP is delivered over loopback, which every layer here allows, so the check passes
no matter how exposed the origin is:

```
curl -sI --max-time 5 http://<server-ip>/     # must time out (exit 28)
```

## Phase G — Verify

```
curl -sI https://edustajavahti.fi/health                      # 200
curl -s  https://edustajavahti.fi/robots.txt | grep Sitemap   # must say https://edustajavahti.fi/...
curl -sI https://edustajavahti.fi/ | grep -i '^server'        # cloudflare
curl -sI --max-time 5 http://<server-ip>/ ; echo "exit $?"    # must time out, not answer
```

- Sitemap saying `http://` → `VoteCheck__BehindProxy` is not taking effect and every
  shared permalink advertises the wrong scheme.
- The direct-IP probe answering → the Phase F lockdown is not applied.
- App logs showing Cloudflare's IPs instead of visitors' → the overlay's
  `trusted_proxies` is not in effect.

Finally, put an external uptime check (UptimeRobot or similar, free tier) on
`https://edustajavahti.fi/health`. Everything below assumes problems announce themselves;
this is the thing that makes that true.

## During an attack

- Cloudflare → **Security → Settings → Under Attack Mode: on.** Every visitor gets a
  JavaScript interstitial; crude but effective. Turn it back to Medium afterwards.
- If `/search` is the target, tighten the rate-limit rule from Phase A.
- The origin itself should barely notice a volumetric flood: the edge absorbs it, the
  cache serves `/vote/*` and `/mp/*`, and the UpCloud firewall drops anything aimed at
  the raw IP. If origin CPU is nonetheless pinned, the traffic is getting through as
  legitimate-looking requests — check which path in the Caddy logs and cache or block it
  at the edge.

## Updating

```
cd ~/VoteCheck && git pull
DOMAIN=edustajavahti.fi ACME_EMAIL=<your-email> \
  docker compose -f docker-compose.prod.yml -f docker-compose.cloudflare.yml up -d --build
```

The mirror lives on a named volume and survives rebuilds. Schema changes are the
exception: the app creates missing tables but does not migrate existing ones, so if
`Db.EnsureSchema` gains a column, delete the `votecheck-data` volume and let it
re-backfill. Keep the `caddy-data` volume always — it holds the certificates, and
recreating it re-issues against Let's Encrypt rate limits.

## Operating notes

- **Reboots are yours.** Security patches apply unattended; kernel updates still need a
  reboot. `ls /var/run/reboot-required` says when one is pending. The stack comes back by
  itself (`restart: unless-stopped` + Docker enabled at boot).
- **Disk is the thing that fills.** Container logs are capped at 30 MB each by `setup.sh`;
  the mirror grows slowly and predictably.
- **Backups:** the Day tier snapshot (~€0.60/month) is the only one needed. Do not build
  backup machinery for the mirror — it is rebuildable from api.eduskunta.fi.
- **Turning the proxy off** (grey cloud) requires reverting both firewall layers *first*
  — `sudo bash deploy/cloudflare-firewall.sh --open` plus the UpCloud rules — or the site
  becomes unreachable. Then redeploy without the overlay. But see Phase E: a grey-cloud
  record permanently burns the origin IP; prefer moving the server (new IP) if the proxy
  ever truly has to go.

## If something is wrong

| Symptom | Likely cause |
|---|---|
| Certificate never issues | `CLOUDFLARE_API_TOKEN` missing/wrong scope, or nameservers not yet propagated to Cloudflare — check `caddy` logs |
| Permalinks say `http://` | `VoteCheck__BehindProxy` not `true`, or Caddy not sending `X-Forwarded-Proto` |
| Front page empty | Backfill still running, or failed — check `votecheck` logs |
| `403` in sync logs | Upstream rejects requests without a User-Agent; `VoteCheck.Core` sets one, so something is stripping it in transit |
| `ufw status` lists Cloudflare ranges but the origin still answers on its raw IP | Docker's iptables rules run before ufw's INPUT chain, so published ports ignore it — re-run `cloudflare-firewall.sh`, which filters in `DOCKER-USER`, and re-check from another machine |
| Site unreachable after firewall work | Cloudflare's ranges changed, or the proxy was turned off while the lockdown was active — `cloudflare-firewall.sh --open` over SSH, then fix the UpCloud rules |
| Sync stopped, cert renewals failing, no other symptoms | The stateless UpCloud firewall is dropping replies — check the reply rules (source port 443/80, both IPv4 and `::/0`) sit after the drops and before the default, and that outbound is Accept all |
| Logs show Cloudflare IPs, not visitors | The Cloudflare overlay is not in use, so `trusted_proxies` is unset |
| Sync stops with "window exhausted" | `SyncMinYear` reaches further back than upstream will page (10,000 results); raise it |
