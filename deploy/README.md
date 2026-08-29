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
  a 1 GB box fails or crawls during that build. (On 1 GB, `setup.sh` adds a 2 GB swapfile
  automatically, which makes it survivable but slow.)
- **Operating system:** Ubuntu LTS.
- **SSH keys:** add yours **at deploy time** — Linux servers are key-only, and retrofitting
  through the console is the hard way.
- **Backups:** the free **Day** tier. The mirror is rebuildable and not worth backing up,
  but a whole-server snapshot costs nothing and undoes "deleted the wrong thing".
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

Two layers, outer first:

**1. UpCloud firewall (control panel → the server → Firewall).** This sits *before* the
network interface — flood traffic aimed at the raw IP is dropped upstream and never
consumes the server's bandwidth, which `ufw` cannot do. Rules:

- Accept TCP 80 and 443 **only from [Cloudflare's published ranges](https://www.cloudflare.com/ips/)**
  (IPv4 and IPv6).
- Accept TCP 22 from your own IP(s) if static, otherwise from anywhere (key-only auth is
  the real gate).
- Default inbound: **drop**.
- **Leave outbound fully open.** UpCloud's firewall is *stateless*: an outbound-blocking
  rule set silently kills the sync's requests to api.eduskunta.fi and Caddy's certificate
  renewals — both failures that surface weeks later.

**2. `ufw` on the host, as the inner layer:**

```
sudo bash deploy/cloudflare-firewall.sh
```

Fetches Cloudflare's current ranges and restricts 80/443 to them (SSH untouched, so a
mistake cannot lock you out; it refuses a suspiciously short range list). Re-run it — and
refresh the UpCloud rules — when Cloudflare updates its ranges.

Without this phase Cloudflare is decorative: the origin IP leaks eventually (certificate
transparency logs, any outbound connection) and an attacker simply targets the server
directly.

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
- **Backups:** the Day tier snapshot is the only one needed. Do not build backup
  machinery for the mirror — it is rebuildable from api.eduskunta.fi.
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
| Site unreachable after firewall work | Cloudflare's ranges changed, or the proxy was turned off while the lockdown was active — `cloudflare-firewall.sh --open` over SSH, then fix the UpCloud rules |
| Sync stopped, cert renewals failing, no other symptoms | Outbound blocked at the stateless UpCloud firewall — reopen outbound |
| Logs show Cloudflare IPs, not visitors | The Cloudflare overlay is not in use, so `trusted_proxies` is unset |
| Sync stops with "window exhausted" | `SyncMinYear` reaches further back than upstream will page (10,000 results); raise it |
