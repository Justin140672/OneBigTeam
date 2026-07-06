using FluentValidation;

namespace HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;

internal sealed class AddRequiredAssetValidator : AbstractValidator<AddRequiredAssetRequest>
{
    public AddRequiredAssetValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PositionProfileId).NotEmpty();
        RuleFor(r => r.AssetCategoryId).NotEmpty();

        RuleFor(r => r.Quantity)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Quantity must be 1 or greater.");
    }
}
