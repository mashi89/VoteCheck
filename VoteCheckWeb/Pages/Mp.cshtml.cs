using Microsoft.AspNetCore.Mvc.RazorPages;
using VoteCheckWeb.Data;

namespace VoteCheckWeb.Pages;

public class MpModel : PageModel {

    private readonly Queries _queries;

    public MpProfile? Profile { get; private set; }

    // The breakdown behind the attendance figure — how the member's ballots divide between
    // Jaa, Ei, tyhjä and absence. The profile carries the totals but not the shape of them,
    // and the shape is what the product is named for.
    public MpActivity? Activity { get; private set; }

    public MpModel( Queries queries ) => _queries = queries;

    public void OnGet( int personNumber ) {
        Profile = _queries.GetMpProfile( personNumber, 50 );
        if ( Profile != null ) Activity = _queries.GetMpActivity( personNumber );
    }
}
