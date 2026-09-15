namespace VoteCheckWeb.Data;

// Upstream identifies a parliamentary group by a lowercase abbreviation — "kok", "r", "tv".
// Those are fine as identifiers and poor as labels: "r" is the Swedish People's Party and
// "tv" is a one-member group named after its member, neither of which is guessable by a
// reader who does not already follow politics. This maps the abbreviation to something a
// visitor recognises, and is the only place that mapping lives.
//
// Deliberately no party colours. Several Finnish groups share a hue (kok, ps and kd are all
// blue; sd and vas both red; kesk and vihr both green), so colour cannot identify a group
// without a label anyway — and a second colour system on the vote page would compete with
// the one that carries meaning there, which is how each member voted. Groups are named, not
// coloured.
public static class Party {

    // Short display name — what a reader would call the group in conversation.
    private static readonly Dictionary<string, string> Names = new( StringComparer.OrdinalIgnoreCase ) {
        [ "kok" ]  = "Kokoomus",
        [ "sd" ]   = "SDP",
        [ "ps" ]   = "Perussuomalaiset",
        [ "kesk" ] = "Keskusta",
        [ "vihr" ] = "Vihreät",
        [ "vas" ]  = "Vasemmistoliitto",
        [ "r" ]    = "RKP",
        [ "kd" ]   = "Kristillisdemokraatit",
        [ "liik" ] = "Liike Nyt",
        [ "tv" ]   = "Timo Vornanen",
    };

    // The formal name, used where there is room for it — a tooltip, a title attribute.
    private static readonly Dictionary<string, string> FormalNames = new( StringComparer.OrdinalIgnoreCase ) {
        [ "kok" ]  = "Kansallisen kokoomuksen eduskuntaryhmä",
        [ "sd" ]   = "Sosialidemokraattinen eduskuntaryhmä",
        [ "ps" ]   = "Perussuomalaisten eduskuntaryhmä",
        [ "kesk" ] = "Keskustan eduskuntaryhmä",
        [ "vihr" ] = "Vihreä eduskuntaryhmä",
        [ "vas" ]  = "Vasemmistoliiton eduskuntaryhmä",
        [ "r" ]    = "Ruotsalainen eduskuntaryhmä",
        [ "kd" ]   = "Kristillisdemokraattinen eduskuntaryhmä",
        [ "liik" ] = "Liike Nyt -eduskuntaryhmä",
        [ "tv" ]   = "Eduskuntaryhmä Timo Vornanen",
    };

    // Four characters at most, for labelling the chamber diagram, where a name has to fit
    // under a group that may be only a few seats wide. These are the forms Finnish election
    // graphics use — "RKP" rather than the bare "r" upstream sends, which names nothing.
    private static readonly Dictionary<string, string> ShortNames = new( StringComparer.OrdinalIgnoreCase ) {
        [ "kok" ]  = "KOK",
        [ "sd" ]   = "SDP",
        [ "ps" ]   = "PS",
        [ "kesk" ] = "KESK",
        [ "vihr" ] = "VIHR",
        [ "vas" ]  = "VAS",
        [ "r" ]    = "RKP",
        [ "kd" ]   = "KD",
        [ "liik" ] = "LIIK",
        [ "tv" ]   = "TV",
    };

    public static string ShortName( string? abbreviation ) {
        var key = ( abbreviation ?? "" ).Trim();
        return ShortNames.TryGetValue( key, out var name ) ? name : key.ToUpperInvariant();
    }

    // The conventional left-to-right ordering of the groups, used to seat the chamber diagram.
    // It is the arrangement Finnish election graphics and the chamber itself use, and it is a
    // convention rather than a measurement — nothing downstream depends on it being an exact
    // account of any group's politics, only on the ordering being stable and familiar.
    private static readonly string[] LeftToRight =
        [ "vas", "sd", "vihr", "kesk", "liik", "r", "kd", "kok", "ps" ];

    // Unplaced groups sort last rather than first: a new or one-member group appearing at the
    // left edge would read as a claim about its politics that we have not made.
    public static int SeatingRank( string? abbreviation ) {
        var index = Array.FindIndex( LeftToRight,
            a => a.Equals( ( abbreviation ?? "" ).Trim(), StringComparison.OrdinalIgnoreCase ) );
        return index < 0 ? LeftToRight.Length : index;
    }

    // An unknown abbreviation is returned as-is rather than replaced or dropped. Groups form
    // and split between elections, and showing "xyz" is honest about holding a member we
    // cannot name; showing nothing would lose the member from a tally that must still add up.
    public static string Name( string? abbreviation ) {
        var key = ( abbreviation ?? "" ).Trim();
        return Names.TryGetValue( key, out var name ) ? name : key;
    }

    // Null when there is no formal name on file, so a caller can omit the attribute entirely
    // rather than render a tooltip repeating the abbreviation.
    public static string? FormalName( string? abbreviation ) {
        var key = ( abbreviation ?? "" ).Trim();
        return FormalNames.TryGetValue( key, out var name ) ? name : null;
    }
}
