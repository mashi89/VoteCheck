using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class IndexModel : PageModel {

    private readonly Queries _queries;

    public IReadOnlyList<SessionSummary> Sessions { get; private set; } = [];

    public IndexModel( Queries queries ) => _queries = queries;

    public void OnGet() => Sessions = _queries.LatestSessions( 50 );
}
