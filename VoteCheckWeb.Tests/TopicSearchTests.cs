using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace VoteCheckWeb.Tests;

// Finding a division by what it is about, rather than by the wording of the bill.
[TestClass]
public class TopicSearchTests {

    // A bill whose legal title contains none of the words a person would search for. That gap
    // is the whole point: "Hallituksen esitys eduskunnalle laiksi alkoholilain muuttamisesta"
    // never says the vote was about off-licence sales or age limits.
    private static TestDb WithAlcoholBill() {
        var db = new TestDb();
        db.AddSession( "2026-10-1", "2026-03-01",
            "1. lakiehdotus: mietintö JAA / vastalause EI",
            "Hallituksen esitys eduskunnalle laiksi alkoholilain muuttamisesta",
            2026, 10, 1, yes: 100, no: 80 );
        db.SetDocument( "2026-10-1", "HE 40/2026 vp" );
        db.AddMatter( "HE 40/2026 vp", keywords: "alkoholijuomat\netämyynti\nikärajat" );
        return db;
    }

    [TestMethod]
    public void ADivisionIsFoundByATermThatAppearsNowhereInItsTitle() {
        using var db = WithAlcoholBill();

        foreach ( var term in new[] { "etämyynti", "ikärajat", "alkoholijuomat" } ) {
            var hits = db.Queries.SearchSessions( term, 10 );
            Assert.AreEqual( 1, hits.Count, $"searching '{term}' found nothing" );
            Assert.AreEqual( "2026-10-1", hits[ 0 ].Id );
        }
    }

    [TestMethod]
    public void TheTitleAndSubjectStillFindIt() {
        // The keywords are added to the index, not substituted for what was there.
        using var db = WithAlcoholBill();

        Assert.AreEqual( 1, db.Queries.SearchSessions( "alkoholilain", 10 ).Count );
        Assert.AreEqual( 1, db.Queries.SearchSessions( "mietintö", 10 ).Count );
    }

    [TestMethod]
    public void ADivisionWhoseMatterHasNotArrivedIsStillFoundByItsTitle() {
        // Matters are fetched in a later pass, so for a while a division has none. It must stay
        // searchable meanwhile — the index is written when the division arrives and the
        // keywords are filled in afterwards.
        using var db = new TestDb();
        db.AddSession( "2026-11-1", "2026-03-02", "otsikko",
            "Hallituksen esitys laiksi jostakin", 2026, 11, 1 );

        Assert.AreEqual( 1, db.Queries.SearchSessions( "jostakin", 10 ).Count );
    }

    [TestMethod]
    public void KeywordsReachDivisionsImportedBeforeTheMatterArrived() {
        // The order the sync actually works in: every division of a matter is indexed first,
        // and one write has to reach all of them when the matter lands.
        using var db = new TestDb();
        for ( var i = 1; i <= 3; i++ ) {
            db.AddSession( $"2026-12-{i}", "2026-03-03", "otsikko", "aihe", 2026, 12, i );
            db.SetDocument( $"2026-12-{i}", "HE 41/2026 vp" );
        }
        db.AddMatter( "HE 41/2026 vp", keywords: "ydinvoima" );

        Assert.AreEqual( 3, db.Queries.SearchSessions( "ydinvoima", 10 ).Count );
    }

    [TestMethod]
    public void AKeywordMatchOutranksAnIncidentalMentionInATitle() {
        // Subject terms are assigned because the matter is about them. A long legal title that
        // happens to contain the same word is a weaker match, and bill titles are long enough
        // to collect words by accident, so the ranking has to prefer the assigned one.
        using var db = new TestDb();

        db.AddSession( "2026-20-1", "2026-04-01", "otsikko",
            "Hallituksen esitys laiksi ydinvoimalaitosten valvonnasta ja ydinvoima-alan "
            + "koulutuksesta sekä eräiden siihen liittyvien lakien muuttamisesta",
            2026, 20, 1 );

        db.AddSession( "2026-21-1", "2026-04-02", "otsikko",
            "Hallituksen esitys laiksi energiantuotannon rakenteesta", 2026, 21, 1 );
        db.SetDocument( "2026-21-1", "HE 50/2026 vp" );
        db.AddMatter( "HE 50/2026 vp", keywords: "ydinvoima\nenergiapolitiikka" );

        var hits = db.Queries.SearchSessions( "ydinvoima", 10 );
        Assert.AreEqual( 2, hits.Count, "both mention it, so both should be found" );
        Assert.AreEqual( "2026-21-1", hits[ 0 ].Id,
            "the division the term was assigned to should rank above one whose title "
            + "merely contains the word" );
    }

    [TestMethod]
    public void AnnulledDivisionsStayOutOfTopicResults() {
        // Cancelled divisions are excluded everywhere else; adding a second way in through the
        // keywords would have quietly reintroduced them.
        using var db = new TestDb();
        db.AddSession( "2026-30-1", "2026-05-01", "otsikko", "aihe",
            2026, 30, 1, cancelled: true );
        db.SetDocument( "2026-30-1", "HE 60/2026 vp" );
        db.AddMatter( "HE 60/2026 vp", keywords: "kalastus" );

        Assert.AreEqual( 0, db.Queries.SearchSessions( "kalastus", 10 ).Count );
    }
}
