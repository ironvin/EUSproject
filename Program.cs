using System;
using System.IO;
using Core.Models;
using Core.Services;
using Infra;

// Ensure database schema exists
DatabaseManager.EnsureSchema();

// Ensure Administrator user exists and has a known password
using (var con = DatabaseManager.GetConnection())
{
    con.Open();
    var hash = DatabaseManager.SimpleHash("SuperCoolBuilding123");

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"
INSERT INTO Users (Username, PasswordHash, Role)
VALUES (@u, @p, @r)
ON CONFLICT(Username) DO UPDATE 
    SET PasswordHash = excluded.PasswordHash,
        Role = excluded.Role;";
    cmd.Parameters.AddWithValue("@u", "Administrator"); // Log in name
    cmd.Parameters.AddWithValue("@p", hash);
    cmd.Parameters.AddWithValue("@r", 0); // 0 = Admin
    cmd.ExecuteNonQuery();


    // Building Manager user
    var mgrHash = DatabaseManager.SimpleHash("ManagerPassword123");

    using (var cmd2 = con.CreateCommand())
    {
        cmd2.CommandText = @"
    INSERT INTO Users (Username, PasswordHash, Role)
    VALUES (@u, @p, @r)
    ON CONFLICT(Username) DO UPDATE 
    SET PasswordHash = excluded.PasswordHash,
        Role = excluded.Role;";
        cmd2.Parameters.AddWithValue("@u", "Manager"); // Log in name
        cmd2.Parameters.AddWithValue("@p", mgrHash);
        cmd2.Parameters.AddWithValue("@r", 1); // 1 = BuildingManager
        cmd2.ExecuteNonQuery();
    }
}
// --- Login ---
var userSvc = new UserService();
Console.WriteLine("=== Advanced Energy Usage Management System ===");
Console.Write("Username: ");
var u = Console.ReadLine() ?? "";
Console.Write("Password: ");
var p = ReadPassword();

var user = userSvc.Authenticate(u, p);
if (user is null)
{
    Console.WriteLine("\nInvalid credentials.");
    return;
}

bool isAdmin = user.Role == Role.Admin;

Console.WriteLine($"\nWelcome, {user.Username} ({user.Role}).");
Console.WriteLine();

// Core services
var csv = new CsvService();
var buildingSvc = new BuildingService();
var usageSvc = new UsageService();
var reportSvc = new ReportService();
var alertSvc = new AlertService();

// Hard-coded demo period to match your sample data
const int DemoYear = 2025;
const int DemoMonth = 10;

// --- Auto import sample data from /Data ---
AutoImportSampleData(csv);

// --- Main menu loop (professor-friendly) ---
while (true)
{
    Console.WriteLine();
    Console.WriteLine("1) View building overview");
    Console.WriteLine("2) View monthly summary for all buildings");
    Console.WriteLine("3) View per-unit usage & thresholds");
    Console.WriteLine("4) View alerts & export to CSV");
    if (isAdmin)
    {
        Console.WriteLine("5) Demo: simulate over-usage for a unit");
        Console.WriteLine("6) Export building and unit data to CSV");
    }
    Console.WriteLine("7) Export usage over time for charting");
    Console.WriteLine("8) Show usage bar chart (console)");
    Console.WriteLine("0) Exit");
    Console.Write("Choice: ");
    var choice = Console.ReadLine();

    try
    {
        switch (choice)
        {
            case "1":
                ShowBuildingOverview();
                break;

            case "2":
                ShowMonthlySummaryForAllBuildings(reportSvc, DemoYear, DemoMonth);
                break;

            case "3":
                ShowPerUnitUsage(DemoYear, DemoMonth);
                break;

            case "4":
                ShowAlertsAndExport(alertSvc, DemoYear, DemoMonth, csv);
                break;

            case "5":
                if (!isAdmin) { Console.WriteLine("Admin-only option."); break; }
                DemoSimulateOverUsage(usageSvc, DemoYear, DemoMonth);
                break;

            case "6":
                if (!isAdmin) { Console.WriteLine("Admin-only option."); break; }
                ExportBuildingAndUnitData();
                break;

            case "7":
                ExportUsageForCharting();
                break;

            case "8":
                ShowUsageBarChart(DemoYear, DemoMonth);
                break;

            case "0":
                return;

            default:
                Console.WriteLine("Invalid choice.");
                break;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}


// ======================
// Helper methods
// ======================

static void AutoImportSampleData(CsvService csv)
{
    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    string dataDir = Path.Combine(baseDir, "Data");

    string buildingsPath = Path.Combine(dataDir, "Buildings.csv");
    string unitsPath = Path.Combine(dataDir, "Units.csv");
    string usagePath = Path.Combine(dataDir, "Usage.csv");

    Console.WriteLine($"Loading sample data from: {dataDir}");

    try
    {
        int bCount = csv.ImportBuildings(buildingsPath);
        int uCount = csv.ImportUnits(unitsPath);
        int usCount = csv.ImportUsage(usagePath);

        Console.WriteLine($"Imported {bCount} building(s), {uCount} unit(s), {usCount} usage record(s).");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Warning: Could not import sample data automatically.");
        Console.WriteLine(ex.Message);
    }

    Console.WriteLine();
}

static void ShowBuildingOverview()
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"
SELECT b.Id, b.Name, b.EnergyRatePerKwh,
       COUNT(u.Id) AS UnitCount
FROM Buildings b
LEFT JOIN Units u ON u.BuildingId = b.Id
GROUP BY b.Id, b.Name, b.EnergyRatePerKwh
ORDER BY b.Id;";

    using var r = cmd.ExecuteReader();

    Console.WriteLine();
    Console.WriteLine("=== Building Overview ===");

    if (!r.HasRows)
    {
        Console.WriteLine("No buildings found. (Did sample data import?)");
        return;
    }

    while (r.Read())
    {
        var id = r.GetInt32(0);
        var name = r.GetString(1);
        var rate = r.GetDecimal(2);
        var units = r.GetInt32(3);

        Console.WriteLine($"[{id}] {name}");
        Console.WriteLine($"   Rate: {rate:C} per kWh");
        Console.WriteLine($"   Units: {units}");
    }
}

static void ShowMonthlySummaryForAllBuildings(ReportService reportSvc, int year, int month)
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    using var bCmd = con.CreateCommand();
    bCmd.CommandText = "SELECT Id, Name FROM Buildings ORDER BY Id;";

    using var r = bCmd.ExecuteReader();

    Console.WriteLine();
    Console.WriteLine($"=== Monthly Summary for All Buildings ({year}-{month:D2}) ===");

    if (!r.HasRows)
    {
        Console.WriteLine("No buildings found.");
        return;
    }

    while (r.Read())
    {
        var id = r.GetInt32(0);
        var name = r.GetString(1);

        var (totalKwh, totalCost) = reportSvc.BuildingMonthlyTotals(id, year, month);

        Console.WriteLine($"[{id}] {name}");
        Console.WriteLine($"   Total kWh: {totalKwh}");
        Console.WriteLine($"   Total Cost: {totalCost:C}");
    }
}

static void ShowPerUnitUsage(int year, int month)
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    using var bCmd = con.CreateCommand();
    bCmd.CommandText = "SELECT Id, Name FROM Buildings ORDER BY Id;";
    using var br = bCmd.ExecuteReader();

    if (!br.HasRows)
    {
        Console.WriteLine("No buildings found.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"=== Per-Unit Usage ({year}-{month:D2}) ===");

    while (br.Read())
    {
        var buildingId = br.GetInt32(0);
        var buildingName = br.GetString(1);

        Console.WriteLine();
        Console.WriteLine($"Building [{buildingId}] {buildingName}");

        using var uCmd = con.CreateCommand();
        uCmd.CommandText = @"
SELECT u.Id, u.UnitNumber,
       IFNULL(e.Kwh, 0) AS Kwh,
       IFNULL(u.MonthlyUsageThresholdKwh, 0) AS Threshold
FROM Units u
LEFT JOIN EnergyUsage e 
  ON e.UnitId = u.Id AND e.Year = @y AND e.Month = @m
WHERE u.BuildingId = @b
ORDER BY u.UnitNumber;";
        uCmd.Parameters.AddWithValue("@b", buildingId);
        uCmd.Parameters.AddWithValue("@y", year);
        uCmd.Parameters.AddWithValue("@m", month);

        using var ur = uCmd.ExecuteReader();
        if (!ur.HasRows)
        {
            Console.WriteLine("   No units found.");
            continue;
        }

        while (ur.Read())
        {
            var unitId = ur.GetInt32(0);
            var unitNumber = ur.GetString(1);
            var kwh = ur.GetDecimal(2);
            var threshold = ur.GetDecimal(3);

            string status = threshold > 0 && kwh > threshold ? "EXCEEDS THRESHOLD" : "OK";

            Console.WriteLine($"   Unit {unitNumber} (Id={unitId}): {kwh} kWh  " +
                              (threshold > 0 ? $"(Threshold {threshold}) " : "") +
                              $"- {status}");
        }
    }
}

static void ShowAlertsAndExport(AlertService alertSvc, int year, int month, CsvService csv)
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    using var bCmd = con.CreateCommand();
    bCmd.CommandText = "SELECT Id, Name FROM Buildings ORDER BY Id;";

    using var br = bCmd.ExecuteReader();

    Console.WriteLine();
    Console.WriteLine($"=== Alerts ({year}-{month:D2}) ===");

    var allAlerts = new System.Collections.Generic.List<AlertRecord>();

    while (br.Read())
    {
        var buildingId = br.GetInt32(0);
        var buildingName = br.GetString(1);

        var alerts = alertSvc.CheckMonthlyThresholds(buildingId, year, month);
        if (alerts.Count == 0) continue;

        Console.WriteLine();
        Console.WriteLine($"Building [{buildingId}] {buildingName}:");

        foreach (var a in alerts)
        {
            Console.WriteLine($"   UnitId {a.UnitId}: {a.Kwh} kWh > Threshold {a.Threshold}");
            allAlerts.Add(a);
        }
    }

    if (allAlerts.Count == 0)
    {
        Console.WriteLine("No alerts found. All units are within thresholds.");
        return;
    }

    // Auto-export alerts to a CSV in the current directory
    string fileName = $"alerts_{year}_{month:D2}.csv";
    string fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
    csv.ExportAlerts(fullPath, allAlerts);

    Console.WriteLine();
    Console.WriteLine($"Alerts exported to: {fullPath}");
}

static void DemoSimulateOverUsage(UsageService usageSvc, int year, int month)
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    // Pick the first unit that has a non-null threshold
    using var cmd = con.CreateCommand();
    cmd.CommandText = @"
SELECT u.Id, u.UnitNumber, u.MonthlyUsageThresholdKwh, b.Name
FROM Units u
JOIN Buildings b ON b.Id = u.BuildingId
WHERE u.MonthlyUsageThresholdKwh IS NOT NULL
ORDER BY u.Id
LIMIT 1;";
    using var r = cmd.ExecuteReader();

    if (!r.Read())
    {
        Console.WriteLine("No units with thresholds found. Cannot simulate over-usage.");
        return;
    }

    var unitId = r.GetInt32(0);
    var unitNumber = r.GetString(1);
    var threshold = r.GetDecimal(2);
    var buildingName = r.GetString(3);

    // Set kWh to threshold + 100 for the demo period
    var newKwh = threshold + 100m;

    usageSvc.Record(unitId, year, month, newKwh);

    Console.WriteLine();
    Console.WriteLine("=== Demo: Simulated Over-Usage ===");
    Console.WriteLine($"Building: {buildingName}");
    Console.WriteLine($"Unit: {unitNumber} (Id={unitId})");
    Console.WriteLine($"Threshold: {threshold} kWh");
    Console.WriteLine($"New recorded usage for {year}-{month:D2}: {newKwh} kWh (EXCEEDS THRESHOLD)");
    Console.WriteLine("Now choose '4) View alerts & export to CSV' to see the alert.");
}
static void ExportBuildingAndUnitData()
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    string baseDir = AppDomain.CurrentDomain.BaseDirectory;

    // Export Buildings
    string buildingsOut = Path.Combine(baseDir, "buildings_export.csv");
    using (var bCmd = con.CreateCommand())
    {
        bCmd.CommandText = "SELECT Id, Name, EnergyRatePerKwh FROM Buildings ORDER BY Id;";
        using var r = bCmd.ExecuteReader();
        using var w = new StreamWriter(buildingsOut);
        w.WriteLine("BuildingId,Name,RatePerKwh");
        while (r.Read())
        {
            w.WriteLine($"{r.GetInt32(0)},{r.GetString(1)},{r.GetDecimal(2)}");
        }
    }

    // Export Units
    string unitsOut = Path.Combine(baseDir, "units_export.csv");
    using (var uCmd = con.CreateCommand())
    {
        uCmd.CommandText = @"
        SELECT u.Id, b.Name AS BuildingName, u.UnitNumber, 
           IFNULL(u.MonthlyUsageThresholdKwh,0)
        FROM Units u
        JOIN Buildings b ON b.Id = u.BuildingId
        ORDER BY b.Name, u.UnitNumber;";
        using var r = uCmd.ExecuteReader();
        using var w = new StreamWriter(unitsOut);
        w.WriteLine("UnitId,BuildingName,UnitNumber,ThresholdKwh");
        while (r.Read())
        {
            w.WriteLine($"{r.GetInt32(0)},{r.GetString(1)},{r.GetString(2)},{r.GetDecimal(3)}");
        }
    }

    Console.WriteLine();
    Console.WriteLine("Exported buildings to: " + buildingsOut);
    Console.WriteLine("Exported units to: " + unitsOut);
}

static void ExportUsageForCharting()
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
    string outPath = Path.Combine(baseDir, "usage_for_charts.csv");

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"
SELECT b.Name AS BuildingName,
       e.Year,
       e.Month,
       SUM(e.Kwh) AS TotalKwh
FROM EnergyUsage e
JOIN Units u ON u.Id = e.UnitId
JOIN Buildings b ON b.Id = u.BuildingId
GROUP BY b.Name, e.Year, e.Month
ORDER BY b.Name, e.Year, e.Month;";

    using var r = cmd.ExecuteReader();
    using var w = new StreamWriter(outPath);

    w.WriteLine("BuildingName,Year,Month,TotalKwh");
    while (r.Read())
    {
        string buildingName = r.GetString(0);
        int year = r.GetInt32(1);
        int month = r.GetInt32(2);
        decimal kwh = r.GetDecimal(3);
        w.WriteLine($"{buildingName},{year},{month},{kwh}");
    }

    Console.WriteLine();
    Console.WriteLine("Exported usage for charting to: " + outPath);
    Console.WriteLine("You can open this CSV in Excel/Sheets and create line charts.");
}

static void ShowUsageBarChart(int year, int month)
{
    using var con = DatabaseManager.GetConnection();
    con.Open();

    using var cmd = con.CreateCommand();
    cmd.CommandText = @"
SELECT b.Name AS BuildingName,
       IFNULL(SUM(e.Kwh), 0) AS TotalKwh
FROM Buildings b
LEFT JOIN Units u ON u.BuildingId = b.Id
LEFT JOIN EnergyUsage e 
  ON e.UnitId = u.Id AND e.Year = @y AND e.Month = @m
GROUP BY b.Name
ORDER BY b.Name;";
    cmd.Parameters.AddWithValue("@y", year);
    cmd.Parameters.AddWithValue("@m", month);

    using var r = cmd.ExecuteReader();

    Console.WriteLine();
    Console.WriteLine($"=== Usage Bar Chart ({year}-{month:D2}) ===");

    if (!r.HasRows)
    {
        Console.WriteLine("No buildings or usage data found.");
        return;
    }

    var rows = new System.Collections.Generic.List<(string name, decimal kwh)>();
    decimal maxKwh = 0;

    while (r.Read())
    {
        var name = r.GetString(0);
        var kwh = r.GetDecimal(1);
        rows.Add((name, kwh));
        if (kwh > maxKwh) maxKwh = kwh;
    }

    if (maxKwh <= 0)
    {
        Console.WriteLine("All buildings have 0 kWh for this period.");
        return;
    }

    const int barWidth = 40;

    foreach (var row in rows)
    {
        var name = row.name;
        var kwh = row.kwh;

        int len = (int)Math.Round((double)(kwh / maxKwh) * barWidth);
        if (len < 1 && kwh > 0) len = 1;

        string bar = new string('#', len);
        Console.WriteLine($"{name,-20} | {bar} {kwh} kWh");
    }
}

static string ReadPassword()
{
    var pwd = "";
    ConsoleKeyInfo k;
    while ((k = Console.ReadKey(true)).Key != ConsoleKey.Enter)
    {
        if (k.Key == ConsoleKey.Backspace && pwd.Length > 0)
        {
            pwd = pwd[..^1];
            Console.Write("\b \b");
        }
        else if (!char.IsControl(k.KeyChar))
        {
            pwd += k.KeyChar;
            Console.Write("*");
        }
    }
    return pwd;
}
