using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheck.Core.Models;
using VoteCheckWeb.Data;
using VoteCheckWeb.Sync;

namespace VoteCheckWeb.Tests;

// What a session's minutes say about its votes, from the parsed document to the rows the
// division page reads.
[TestClass]
public class MinutesImportTests {

    private TestDb _db = null!;

    [TestInitialize]
    public void Setup() => _db = new TestDb();

    [TestCleanup]
    public void Cleanup() => _db.Dispose();

    private static PoytakirjaAanestys Vote( int number, string yes = "Mietintö", string no = "Hanna Sarkkisen ehdotus 2",
                                            string noText = "Lisätään 8 500 000 euroa etsivään nuorisotyöhön." ) => new() {
        Valtiopaivavuosi = 2024, Istuntonumero = 39, Aanestysnumero = number,
        Asettelu = $"{yes} ”jaa”, {no} ”ei”.",
        Tulos = "Eduskunta hyväksyi mietinnön.",
        Jaa = new PoytakirjaVaihtoehto { Nimi = yes },
        Ei = new PoytakirjaVaihtoehto { Nimi = no, Kohta = "29.91.51", Teksti = noText },
    };

    private int Store( Poytakirja? minutes ) {
        using var conn = _db.Db.Open();
        return VoteSyncService.StoreMinutes( conn, 2024, 39, minutes );
    }

    private string? SittingDocument() {
        using var conn = _db.Db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT document FROM sitting_minutes WHERE vp_year = 2024 AND session_number = 39";
        return cmd.ExecuteScalar() as string;
    }

    [TestMethod]
    public void Minutes_AreReadBackForTheDivisionTheyRecord() {
        _db.AddSession( "2024-39-1", "2024-04-19", "29.91.51, mietintö / 1", "Lisätalousarvio", 2024, 39, 1 );

        Assert.AreEqual( 1, Store( new Poytakirja { Edktunnus = "EDK-2024-AK-15838", Aanestykset = [ Vote( 1 ) ] } ) );

        var minutes = _db.Queries.GetMinutes( "2024-39-1" );
        Assert.IsNotNull( minutes );
        Assert.AreEqual( "Eduskunta hyväksyi mietinnön.", minutes.Outcome );
        Assert.AreEqual( new MinutesOption( "Mietintö", "", "" ), minutes.Yes );
        Assert.AreEqual(
            new MinutesOption( "Hanna Sarkkisen ehdotus 2", "29.91.51", "Lisätään 8 500 000 euroa etsivään nuorisotyöhön." ),
            minutes.No );
        Assert.AreEqual( "EDK-2024-AK-15838", SittingDocument() );
    }

    [TestMethod]
    public void VotesTheMirrorDoesNotHold_AreSkipped() {
        // The minutes record every vote of the session. One the mirror lacks has no page.
        _db.AddSession( "2024-39-1", "2024-04-19", "", "", 2024, 39, 1 );

        Assert.AreEqual( 1, Store( new Poytakirja { Edktunnus = "EDK", Aanestykset = [ Vote( 1 ), Vote( 2 ) ] } ) );

        Assert.IsNull( _db.Queries.GetMinutes( "2024-39-2" ) );
    }

    [TestMethod]
    public void UnpublishedMinutes_AreRecordedAsNotFoundYet() {
        _db.AddSession( "2024-39-1", "2024-04-19", "", "", 2024, 39, 1 );

        Assert.AreEqual( 0, Store( null ) );

        Assert.AreEqual( "", SittingDocument(), "asked, and to be asked again later" );
        Assert.IsNull( _db.Queries.GetMinutes( "2024-39-1" ) );
    }

    [TestMethod]
    public void MinutesWithNoReadableVotes_AreAskedForAgain() {
        // An unreadable or half-published document is something upstream may still fix, so it
        // is not marked as read for ever.
        _db.AddSession( "2024-39-1", "2024-04-19", "", "", 2024, 39, 1 );

        Store( new Poytakirja { Edktunnus = "EDK", Aanestykset = [] } );

        Assert.AreEqual( "", SittingDocument() );
    }

    [TestMethod]
    public void ReadingAgain_ReplacesWhatWasStored() {
        _db.AddSession( "2024-39-1", "2024-04-19", "", "", 2024, 39, 1 );
        Store( new Poytakirja { Edktunnus = "EDK", Aanestykset = [ Vote( 1, noText: "" ) ] } );

        Store( new Poytakirja { Edktunnus = "EDK", Aanestykset = [ Vote( 1, noText: "Korjattu teksti." ) ] } );

        Assert.AreEqual( "Korjattu teksti.", _db.Queries.GetMinutes( "2024-39-1" )?.No?.Text );
    }

    [TestMethod]
    public void SetupWithoutBothSides_HasNoOptions() {
        // One side alone would invite a reader to infer the other. The setup is shown instead.
        _db.AddSession( "2024-39-1", "2024-04-19", "", "", 2024, 39, 1 );
        var vote = Vote( 1 );
        vote.Ei = null;

        Store( new Poytakirja { Edktunnus = "EDK", Aanestykset = [ vote ] } );

        var minutes = _db.Queries.GetMinutes( "2024-39-1" );
        Assert.IsNotNull( minutes );
        Assert.IsNull( minutes.Yes );
        Assert.IsNull( minutes.No );
        StringAssert.StartsWith( minutes.Setup, "Mietintö ”jaa”" );
    }
}
