using FluentValidation;

namespace HR.Modules.Assets.Features.CreateAsset;

internal sealed class CreateAssetValidator : AbstractValidator<CreateAssetRequest>
{
    public CreateAssetValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        // Requiredness depends on the company's AssetNumberMode (Manual vs Automatic), which this
        // shape-only validator has no DB access to check — that check lives in the handler,
        // mirroring CreateEmployeeValidator/CreateEmployeeHandler's own EmployeeNumber split.
        RuleFor(r => r.AssetNumber).MaximumLength(50);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Manufacturer).MaximumLength(100);
        RuleFor(r => r.Model).MaximumLength(100);
        RuleFor(r => r.SerialNumber).MaximumLength(100);
        RuleFor(r => r.PurchasePrice).GreaterThan(0).When(r => r.PurchasePrice.HasValue);
    }
}
