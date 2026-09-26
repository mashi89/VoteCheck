using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoteCheckWeb.Pages;

namespace VoteCheckWeb.Tests;

// Reading which proposal Jaa and Ei stood for off a division's procedural title. Every title
// here is one the live archive actually carries.
[TestClass]
public class BallotOptionsTests {

    [TestMethod]
    public void ReadsBothSidesAndTheQuestion() {
        var options = BallotOptions.Parse(
            "Käsittelyn pohja: Helena Marttilan ehdotus (vl 1) JAA / Veronika Honkasalon ehdotus (vl 2) EI" );

        Assert.IsNotNull( options );
        Assert.AreEqual( "Käsittelyn pohja", options.Question );
        Assert.AreEqual( "Helena Marttilan ehdotus (vastalause 1)", options.Yes );
        Assert.AreEqual( "Veronika Honkasalon ehdotus (vastalause 2)", options.No );
    }

    [TestMethod]
    public void NamesWhoseReportABareMietintoIs() {
        var options = BallotOptions.Parse( "Mietintö JAA / Markus Lohen lausumaehdotus 1 (vl 2) EI" );

        Assert.AreEqual( "Valiokunnan mietintö", options?.Yes );
        Assert.AreEqual( "Markus Lohen lausumaehdotus 1 (vastalause 2)", options?.No );
        Assert.IsNull( options?.Question );
    }

    [TestMethod]
    public void KeepsASectionReferenceAsTheQuestion() {
        var options = BallotOptions.Parse( "1. lakiehdotus 8 b §: mietintö JAA / Saku Nikkasen ehdotus (vl 1) EI" );

        Assert.AreEqual( "1. lakiehdotus 8 b §", options?.Question );
        Assert.AreEqual( "Valiokunnan mietintö", options?.Yes );
        Assert.AreEqual( "Saku Nikkasen ehdotus (vastalause 1)", options?.No );
    }

    [TestMethod]
    public void CopesWithTheSpacingAndCaseUpstreamActuallySends() {
        // A missing space after the slash, a space before the colon, a lower-case "ei", and
        // an EI left off altogether — all four occur.
        Assert.AreEqual( "Hylkääminen",
            BallotOptions.Parse( "Lakiehdotuksen hyväksyminen JAA /hylkääminen EI" )?.No );
        Assert.AreEqual( "1 luvun 1 §",
            BallotOptions.Parse( "1 luvun 1 § : mietintö JAA / Ilmari Nurminen (vi 1 ) EI" )?.Question );
        Assert.AreEqual( "Saara Hyrkön ehdotus (vastalause 3)",
            BallotOptions.Parse( "Hannu Hoskosen ehdotus (vl 2) JAA / Saara Hyrkön ehdotus (vl 3) ei" )?.No );
        Assert.AreEqual( "Hanna Räsäsen lausumaehdotus 2 (vastalause 2)",
            BallotOptions.Parse( "Mietintö JAA / Hanna Räsäsen lausumaehdotus 2 (vl 2)" )?.No );
    }

    [TestMethod]
    public void CopesWithTheRarerPunctuation() {
        // JAA straight against a bracket, a semicolon before the question, and an EI marker
        // placed before a trailing note rather than at the end.
        Assert.AreEqual( "Seppo Eskelisen ehdotus (vastalause 1)",
            BallotOptions.Parse( "Seppo Eskelisen ehdotus (vl 1)JAA /  Hannu Hoskosen ehdotus (vl 2) ei" )?.Yes );
        Assert.AreEqual( "1. lakiehdotus 32 §",
            BallotOptions.Parse( "1. lakiehdotus 32 § ; mietintö JAA / Tiina Elon ehdotus (vl 1) EI" )?.Question );
        Assert.AreEqual( "Hanna Kosonen (moniste)",
            BallotOptions.Parse( "1. lakiehdotus 5 § 1 momentti, mietintö JAA / Hanna Kosonen EI (moniste)" )?.No );
    }

    [TestMethod]
    public void RefusesAQuestionWithoutOptions() {
        // Jaa for, Ei against. There are no options to name, and the title says it plainly.
        Assert.IsNull( BallotOptions.Parse( "Lakiehdotuksen julistaminen kiireelliseksi: JAA / EI" ) );
    }

    [TestMethod]
    public void ReadsTheCommaSeparatedForm() {
        var options = BallotOptions.Parse( "1. lakiehdotuksen hyväksyminen JAA, hylkääminen EI" );

        Assert.AreEqual( "1. lakiehdotuksen hyväksyminen", options?.Yes );
        Assert.AreEqual( "Hylkääminen", options?.No );
    }

    [TestMethod]
    public void LeavesAnUnnumberedDissentUnnumbered() {
        Assert.AreEqual( "Hanna Laine-Nousimaan lausumaehdotus 16 (vastalause)",
            BallotOptions.Parse( "Mietintö JAA / Hanna Laine-Nousimaan lausumaehdotus 16 (vl) EI" )?.No );
    }

    [TestMethod]
    public void RefusesATitleThatDoesNotSayWhichSideIsJaa() {
        // These follow the left-is-Jaa order by convention, but do not state it. Getting a
        // side wrong would tell a reader their representative voted for the opposite of what
        // they did, so a convention is not enough to go on.
        Assert.IsNull( BallotOptions.Parse( "28.89.31, lausumaehdotus, mietintö / 47" ) );
        Assert.IsNull( BallotOptions.Parse( "ed. Lauri Lylyn ehdotus / ed. Tuomas Kettusen ehdotus" ) );
        Assert.IsNull( BallotOptions.Parse( "Ministeri Wille Rydman, nauttii luottamusta / Veronika Honkasalon ehdotus" ) );
        Assert.IsNull( BallotOptions.Parse( "33.60.52, mietintö, 57" ) );
    }

    [TestMethod]
    public void RefusesEmptyAndOneSidedTitles() {
        Assert.IsNull( BallotOptions.Parse( null ) );
        Assert.IsNull( BallotOptions.Parse( "" ) );
        Assert.IsNull( BallotOptions.Parse( "   " ) );
        Assert.IsNull( BallotOptions.Parse( "Mietintö JAA" ) );
        Assert.IsNull( BallotOptions.Parse( "JAA / EI" ) );
    }

    [TestMethod]
    public void DoesNotMistakeANameForTheMarker() {
        // "Jaana" contains the letters but is not the marker, which must stand on its own.
        Assert.IsNull( BallotOptions.Parse( "Jaana Pelkosen ehdotus / Jaakko Ketolan ehdotus" ) );
    }
}
