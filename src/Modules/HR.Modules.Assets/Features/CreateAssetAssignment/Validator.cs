using FluentValidation;

namespace HR.Modules.Assets.Features.CreateAssetAssignment;

internal sealed class CreateAssetAssignmentValidator : AbstractValidator<CreateAssetAssignmentRequest>
{
    public CreateAssetAssignmentValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.AssetId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
        RuleFor(r => r.AssignedBy).NotEmpty();
        RuleFor(r => r.Notes).MaximumLength(2000);
    }
}
