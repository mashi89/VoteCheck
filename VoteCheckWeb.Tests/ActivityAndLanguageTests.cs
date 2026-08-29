using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Tests;

// Covers the two capabilities folded in from VoteCheck.Api (design.md §7 step 6):
// the per-MP activity rollup, and resolving descriptive text to one language.
[TestClass]
public class ActivityAndLanguageTests {

    private TestDb _db = null!;

    [TestInitialize]
    public void Setup() {
        _db = new TestDb();

        _db.AddSession( "2024-1-1", "2024-01-10", "Ensimmäinen", "Lakiehdotus", 2024, 1, 1,
            titleSv: "Första", subjectSv: "Lagförslag" );
        _db.AddSession( "2024-1-2", "2024-01-10", "Toinen", "Toinen aihe", 2024, 1, 2 );
        _db.AddSession( "2024-2-1", "2024-02-01", "Kolmas", "Kolmas aihe", 2024, 2, 1 );
        _db.AddSession( "2024-9-9", "2024-09-09", "Mitätöity", "Peruttu", 2024, 9, 9, cancelled: true );

        _db.AddMp( 1, "Aino", "Aaltonen", "kok" );
        _db.AddMp( 2, "Bertta", "Bergström", "sd" );

        // Aino: one of each value across three live divisions, plus one annulled.
        _db.AddVote( "2024-1-1", 1, "kok", VoteValue.Yes );
        _db.AddVote( "2024-1-2", 1, "kok", VoteValue.Blank );
        _db.AddVote( "2024-2-1", 1, "kok", VoteValue.Absent );
        _db.AddVote( "2024-9-9", 1, "kok", VoteValue.Absent );
    }

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    [TestMethod]
    public void GetMpActivity_BreaksDownEachVoteValue() {
        var activity = _db.Queries.GetMpActivity( 1 );

        Assert.IsNotNull( activity );
        Assert.AreEqual( 1, activity!.Yes );
        Assert.AreEqual( 0, activity.No );
        Assert.AreEqual( 1, activity.Blank );
        Assert.AreEqual( 1, activity.Absent );
    }

    [TestMethod]
    public void GetMpActivity_ExcludesAnnulledDivisions() {
        // The annulled division was struck from the record, so counting the absence there
        // would penalise a member for a vote that officially did not happen.
        var activity = _db.Queries.GetMpActivity( 1 );

        Assert.AreEqual( 3, activity!.TotalVotes, "the annulled fourth division must not count" );
    }

    [TestMethod]
    public void GetMpActivity_CountsBlankAsPresent() {
        // Turning up and abstaining is attendance; only Poissa is an absence.
        var activity = _db.Queries.GetMpActivity( 1 );

        Assert.AreEqual( 2, activity!.Present );
        Assert.AreEqual( 2d / 3d, activity.AttendanceRate!.Value, 0.0001 );
    }

    [TestMethod]
    public void GetMpActivity_ReportsNullAttendance_WhenThereAreNoDivisions() {
        // "We hold no data for this member" must stay distinguishable from "never showed up",
        // which a 0% rate would conflate.
        var activity = _db.Queries.GetMpActivity( 2 );

        Assert.IsNotNull( activity );
        Assert.AreEqual( 0, activity!.TotalVotes );
        Assert.IsNull( activity.AttendanceRate );
    }

    [TestMethod]
    public void GetMpActivity_ReturnsNull_ForUnknownMp() =>
        Assert.IsNull( _db.Queries.GetMpActivity( 999 ) );

    [TestMethod]
    public void GetMpProfile_AndGetMpActivity_AgreeOnAttendance() {
        // These are two views of one fact and are rendered on the same site — the MP page
        // from the profile, /api/v1/mps/{id}/activity from the rollup. If one counts
        // annulled divisions and the other does not, the site contradicts itself.
        var profile = _db.Queries.GetMpProfile( 1, 50 );
        var activity = _db.Queries.GetMpActivity( 1 );

        Assert.AreEqual( activity!.TotalVotes, profile!.TotalVotes );
        Assert.AreEqual( activity.Present, profile.Present );
    }

    [TestMethod]
    public void GetMpProfile_OmitsAnnulledDivisionsFromHistory() {
        var profile = _db.Queries.GetMpProfile( 1, 50 );

        Assert.IsFalse( profile!.LatestVotes.Any( v => v.SessionId == "2024-9-9" ) );
    }

    [TestMethod]
    public void GetSession_ResolvesSwedish_WhenAsked() {
        Assert.AreEqual( "Första", _db.Queries.GetSession( "2024-1-1", "sv" )!.Title );
        Assert.AreEqual( "Lagförslag", _db.Queries.GetSession( "2024-1-1", "sv" )!.Subject );
    }

    [TestMethod]
    public void GetSession_DefaultsToFinnish() {
        Assert.AreEqual( "Ensimmäinen", _db.Queries.GetSession( "2024-1-1" )!.Title );
        Assert.AreEqual( "Ensimmäinen", _db.Queries.GetSession( "2024-1-1", "fi" )!.Title );
    }

    [TestMethod]
    public void GetSession_FallsBackToFinnish_WhenSwedishIsMissing() {
        // Falling back per row means a partially translated archive never renders blank
        // headings — worse than showing the Finnish original.
        Assert.AreEqual( "Toinen", _db.Queries.GetSession( "2024-1-2", "sv" )!.Title );
    }

    [TestMethod]
    public void GetSession_TreatsUnsupportedLanguageAsFinnish() {
        // Upstream carries no English on vote data, so en resolves to Finnish rather than
        // returning blanks.
        Assert.AreEqual( "Ensimmäinen", _db.Queries.GetSession( "2024-1-1", "en" )!.Title );
    }

    [TestMethod]
    public void LatestSessions_AndSearch_HonourLanguage() {
        Assert.AreEqual( "Första", _db.Queries.LatestSessions( 10, "sv" ).Single( s => s.Id == "2024-1-1" ).Title );
        // Search matches the Finnish index but renders in the requested language.
        Assert.AreEqual( "Första", _db.Queries.SearchSessions( "lakiehdotus", 10, "sv" ).Single().Title );
    }
}
