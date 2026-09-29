using System.ComponentModel.DataAnnotations;

namespace HR.Modules.Support.Domain;

internal enum SupportRequestType
{
    [Display(Name = "Report a Problem")]
    ReportProblem,
    [Display(Name = "Request a Feature")]
    RequestFeature,
    [Display(Name = "Ask a Question")]
    AskQuestion
}
