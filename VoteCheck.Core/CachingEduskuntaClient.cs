using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using VoteCheck.Core.Models;

namespace VoteCheck.Core
{
    // Caching decorator over IEduskuntaClient.
    //
    // Two reasons this matters (design.md §3, §6): upstream payloads are heavy — a single vote
    // carries ~199 ballots plus three breakdown sets — and completed votes never change, so
    // re-fetching them is pure waste. Historical data therefore gets a long TTL while the
    // "what's current" endpoints get a short one.
    //
    // Concurrent callers asking for the same uncached key are collapsed onto a single upstream
    // request, so a burst of traffic can't fan out into duplicate fetches.
    public sealed class CachingEduskuntaClient : IEduskuntaClient
    {
        // A completed vote is immutable; the only reason to expire it at all is memory.
        public static readonly TimeSpan DefaultImmutableTtl = TimeSpan.FromHours(12);

        // "Current MPs" and "recent votes" change as the parliament sits.
        public static readonly TimeSpan DefaultVolatileTtl = TimeSpan.FromMinutes(10);

        private readonly IEduskuntaClient _inner;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _immutableTtl;
        private readonly TimeSpan _volatileTtl;

        // Guards against a cache stampede: one in-flight fetch per key, shared by all waiters.
        private readonly Dictionary<string, Task> _inFlight = new();
        private readonly object _inFlightLock = new();

        public CachingEduskuntaClient(
            IEduskuntaClient inner,
            IMemoryCache cache,
            TimeSpan? immutableTtl = null,
            TimeSpan? volatileTtl = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _immutableTtl = immutableTtl ?? DefaultImmutableTtl;
            _volatileTtl = volatileTtl ?? DefaultVolatileTtl;
        }

        public Task<IReadOnlyList<Mp>> GetMpsAsync(CancellationToken cancellationToken = default) =>
            GetOrCreateAsync("mps", _volatileTtl, ct => _inner.GetMpsAsync(ct), cancellationToken);

        public Task<Mp?> GetMpAsync(int henkilonumero, CancellationToken cancellationToken = default) =>
            GetOrCreateAsync(
                $"mp:{henkilonumero}", _volatileTtl,
                ct => _inner.GetMpAsync(henkilonumero, ct), cancellationToken);

        // A completed vote never changes, so this is the big win.
        public Task<Aanestys?> GetVoteAsync(
            string aanestystunnus, CancellationToken cancellationToken = default) =>
            GetOrCreateAsync(
                $"vote:{aanestystunnus}", _immutableTtl,
                ct => _inner.GetVoteAsync(aanestystunnus, ct), cancellationToken);

        public Task<IReadOnlyList<Aanestys>> GetVotesInSessionAsync(
            string istuntotunnus, CancellationToken cancellationToken = default) =>
            GetOrCreateAsync(
                $"session-votes:{istuntotunnus}", _immutableTtl,
                ct => _inner.GetVotesInSessionAsync(istuntotunnus, ct), cancellationToken);

        // Votes on a matter can still be added while the matter is in progress, so this is
        // treated as volatile rather than immutable.
        public Task<IReadOnlyList<Aanestys>> GetVotesForMatterAsync(
            string eduskuntatunnus, CancellationToken cancellationToken = default) =>
            GetOrCreateAsync(
                $"matter-votes:{eduskuntatunnus}", _volatileTtl,
                ct => _inner.GetVotesForMatterAsync(eduskuntatunnus, ct), cancellationToken);

        public Task<IReadOnlyList<Aanestys>> GetRecentVotesAsync(
            CancellationToken cancellationToken = default) =>
            GetOrCreateAsync(
                "recent-votes", _volatileTtl,
                ct => _inner.GetRecentVotesAsync(ct), cancellationToken);

        private async Task<T> GetOrCreateAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken)
        {
            if (_cache.TryGetValue(key, out T? cached))
                return cached!;

            Task<T> fetch;
            bool owns = false;

            lock (_inFlightLock)
            {
                if (_inFlight.TryGetValue(key, out Task? existing))
                {
                    fetch = (Task<T>)existing;
                }
                else
                {
                    // Not started under the lock — the factory runs outside it.
                    fetch = FetchAndCacheAsync(key, ttl, factory, cancellationToken);
                    _inFlight[key] = fetch;
                    owns = true;
                }
            }

            try
            {
                return await fetch.ConfigureAwait(false);
            }
            finally
            {
                if (owns)
                {
                    lock (_inFlightLock)
                    {
                        _inFlight.Remove(key);
                    }
                }
            }
        }

        private async Task<T> FetchAndCacheAsync<T>(
            string key,
            TimeSpan ttl,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken)
        {
            T value = await factory(cancellationToken).ConfigureAwait(false);

            // A missing entity (null) is not cached — a 404 today may be a real record
            // tomorrow, and caching it would pin the miss for the whole TTL.
            if (value is not null)
                _cache.Set(key, value, ttl);

            return value;
        }
    }
}
