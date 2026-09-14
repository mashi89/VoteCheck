using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Data;
using VoteCheckWeb.Pages;

namespace VoteCheckWeb.Tests;

// Covers what the vote page derives before it renders: how a division is summarised, how a
// group is named, and how a date is written.
[TestClass]
public class PresentationTests {

    private static SessionSummary Division( int yes, int no, int blank = 0, int absent = 0,
                                            string id = "2026-80-3" ) =>
        new( id, "2026-09-11", "mietintö JAA / vastalause EI", "Aihe", yes, no, blank, absent );

    // ---- which side had more votes -------------------------------------------------

    [TestMethod]
    public void Majority_NamesTheSideWithMoreVotes() {
        Assert.AreEqual( VoteValue.Yes, Division( yes: 102, no: 65 ).Majority );
        Assert.AreEqual( VoteValue.No, Division( yes: 65, no: 102 ).Majority );
    }

    [TestMethod]
    public void Majority_IsNullOnATie() {
        // The page says "äänet menivät tasan" rather than picking a winner, so a tie has to be
        // distinguishable from a Jaa majority instead of falling through to one.
        Assert.IsNull( Division( yes: 84, no: 84 ).Majority );
    }

    [TestMethod]
    public void Majority_IgnoresBlankAndAbsentBallots() {
        // Only Jaa and Ei decide a division. A large number of abstentions or absences must not
        // change which side is reported as larger.
        Assert.AreEqual( VoteValue.Yes, Division( yes: 20, no: 19, blank: 60, absent: 101 ).Majority );
    }

    // ---- the counts add up ---------------------------------------------------------

    [TestMethod]
    public void TotalCoversTheWholeChamberAndCastExcludesAbsences() {
        var d = Division( yes: 102, no: 65, blank: 1, absent: 32 );
        Assert.AreEqual( 200, d.Total, "every member is counted exactly once" );
        Assert.AreEqual( 168, d.Cast );
    }

    // ---- identifier components -----------------------------------------------------

    [TestMethod]
    public void IdentifierComponents_AreReadBackFromTheId() {
        var d = Division( 1, 1, id: "2026-80-3" );
        Assert.AreEqual( 80, d.SessionNumber );
        Assert.AreEqual( 3, d.VoteNumber );
    }

    [TestMethod]
    public void IdentifierComponents_AreNullWhenTheIdIsNotTheExpectedShape() {
        // The page omits the "Täysistunto N, äänestys M" line rather than printing a partial
        // one, so an unexpected identifier has to report null instead of guessing.
        Assert.IsNull( Division( 1, 1, id: "2026-80" ).SessionNumber );
        Assert.IsNull( Division( 1, 1, id: "rubbish" ).VoteNumber );
        Assert.IsNull( Division( 1, 1, id: "2026-eighty-3" ).SessionNumber );
    }

    // ---- group names ---------------------------------------------------------------

    [TestMethod]
    public void PartyName_ResolvesTheAbbreviationsUpstreamActuallySends() {
        // The full set observed in the live mirror. "r" and "tv" are the ones a reader is most
        // likely to be defeated by, which is the reason this mapping exists at all.
        Assert.AreEqual( "Kokoomus", Party.Name( "kok" ) );
        Assert.AreEqual( "RKP", Party.Name( "r" ) );
        Assert.AreEqual( "Timo Vornanen", Party.Name( "tv" ) );
        Assert.AreEqual( "Kristillisdemokraatit", Party.Name( "kd" ) );
    }

    [TestMethod]
    public void PartyName_PassesAnUnknownAbbreviationThrough() {
        // Groups form and split between elections. Showing the raw abbreviation is honest about
        // holding a member we cannot name; dropping it would lose them from a tally that still
        // has to add up.
        Assert.AreEqual( "xyz", Party.Name( "xyz" ) );
        Assert.AreEqual( "", Party.Name( null ) );
    }

    [TestMethod]
    public void PartyName_IgnoresCaseAndPadding() {
        Assert.AreEqual( "SDP", Party.Name( "SD" ) );
        Assert.AreEqual( "Vihreät", Party.Name( " vihr " ) );
    }

    [TestMethod]
    public void FormalName_IsNullWhenUnknownSoTheTooltipCanBeOmitted() {
        Assert.AreEqual( "Ruotsalainen eduskuntaryhmä", Party.FormalName( "r" ) );
        Assert.IsNull( Party.FormalName( "xyz" ) );
    }

    // ---- dates ---------------------------------------------------------------------

    [TestMethod]
    public void Date_IsWrittenTheFinnishWay() {
        Assert.AreEqual( "11.9.2026", Format.Date( "2026-09-11" ) );
        Assert.AreEqual( "1.2.2026", Format.Date( "2026-02-01" ) );
    }

    [TestMethod]
    public void Date_AcceptsTheTimeAndOffsetUpstreamAttaches() {
        Assert.AreEqual( "11.9.2026", Format.Date( "2026-09-11T14:03:00+03:00" ) );
    }

    [TestMethod]
    public void Date_DoesNotFollowTheAmbientCulture() {
        // The container's locale is not a deployment decision anyone makes deliberately, so
        // rendering must not depend on it. Under fi-FI a naive ToString would still give
        // d.M.yyyy, so this pins a culture that formats dates differently.
        var original = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo( "en-US" );
            Assert.AreEqual( "11.9.2026", Format.Date( "2026-09-11" ) );
        }
        finally {
            CultureInfo.CurrentCulture = original;
        }
    }

    [TestMethod]
    public void Date_PassesThroughWhatItCannotParse() {
        Assert.AreEqual( "", Format.Date( null ) );
        Assert.AreEqual( "ei tiedossa", Format.Date( "ei tiedossa" ) );
    }

    [TestMethod]
    public void Iso_KeepsTheDatePartForMachineReadableAttributes() {
        Assert.AreEqual( "2026-09-11", Format.Iso( "2026-09-11T14:03:00+03:00" ) );
        Assert.AreEqual( "2026-09-11", Format.Iso( "2026-09-11" ) );
    }

    [TestMethod]
    public void Iso_DoesNotTruncateTextThatIsNotADate() {
        // Taking the first ten characters of anything turns unexpected text into a fragment
        // that looks deliberate. "ei tiedossa" became "ei tiedoss" before this was guarded.
        Assert.AreEqual( "ei tiedossa", Format.Iso( "ei tiedossa" ) );
    }

    [TestMethod]
    public void Date_KeepsTheCalendarDateAcrossAnOffset() {
        // A division just after midnight in Helsinki is +03:00, which is the previous evening
        // in UTC. Parsing the timestamp as an instant and formatting it in the server's zone
        // would report the sitting a day early; the date is read literally to prevent that.
        Assert.AreEqual( "11.9.2026", Format.Date( "2026-09-11T00:30:00+03:00" ) );
        Assert.AreEqual( "11.9.2026", Format.Date( "2026-09-11T23:45:00+03:00" ) );
    }
}
