using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Tests;

// Read-side behaviour against the real schema. Rows are chosen so a wrong answer is
// obvious: distinct tallies per party, one absentee, one annulled division.
[TestClass]
public class QueriesTests {

    private TestDb _db = null!;

    [TestInitialize]
    public void Setup() {
        _db = new TestDb();

        // Two divisions in one sitting, plus an earlier one, plus an annulled one.
        // Sitting 114 happened in November, sitting 24 in March — the identifiers sort
        // the other way round as text, which is the whole point of the year/number columns.
        _db.AddSession( "2009-24-1", "2009-03-13", "Ensimmäinen äänestys",
            "Hallituksen esitys laiksi opintotuesta", 2009, 24, 1, yes: 2, no: 1 );
        _db.AddSession( "2009-114-4", "2009-11-27", "Toinen äänestys",
            "Hallituksen esitys tuloveroasteikkolaiksi", 2009, 114, 4, yes: 1, no: 2 );
        _db.AddSession( "2009-114-5", "2009-11-27", "Kolmas äänestys",
            "Lakiehdotuksen hylkääminen", 2009, 114, 5, yes: 3 );
        _db.AddSession( "2009-200-1", "2009-12-01", "Mitätöity äänestys",
            "Peruttu käsittely", 2009, 200, 1, cancelled: true );

        _db.AddMp( 1, "Aino", "Aaltonen", "kok" );
        _db.AddMp( 2, "Bertta", "Bergström", "sd" );
        _db.AddMp( 3, "Kalle", "Korhonen", "kok" );

        // 2009-24-1: kok votes Jaa twice, sd votes Ei.
        _db.AddVote( "2009-24-1", 1, "kok", VoteValue.Yes );
        _db.AddVote( "2009-24-1", 3, "kok", VoteValue.Yes );
        _db.AddVote( "2009-24-1", 2, "sd", VoteValue.No );

        // 2009-114-4: one of each value, so distribution sums are unambiguous.
        _db.AddVote( "2009-114-4", 1, "kok", VoteValue.Yes );
        _db.AddVote( "2009-114-4", 3, "kok", VoteValue.Blank );
        _db.AddVote( "2009-114-4", 2, "sd", VoteValue.Absent );

        _db.AddVote( "2009-114-5", 1, "kok", VoteValue.Yes );
        _db.AddVote( "2009-200-1", 1, "kok", VoteValue.Yes );
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    [TestMethod]
    public void LatestSessions_OrdersChronologically_NotByIdentifierText() {
        // "2009-114-4" sorts before "2009-24-1" as text but happened eight months later.
        // Ordering by the identifier would silently reverse history.
        var sessions = _db.Queries.LatestSessions( 10 );

        CollectionAssert.AreEqual(
            new[] { "2009-114-5", "2009-114-4", "2009-24-1" },
            sessions.Select( s => s.Id ).ToArray() );
    }

    [TestMethod]
    public void LatestSessions_ExcludesAnnulledDivisions() {
        var sessions = _db.Queries.LatestSessions( 10 );

        Assert.IsFalse( sessions.Any( s => s.Id == "2009-200-1" ) );
    }

    [TestMethod]
    public void GetSession_FindsByStringIdentifier() {
        var session = _db.Queries.GetSession( "2009-114-4" );

        Assert.IsNotNull( session );
        Assert.AreEqual( "Toinen äänestys", session!.Title );
        Assert.AreEqual( 1, session.Yes );
        Assert.AreEqual( 2, session.No );
    }

    [TestMethod]
    public void GetSession_ReturnsNull_ForUnknownIdentifier() {
        Assert.IsNull( _db.Queries.GetSession( "1999-1-1" ) );
    }

    [TestMethod]
    public void GetSession_StillResolvesAnnulledDivisions() {
        // They are hidden from listings, but a shared permalink must keep working.
        Assert.IsNotNull( _db.Queries.GetSession( "2009-200-1" ) );
    }

    [TestMethod]
    public void GetPartyDistribution_SumsEachValuePerParty() {
        var parties = _db.Queries.GetPartyDistribution( "2009-114-4" );

        var kok = parties.Single( p => p.Party == "kok" );
        Assert.AreEqual( 1, kok.Yes );
        Assert.AreEqual( 0, kok.No );
        Assert.AreEqual( 1, kok.Blank );
        Assert.AreEqual( 0, kok.Absent );

        var sd = parties.Single( p => p.Party == "sd" );
        Assert.AreEqual( 1, sd.Absent );
    }

    [TestMethod]
    public void GetIndividualVotes_ReturnsEveryBallot_SortedByName() {
        var votes = _db.Queries.GetIndividualVotes( "2009-24-1" );

        CollectionAssert.AreEqual(
            new[] { "Aaltonen", "Bergström", "Korhonen" },
            votes.Select( v => v.LastName ).ToArray() );
    }

    [TestMethod]
    public void GetIndividualVotes_FiltersToOneParty() {
        var votes = _db.Queries.GetIndividualVotes( "2009-24-1", "kok" );

        Assert.AreEqual( 2, votes.Count );
        Assert.IsTrue( votes.All( v => v.Party == "kok" ) );
    }

    [TestMethod]
    public void GetIndividualVotes_PartyFilterIgnoresCase() {
        Assert.AreEqual( 2, _db.Queries.GetIndividualVotes( "2009-24-1", "KOK" ).Count );
    }

    [TestMethod]
    public void FindMps_ReturnsAll_WhenNoFilter() {
        Assert.AreEqual( 3, _db.Queries.FindMps().Count );
    }

    [TestMethod]
    public void FindMps_MatchesSurnamePrefix_CaseInsensitively() {
        var mps = _db.Queries.FindMps( "aal" );

        Assert.AreEqual( 1, mps.Count );
        Assert.AreEqual( "Aaltonen", mps[0].LastName );
    }

    [TestMethod]
    public void GetMpProfile_CountsAttendanceAcrossDivisions() {
        // Bertta voted in two divisions and was absent from one of them.
        var profile = _db.Queries.GetMpProfile( 2, 10 );

        Assert.IsNotNull( profile );
        Assert.AreEqual( 2, profile!.TotalVotes );
        Assert.AreEqual( 1, profile.Present );
    }

    [TestMethod]
    public void GetMpProfile_ListsVotesNewestFirst() {
        var profile = _db.Queries.GetMpProfile( 1, 10 );

        CollectionAssert.AreEqual(
            new[] { "2009-200-1", "2009-114-5", "2009-114-4", "2009-24-1" },
            profile!.LatestVotes.Select( v => v.SessionId ).ToArray() );
    }

    [TestMethod]
    public void GetMpProfile_ReturnsNull_ForUnknownMp() {
        Assert.IsNull( _db.Queries.GetMpProfile( 999, 10 ) );
    }

    [TestMethod]
    public void SearchSessions_MatchesFinnishCompoundsByPrefix() {
        // "laki" must find "laiksi"/"lakiehdotuksen" — exact-token matching would miss
        // most hits in Finnish, so each word is turned into a prefix term.
        var hits = _db.Queries.SearchSessions( "laki", 10 );

        CollectionAssert.AreEquivalent(
            new[] { "2009-114-5" },
            hits.Select( h => h.Id ).ToArray() );
    }

    [TestMethod]
    public void SearchSessions_MatchesOnSubjectAsWellAsTitle() {
        var hits = _db.Queries.SearchSessions( "tuloveroasteikko", 10 );

        Assert.AreEqual( 1, hits.Count );
        Assert.AreEqual( "2009-114-4", hits[0].Id );
    }

    [TestMethod]
    public void SearchSessions_RequiresAllTerms() {
        Assert.AreEqual( 1, _db.Queries.SearchSessions( "hallituksen opintotuesta", 10 ).Count );
        Assert.AreEqual( 0, _db.Queries.SearchSessions( "hallituksen kalastuksesta", 10 ).Count );
    }

    [TestMethod]
    public void SearchSessions_ExcludesAnnulledDivisions() {
        Assert.AreEqual( 0, _db.Queries.SearchSessions( "peruttu", 10 ).Count );
    }

    [TestMethod]
    public void SearchSessions_ToleratesQuotesInUserInput() {
        // FTS5 treats a double quote as a phrase delimiter; an unescaped one is a syntax
        // error, which would surface as a 500 on a user-supplied search string.
        var hits = _db.Queries.SearchSessions( "\"laki", 10 );

        Assert.IsNotNull( hits );
    }
}
