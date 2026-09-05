namespace HR.Web.Components.Pages.Recruitment;

/// <summary>
/// Closed set of screens that can launch the Add Candidate editor (CandidateDetail.razor) and
/// that Save/Close should return to. Deliberately NOT an arbitrary URL/redirect string — carried
/// as a whitelisted "origin" query value (see CandidateDetail.razor) so a crafted query string
/// can never redirect the user off an allowed destination.
/// </summary>
public enum CandidateReturnDestination
{
    CandidatesList,
    Dashboard
}
