using Microsoft.Data.Sqlite;

namespace Infra;

public static class DatabaseManager
{
    private static readonly string _connString = "Data Source=energy.db;Cache=Shared";

    public static SqliteConnection GetConnection() => new(_connString);

    public static void EnsureSchema()
    {
        using var con = GetConnection();
        con.Open();

        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = @"
PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS Users(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Username TEXT UNIQUE NOT NULL,
  PasswordHash TEXT NOT NULL,
  Role INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS Buildings(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  Name TEXT UNIQUE NOT NULL,
  EnergyRatePerKwh REAL NOT NULL
);

CREATE TABLE IF NOT EXISTS Units(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  BuildingId INTEGER NOT NULL,
  UnitNumber TEXT NOT NULL,
  MonthlyUsageThresholdKwh REAL,
  FOREIGN KEY(BuildingId) REFERENCES Buildings(Id) ON DELETE CASCADE,
  UNIQUE(BuildingId, UnitNumber)
);

CREATE TABLE IF NOT EXISTS EnergyUsage(
  Id INTEGER PRIMARY KEY AUTOINCREMENT,
  UnitId INTEGER NOT NULL,
  Year INTEGER NOT NULL,
  Month INTEGER NOT NULL,
  Kwh REAL NOT NULL,
  FOREIGN KEY(UnitId) REFERENCES Units(Id) ON DELETE CASCADE,
  UNIQUE(UnitId, Year, Month)
);

CREATE INDEX IF NOT EXISTS IX_Usage_Unit_Month ON EnergyUsage(UnitId, Year, Month);
";
            cmd.ExecuteNonQuery();
        }

        // Seed admin if missing
        using var seed = con.CreateCommand();
        seed.CommandText = @"INSERT OR IGNORE INTO Users(Username,PasswordHash,Role)
                             VALUES('admin', @hash, 1);";
        seed.Parameters.AddWithValue("@hash", SimpleHash("admin123"));
        seed.ExecuteNonQuery();
    }

    // NOTE: For production use a salted hash like BCrypt/Argon2.
    public static string SimpleHash(string input) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(input)
            )
        );
}
