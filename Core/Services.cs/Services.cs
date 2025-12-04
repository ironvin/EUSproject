using Core.Models;
using Microsoft.Data.Sqlite;
using Infra;

namespace Core.Services;

public sealed class UserService
{
    public User? Authenticate(string username, string password)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT Id, Username, PasswordHash, Role FROM Users WHERE Username=@u";
        cmd.Parameters.AddWithValue("@u", username);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var hash = DatabaseManager.SimpleHash(password);
        if (!string.Equals(hash, r.GetString(2), StringComparison.OrdinalIgnoreCase)) return null;

        return new User
        {
            Id = r.GetInt32(0),
            Username = r.GetString(1),
            PasswordHash = r.GetString(2),
            Role = (Role)r.GetInt32(3)
        };
    }
}

public sealed class BuildingService
{
    public int Upsert(string name, decimal ratePerKwh)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Buildings(Name, EnergyRatePerKwh) VALUES(@n,@r)
ON CONFLICT(Name) DO UPDATE SET EnergyRatePerKwh=excluded.EnergyRatePerKwh
RETURNING Id;";
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@r", ratePerKwh);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public decimal GetRate(int buildingId)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT EnergyRatePerKwh FROM Buildings WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", buildingId);

        return Convert.ToDecimal(cmd.ExecuteScalar()!);
    }
}

public sealed class UnitService
{
    public int Upsert(int buildingId, string unitNumber, decimal? thresholdKwh = null)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Units(BuildingId, UnitNumber, MonthlyUsageThresholdKwh) VALUES(@b,@u,@t)
ON CONFLICT(BuildingId, UnitNumber) DO UPDATE SET MonthlyUsageThresholdKwh=excluded.MonthlyUsageThresholdKwh
RETURNING Id;";
        cmd.Parameters.AddWithValue("@b", buildingId);
        cmd.Parameters.AddWithValue("@u", unitNumber);
        cmd.Parameters.AddWithValue("@t", (object?)thresholdKwh ?? DBNull.Value);

        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

public sealed class UsageService
{
    public void Record(int unitId, int year, int month, decimal kwh)
    {
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        if (kwh < 0) throw new ArgumentOutOfRangeException(nameof(kwh));

        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
INSERT INTO EnergyUsage(UnitId,Year,Month,Kwh) VALUES(@u,@y,@m,@k)
ON CONFLICT(UnitId,Year,Month) DO UPDATE SET Kwh=excluded.Kwh;";
        cmd.Parameters.AddWithValue("@u", unitId);
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);
        cmd.Parameters.AddWithValue("@k", kwh);

        cmd.ExecuteNonQuery();
    }

    public BillLineItem GetBillForMonth(int unitId, int year, int month)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        // kWh for the month
        decimal kwh = 0m;
        using (var kCmd = con.CreateCommand())
        {
            kCmd.CommandText = "SELECT Kwh FROM EnergyUsage WHERE UnitId=@u AND Year=@y AND Month=@m";
            kCmd.Parameters.AddWithValue("@u", unitId);
            kCmd.Parameters.AddWithValue("@y", year);
            kCmd.Parameters.AddWithValue("@m", month);
            var kwhObj = kCmd.ExecuteScalar();
            kwh = kwhObj is null ? 0m : Convert.ToDecimal(kwhObj);
        }

        // Rate from building
        decimal rate;
        using (var bCmd = con.CreateCommand())
        {
            bCmd.CommandText = @"SELECT b.EnergyRatePerKwh 
                                 FROM Units u JOIN Buildings b ON u.BuildingId=b.Id
                                 WHERE u.Id=@u";
            bCmd.Parameters.AddWithValue("@u", unitId);
            rate = Convert.ToDecimal(bCmd.ExecuteScalar()!);
        }

        return new BillLineItem { UnitId = unitId, Year = year, Month = month, Kwh = kwh, Rate = rate };
    }
}

public sealed class ReportService
{
    public (decimal totalKwh, decimal totalCost) BuildingMonthlyTotals(int buildingId, int year, int month)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT IFNULL(SUM(e.Kwh),0), b.EnergyRatePerKwh
FROM Units u 
LEFT JOIN EnergyUsage e ON e.UnitId=u.Id AND e.Year=@y AND e.Month=@m
JOIN Buildings b ON b.Id=u.BuildingId
WHERE u.BuildingId=@b;";
        cmd.Parameters.AddWithValue("@b", buildingId);
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);

        using var r = cmd.ExecuteReader();
        r.Read();
        var kwh = r.IsDBNull(0) ? 0m : r.GetDecimal(0);
        var rate = r.GetDecimal(1);

        return (kwh, decimal.Round(kwh * rate, 2));
    }

    public decimal AveragePerUnitKwh(int buildingId, int year, int month)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
WITH k AS (
  SELECT u.Id AS UnitId, IFNULL(e.Kwh,0) AS Kwh
  FROM Units u 
  LEFT JOIN EnergyUsage e ON e.UnitId=u.Id AND e.Year=@y AND e.Month=@m
  WHERE u.BuildingId=@b
)
SELECT AVG(Kwh) FROM k;";
        cmd.Parameters.AddWithValue("@b", buildingId);
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);

        var avg = cmd.ExecuteScalar();
        return avg is null ? 0m : decimal.Round(Convert.ToDecimal(avg), 2);
    }
}

public sealed class AlertRecord
{
    public int UnitId;
    public int Year;
    public int Month;
    public decimal Kwh;
    public decimal Threshold;
}

public sealed class AlertService
{
    public List<AlertRecord> CheckMonthlyThresholds(int buildingId, int year, int month)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"
SELECT u.Id, IFNULL(e.Kwh,0), IFNULL(u.MonthlyUsageThresholdKwh, 0)
FROM Units u
LEFT JOIN EnergyUsage e ON e.UnitId=u.Id AND e.Year=@y AND e.Month=@m
WHERE u.BuildingId=@b;";
        cmd.Parameters.AddWithValue("@b", buildingId);
        cmd.Parameters.AddWithValue("@y", year);
        cmd.Parameters.AddWithValue("@m", month);

        var list = new List<AlertRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var unitId = r.GetInt32(0);
            var kwh = r.IsDBNull(1) ? 0m : r.GetDecimal(1);
            var threshold = r.IsDBNull(2) ? 0m : r.GetDecimal(2);

            if (threshold > 0 && kwh > threshold)
            {
                list.Add(new AlertRecord
                {
                    UnitId = unitId,
                    Year = year,
                    Month = month,
                    Kwh = kwh,
                    Threshold = threshold
                });
            }
        }
        return list;
    }
}
