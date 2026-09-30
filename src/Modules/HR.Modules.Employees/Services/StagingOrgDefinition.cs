namespace HR.Modules.Employees.Services;

internal sealed record StagingDepartmentDefinition(int Number, string Name, string Description);

internal sealed record StagingPositionDefinition(int Number, string Name, string DepartmentName);

internal sealed record StagingEmployeeDefinition(
    int Number,
    string FirstName,
    string LastName,
    string PositionName,
    int? ManagerNumber,
    DateOnly StartDate,
    DateOnly DateOfBirth,
    string Nationality,
    string Gender,
    string Phone,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string? County,
    string PostCode,
    decimal Salary,
    string EmploymentTypeName = StagingOrgDefinition.PermanentEmploymentType);

internal static class StagingOrgDefinition
{
    public const string PermanentEmploymentType = "Permanent";
    public const string ContractorEmploymentType = "Contractor";
    public static readonly IReadOnlyList<string> VacantPositionNames =
        ["Marketing Coordinator", "Software Engineer", "Data Analyst"];
    public const string HomeLocationName = "Home";
    public const string OfficeLocationName = "London Office";
    public const string OfficeLocationTypeName = "Office";
    public const string RemoteLocationTypeName = "Remote";

    public static readonly IReadOnlyList<string> EmploymentTypeNames =
        ["Permanent", "Fixed Term", "Contractor", "Casual", "Apprentice"];

    public static readonly IReadOnlyList<StagingDepartmentDefinition> Departments =
    [
        new(1, "Executive", "Executive leadership"),
        new(2, "Engineering", "Product and platform engineering"),
        new(3, "People & HR", "HR and people operations"),
        new(4, "Finance", "Finance and accounting"),
        new(5, "Recruitment", "Talent acquisition and recruitment"),
        new(6, "Sales", "Sales and account management"),
    ];

    public static readonly IReadOnlyList<StagingPositionDefinition> Positions =
    [
        new(1, "Chief Executive Officer", "Executive"),
        new(2, "Chief Technology Officer", "Engineering"),
        new(3, "Principal Engineer", "Engineering"),
        new(4, "Staff Engineer", "Engineering"),
        new(5, "Senior Backend Engineer", "Engineering"),
        new(6, "Senior Frontend Engineer", "Engineering"),
        new(7, "Backend Engineer", "Engineering"),
        new(8, "Frontend Engineer", "Engineering"),
        new(9, "QA Engineer", "Engineering"),
        new(10, "Software Engineer", "Engineering"),
        new(11, "Chief Financial Officer", "Finance"),
        new(12, "Finance Manager", "Finance"),
        new(13, "Finance Analyst", "Finance"),
        new(14, "Data Analyst", "Finance"),
        new(15, "HR Manager", "People & HR"),
        new(16, "HR Advisor", "People & HR"),
        new(17, "HR Coordinator", "People & HR"),
        new(18, "Head of Talent Acquisition", "Recruitment"),
        new(19, "Senior Recruiter", "Recruitment"),
        new(20, "Recruiter", "Recruitment"),
        new(21, "Recruitment Coordinator", "Recruitment"),
        new(22, "Sales Manager", "Sales"),
        new(23, "Senior Account Executive", "Sales"),
        new(24, "Account Executive", "Sales"),
        new(25, "Sales Development Representative", "Sales"),
        new(26, "Marketing Coordinator", "Sales"),
    ];

    public static readonly IReadOnlyList<StagingEmployeeDefinition> Employees =
    [
        new(1, "Sarah", "Chen", "Chief Executive Officer", null, new(2018, 1, 8), new(1979, 5, 21), "Taiwanese", "Female",
            "07700 900101", "14 Rivington Street", null, "London", "Greater London", "EC2A 3DU", 180000m),
        new(2, "James", "Okafor", "Chief Technology Officer", 1, new(2019, 2, 4), new(1984, 7, 22), "Nigerian", "Male",
            "07700 900102", "27 Coldharbour Lane", "Flat 4", "London", "Greater London", "SE5 9NR", 150000m),
        new(3, "Priya", "Shah", "Chief Financial Officer", 1, new(2019, 3, 1), new(1980, 4, 9), "British", "Female",
            "07700 900103", "22 Chiswick High Road", null, "London", "Greater London", "W4 2DT", 140000m),
        new(4, "Laura", "Bennett", "HR Manager", 1, new(2019, 6, 3), new(1981, 9, 28), "British", "Female",
            "07700 900104", "3 Thornton Avenue", null, "London", "Greater London", "SW2 4HX", 75000m),
        new(5, "Nina", "Patel", "Head of Talent Acquisition", 1, new(2021, 4, 12), new(1987, 9, 3), "British", "Female",
            "07700 900105", "2 Albert Road", null, "London", "Greater London", "N4 3RB", 85000m),
        new(6, "David", "Park", "Sales Manager", 1, new(2018, 8, 22), new(1976, 12, 8), "British", "Male",
            "07700 900106", "44 Harborne Park Road", null, "Birmingham", "West Midlands", "B17 0DH", 80000m),
        new(7, "Priya", "Sharma", "Principal Engineer", 2, new(2020, 9, 1), new(1988, 11, 5), "Indian", "Female",
            "07700 900107", "8 Brick Lane", "Apt 2B", "London", "Greater London", "E1 6RF", 95000m),
        new(8, "Ravi", "Menon", "Staff Engineer", 2, new(2021, 3, 15), new(1986, 2, 17), "Indian", "Male",
            "07700 900108", "31 Whitworth Street", null, "Manchester", "Greater Manchester", "M1 3NR", 92000m),
        new(9, "Tom", "Williams", "Senior Backend Engineer", 7, new(2022, 2, 20), new(1992, 4, 12), "British", "Male",
            "07700 900109", "52 Didsbury Road", null, "Manchester", "Greater Manchester", "M20 5LH", 78000m),
        new(10, "Hannah", "Brooks", "Senior Frontend Engineer", 8, new(2022, 7, 11), new(1993, 8, 30), "British", "Female",
            "07700 900110", "17 Park Row", null, "Bristol", "Bristol", "BS1 5LJ", 76000m),
        new(11, "Leo", "Fischer", "Backend Engineer", 7, new(2023, 5, 8), new(1996, 1, 14), "German", "Male",
            "07700 900111", "9 Kirkgate", "Flat 3", "Leeds", "West Yorkshire", "LS2 7DJ", 55000m),
        new(12, "Amara", "Nwosu", "Frontend Engineer", 8, new(2024, 1, 15), new(1998, 6, 2), "Nigerian", "Female",
            "07700 900112", "64 Peckham Rye", null, "London", "Greater London", "SE15 3UB", 52000m),
        new(13, "Ben", "Hartley", "QA Engineer", 7, new(2024, 6, 3), new(1995, 10, 19), "British", "Male",
            "07700 900113", "5 Queens Road", null, "Reading", "Berkshire", "RG1 4AR", 48000m),
        new(14, "Sophie", "Laurent", "Finance Manager", 3, new(2020, 4, 14), new(1985, 6, 30), "French", "Female",
            "07700 900114", "61 Gloucester Road", null, "London", "Greater London", "SW7 4PE", 72000m),
        new(15, "Daniel", "Reid", "Finance Analyst", 14, new(2023, 9, 4), new(1997, 3, 25), "British", "Male",
            "07700 900115", "12 Leith Walk", null, "Edinburgh", "Midlothian", "EH6 5HB", 42000m),
        new(16, "Marcus", "Diallo", "HR Advisor", 4, new(2022, 11, 7), new(1990, 2, 14), "French", "Male",
            "07700 900116", "19 Seven Sisters Road", "Floor 2", "London", "Greater London", "N4 2BY", 45000m),
        new(17, "Chloe", "Grant", "HR Coordinator", 4, new(2024, 3, 18), new(1994, 12, 3), "British", "Female",
            "07700 900117", "28 Mill Lane", null, "Cambridge", "Cambridgeshire", "CB2 1RX", 43000m),
        new(18, "Olivia", "Reyes", "Senior Recruiter", 5, new(2021, 9, 13), new(1989, 5, 16), "Spanish", "Female",
            "07700 900118", "40 Northumberland Road", null, "Newcastle upon Tyne", "Tyne and Wear", "NE1 8JF", 52000m),
        new(19, "Sam", "Whitfield", "Recruiter", 5, new(2023, 1, 9), new(1995, 7, 27), "British", "Male",
            "07700 900119", "7 Abbey Road", null, "Sheffield", "South Yorkshire", "S7 1FJ", 40000m),
        new(20, "Isla", "Fraser", "Recruitment Coordinator", 18, new(2025, 2, 10), new(2000, 9, 8), "British", "Female",
            "07700 900120", "15 Bath Street", "Flat 2", "Glasgow", "Lanarkshire", "G2 4JR", 32000m),
        new(21, "Emma", "Jones", "Senior Account Executive", 6, new(2023, 5, 2), new(1998, 8, 17), "British", "Female",
            "07700 900121", "11 Cowley Road", "Flat 1", "Oxford", "Oxfordshire", "OX4 1HZ", 40000m),
        new(22, "Carlos", "Rivera", "Account Executive", 6, new(2024, 1, 8), new(1991, 1, 25), "Spanish", "Male",
            "07700 900122", "5 Western Road", null, "Brighton", "East Sussex", "BN1 2DA", 38000m, ContractorEmploymentType),
        new(23, "Zara", "Ahmed", "Sales Development Representative", 6, new(2025, 6, 2), new(1999, 11, 11), "British", "Female",
            "07700 900123", "33 Stokes Croft", null, "Bristol", "Bristol", "BS1 3PR", 36000m),
    ];

    public static string EmployeeNumberFor(int number, string prefix, int minimumLength) =>
        $"{prefix}{number.ToString().PadLeft(minimumLength, '0')}";

    public static string EmailFor(StagingEmployeeDefinition employee, string domain) =>
        $"{employee.FirstName}.{employee.LastName}@{domain}".ToLowerInvariant();
}
