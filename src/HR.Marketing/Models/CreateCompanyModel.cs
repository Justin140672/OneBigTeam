using System.ComponentModel.DataAnnotations;

namespace HR.Marketing.Models;

// Mirrors HR.Modules.Identity's SignUpRequest/SignUpValidator constraints exactly, so this model
// is ready to bind and validate the SignUp.razor form directly once SSR attribute-based
// validation lands in .NET 11 — the /signup-submit minimal API endpoint doesn't apply these
// attributes today, native HTML5 constraints on the form fields still do the actual client-side
// enforcement in the meantime.
public sealed class CreateCompanyModel
{
    [Required]
    [StringLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string AdminFirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string AdminLastName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string AdminEmail { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;
}
