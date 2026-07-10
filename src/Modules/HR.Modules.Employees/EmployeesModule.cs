using FluentValidation;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.AddMyEmergencyContact;
using HR.Modules.Employees.Features.AssignManager;
using HR.Modules.Employees.Features.GetMyContactDetails;
using HR.Modules.Employees.Features.GetMyEmergencyContacts;
using HR.Modules.Employees.Features.GetEmployeeEmergencyContacts;
using HR.Modules.Employees.Features.GetCurrentCompensation;
using HR.Modules.Employees.Features.GetCompensationHistory;
using HR.Modules.Employees.Features.GetEmployeeAuditHistory;
using HR.Modules.Employees.Features.CreateCompensationRecord;
using HR.Modules.Employees.Features.UpdateFutureCompensationRecord;
using HR.Modules.Employees.Features.DeleteFutureCompensationRecord;
using HR.Modules.Employees.Features.ListNationalities;
using HR.Modules.Employees.Features.RemoveMyEmergencyContact;
using HR.Modules.Employees.Features.UpdateMyContactDetails;
using HR.Modules.Employees.Features.UpdateMyEmergencyContact;
using HR.Modules.Employees.Features.CreateDepartment;
using HR.Modules.Employees.Features.CreateEmployee;
using HR.Modules.Employees.Features.CreatePositionProfile;
using HR.Modules.Employees.Features.DeactivateDepartment;
using HR.Modules.Employees.Features.GetDepartment;
using HR.Modules.Employees.Features.GetEmployee;
using HR.Modules.Employees.Features.GetHeadcountSummary;
using HR.Modules.Employees.Features.GetMyEmployee;
using HR.Modules.Employees.Features.GetNewHiresTrend;
using HR.Modules.Employees.Features.GetMyPersonalDetails;
using HR.Modules.Employees.Features.RequestPersonalDetailsChange;
using HR.Modules.Employees.Features.ListDepartments;
using HR.Modules.Employees.Features.ListEmployees;
using HR.Modules.Employees.Features.SetEmployeeWorkingPattern;
using HR.Modules.Employees.Features.UpdateDepartment;
using HR.Modules.Employees.Features.GetPositionProfile;
using HR.Modules.Employees.Features.ListPositionProfiles;
using HR.Modules.Employees.Features.UpdateEmployeeProfile;
using HR.Modules.Employees.Features.UpdateEmploymentDetails;
using HR.Modules.Employees.Features.AddRequiredDocumentToPositionProfile;
using HR.Modules.Employees.Features.ListRequiredDocumentsForPositionProfile;
using HR.Modules.Employees.Features.RemoveRequiredDocumentFromPositionProfile;
using HR.Modules.Employees.Features.AddRequiredAssetToPositionProfile;
using HR.Modules.Employees.Features.ListRequiredAssetsForPositionProfile;
using HR.Modules.Employees.Features.RemoveRequiredAssetFromPositionProfile;
using HR.Modules.Employees.Features.UpdatePositionProfile;
using HR.Modules.Employees.Features.ListEmploymentTypes;
using HR.Modules.Employees.Features.CreateEmploymentType;
using HR.Modules.Employees.Features.UpdateEmploymentType;
using HR.Modules.Employees.Features.DeactivateEmploymentType;
using HR.Modules.Employees.Features.CreateOnboardingTemplate;
using HR.Modules.Employees.Features.GetOnboardingTemplate;
using HR.Modules.Employees.Features.ListOnboardingTemplates;
using HR.Modules.Employees.Features.UpdateOnboardingTemplate;
using HR.Modules.Employees.Features.DeactivateOnboardingTemplate;
using HR.Modules.Employees.Features.CreateLocationType;
using HR.Modules.Employees.Features.UpdateLocationType;
using HR.Modules.Employees.Features.DeactivateLocationType;
using HR.Modules.Employees.Features.ListLocationTypes;
using HR.Modules.Employees.Features.CreateLocation;
using HR.Modules.Employees.Features.UpdateLocation;
using HR.Modules.Employees.Features.DeactivateLocation;
using HR.Modules.Employees.Features.GetLocation;
using HR.Modules.Employees.Features.ListLocations;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Services;
using HR.SharedKernel;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Employees;

public static class EmployeesModule
{
    public static IServiceCollection AddEmployeesModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<EmployeesDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "employees")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateDepartmentHandler>();
        services.AddScoped<IValidator<CreateDepartmentRequest>, CreateDepartmentValidator>();

        services.AddScoped<UpdateDepartmentHandler>();
        services.AddScoped<IValidator<UpdateDepartmentRequest>, UpdateDepartmentValidator>();

        services.AddScoped<DeactivateDepartmentHandler>();

        services.AddScoped<CreatePositionProfileHandler>();
        services.AddScoped<IValidator<CreatePositionProfileRequest>, CreatePositionProfileValidator>();

        services.AddScoped<GetPositionProfileHandler>();

        services.AddScoped<ListPositionProfilesHandler>();
        services.AddScoped<IValidator<ListPositionProfilesRequest>, ListPositionProfilesValidator>();

        services.AddScoped<UpdatePositionProfileHandler>();
        services.AddScoped<IValidator<UpdatePositionProfileRequest>, UpdatePositionProfileValidator>();

        services.AddScoped<AddRequiredDocumentHandler>();
        services.AddScoped<IValidator<AddRequiredDocumentRequest>, AddRequiredDocumentValidator>();

        services.AddScoped<RemoveRequiredDocumentHandler>();

        services.AddScoped<ListRequiredDocumentsHandler>();

        services.AddScoped<AddRequiredAssetHandler>();
        services.AddScoped<IValidator<AddRequiredAssetRequest>, AddRequiredAssetValidator>();

        services.AddScoped<RemoveRequiredAssetHandler>();

        services.AddScoped<ListRequiredAssetsHandler>();

        services.AddScoped<CreateEmployeeHandler>();
        services.AddScoped<IValidator<CreateEmployeeRequest>, CreateEmployeeValidator>();

        services.AddScoped<GetEmployeeHandler>();
        services.AddScoped<GetMyEmployeeHandler>();
        services.AddScoped<GetMyPersonalDetailsHandler>();
        services.AddScoped<RequestPersonalDetailsChangeHandler>();
        services.AddScoped<IValidator<RequestPersonalDetailsChangeRequest>, RequestPersonalDetailsChangeValidator>();

        services.AddScoped<ListDepartmentsHandler>();
        services.AddScoped<GetDepartmentHandler>();
        services.AddScoped<IValidator<ListDepartmentsRequest>, ListDepartmentsValidator>();

        services.AddScoped<ListEmployeesHandler>();
        services.AddScoped<IValidator<ListEmployeesRequest>, ListEmployeesValidator>();

        services.AddScoped<GetHeadcountSummaryHandler>();
        services.AddScoped<IValidator<GetHeadcountSummaryRequest>, GetHeadcountSummaryValidator>();

        services.AddScoped<GetNewHiresTrendHandler>();
        services.AddScoped<IValidator<GetNewHiresTrendRequest>, GetNewHiresTrendValidator>();

        services.AddScoped<UpdateEmployeeProfileHandler>();
        services.AddScoped<IValidator<UpdateEmployeeProfileRequest>, UpdateEmployeeProfileValidator>();

        services.AddScoped<UpdateEmploymentDetailsHandler>();
        services.AddScoped<IValidator<UpdateEmploymentDetailsRequest>, UpdateEmploymentDetailsValidator>();

        services.AddScoped<AssignManagerHandler>();
        services.AddScoped<IValidator<AssignManagerRequest>, AssignManagerValidator>();

        services.AddScoped<SetEmployeeWorkingPatternHandler>();
        services.AddScoped<IValidator<SetEmployeeWorkingPatternRequest>, SetEmployeeWorkingPatternValidator>();

        services.AddScoped<ListNationalitiesHandler>();

        services.AddScoped<GetMyContactDetailsHandler>();
        services.AddScoped<UpdateMyContactDetailsHandler>();
        services.AddScoped<IValidator<UpdateMyContactDetailsRequest>, UpdateMyContactDetailsValidator>();

        services.AddScoped<GetMyEmergencyContactsHandler>();
        services.AddScoped<AddMyEmergencyContactHandler>();
        services.AddScoped<IValidator<AddMyEmergencyContactRequest>, AddMyEmergencyContactValidator>();
        services.AddScoped<UpdateMyEmergencyContactHandler>();
        services.AddScoped<IValidator<UpdateMyEmergencyContactRequest>, UpdateMyEmergencyContactValidator>();
        services.AddScoped<RemoveMyEmergencyContactHandler>();
        services.AddScoped<GetEmployeeEmergencyContactsHandler>();

        services.AddScoped<GetCurrentCompensationHandler>();
        services.AddScoped<GetCompensationHistoryHandler>();
        services.AddScoped<GetEmployeeAuditHistoryHandler>();
        services.AddScoped<CreateCompensationRecordHandler>();
        services.AddScoped<IValidator<CreateCompensationRecordRequest>, CreateCompensationRecordValidator>();
        services.AddScoped<UpdateFutureCompensationRecordHandler>();
        services.AddScoped<IValidator<UpdateFutureCompensationRecordRequest>, UpdateFutureCompensationRecordValidator>();
        services.AddScoped<DeleteFutureCompensationRecordHandler>();

        services.AddScoped<ListEmploymentTypesHandler>();
        services.AddScoped<IValidator<ListEmploymentTypesRequest>, ListEmploymentTypesValidator>();

        services.AddScoped<CreateEmploymentTypeHandler>();
        services.AddScoped<IValidator<CreateEmploymentTypeRequest>, CreateEmploymentTypeValidator>();

        services.AddScoped<UpdateEmploymentTypeHandler>();
        services.AddScoped<IValidator<UpdateEmploymentTypeRequest>, UpdateEmploymentTypeValidator>();

        services.AddScoped<DeactivateEmploymentTypeHandler>();
        services.AddScoped<IValidator<DeactivateEmploymentTypeRequest>, DeactivateEmploymentTypeValidator>();

        services.AddScoped<CreateOnboardingTemplateHandler>();
        services.AddScoped<IValidator<CreateOnboardingTemplateRequest>, CreateOnboardingTemplateValidator>();

        services.AddScoped<GetOnboardingTemplateHandler>();

        services.AddScoped<ListOnboardingTemplatesHandler>();
        services.AddScoped<IValidator<ListOnboardingTemplatesRequest>, ListOnboardingTemplatesValidator>();

        services.AddScoped<UpdateOnboardingTemplateHandler>();
        services.AddScoped<IValidator<UpdateOnboardingTemplateRequest>, UpdateOnboardingTemplateValidator>();

        services.AddScoped<DeactivateOnboardingTemplateHandler>();

        services.AddScoped<CreateLocationTypeHandler>();
        services.AddScoped<IValidator<CreateLocationTypeRequest>, CreateLocationTypeValidator>();

        services.AddScoped<UpdateLocationTypeHandler>();
        services.AddScoped<IValidator<UpdateLocationTypeRequest>, UpdateLocationTypeValidator>();

        services.AddScoped<DeactivateLocationTypeHandler>();
        services.AddScoped<IValidator<DeactivateLocationTypeRequest>, DeactivateLocationTypeValidator>();

        services.AddScoped<ListLocationTypesHandler>();
        services.AddScoped<IValidator<ListLocationTypesRequest>, ListLocationTypesValidator>();

        services.AddScoped<CreateLocationHandler>();
        services.AddScoped<IValidator<CreateLocationRequest>, CreateLocationValidator>();

        services.AddScoped<UpdateLocationHandler>();
        services.AddScoped<IValidator<UpdateLocationRequest>, UpdateLocationValidator>();

        services.AddScoped<DeactivateLocationHandler>();
        services.AddScoped<IValidator<DeactivateLocationRequest>, DeactivateLocationValidator>();

        services.AddScoped<GetLocationHandler>();
        services.AddScoped<IValidator<GetLocationRequest>, GetLocationValidator>();

        services.AddScoped<ListLocationsHandler>();
        services.AddScoped<IValidator<ListLocationsRequest>, ListLocationsValidator>();

        services.AddScoped<IProbationDateResolver, ProbationDateResolver>();
        services.AddScoped<IWorkingPatternProvider, WorkingPatternProvider>();
        services.AddScoped<IDirectReportsReader, DirectReportsReader>();
        services.AddScoped<IEmployeeNameReader, EmployeeNameReader>();
        services.AddScoped<IManagerReader, ManagerReader>();
        services.AddScoped<IPositionProfileDocumentsReader, PositionProfileDocumentsReader>();
        services.AddScoped<IPositionProfileAssetsReader, PositionProfileAssetsReader>();
        services.AddScoped<IOnboardingTemplateReader, OnboardingTemplateReader>();
        services.AddScoped<IEmployeeProvisioningService, EmployeeProvisioningService>();
        services.AddScoped<IEmployeeImportLookupReader, EmployeeImportLookupReader>();
        services.AddScoped<IImportLookupResolver, ImportLookupResolver>();
        services.AddScoped<IEmployeeImportWriter, EmployeeImportWriter>();
    }

    public static async Task MigrateEmployeesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS employees");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedEmployeesAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var now = DateTimeOffset.UtcNow;

        // ── Nationalities (global reference data) ─────────────────────────────
        if (!await db.Nationalities.AnyAsync())
        {
            string[] names =
            [
                "Afghan", "Albanian", "Algerian", "American", "Argentine", "Armenian",
                "Australian", "Austrian", "Azerbaijani", "Bangladeshi", "Belgian",
                "Bolivian", "Brazilian", "British", "Bulgarian", "Cambodian", "Canadian",
                "Chilean", "Chinese", "Colombian", "Croatian", "Czech", "Danish", "Dutch",
                "Egyptian", "Ethiopian", "Filipino", "Finnish", "French", "Georgian",
                "German", "Ghanaian", "Greek", "Hungarian", "Indian", "Indonesian",
                "Iranian", "Iraqi", "Irish", "Israeli", "Italian", "Jamaican", "Japanese",
                "Jordanian", "Kenyan", "Korean", "Lebanese", "Malaysian", "Mexican",
                "Moroccan", "Nepalese", "New Zealander", "Nigerian", "Norwegian",
                "Pakistani", "Peruvian", "Polish", "Portuguese", "Romanian", "Russian",
                "Saudi Arabian", "Serbian", "Singaporean", "Somali", "South African",
                "Spanish", "Sri Lankan", "Swedish", "Swiss", "Syrian", "Taiwanese",
                "Thai", "Turkish", "Ugandan", "Ukrainian", "Vietnamese", "Zimbabwean"
            ];

            for (var i = 0; i < names.Length; i++)
                db.Nationalities.Add(Nationality.Create(i + 1, names[i]));

            await db.SaveChangesAsync();
        }

        // ── Acme Corporation ─────────────────────────────────────────────────
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (!await db.Employees.AnyAsync(e => e.CompanyId == acmeId))
        {
            // Seed employment types for Acme
            var etPermId      = Guid.Parse("40000000-0000-0000-0000-000000000001");
            var etFixedTermId = Guid.Parse("40000000-0000-0000-0000-000000000002");
            var etContractId  = Guid.Parse("40000000-0000-0000-0000-000000000003");
            var etCasualId    = Guid.Parse("40000000-0000-0000-0000-000000000004");
            var etApprentId   = Guid.Parse("40000000-0000-0000-0000-000000000005");

            db.EmploymentTypes.AddRange(
                EmploymentType.Create(etPermId,      acmeId, "Permanent",   null, now),
                EmploymentType.Create(etFixedTermId, acmeId, "Fixed Term",  null, now),
                EmploymentType.Create(etContractId,  acmeId, "Contractor",  null, now),
                EmploymentType.Create(etCasualId,    acmeId, "Casual",      null, now),
                EmploymentType.Create(etApprentId,   acmeId, "Apprentice",  null, now));

            var deptEngId      = Guid.Parse("10000000-0000-0000-0000-000000000001");
            var deptHrId       = Guid.Parse("10000000-0000-0000-0000-000000000002");
            var deptFinanceId  = Guid.Parse("10000000-0000-0000-0000-000000000003");
            var deptSalesId    = Guid.Parse("10000000-0000-0000-0000-000000000004");

            db.Departments.AddRange(
                Department.Create(deptEngId,     acmeId, "Engineering", "Product and platform engineering", now),
                Department.Create(deptHrId,      acmeId, "People & HR",  "HR and people operations",       now),
                Department.Create(deptFinanceId, acmeId, "Finance",       "Finance and accounting",         now),
                Department.Create(deptSalesId,   acmeId, "Sales",         "Sales and account management",   now));

            // A single seeded office location, referenced by one position profile below so
            // the "position profile defaults" cascade (Department + Location) has real data
            // to demonstrate in the UI and in E2E coverage.
            var locTypeOfficeId = Guid.Parse("60000000-0000-0000-0000-000000000001");
            var locLondonId     = Guid.Parse("70000000-0000-0000-0000-000000000001");

            db.LocationTypes.Add(LocationType.Create(locTypeOfficeId, acmeId, "Office", null, now));
            db.Locations.Add(Location.Create(locLondonId, acmeId, locTypeOfficeId, "London Office", null, now));

            var posCtoId        = Guid.Parse("20000000-0000-0000-0000-000000000001");
            var posSenDevId     = Guid.Parse("20000000-0000-0000-0000-000000000002");
            var posDevId        = Guid.Parse("20000000-0000-0000-0000-000000000003");
            var posHrMgrId      = Guid.Parse("20000000-0000-0000-0000-000000000004");
            var posHrAdvisorId  = Guid.Parse("20000000-0000-0000-0000-000000000005");
            var posFinanceMgrId = Guid.Parse("20000000-0000-0000-0000-000000000006");
            var posSalesMgrId   = Guid.Parse("20000000-0000-0000-0000-000000000007");
            var posAeId         = Guid.Parse("20000000-0000-0000-0000-000000000008");

            db.PositionProfiles.AddRange(
                PositionProfile.Create(posCtoId,        acmeId, deptEngId,     null, "Chief Technology Officer", null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posSenDevId,     acmeId, deptEngId,     locLondonId, "Senior Software Engineer", null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posDevId,        acmeId, deptEngId,     null, "Software Engineer",        null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posHrMgrId,      acmeId, deptHrId,      null, "HR Manager",               null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posHrAdvisorId,  acmeId, deptHrId,      null, "HR Advisor",               null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posFinanceMgrId, acmeId, deptFinanceId, null, "Finance Manager",          null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posSalesMgrId,   acmeId, deptSalesId,   null, "Sales Manager",            null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(posAeId,         acmeId, deptSalesId,   null, "Account Executive",        null, null, null, null, null, null, null, null, now));

            var empCtoId      = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var empSenDev1Id  = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var empSenDev2Id  = Guid.Parse("30000000-0000-0000-0000-000000000003");
            var empDev1Id     = Guid.Parse("30000000-0000-0000-0000-000000000004");
            var empHrMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000005");
            var empHrAdvId    = Guid.Parse("30000000-0000-0000-0000-000000000006");
            var empFinMgrId   = Guid.Parse("30000000-0000-0000-0000-000000000007");
            var empSalesMgrId = Guid.Parse("30000000-0000-0000-0000-000000000008");
            var empAe1Id      = Guid.Parse("30000000-0000-0000-0000-000000000009");
            var empAe2Id      = Guid.Parse("30000000-0000-0000-0000-000000000010");

            Employee MakeAcme(Guid id, string first, string last, string email, DateOnly start,
                              Guid? deptId, Guid? posId, Guid? managerId,
                              DateOnly dob, string nationality, string gender,
                              string? personalEmail, string? phone,
                              string addr1, string? addr2, string city, string? county, string postCode,
                              string employeeNumber, Guid employmentTypeId,
                              string? preferredName = null)
            {
                var e = Employee.Create(id, acmeId, first, last, email, start, hasSystemAccess: true, now);
                e.Assign(deptId, posId, null, managerId, now);
                e.UpdatePersonalDetails(preferredName ?? first, dob, nationality, gender, null, now);
                e.UpdateContactDetails(personalEmail, phone, null, addr1, addr2, city, county, postCode, "United Kingdom", now);
                e.UpdateEmploymentDetails(employeeNumber, employmentTypeId, start, null, null, null, null, now);
                e.Activate(now);
                return e;
            }

            db.Employees.AddRange(
                MakeAcme(empCtoId,      "Sarah",  "Chen",     "sarah.chen@acme.example",     new DateOnly(2020, 1, 6),  deptEngId,     posCtoId,        null,         new DateOnly(1982, 3, 15),  "Taiwanese", "Female", "sarah.chen@gmail.com",       "07700 900001", "14 Rivington Street",  null,       "London",      "Greater London",     "EC2A 3DU",  "ACME-001", etPermId),
                MakeAcme(empSenDev1Id,  "James",  "Okafor",   "james.okafor@acme.example",   new DateOnly(2021, 3, 15), deptEngId,     posSenDevId,     empCtoId,     new DateOnly(1988, 7, 22),  "Nigerian",  "Male",   "james.okafor@outlook.com",   "07700 900002", "27 Coldharbour Lane",  "Flat 4",   "London",      "Greater London",     "SE5 9NR",   "ACME-002", etPermId),
                MakeAcme(empSenDev2Id,  "Priya",  "Sharma",   "priya.sharma@acme.example",   new DateOnly(2021, 9, 1),  deptEngId,     posSenDevId,     empCtoId,     new DateOnly(1990, 11, 5),  "Indian",    "Female", "priya.sharma@gmail.com",     "07700 900003", "8 Brick Lane",         "Apt 2B",   "London",      "Greater London",     "E1 6RF",    "ACME-003", etPermId),
                MakeAcme(empDev1Id,     "Tom",    "Williams", "tom.williams@acme.example",   new DateOnly(2023, 2, 20), deptEngId,     posDevId,        empSenDev1Id, new DateOnly(1996, 4, 12),  "British",   "Male",   "tom.williams@hotmail.com",   "07700 900004", "52 Didsbury Road",     null,       "Manchester",  "Greater Manchester", "M20 5LH",   "ACME-004", etFixedTermId),
                MakeAcme(empHrMgrId,    "Laura",  "Bennett",  "laura.bennett@acme.example",  new DateOnly(2019, 6, 3),  deptHrId,      posHrMgrId,      null,         new DateOnly(1979, 9, 28),  "British",   "Female", "laura.bennett@gmail.com",    "07700 900005", "3 Thornton Avenue",    null,       "London",      "Greater London",     "SW2 4HX",   "ACME-005", etPermId),
                MakeAcme(empHrAdvId,    "Marcus", "Diallo",   "marcus.diallo@acme.example",  new DateOnly(2022, 11, 7), deptHrId,      posHrAdvisorId,  empHrMgrId,   new DateOnly(1991, 2, 14),  "French",    "Male",   "marcus.diallo@gmail.com",    "07700 900006", "19 Seven Sisters Road","Floor 2",  "London",      "Greater London",     "N4 2BY",    "ACME-006", etPermId),
                MakeAcme(empFinMgrId,   "Sophie", "Laurent",  "sophie.laurent@acme.example", new DateOnly(2020, 4, 14), deptFinanceId, posFinanceMgrId, null,         new DateOnly(1985, 6, 30),  "French",    "Female", "sophie.laurent@gmail.com",   "07700 900007", "61 Gloucester Road",   null,       "London",      "Greater London",     "SW7 4PE",   "ACME-007", etPermId),
                MakeAcme(empSalesMgrId, "David",  "Park",     "david.park@acme.example",     new DateOnly(2018, 8, 22), deptSalesId,   posSalesMgrId,   null,         new DateOnly(1975, 12, 8),  "Korean",    "Male",   "david.park@outlook.com",     "07700 900008", "44 Harborne Park Road",null,       "Birmingham",  "West Midlands",      "B17 0DH",   "ACME-008", etPermId),
                MakeAcme(empAe1Id,      "Emma",   "Jones",    "emma.jones@acme.example",     new DateOnly(2023, 5, 2),  deptSalesId,   posAeId,         empSalesMgrId, new DateOnly(1998, 8, 17), "British",   "Female", "emma.jones@gmail.com",       "07700 900009", "11 Cowley Road",       "Flat 1",   "Oxford",      "Oxfordshire",        "OX4 1HZ",   "ACME-009", etPermId),
                MakeAcme(empAe2Id,      "Carlos", "Rivera",   "carlos.rivera@acme.example",  new DateOnly(2024, 1, 8),  deptSalesId,   posAeId,         empSalesMgrId, new DateOnly(2000, 1, 25), "Spanish",   "Male",   "carlos.rivera@gmail.com",    "07700 900010", "5 Western Road",       null,       "Brighton",    "East Sussex",        "BN1 2DA",   "ACME-010", etContractId));

            await db.SaveChangesAsync();

            var ctoStartingSalary = Compensation.Create(
                Guid.Parse("50000000-0000-0000-0000-000000000002"), acmeId, empCtoId,
                new DateOnly(2020, 1, 6), SalaryType.Annual, 120000m, "GBP", 37.5m, 1m,
                "Starting salary", now);
            ctoStartingSalary.Close(new DateOnly(2022, 12, 31), now);

            var ctoCurrentSalary = Compensation.Create(
                Guid.Parse("50000000-0000-0000-0000-000000000001"), acmeId, empCtoId,
                new DateOnly(2023, 1, 1), SalaryType.Annual, 145000m, "GBP", 37.5m, 1m,
                "Promoted to CTO", now);

            db.Compensations.AddRange(ctoStartingSalary, ctoCurrentSalary);

            await db.SaveChangesAsync();
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        if (!await db.Employees.AnyAsync(e => e.CompanyId == betaCorpId))
        {
            var betaDeptEngId  = Guid.Parse("10000000-0000-0000-0000-000000000011");
            var betaPosEngMgrId = Guid.Parse("20000000-0000-0000-0000-000000000011");
            var betaPosDevId    = Guid.Parse("20000000-0000-0000-0000-000000000012");
            var betaEmpMgrId    = Guid.Parse("30000000-0000-0000-0000-000000000011");
            var betaEmpDevId    = Guid.Parse("30000000-0000-0000-0000-000000000012");

            var betaEtPermId = Guid.Parse("40000000-0000-0000-0000-000000000011");
            db.EmploymentTypes.Add(EmploymentType.Create(betaEtPermId, betaCorpId, "Permanent", null, now));

            db.Departments.Add(
                Department.Create(betaDeptEngId, betaCorpId, "Engineering", "Software engineering", now));

            db.PositionProfiles.AddRange(
                PositionProfile.Create(betaPosEngMgrId, betaCorpId, betaDeptEngId, null, "Engineering Manager", null, null, null, null, null, null, null, null, now),
                PositionProfile.Create(betaPosDevId,    betaCorpId, betaDeptEngId, null, "Software Developer",  null, null, null, null, null, null, null, null, now));

            Employee MakeBeta(Guid id, string first, string last, string email, DateOnly start,
                              Guid? posId, Guid? managerId, DateOnly dob, string nationality, string gender,
                              string? personalEmail, string? phone,
                              string addr1, string? addr2, string city, string? county, string postCode,
                              string employeeNumber, Guid employmentTypeId)
            {
                var e = Employee.Create(id, betaCorpId, first, last, email, start, hasSystemAccess: true, now);
                e.Assign(betaDeptEngId, posId, null, managerId, now);
                e.UpdatePersonalDetails(first, dob, nationality, gender, null, now);
                e.UpdateContactDetails(personalEmail, phone, null, addr1, addr2, city, county, postCode, "United Kingdom", now);
                e.UpdateEmploymentDetails(employeeNumber, employmentTypeId, start, null, null, null, null, now);
                e.Activate(now);
                return e;
            }

            db.Employees.AddRange(
                MakeBeta(betaEmpMgrId, "Alice", "Morgan", "alice.morgan@betacorp.example", new DateOnly(2022, 3, 1), betaPosEngMgrId, null,         new DateOnly(1987, 5, 20),  "British", "Female", "alice.morgan@gmail.com", "07700 900021", "33 Headingley Lane", null,     "Leeds", "West Yorkshire", "LS6 1BL", "BETA-001", betaEtPermId),
                MakeBeta(betaEmpDevId, "Bob",   "Taylor", "bob.taylor@betacorp.example",   new DateOnly(2023, 9, 4), betaPosDevId,    betaEmpMgrId, new DateOnly(1993, 10, 11), "British", "Male",   "bob.taylor@hotmail.com", "07700 900022", "7 Kirkstall Road",   "Flat 2", "Leeds", "West Yorkshire", "LS3 1LH", "BETA-002", betaEtPermId));

            await db.SaveChangesAsync();
        }
    }
}
