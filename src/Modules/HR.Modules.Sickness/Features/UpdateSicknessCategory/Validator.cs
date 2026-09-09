using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed class UpdateSicknessCategoryValidator : AbstractValidator<UpdateSicknessCategoryRequest>
{
    public UpdateSicknessCategoryValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
