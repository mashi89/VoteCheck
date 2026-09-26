using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core.Models;
using VoteCheckWeb.Data;
using VoteCheckWeb.Sync;

namespace VoteCheckWeb.Tests;

// The breakdowns upstream computes for a division — government against opposition, and by
// electoral district — from the payload the sync already downloads to the rows the page reads.
[TestClass]
public class SplitImportTests {

    private TestDb _db = null!;

    [TestInitialize]
    public void Setup() => _db = new TestDb();

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    // The government split and three districts of 2026-60-1 as upstream sent them, Swedish
    // names included. Districts are deliberately not alphabetical: upstream's order is the
    // official one, and the page has to keep it.
    private static Aanestys Division( string id = "2026-60-1", int governmentYes = 86 ) => new() {
        Id = id,
        Istuntopvm = "2026-06-03+03:00",
        Istuntovpvuosi = "2026", Istuntonumero = "60", Aanestysnumero = "1",
        Aanestysotsikko = new LocalizedText { Fi = "mietintö JAA / vastalause EI" },
        Kohta = new Kohta { Otsikko = new LocalizedText { Fi = "Hallituksen esitys" } },
        Aanestystulos = new AanestysTulos { Jaa = 143, Ei = 21, Poissa = 35, Yhteensa = 199 },
        HallitusoppositioJakaumat = [
            Split( "Hallitusryhmät", "Regeringsgrupperna", governmentYes, 0, 0, 21 ),
            Split( "Oppositioryhmät", "Oppositionsgrupperna", 57, 21, 0, 14 ),
        ],
        VaalipiiriJakaumat = [
            Split( "Helsingin vaalipiiri", "Helsingfors valkrets", 13, 5, 0, 4 ),
            Split( "Uudenmaan vaalipiiri", "Nylands valkrets", 30, 3, 0, 4 ),
            Split( "Ahvenanmaan maakunnan vaalipiiri", "Landskapet Ålands valkrets", 1, 0, 0, 0 ),
        ],
    };

    private static Aanestysjakauma Split( string fi, string sv, int yes, int no, int blank, int absent ) => new() {
        Nimi = new LocalizedText { Fi = fi, Sv = sv },
        Jaa = yes, Ei = no, Tyhjia = blank, Poissa = absent,
        Yhteensa = yes + no + blank + absent,
    };

    private void Import( params Aanestys[] votes ) {
        using var conn = _db.Db.Open();
        VoteSyncService.ImportPage( conn, new VotePage { Votes = votes, TotalCount = votes.Length } );
    }

    [TestMethod]
    public void GovernmentSplit_IsStoredWithGovernmentFirst() {
        Import( Division() );

        var government = _db.Queries.GetSplits( "2026-60-1", SplitKind.Government );

        Assert.AreEqual( 2, government.Count );
        Assert.AreEqual( new Split( "Hallitusryhmät", 86, 0, 0, 21 ), government[ 0 ] );
        Assert.AreEqual( new Split( "Oppositioryhmät", 57, 21, 0, 14 ), government[ 1 ] );
    }

    [TestMethod]
    public void DistrictSplit_KeepsUpstreamsOrderRatherThanAlphabetical() {
        Import( Division() );

        var districts = _db.Queries.GetSplits( "2026-60-1", SplitKind.District );

        CollectionAssert.AreEqual(
            new[] { "Helsingin vaalipiiri", "Uudenmaan vaalipiiri", "Ahvenanmaan maakunnan vaalipiiri" },
            districts.Select( d => d.Name ).ToArray() );
    }

    [TestMethod]
    public void Splits_AreKeptApartByKind() {
        Import( Division() );

        Assert.IsFalse( _db.Queries.GetSplits( "2026-60-1", SplitKind.Government )
            .Any( s => s.Name.EndsWith( "vaalipiiri" ) ) );
        Assert.IsFalse( _db.Queries.GetSplits( "2026-60-1", SplitKind.District )
            .Any( s => s.Name.EndsWith( "ryhmät" ) ) );
    }

    [TestMethod]
    public void Splits_ResolveToSwedishWhenAskedFor() {
        Import( Division() );

        Assert.AreEqual( "Regeringsgrupperna",
            _db.Queries.GetSplits( "2026-60-1", SplitKind.Government, "sv" )[ 0 ].Name );
        Assert.AreEqual( "Hallitusryhmät",
            _db.Queries.GetSplits( "2026-60-1", SplitKind.Government, "en" )[ 0 ].Name );
    }

    [TestMethod]
    public void Reimport_FillsInADivisionThatArrivedBeforeTheTableExisted() {
        // The backfill case. The division row is already in the mirror with no breakdowns, and
        // the one-time rewind walks it again: the session upsert leaves it alone, and the
        // breakdowns have to be written anyway rather than skipped with the rest of the row.
        _db.AddSession( "2026-60-1", "2026-06-03", "mietintö JAA / vastalause EI", "Hallituksen esitys",
            2026, 60, 1, yes: 143, no: 21, absent: 35 );
        Assert.AreEqual( 0, _db.Queries.GetSplits( "2026-60-1", SplitKind.Government ).Count );

        Import( Division() );

        Assert.AreEqual( 2, _db.Queries.GetSplits( "2026-60-1", SplitKind.Government ).Count );
        Assert.AreEqual( 3, _db.Queries.GetSplits( "2026-60-1", SplitKind.District ).Count );
    }

    [TestMethod]
    public void Reimport_OverwritesCountsRatherThanDuplicatingRows() {
        Import( Division( governmentYes: 85 ) );
        Import( Division( governmentYes: 86 ) );

        var government = _db.Queries.GetSplits( "2026-60-1", SplitKind.Government );
        Assert.AreEqual( 2, government.Count );
        Assert.AreEqual( 86, government[ 0 ].Yes );
    }

    [TestMethod]
    public void Splits_AreEmptyForADivisionWithoutThem() {
        // What the page sees for a division the backfill has not reached yet.
        _db.AddSession( "2009-24-1", "2009-03-13", "Äänestys", "Aihe", 2009, 24, 1 );

        Assert.AreEqual( 0, _db.Queries.GetSplits( "2009-24-1", SplitKind.Government ).Count );
        Assert.AreEqual( 0, _db.Queries.GetSplits( "no-such-division", SplitKind.District ).Count );
    }

    [TestMethod]
    public void Split_WithNoNameIsSkippedRatherThanStoredBlank() {
        var division = Division();
        division.VaalipiiriJakaumat.Add( new Aanestysjakauma { Nimi = null, Jaa = 1 } );

        Import( division );

        Assert.AreEqual( 3, _db.Queries.GetSplits( "2026-60-1", SplitKind.District ).Count );
    }
}
