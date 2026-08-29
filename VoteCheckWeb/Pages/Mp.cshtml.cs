using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class MpModel : PageModel {

    private readonly Queries _queries;

    public MpProfile? Profile { get; private set; }

    public MpModel( Queries queries ) => _queries = queries;

    public void OnGet( int personNumber ) => Profile = _queries.GetMpProfile( personNumber, 50 );
}
