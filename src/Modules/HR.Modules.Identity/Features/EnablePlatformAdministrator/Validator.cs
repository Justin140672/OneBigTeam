using FluentValidation;

namespace HR.Modules.Identity.Features.EnablePlatformAdministrator;

internal sealed class EnablePlatformAdministratorValidator : AbstractValidator<EnablePlatformAdministratorRequest>
{
    public EnablePlatformAdministratorValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
