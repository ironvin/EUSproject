namespace Core.Models;

public enum Role { Admin = 1, Manager = 2 }

public sealed class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
}

public sealed class Building
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal EnergyRatePerKwh { get; set; }
}

public sealed class Unit
{
    public int Id { get; set; }
    public int BuildingId { get; set; }
    public string UnitNumber { get; set; } = "";
    public decimal? MonthlyUsageThresholdKwh { get; set; }
}

public sealed class EnergyUsageRecord
{
    public int Id { get; set; }
    public int UnitId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Kwh { get; set; }
}

public sealed class BillLineItem
{
    public int UnitId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public decimal Kwh { get; set; }
    public decimal Rate { get; set; }
    public decimal Cost => decimal.Round(Kwh * Rate, 2);
}
