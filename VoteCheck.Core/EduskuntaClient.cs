using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VoteCheck.Core.Models;

namespace VoteCheck.Core
{
    // Typed, async wrapper over the new Eduskunta open data API (api.eduskunta.fi), which
    // replaces the legacy avoindata.eduskunta.fi table API — see design.md for the migration
    // context and the endpoint map (§3.1) this class implements.
    //
    // Model shapes are validated against real captured responses, kept as fixtures in
    // VoteCheck.Core.Tests/Fixtures/ and asserted by the round-trip tests there. The
    // endpoints not yet covered here (matters, documents, search, reference data) have not
    // been checked against live output.
    public sealed class EduskuntaClient : IEduskuntaClient
    {
        public const string DefaultBaseUrl = "https://api.eduskunta.fi/api/v1/";

        // Upstream refuses a search whose startFromIndex + maxResults exceeds this.
        public const int MaxSearchWindow = 10000;

        // api.eduskunta.fi answers 403 to any request without a User-Agent, on every
        // endpoint. HttpClient sends none by default, so one is set here — otherwise
        // nothing this class does works against the live service.
        public const string DefaultUserAgent = "VoteCheck/1.0 (+https://github.com/mashi89/VoteCheck)";

        private readonly HttpClient _httpClient;

        public EduskuntaClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.BaseAddress ??= new Uri(DefaultBaseUrl);

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
                _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
        }

        public async Task<IReadOnlyList<Mp>> GetMpsAsync(CancellationToken cancellationToken = default)
        {
            var mps = await GetAsync<List<Mp>>("kansanedustajat", cancellationToken).ConfigureAwait(false);
            return mps ?? new List<Mp>();
        }

        public Task<Mp?> GetMpAsync(int henkilonumero, CancellationToken cancellationToken = default) =>
            GetAsync<Mp>($"kansanedustajat/{henkilonumero}", cancellationToken);

        public Task<Aanestys?> GetVoteAsync(string aanestystunnus, CancellationToken cancellationToken = default) =>
            GetAsync<Aanestys>(
                $"taysistunnot/aanestykset/{Uri.EscapeDataString(aanestystunnus)}", cancellationToken);

        public async Task<IReadOnlyList<Aanestys>> GetVotesInSessionAsync(
            string istuntotunnus, CancellationToken cancellationToken = default)
        {
            var votes = await GetAsync<List<Aanestys>>(
                $"taysistunnot/istunnon-aanestykset/{Uri.EscapeDataString(istuntotunnus)}",
                cancellationToken).ConfigureAwait(false);
            return votes ?? new List<Aanestys>();
        }

        public async Task<IReadOnlyList<Aanestys>> GetVotesForMatterAsync(
            string eduskuntatunnus, CancellationToken cancellationToken = default)
        {
            var votes = await GetAsync<List<Aanestys>>(
                $"taysistunnot/asian-aanestykset/{Uri.EscapeDataString(eduskuntatunnus)}",
                cancellationToken).ConfigureAwait(false);
            return votes ?? new List<Aanestys>();
        }

        // Note: unlike the other vote endpoints, uusimmat-aanestykset returns a *nested*
        // array — a list of single-element lists, each wrapping one Aanestys. Confirmed
        // against a live response; we flatten it so callers see a flat list like elsewhere.
        public async Task<IReadOnlyList<Aanestys>> GetRecentVotesAsync(
            CancellationToken cancellationToken = default)
        {
            var groups = await GetAsync<List<List<Aanestys>>>(
                "taysistunnot/uusimmat-aanestykset", cancellationToken).ConfigureAwait(false);

            if (groups is null)
                return new List<Aanestys>();

            var flattened = new List<Aanestys>();
            foreach (var group in groups)
            {
                if (group is not null)
                    flattened.AddRange(group);
            }

            return flattened;
        }

        public async Task<VotePage> GetVotePageAsync(
            int fromVpYear,
            int startFromIndex,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            if (startFromIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(startFromIndex));
            if (maxResults <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxResults));
            if (startFromIndex + maxResults > MaxSearchWindow)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(startFromIndex),
                    $"startFromIndex + maxResults must not exceed {MaxSearchWindow} " +
                    "(upstream rejects deeper paging). Raise fromVpYear to narrow the window, " +
                    "or use the async dataset export for the full archive.");
            }

            // Filtering on istuntovpvuosi rather than the sitting date is deliberate: the
            // parliamentary year is not the calendar year, and the two disagree materially
            // (a 2023-onward filter yields ~2,771 divisions by year but ~1,875 by date).
            // The year is also what the mirror's SyncMinYear has always meant.
            var request = new
            {
                category = "aanestys",
                maxResults,
                startFromIndex,
                sort = new[] { new { property = "istuntopvm", ascending = true } },
                expression = new { property = "istuntovpvuosi", from = fromVpYear, to = 9999 },
            };

            var response = await PostAsync<VoteSearchResponse>("search", request, cancellationToken)
                .ConfigureAwait(false);

            var votes = new List<Aanestys>();
            foreach (var hit in response?.Results ?? new List<VoteSearchHit>())
            {
                // Every hit is an envelope with one slot per category; only ours is populated.
                if (hit?.Aanestys is not null)
                    votes.Add(hit.Aanestys);
            }

            return new VotePage
            {
                Votes = votes,
                TotalCount = response?.SearchMetadata?.TotalResultCount ?? votes.Count,
                StartIndex = startFromIndex,
            };
        }

        private async Task<T?> PostAsync<T>(
            string relativeUrl, object body, CancellationToken cancellationToken)
            where T : class
        {
            using var content = new StringContent(
                JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            // Large result sets come back via a 302 to blob storage; HttpClient follows it by
            // default, downgrading to GET on the way, which is what upstream expects.
            using var response = await _httpClient
                .PostAsync(relativeUrl, content, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            string json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return JsonConvert.DeserializeObject<T>(json);
        }

        // Search returns category-tagged envelopes rather than bare objects.
        private sealed class VoteSearchResponse
        {
            public List<VoteSearchHit>? Results { get; set; }
            public VoteSearchMetadata? SearchMetadata { get; set; }
        }

        private sealed class VoteSearchHit
        {
            public Aanestys? Aanestys { get; set; }
        }

        private sealed class VoteSearchMetadata
        {
            public int TotalResultCount { get; set; }
        }

        private async Task<T?> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
            where T : class
        {
            using var response = await _httpClient
                .GetAsync(relativeUrl, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            string json = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}
