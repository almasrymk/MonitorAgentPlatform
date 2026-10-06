namespace MonitorCloud.Infrastructure.Seeding;

/// <summary>The fixed demo data of 08: platform users and the eight detailed customers.</summary>
internal static class DemoData
{
    public const string DemoPassword = "Demo@12345";
    public const int Seed = 20261006;

    public static readonly (string Email, string Password, string Role, string Name)[] PlatformUsers =
    [
        ("admin@monitor.local", "Admin@12345", "PlatformAdmin", "Admin User"),
        ("support@monitor.local", "Support@12345", "PlatformSupport", "Support User"),
    ];

    public sealed record LocationSpec(string Name, string Code, string City, string Country, string TimeZone);

    public sealed record CustomerSpec(
        string Name, string Code, string Plan, bool Suspended, int Devices, int RenewsInDays, int SinceMonths, string Country, string City, string TimeZone, LocationSpec[] Locations);

    public static readonly CustomerSpec[] Detailed =
    [
        new("Acme Corporation", "ACME", "ENTERPRISE", false, 316, 101, 33, "Egypt", "Cairo", "Africa/Cairo",
        [
            new("Cairo HQ", "CAIRO-HQ", "Cairo", "Egypt", "Africa/Cairo"),
            new("Alexandria Branch", "ALEX", "Alexandria", "Egypt", "Africa/Cairo"),
            new("Dubai Office", "DUBAI", "Dubai", "UAE", "Asia/Dubai"),
        ]),
        new("Nile Trading Group", "NILE", "BUSINESS", false, 198, 47, 24, "Egypt", "Giza", "Africa/Cairo",
        [
            new("Giza Head Office", "GIZA-HO", "Giza", "Egypt", "Africa/Cairo"),
            new("Port Said Warehouse", "PSAID", "Port Said", "Egypt", "Africa/Cairo"),
            new("Aswan Branch", "ASWAN", "Aswan", "Egypt", "Africa/Cairo"),
        ]),
        new("Delta Logistics", "DELTA", "PROFESSIONAL", false, 142, 65, 18, "Egypt", "Mansoura", "Africa/Cairo",
        [
            new("Mansoura Hub", "MANS-HUB", "Mansoura", "Egypt", "Africa/Cairo"),
            new("Tanta Depot", "TANTA", "Tanta", "Egypt", "Africa/Cairo"),
            new("Damietta Port", "DAMIETTA", "Damietta", "Egypt", "Africa/Cairo"),
            new("Zagazig Depot", "ZAGAZIG", "Zagazig", "Egypt", "Africa/Cairo"),
        ]),
        new("Horizon Retail", "HORIZON", "ENTERPRISE", false, 124, 22, 36, "UAE", "Abu Dhabi", "Asia/Dubai",
        [
            new("Abu Dhabi Store", "AUH", "Abu Dhabi", "UAE", "Asia/Dubai"),
            new("Sharjah Store", "SHJ", "Sharjah", "UAE", "Asia/Dubai"),
            new("Al Ain Store", "AAN", "Al Ain", "UAE", "Asia/Dubai"),
            new("Ajman Store", "AJM", "Ajman", "UAE", "Asia/Dubai"),
            new("Fujairah Store", "FJR", "Fujairah", "UAE", "Asia/Dubai"),
            new("Ras Al Khaimah Store", "RAK", "Ras Al Khaimah", "UAE", "Asia/Dubai"),
        ]),
        new("Sahara Foods", "SAHARA", "BUSINESS", false, 96, 131, 14, "Egypt", "6th of October", "Africa/Cairo",
        [
            new("October Factory", "OCT-FAC", "6th of October", "Egypt", "Africa/Cairo"),
            new("Sadat City Plant", "SADAT", "Sadat City", "Egypt", "Africa/Cairo"),
        ]),
        new("Gulf Engineering", "GULF", "ENTERPRISE", false, 428, 28, 48, "Saudi Arabia", "Riyadh", "Asia/Riyadh",
        [
            new("Riyadh HQ", "RUH-HQ", "Riyadh", "Saudi Arabia", "Asia/Riyadh"),
            new("Jeddah Office", "JED", "Jeddah", "Saudi Arabia", "Asia/Riyadh"),
            new("Dammam Site", "DMM", "Dammam", "Saudi Arabia", "Asia/Riyadh"),
            new("Doha Office", "DOH", "Doha", "Qatar", "Asia/Qatar"),
            new("Kuwait Office", "KWI", "Kuwait City", "Kuwait", "Asia/Kuwait"),
            new("Muscat Office", "MCT", "Muscat", "Oman", "Asia/Muscat"),
            new("Manama Office", "BAH", "Manama", "Bahrain", "Asia/Bahrain"),
            new("Amman Office", "AMM", "Amman", "Jordan", "Asia/Amman"),
        ]),
        new("Pyramid Pharma", "PYRAMID", "PROFESSIONAL", false, 87, 96, 12, "Egypt", "Cairo", "Africa/Cairo",
        [
            new("New Cairo Plant", "NCAI", "New Cairo", "Egypt", "Africa/Cairo"),
            new("Badr City Plant", "BADR", "Badr City", "Egypt", "Africa/Cairo"),
            new("Ismailia Lab", "ISM", "Ismailia", "Egypt", "Africa/Cairo"),
        ]),
        new("Oasis Hospitality", "OASIS", "STARTER", true, 24, -1, 8, "Egypt", "Hurghada", "Africa/Cairo",
        [
            new("Hurghada Resort", "HRG", "Hurghada", "Egypt", "Africa/Cairo"),
        ]),
    ];

    public static readonly (string City, string Country, string TimeZone)[] Cities =
    [
        ("Cairo", "Egypt", "Africa/Cairo"), ("Alexandria", "Egypt", "Africa/Cairo"), ("Giza", "Egypt", "Africa/Cairo"),
        ("Luxor", "Egypt", "Africa/Cairo"), ("Suez", "Egypt", "Africa/Cairo"), ("Dubai", "UAE", "Asia/Dubai"),
        ("Riyadh", "Saudi Arabia", "Asia/Riyadh"), ("Jeddah", "Saudi Arabia", "Asia/Riyadh"), ("Doha", "Qatar", "Asia/Qatar"),
        ("Kuwait City", "Kuwait", "Asia/Kuwait"), ("Muscat", "Oman", "Asia/Muscat"), ("Amman", "Jordan", "Asia/Amman"),
    ];
}
