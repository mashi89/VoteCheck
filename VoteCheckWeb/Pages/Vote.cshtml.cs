using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class VoteModel : PageModel {

    private readonly Queries _queries;

    public SessionSummary? Session { get; private set; }
    public IReadOnlyList<PartyDistribution> Parties { get; private set; } = [];
    public IReadOnlyList<IndividualVote> Votes { get; private set; } = [];
    public string? Party { get; private set; }

    public VoteModel( Queries queries ) => _queries = queries;

    public void OnGet( string id, string? party ) {
        Session = _queries.GetSession( id );
        if ( Session == null ) return;

        Party = party;
        Parties = _queries.GetPartyDistribution( id );
        // Without a party filter show all individual votes; with one, just that group.
        Votes = _queries.GetIndividualVotes( id, party );
    }
}
