using FluentValidation;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class CreateAssetValidator : AbstractValidator<CreateAssetRequest>
{
    public CreateAssetValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.AssetNumber).NotEmpty().MaximumLength(50);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Manufacturer).MaximumLength(100);
        RuleFor(r => r.Model).MaximumLength(100);
        RuleFor(r => r.SerialNumber).MaximumLength(100);
        RuleFor(r => r.PurchasePrice).GreaterThan(0).When(r => r.PurchasePrice.HasValue);
    }
}
