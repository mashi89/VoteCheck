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
