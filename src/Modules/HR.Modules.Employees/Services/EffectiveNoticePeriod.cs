using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Services;

internal sealed record EffectiveNoticePeriod(NoticePeriodUnit Unit, int Length, NoticePeriodSource Source);
