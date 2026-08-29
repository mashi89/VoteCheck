using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class MpsModel : PageModel {

    private readonly Queries _queries;

    [BindProperty( SupportsGet = true )]
    public string? Name { get; set; }

    public IReadOnlyList<MpSummary> Mps { get; private set; } = [];

    public MpsModel( Queries queries ) => _queries = queries;

    public void OnGet() => Mps = _queries.FindMps( string.IsNullOrWhiteSpace( Name ) ? null : Name.Trim() );
}
