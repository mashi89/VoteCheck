# Deploying VoteCheck

Target: a single UpCloud Cloud Server in **Helsinki (`fi-hel1` / `fi-hel2`)**, running the
app behind Caddy. One container, one volume, automatic TLS.

Helsinki is chosen because essentially every user is in Finland, and because the mirror is
SQLite — which wants **local** disk. UpCloud's MaxIOPS storage is local NVMe, which is what
makes this work; SQLite over a network filesystem (SMB/NFS) risks corruption and is why
Azure App Service, despite being named in early drafts of `design.md`, is not used here.

## Sizing

The app is small: one .NET process, an in-memory cache, and a SQLite file. The full
2023-onward mirror is a few hundred MB. The smallest general-purpose plan (1 vCPU, 1–2 GB
RAM, 25 GB disk) is enough; the constraint is disk, not CPU.

Do **not** run two instances. The sync is the single writer, and a second replica would
mean two processes writing one SQLite file.

## Steps

**1. Create the server.** UpCloud control panel → Deploy a server → Helsinki, Ubuntu LTS,
smallest general-purpose plan, add your SSH key. Note the IPv4 address.

**2. Point DNS at it.** An `A` record for your domain to the IPv4 address, and an `AAAA` to
the IPv6 if you have one. Do this *before* step 4 — Caddy proves control of the domain over
HTTP to obtain the certificate, so the name must already resolve.

**3. Provision.**

```
ssh root@<server-ip>
curl -fsSL https://raw.githubusercontent.com/mashi89/VoteCheck/master/deploy/setup.sh | bash
```

Installs Docker, restricts the firewall to SSH/HTTP/HTTPS, enables automatic security
updates, and caps container log growth. Idempotent.

**4. Deploy.**

```
git clone https://github.com/mashi89/VoteCheck.git
cd VoteCheck
DOMAIN=your.domain [email protected] \
  docker compose -f docker-compose.prod.yml up -d --build
```

`ACME_EMAIL` is where Let's Encrypt sends expiry warnings. `DOMAIN` is required; the stack
refuses to start without it rather than issuing a certificate for the wrong name.

**5. Wait for the first backfill.**

```
docker compose -f docker-compose.prod.yml logs -f votecheck
```

The 2023-onward window is ~2,800 divisions fetched ~50 at a time with a politeness delay,
so expect a few minutes. The site is up immediately but the front page fills gradually.
You are done when the log says `Backfill complete`.

**6. Check it.**

```
curl -sI https://your.domain/health
curl -s https://your.domain/robots.txt | grep Sitemap    # must say https://your.domain/...
```

If the sitemap line says `http://`, `VoteCheck__BehindProxy` is not taking effect and every
shared link will advertise the wrong scheme.

## Updating

```
git pull && docker compose -f docker-compose.prod.yml up -d --build
```

The mirror is on a named volume and survives rebuilds. A schema change is the exception:
the app creates missing tables but does not migrate existing ones, so if `Db.EnsureSchema`
gains a column, delete the volume and let it re-backfill.

## Operating notes

- **Backups are optional.** The mirror is rebuildable from api.eduskunta.fi. Losing it costs
  a re-backfill, not data. Do not build backup machinery for it.
- **Reboots are yours.** Security patches apply unattended; kernel updates still need a
  reboot. `ls /var/run/reboot-required` tells you when one is pending.
- **Disk is the thing that fills.** Docker logs are capped at 30 MB/container by the setup
  script; the mirror grows slowly and predictably.
- **Certificates renew themselves.** The `caddy-data` volume holds them — keep it, or every
  recreate re-issues and counts against Let's Encrypt rate limits.

## If something is wrong

| Symptom | Likely cause |
|---|---|
| TLS fails on first start | DNS not resolving yet, or port 80 blocked — Caddy needs both |
| Permalinks say `http://` | `VoteCheck__BehindProxy` not `true`, or Caddy not sending `X-Forwarded-Proto` |
| Front page empty | Backfill still running, or it failed — check the logs |
| `403` in sync logs | Upstream rejects requests without a User-Agent; `VoteCheck.Core` sets one, so this means it is being stripped in transit |
| Sync stops with "window exhausted" | `SyncMinYear` reaches further back than upstream will page (10,000 results); raise it |
