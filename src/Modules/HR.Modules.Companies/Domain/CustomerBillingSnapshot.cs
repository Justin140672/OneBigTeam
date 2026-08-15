namespace HR.Modules.Companies.Domain;

/// <summary>
/// Point-in-time snapshot of how a customer's monthly bill was calculated, computed each time an
/// admin views the Admin Portal billing breakdown for a customer. Accumulates as an append-only
/// history — there is no attempt to backfill history predating this feature.
/// </summary>
internal sealed class CustomerBillingSnapshot
{
    private CustomerBillingSnapshot() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTimeOffset ComputedAt { get; private set; }
    public int ActiveEmployees { get; private set; }
    public int FutureStarters { get; private set; }
    public int Leavers { get; private set; }
    public int ChargeableEmployees { get; private set; }
    public decimal PricePerEmployee { get; private set; }
    public decimal Discounts { get; private set; }
    public decimal MonthlyTotal { get; private set; }

    public static CustomerBillingSnapshot Create(
        Guid companyId,
        DateTimeOffset computedAt,
        int activeEmployees,
        int futureStarters,
        int leavers,
        int chargeableEmployees,
        decimal pricePerEmployee,
        decimal discounts,
        decimal monthlyTotal)
    {
        return new CustomerBillingSnapshot
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            ComputedAt = computedAt,
            ActiveEmployees = activeEmployees,
            FutureStarters = futureStarters,
            Leavers = leavers,
            ChargeableEmployees = chargeableEmployees,
            PricePerEmployee = pricePerEmployee,
            Discounts = discounts,
            MonthlyTotal = monthlyTotal,
        };
    }
}
