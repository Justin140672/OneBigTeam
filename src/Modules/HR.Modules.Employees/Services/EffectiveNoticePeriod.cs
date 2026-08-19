using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Services;

internal sealed record EffectiveNoticePeriod(NoticePeriodUnit Unit, int Length, NoticePeriodSource Source);
