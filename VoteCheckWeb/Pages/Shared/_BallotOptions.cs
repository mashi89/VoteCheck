using System.Text.RegularExpressions;

namespace VoteCheckWeb.Pages;

// What voting Jaa and voting Ei stood for in one division, read off its procedural title.
//
// Upstream states the options as aanestysotsikko, in a register nobody reads:
//
//     Käsittelyn pohja: mietintö JAA / Lauri Lylyn ehdotus (vl 1) EI
//
// which says: on the question of what to take as the basis for the reading, Jaa supported
// the committee's report and Ei supported Lauri Lyly's proposal in dissenting opinion 1.
//
// Only a title that marks the Jaa side in so many words is read. Most titles in the archive
// follow the same left-is-Jaa order without saying so ("28.89.31, mietintö / 47"), but a
// page telling a reader that their representative voted for the opposite of what they did
// is the worst mistake this site could make, and a convention is not a statement. Those
// titles keep being shown as they are, untranslated.
//
// Nothing here writes a word the title did not contain. The only changes are expanding two
// abbreviations whose meaning is fixed, and capitalising the first letter.
public sealed record BallotOptions( string? Question, string Yes, string No ) {

    // "context: yes JAA / no EI". The context is optional and sometimes follows a semicolon
    // instead; the options are split by a slash or, on a few divisions, a comma; JAA can sit
    // straight against a closing bracket; and the trailing EI is sometimes lower case or left
    // off. All of these occur in the archive.
    private static readonly Regex Shape = new(
        @"^\s*(?:(?<question>[^:;/]+?)\s*[:;]\s*)?(?<yes>[^/]+?)(?:\s+|(?<=\)))JAA\b\s*[/,]\s*(?<no>.+?)(?:\s+EI)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds( 50 ) );

    // An EI marker that is not at the end: "Hanna Kosonen EI (moniste)". Upper case only —
    // in lower case "ei" is an ordinary Finnish word and could be part of a proposal.
    private static readonly Regex InnerNo = new(
        @"\s+EI\b", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds( 50 ) );

    // "(vl 1)", "(vl)" — vastalause, a committee member's dissenting opinion.
    private static readonly Regex Dissent = new(
        @"\(\s*vl\s*(?<n>\d*)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds( 50 ) );

    public static BallotOptions? Parse( string? title ) {
        if ( string.IsNullOrWhiteSpace( title ) ) return null;

        Match match;
        try {
            match = Shape.Match( title );
        } catch ( RegexMatchTimeoutException ) {
            return null;
        }
        if ( !match.Success ) return null;

        var yes = Side( match.Groups[ "yes" ].Value );
        var no = Side( InnerNo.Replace( match.Groups[ "no" ].Value, "" ) );

        // "Lakiehdotuksen julistaminen kiireelliseksi: JAA / EI" states a question and no
        // options — Jaa is for, Ei against. Reading it as a Jaa option ending in a colon and
        // an Ei option called "EI" would be nonsense, and the title already says it plainly.
        if ( yes.Length == 0 || no.Length == 0 || yes.EndsWith( ':' )
             || no.Equals( "EI", StringComparison.OrdinalIgnoreCase ) )
            return null;

        var question = match.Groups[ "question" ].Success
            ? Capitalise( match.Groups[ "question" ].Value.Trim() )
            : null;

        return new BallotOptions( string.IsNullOrEmpty( question ) ? null : question, yes, no );
    }

    private static string Side( string text ) {
        var side = text.Trim();

        // A bare "mietintö" in a plenary vote is always the report of the committee that
        // prepared the matter. Said in full because the bare word does not say whose.
        if ( side.Equals( "mietintö", StringComparison.OrdinalIgnoreCase ) )
            return "Valiokunnan mietintö";

        side = Dissent.Replace( side, m =>
            m.Groups[ "n" ].Value.Length > 0 ? $"(vastalause {m.Groups[ "n" ].Value})" : "(vastalause)" );

        return Capitalise( side );
    }

    private static string Capitalise( string text ) =>
        text.Length == 0 ? text : char.ToUpperInvariant( text[ 0 ] ) + text[ 1.. ];
}
