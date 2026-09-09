using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Assets.Features.UpdateAssetCategory;

internal sealed class UpdateAssetCategoryValidator : AbstractValidator<UpdateAssetCategoryRequest>
{
    public UpdateAssetCategoryValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(100);
        RuleFor(r => r.Description).MaximumLength(500);
    }
}
