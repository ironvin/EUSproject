
# Energy Usage Management System — Flowchart & Pseudocode

This document contains a Mermaid flowchart suitable for embedding in Markdown (e.g., GitHub, VS Code with Mermaid preview)
and structured pseudocode you can include in your report.
---
## Mermaid Main Flowchart
```mermaid
flowchart TD
    A[Start] --> B[EnsureSchema() - create tables if missing]
    B --> C[Prompt username & password]
    C --> D{Authenticate?}
    D -- No --> E[Invalid credentials -> Exit]
    D -- Yes --> F[Show main menu]
    F --> G{User choice}

    G -->|1 Import Buildings CSV| H[CsvService.ImportBuildings()]
    G -->|2 Import Units CSV| I[CsvService.ImportUnits()]
    G -->|3 Import Usage CSV| J[CsvService.ImportUsage()]
    G -->|4 Set/Update Rate| K[BuildingService.Upsert()]
    G -->|5 Record Usage| L[UsageService.Record()]
    G -->|6 Billing: Unit Month| M[UsageService.GetBillForMonth()]
    G -->|7 Report: Building Monthly Totals| N[ReportService.BuildingMonthlyTotals()]
    G -->|8 Report: Avg Per-Unit kWh| O[ReportService.AveragePerUnitKwh()]
    G -->|9 Alerts: Check & Export| P[AlertService.CheckMonthlyThresholds()]
    G -->|0 Exit| Q[End]
---
## Mermaid Subflow — Billing (Option 6)

```mermaid
flowchart TD
    A[Billing Option Selected] --> B[Prompt UnitId, Year, Month]
    B --> C[Lookup usage in EnergyUsage table]
    C --> D[Lookup building rate]
    D --> E[Calculate cost = kWh × rate]
    E --> F[Display total bill]

---

## Mermaid Subflow — Alerts (Option 9)

```mermaid
flowchart TD
    A[Input buildingId, year, month] --> B[For each Unit in Building]
    B --> C[Fetch kWh for (y,m) (null→0)]
    C --> D[Fetch unit.threshold (null/0→ignore)]
    D --> E{threshold > 0 and kWh > threshold?}
    E -- Yes --> F[Add AlertRecord to list]
    E -- No --> B
    F --> B
    B -->|done| G[Print count, export alerts.csv]
```

---

## Pseudocode — Main Program

```text
FUNCTION Main
    CALL DatabaseManager.EnsureSchema()

    PRINT "Username: "
    username ← READ_LINE()
    PRINT "Password: "
    password ← READ_PASSWORD()

    user ← UserService.Authenticate(username, password)
    IF user IS null THEN
        PRINT "Invalid credentials."
        RETURN
    ENDIF

    PRINT "Welcome, {user.Username} ({user.Role})"

    LOOP FOREVER
        DISPLAY_MENU()
        choice ← READ_LINE()

        TRY
            SWITCH choice
                CASE "1":
                    path ← PROMPT_PATH()
                    count ← CsvService.ImportBuildings(path)
                    PRINT "Imported {count} building(s)."

                CASE "2":
                    path ← PROMPT_PATH()
                    count ← CsvService.ImportUnits(path)
                    PRINT "Imported {count} unit(s)."

                CASE "3":
                    path ← PROMPT_PATH()
                    count ← CsvService.ImportUsage(path)
                    PRINT "Imported {count} usage record(s)."

                CASE "4":
                    name ← PROMPT("Building name")
                    rate ← PROMPT_DECIMAL("Rate $/kWh")
                    id ← BuildingService.Upsert(name, rate)
                    PRINT "Rate set. BuildingId={id}"

                CASE "5":
                    unitId ← PROMPT_INT("UnitId")
                    year ← PROMPT_INT("Year")
                    month ← PROMPT_INT("Month 1-12")
                    kwh ← PROMPT_DECIMAL("kWh")
                    UsageService.Record(unitId, year, month, kwh)
                    PRINT "Recorded."

                CASE "6":
                    unitId, year, month ← PROMPT_UNIT_MONTH()
                    bill ← UsageService.GetBillForMonth(unitId, year, month)
                    PRINT "Unit {bill.UnitId} {bill.Year}-{bill.Month}: " +
                          "kWh={bill.Kwh}, Rate={bill.Rate}, Cost={bill.Cost}"

                CASE "7":
                    buildingId, year, month ← PROMPT_BUILDING_MONTH()
                    (totKwh, totCost) ← ReportService.BuildingMonthlyTotals(buildingId, year, month)
                    PRINT "Total kWh={totKwh}, Total Cost={totCost}"

                CASE "8":
                    buildingId, year, month ← PROMPT_BUILDING_MONTH()
                    avg ← ReportService.AveragePerUnitKwh(buildingId, year, month)
                    PRINT "Average per unit kWh={avg}"

                CASE "9":
                    buildingId, year, month ← PROMPT_BUILDING_MONTH()
                    alerts ← AlertService.CheckMonthlyThresholds(buildingId, year, month)
                    PRINT "Alerts found: {COUNT(alerts)}"
                    outPath ← PROMPT_PATH("Export file (e.g., alerts.csv)")
                    CsvService.ExportAlerts(outPath, alerts)

                CASE "0":
                    BREAK
                DEFAULT:
                    PRINT "Invalid option."
            ENDSWITCH
        CATCH ex
            PRINT "Error: " + ex.Message
        ENDTRY
    ENDLOOP
END FUNCTION
```

---

## Pseudocode — Key Services

**Record Usage**
```text
FUNCTION Record(unitId, year, month, kwh)
    REQUIRE 1 ≤ month ≤ 12
    REQUIRE kwh ≥ 0
    UPSERT EnergyUsage(UnitId,Year,Month) with Kwh
END FUNCTION
```

**Get Bill for Month**
```text
FUNCTION GetBillForMonth(unitId, year, month) RETURNS BillLineItem
    kwh ← SELECT Kwh FROM EnergyUsage (null→0)
    rate ← SELECT b.EnergyRatePerKwh FROM Units u JOIN Buildings b ON u.BuildingId=b.Id WHERE u.Id=unitId
    cost ← ROUND(kwh * rate, 2)
    RETURN BillLineItem(unitId, year, month, kwh, rate, cost)
END FUNCTION
```

**Building Monthly Totals**
```text
FUNCTION BuildingMonthlyTotals(buildingId, year, month) RETURNS (totalKwh,totalCost)
    (sumKwh, rate) ← SELECT SUM(kwh), rate for building/month
    totalKwh ← COALESCE(sumKwh, 0)
    totalCost ← ROUND(totalKwh * rate, 2)
    RETURN (totalKwh, totalCost)
END FUNCTION
```

**Alerts**
```text
FUNCTION CheckMonthlyThresholds(buildingId, year, month) RETURNS List<AlertRecord>
    alerts ← []
    FOR EACH unit IN Units WHERE BuildingId=buildingId
        kwh ← SELECT Kwh for unit/year/month (null→0)
        threshold ← COALESCE(unit.MonthlyUsageThresholdKwh, 0)
        IF threshold > 0 AND kwh > threshold THEN
            alerts.ADD( AlertRecord(unit.Id, year, month, kwh, threshold) )
        ENDIF
    ENDFOR
    RETURN alerts
END FUNCTION
```
