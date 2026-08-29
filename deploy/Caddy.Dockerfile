# Caddy with two Cloudflare modules built in.
#
# The stock image cannot do either of the things this deployment needs behind
# Cloudflare's proxy:
#
#   caddy-dns/cloudflare      solves ACME over DNS-01. HTTP-01 is fragile through a
#                             proxy — "Always Use HTTPS" can break a renewal months
#                             later, silently, at 3am.
#   caddy-cloudflare-ip       keeps the trusted-proxy list current from Cloudflare's
#                             published ranges, so real client IPs survive and the
#                             ranges do not go stale in a config file.

FROM caddy:2-builder AS builder
RUN xcaddy build \
    --with github.com/caddy-dns/cloudflare \
    --with github.com/WeidiDeng/caddy-cloudflare-ip

FROM caddy:2-alpine
COPY --from=builder /usr/bin/caddy /usr/bin/caddy
