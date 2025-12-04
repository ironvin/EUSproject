using Core.Models;
using Infra;
using System.Globalization;

namespace Core.Services;

public sealed class CsvService
{
    // Buildings CSV: Name,Rate
    public int ImportBuildings(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => !string.IsNullOrWhiteSpace(l));
        var svc = new BuildingService();
        int count = 0;
        foreach (var line in lines.Skip(1)) // assume header
        {
            var parts = line.Split(',');
            var name = parts[0].Trim();
            var rate = decimal.Parse(parts[1], CultureInfo.InvariantCulture);
            svc.Upsert(name, rate);
            count++;
        }
        return count;
    }

    // Units CSV: BuildingName,UnitNumber,Threshold(optional)
    public int ImportUnits(string path)
    {
        using var con = DatabaseManager.GetConnection();
        con.Open();
        int count = 0;
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            var p = line.Split(',');
            var buildingName = p[0].Trim();
            var unitNumber = p[1].Trim();
            decimal? threshold = p.Length > 2 && !string.IsNullOrWhiteSpace(p[2])
                ? decimal.Parse(p[2], CultureInfo.InvariantCulture) : null;

            // lookup building id
            var bCmd = con.CreateCommand();
            bCmd.CommandText = "SELECT Id FROM Buildings WHERE Name=@n";
            bCmd.Parameters.AddWithValue("@n", buildingName);
            var bidObj = bCmd.ExecuteScalar() ?? throw new Exception($"Building '{buildingName}' not found.");
            var bid = Convert.ToInt32(bidObj);

            var us = new UnitService();
            us.Upsert(bid, unitNumber, threshold);
            count++;
        }
        return count;
    }

    // Usage CSV: UnitId,Year,Month,Kwh
    public int ImportUsage(string path)
    {
        var svc = new UsageService();
        int count = 0;
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            var p = line.Split(',');
            var unitId = int.Parse(p[0]);
            var year = int.Parse(p[1]);
            var month = int.Parse(p[2]);
            var kwh = decimal.Parse(p[3], CultureInfo.InvariantCulture);
            svc.Record(unitId, year, month, kwh);
            count++;
        }
        return count;
    }

    // Alerts export
    public void ExportAlerts(string path, IEnumerable<AlertRecord> alerts)
    {
        using var w = new StreamWriter(path);
        w.WriteLine("UnitId,Year,Month,Kwh,Threshold");
        foreach (var a in alerts)
            w.WriteLine($"{a.UnitId},{a.Year},{a.Month},{a.Kwh},{a.Threshold}");
    }
}
