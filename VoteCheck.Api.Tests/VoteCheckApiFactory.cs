using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VoteCheck.Core;
using VoteCheck.Core.Models;

namespace VoteCheck.Api.Tests;

/// <summary>
/// Boots the real API pipeline (routing, output cache, serialization, problem details) with
/// the upstream client replaced by a stub serving the captured fixtures. So these tests
/// exercise everything VoteCheck actually owns, without touching api.eduskunta.fi.
/// </summary>
internal sealed class VoteCheckApiFactory : WebApplicationFactory<Program>
{
    public StubEduskuntaClient Upstream { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace the cached-HTTP client with the stub. Registered as a singleton so a
            // test can inspect call counts across requests.
            services.RemoveAll<IEduskuntaClient>();
            services.AddSingleton<IEduskuntaClient>(Upstream);
            services.AddScoped(sp => new MpActivityService(sp.GetRequiredService<IEduskuntaClient>()));
        });
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        var found = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in found)
            services.Remove(descriptor);
    }
}

/// <summary>In-memory upstream backed by the real captured payloads.</summary>
internal sealed class StubEduskuntaClient : IEduskuntaClient
{
    public List<Mp> Mps { get; set; } = new();
    public List<Aanestys> RecentVotes { get; set; } = new();
    public Dictionary<string, Aanestys> VotesById { get; } = new();
    public List<Aanestys> SessionVotes { get; set; } = new();

    public int RecentVotesCalls { get; private set; }

    /// <summary>Set to simulate upstream being unreachable or timing out.</summary>
    public Exception? ThrowOnMps { get; set; }

    public Task<IReadOnlyList<Mp>> GetMpsAsync(CancellationToken cancellationToken = default) =>
        ThrowOnMps is not null
            ? Task.FromException<IReadOnlyList<Mp>>(ThrowOnMps)
            : Task.FromResult<IReadOnlyList<Mp>>(Mps);

    public Task<Mp?> GetMpAsync(int henkilonumero, CancellationToken cancellationToken = default) =>
        Task.FromResult(Mps.FirstOrDefault(m => m.Henkilonro == henkilonumero));

    public Task<Aanestys?> GetVoteAsync(string aanestystunnus, CancellationToken cancellationToken = default) =>
        Task.FromResult(VotesById.TryGetValue(aanestystunnus, out var vote) ? vote : null);

    // The API surface does not enumerate the archive; only the mirror sync does, so this
    // stub has nothing meaningful to return.
    public Task<VotePage> GetVotePageAsync(
        int fromVpYear, int startFromIndex, int maxResults,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new VotePage { StartIndex = startFromIndex });

    public Task<IReadOnlyList<Aanestys>> GetVotesInSessionAsync(
        string istuntotunnus, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Aanestys>>(SessionVotes);

    public Task<IReadOnlyList<Aanestys>> GetVotesForMatterAsync(
        string eduskuntatunnus, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Aanestys>>(new List<Aanestys>());

    public Task<IReadOnlyList<Aanestys>> GetRecentVotesAsync(CancellationToken cancellationToken = default)
    {
        RecentVotesCalls++;
        return Task.FromResult<IReadOnlyList<Aanestys>>(RecentVotes);
    }
}
