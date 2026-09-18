using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class VoteModel : PageModel {

    private readonly Queries _queries;

    public SessionSummary? Session { get; private set; }
    public IReadOnlyList<PartyDistribution> Parties { get; private set; } = [];

    // Every ballot in the division. The chamber diagram draws the whole chamber whatever
    // filter is applied — a picture of one group's seats would not be a picture of the vote.
    public IReadOnlyList<IndividualVote> AllVotes { get; private set; } = [];

    // What the ballot list shows: the whole chamber, or one group when filtered.
    public IReadOnlyList<IndividualVote> Votes { get; private set; } = [];

    public string? Party { get; private set; }

    // What the division was about: the bill or report it decides a step of. Null until the
    // sync has fetched it, which is an ordinary state and not an error.
    public Matter? Matter { get; private set; }

    // The document identifier even when the matter itself is not held yet, so the page can
    // still name and link what was being decided.
    public string? DocumentId { get; private set; }

    public VoteModel( Queries queries ) => _queries = queries;

    public void OnGet( string id, string? party ) {
        Session = _queries.GetSession( id );
        if ( Session == null ) return;

        Party = party;
        Parties = _queries.GetPartyDistribution( id );
        Matter = _queries.GetMatterForSession( id );
        DocumentId = Matter?.Id ?? _queries.GetDocumentId( id );

        // Read once and narrow in memory. The page needs both the whole division and the
        // filtered view of it, and asking the database twice for the same rows to throw most
        // of them away the second time would be the slower way to get there.
        AllVotes = _queries.GetIndividualVotes( id );
        Votes = party == null
            ? AllVotes
            : AllVotes.Where( v => string.Equals( v.Party, party, StringComparison.OrdinalIgnoreCase ) ).ToList();
    }
}
