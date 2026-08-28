using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using VoteCheck.Core.Models;

namespace VoteCheck.Api.Tests;

[TestClass]
public class ApiEndpointTests
{
    private static VoteCheckApiFactory _factory = null!;
    private static HttpClient _client = null!;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [ClassInitialize]
    public static void Setup(TestContext _)
    {
        _factory = new VoteCheckApiFactory();

        // Load the same captured payloads the Core tests pin against.
        var mp = JsonConvert.DeserializeObject<Mp>(Fixture("kansanedustaja-1109.json"))!;
        var vote = JsonConvert.DeserializeObject<Aanestys>(Fixture("aanestys-2026-60-1.json"))!;
        var recent = JsonConvert
            .DeserializeObject<List<List<Aanestys>>>(Fixture("uusimmat-aanestykset-trimmed.json"))!
            .SelectMany(g => g)
            .ToList();

        _factory.Upstream.Mps.Add(mp);
        _factory.Upstream.VotesById[vote.Id!] = vote;
        _factory.Upstream.RecentVotes = recent;
        _factory.Upstream.SessionVotes = new List<Aanestys> { vote };

        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine("Fixtures", name));

    private static async Task<T> GetAsync<T>(string url)
    {
        var response = await _client.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, $"GET {url}");
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    // ── Meta ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Health_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task SwaggerDocument_IsServed()
    {
        var response = await _client.GetAsync("/swagger/v1/swagger.json");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "VoteCheck API");
        StringAssert.Contains(body, "/api/mps/{id}/activity");
    }

    // ── MPs ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetMps_ReturnsProjectedSummaries()
    {
        var mps = await GetAsync<List<MpSummary>>("/api/mps");

        Assert.AreEqual(1, mps.Count);
        Assert.AreEqual(1109, mps[0].Id);
        Assert.AreEqual("Jussi Halla-aho", mps[0].Name);
        Assert.AreEqual("Perussuomalaisten eduskuntaryhmä", mps[0].Party);
        Assert.AreEqual("Helsingin vaalipiiri", mps[0].District);
        Assert.AreEqual("Nykyinen", mps[0].Status);
    }

    [TestMethod]
    public async Task GetMps_ResolvesSwedish()
    {
        var mps = await GetAsync<List<MpSummary>>("/api/mps?lang=sv");

        Assert.AreEqual("Sannfinländarnas riksdagsgrupp", mps[0].Party);
        Assert.AreEqual("Helsingfors valkrets", mps[0].District);
    }

    [TestMethod]
    public async Task GetMps_ResolvesEnglish()
    {
        var mps = await GetAsync<List<MpSummary>>("/api/mps?lang=en");

        Assert.AreEqual("The Finns Party Parliamentary Group", mps[0].Party);
    }

    [TestMethod]
    public async Task GetMps_SearchMatchesSurname()
    {
        var hit = await GetAsync<List<MpSummary>>("/api/mps?search=halla");
        var miss = await GetAsync<List<MpSummary>>("/api/mps?search=zzzz");

        Assert.AreEqual(1, hit.Count);
        Assert.AreEqual(0, miss.Count);
    }

    [TestMethod]
    public async Task GetMp_ReturnsDetail()
    {
        var mp = await GetAsync<MpDetail>("/api/mps/1109");

        Assert.AreEqual(1109, mp.Id);
        Assert.AreEqual("Halla-aho", mp.LastName);
        Assert.AreEqual(1971, mp.BirthYear);
        Assert.AreEqual("Helsinki", mp.HomeMunicipality);
        Assert.AreEqual("filosofian tohtori", mp.Profession);
    }

    [TestMethod]
    public async Task GetMp_UnknownId_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync("/api/mps/999999");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Not found");
    }

    [TestMethod]
    public async Task GetMp_UnsupportedLanguage_Returns400()
    {
        var response = await _client.GetAsync("/api/mps/1109?lang=de");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Unsupported language");
    }

    // ── Per-MP votes and activity ────────────────────────────────────────────

    [TestMethod]
    public async Task GetMpVotes_ReturnsRowsWithSubjectNotBallotOptions()
    {
        // 1504 (Aalto-Setälä) appears in the trimmed recent-votes fixture.
        var rows = await GetAsync<List<MpVoteRow>>("/api/mps/1504/votes");

        Assert.IsTrue(rows.Count > 0);
        Assert.IsTrue(rows.All(r => !string.IsNullOrWhiteSpace(r.Subject)));

        // Look the division up by id rather than by position — the list is date-sorted, so
        // which row lands first is not this test's concern.
        var row = rows.Single(r => r.VoteId == "2026-60-1");
        StringAssert.Contains(row.Subject, "hyvinvoinnista");
        Assert.AreEqual("HE 32/2026 vp", row.DocumentId);
        Assert.AreEqual("Poissa", row.Choice);
    }

    [TestMethod]
    public async Task GetMpVotes_AreSortedNewestFirst()
    {
        // Upstream is not chronologically ordered, so the API must sort.
        var rows = await GetAsync<List<MpVoteRow>>("/api/mps/1504/votes");

        var dates = rows.Select(r => r.Date).ToList();
        var sorted = dates.OrderByDescending(d => d, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(sorted, dates, "rows must come back newest first");
    }

    [TestMethod]
    public async Task GetMpActivity_ReturnsBreakdownAndAttendance()
    {
        var activity = await GetAsync<ActivitySummary>("/api/mps/1504/activity");

        Assert.AreEqual(1504, activity.Id);
        Assert.AreEqual("Pauli Aalto-Setälä", activity.Name);
        Assert.IsTrue(activity.TotalVotes > 0);
        Assert.AreEqual(activity.Jaa + activity.Ei + activity.Tyhja, activity.Present);
        Assert.IsNotNull(activity.AttendanceRate);
    }

    [TestMethod]
    public async Task GetMpActivity_UnknownMp_ReportsNullAttendanceNotZero()
    {
        // "No data" and "never showed up" must not render identically.
        var activity = await GetAsync<ActivitySummary>("/api/mps/999999/activity");

        Assert.AreEqual(0, activity.TotalVotes);
        Assert.IsNull(activity.AttendanceRate);
    }

    // ── Votes ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetRecentVotes_ReturnsSummariesSortedNewestFirst()
    {
        var votes = await GetAsync<List<VoteSummary>>("/api/votes");

        Assert.IsTrue(votes.Count > 0);
        var dates = votes.Select(v => v.Date).ToList();
        var sorted = dates.OrderByDescending(d => d, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(sorted, dates);
    }

    [TestMethod]
    public async Task GetRecentVotes_FilterByDatePrefix()
    {
        var june = await GetAsync<List<VoteSummary>>("/api/votes?date=2026-06");
        var none = await GetAsync<List<VoteSummary>>("/api/votes?date=1999");

        Assert.IsTrue(june.Count > 0);
        Assert.IsTrue(june.All(v => v.Date!.StartsWith("2026-06")));
        Assert.AreEqual(0, none.Count);
    }

    [TestMethod]
    public async Task GetVote_ReturnsSummaryWithTally()
    {
        var vote = await GetAsync<VoteSummary>("/api/votes/2026-60-1");

        Assert.AreEqual("2026-60-1", vote.Id);
        Assert.AreEqual("2026-60", vote.SessionId);
        Assert.AreEqual("HE 32/2026 vp", vote.DocumentId);
        Assert.IsFalse(vote.Annulled);
        Assert.AreEqual(143, vote.Result!.Jaa);
        Assert.AreEqual(21, vote.Result.Ei);
        Assert.AreEqual(199, vote.Result.Total);
    }

    [TestMethod]
    public async Task GetVote_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/api/votes/1900-1-1");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetVoteDistribution_ReturnsAllThreeBreakdowns()
    {
        var detail = await GetAsync<VoteDetail>("/api/votes/2026-60-1/distribution");

        Assert.IsTrue(detail.ByParty.Count > 0);
        Assert.AreEqual(2, detail.ByGovernmentOpposition.Count);
        Assert.IsTrue(detail.ByDistrict.Count > 0);

        Assert.AreEqual("Hallitusryhmät", detail.ByGovernmentOpposition[0].Name);
        Assert.AreEqual(107, detail.ByGovernmentOpposition[0].Total);
    }

    [TestMethod]
    public async Task GetVoteDistribution_SumsToTheOfficialTally()
    {
        var detail = await GetAsync<VoteDetail>("/api/votes/2026-60-1/distribution");

        Assert.AreEqual(detail.Vote.Result!.Jaa, detail.ByParty.Sum(r => r.Jaa));
        Assert.AreEqual(detail.Vote.Result.Ei, detail.ByParty.Sum(r => r.Ei));
        Assert.AreEqual(detail.Vote.Result.Total, detail.ByParty.Sum(r => r.Total));
    }

    [TestMethod]
    public async Task GetVoteBallots_ReturnsEverySeat()
    {
        var ballots = await GetAsync<List<Ballot>>("/api/votes/2026-60-1/ballots");

        Assert.AreEqual(199, ballots.Count);
        Assert.IsTrue(ballots.All(b => !string.IsNullOrWhiteSpace(b.Name)));
    }

    [TestMethod]
    public async Task GetVoteBallots_FilterByParty()
    {
        var kok = await GetAsync<List<Ballot>>("/api/votes/2026-60-1/ballots?party=kok");

        Assert.IsTrue(kok.Count > 0);
        Assert.IsTrue(kok.All(b => b.Party == "kok"));
        Assert.IsTrue(kok.Count < 199, "party filter must actually narrow the list");
    }

    [TestMethod]
    public async Task GetVoteBallots_PartyFilterAcceptsSwedishAbbreviation()
    {
        // Abbreviations are bilingual upstream ("kok"/"saml"); both should match.
        var viaFi = await GetAsync<List<Ballot>>("/api/votes/2026-60-1/ballots?party=kok");
        var viaSv = await GetAsync<List<Ballot>>("/api/votes/2026-60-1/ballots?party=saml");

        Assert.AreEqual(viaFi.Count, viaSv.Count);
    }

    [TestMethod]
    public async Task GetVoteBallots_ChoiceIsLocalized()
    {
        var fi = await GetAsync<List<Ballot>>("/api/votes/2026-60-1/ballots?party=kok&lang=fi");
        var sv = await GetAsync<List<Ballot>>("/api/votes/2026-60-1/ballots?party=kok&lang=sv");

        var absentFi = fi.First(b => b.Id == 1504);
        var absentSv = sv.First(b => b.Id == 1504);

        Assert.AreEqual("Poissa", absentFi.Choice);
        Assert.AreEqual("Frånvarande", absentSv.Choice);
    }

    [TestMethod]
    public async Task GetSessionVotes_ReturnsDivisionsInSession()
    {
        var votes = await GetAsync<List<VoteSummary>>("/api/sessions/2026-60/votes");

        Assert.AreEqual(1, votes.Count);
        Assert.AreEqual("2026-60-1", votes[0].Id);
    }

    // ── Upstream failure ─────────────────────────────────────────────────────

    [TestMethod]
    public async Task UpstreamUnreachable_Returns502NotBare500()
    {
        // A separate factory so the failing stub doesn't disturb the shared one.
        using var factory = new VoteCheckApiFactory();
        factory.Upstream.ThrowOnMps = new HttpRequestException("connection refused");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/mps");

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode,
            "an upstream outage is not an internal server error");
        string body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Upstream unavailable");
        StringAssert.Contains(body, "api.eduskunta.fi");
    }

    [TestMethod]
    public async Task UpstreamTimeout_Returns504()
    {
        using var factory = new VoteCheckApiFactory();
        factory.Upstream.ThrowOnMps = new TaskCanceledException("timed out");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/mps");

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Upstream timed out");
    }

    // ── Payload shaping ──────────────────────────────────────────────────────

    [TestMethod]
    public async Task ListResponses_DoNotCarryBallotPayloads()
    {
        // The point of projecting down: a list view must not ship 199 ballots per row.
        var response = await _client.GetAsync("/api/votes");
        string body = await response.Content.ReadAsStringAsync();

        StringAssert.DoesNotMatch(body, new System.Text.RegularExpressions.Regex("aanestystapahtumat"));
        Assert.IsTrue(body.Length < 10_000,
            $"recent-votes list should stay small, was {body.Length} bytes");
    }
}
