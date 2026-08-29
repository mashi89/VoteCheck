using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class SearchModel : PageModel {

    private readonly Queries _queries;

    [BindProperty( SupportsGet = true )]
    public string? Query { get; set; }

    public IReadOnlyList<SessionSummary> Sessions { get; private set; } = [];

    public SearchModel( Queries queries ) => _queries = queries;

    public void OnGet() {
        if ( !string.IsNullOrWhiteSpace( Query ) )
            Sessions = _queries.SearchSessions( Query.Trim(), 50 );
    }
}
