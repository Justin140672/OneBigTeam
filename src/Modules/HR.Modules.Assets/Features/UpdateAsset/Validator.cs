using FluentValidation;
using HR.SharedKernel;

namespace HR.Modules.Assets.Features.UpdateAsset;

internal sealed class UpdateAssetValidator : AbstractValidator<UpdateAssetRequest>
{
    public UpdateAssetValidator()
    {
        // Ticket 2 item 3: a loaded concurrency version is mandatory on this protected update.
        RuleFor(r => r.ExpectedVersion).RequireLoadedVersion();

        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.AssetNumber).NotEmpty().MaximumLength(50);
        RuleFor(r => r.CategoryId).NotEmpty();
        RuleFor(r => r.Name).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Manufacturer).MaximumLength(100);
        RuleFor(r => r.Model).MaximumLength(100);
        RuleFor(r => r.SerialNumber).MaximumLength(100);
        RuleFor(r => r.PurchasePrice).GreaterThan(0).When(r => r.PurchasePrice.HasValue);
    }
}
