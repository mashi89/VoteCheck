using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
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
    // Model field names come from local research against the published OpenAPI spec
    // (https://api.eduskunta.fi/openapi.json), not a captured live response, so exact casing
    // and nesting should be validated against real API output before this client is relied on
    // in production.
    public sealed class EduskuntaClient
    {
        public const string DefaultBaseUrl = "https://api.eduskunta.fi/api/v1/";

        private readonly HttpClient _httpClient;

        public EduskuntaClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _httpClient.BaseAddress ??= new Uri(DefaultBaseUrl);
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

        public async Task<IReadOnlyList<Aanestys>> GetRecentVotesAsync(
            CancellationToken cancellationToken = default)
        {
            var votes = await GetAsync<List<Aanestys>>(
                "taysistunnot/uusimmat-aanestykset", cancellationToken).ConfigureAwait(false);
            return votes ?? new List<Aanestys>();
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
