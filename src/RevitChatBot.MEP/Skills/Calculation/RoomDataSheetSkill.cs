using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Generates a consolidated Room Data Sheet per Space/Room, aggregating
/// all MEP parameters: loads, airflow, lighting, power, and occupancy.
/// Inspired by LINEAR's Room Data Sheet Management feature.
/// </summary>
[Skill("room_data_sheet",
    "Generate MEP Room Data Sheet for each space/room. Aggregates cooling/heating load, " +
    "airflow, lighting power density, electrical load, occupancy, and equipment schedule " +
    "into a single data sheet per room. Useful for design review and submission.")]
[SkillParameter("level_name", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("space_name", "string", "Filter by space name (optional)", isRequired: false)]
[SkillParameter("include_equipment", "string",
    "Include MEP equipment in each room (default: true)",
    isRequired: false, allowedValues: new[] { "true", "false" })]
public class RoomDataSheetSkill : CalculationSkillBase
{
    protected override string SkillName => "room_data_sheet";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelName = parameters.GetValueOrDefault("level_name")?.ToString();
        var spaceName = parameters.GetValueOrDefault("space_name")?.ToString();
        var includeEquip = (parameters.GetValueOrDefault("include_equipment")?.ToString() ?? "true") == "true";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var spaces = new FilteredElementCollector(document)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Cast<Space>()
                .ToList();

            if (levelName is not null)
                spaces = spaces.Where(s =>
                    s.Level?.Name?.Contains(levelName, StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (spaceName is not null)
                spaces = spaces.Where(s =>
                    s.Name?.Contains(spaceName, StringComparison.OrdinalIgnoreCase) == true).ToList();

            List<FamilyInstance>? allEquipment = null;
            if (includeEquip)
            {
                allEquipment = new FilteredElementCollector(document)
                    .OfClass(typeof(FamilyInstance))
                    .WhereElementIsNotElementType()
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.MEPModel is not null)
                    .ToList();
            }

            var sheets = spaces.Select(s =>
            {
                var area = s.Area;
                var volume = s.Volume;
                var areaM2 = area * 0.092903;
                var volumeM3 = volume * 0.0283168;

                var coolingBtu = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_COOLING_LOAD_PARAM)?.AsDouble() ?? 0;
                var heatingBtu = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_HEATING_LOAD_PARAM)?.AsDouble() ?? 0;
                var supplyAirflow = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_SUPPLY_AIRFLOW_PARAM)?.AsDouble() ?? 0;
                var returnAirflow = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_RETURN_AIRFLOW_PARAM)?.AsDouble() ?? 0;
                var exhaustAirflow = s.get_Parameter(BuiltInParameter.ROOM_DESIGN_EXHAUST_AIRFLOW_PARAM)?.AsDouble() ?? 0;
                var lightingLoad = s.get_Parameter(BuiltInParameter.ROOM_ACTUAL_LIGHTING_LOAD_PARAM)?.AsDouble() ?? 0;
                var powerLoad = s.get_Parameter(BuiltInParameter.ROOM_ACTUAL_POWER_LOAD_PARAM)?.AsDouble() ?? 0;
                var occupancy = (int)(s.LookupParameter("Actual Occupancy")?.AsDouble()
                    ?? s.LookupParameter("Number of People")?.AsDouble() ?? 0);

                var coolingKw = coolingBtu * 0.293071 / 1000.0;
                var heatingKw = heatingBtu * 0.293071 / 1000.0;
                var lightingWm2 = areaM2 > 0 ? (lightingLoad * 0.293071) / areaM2 : 0;
                var powerWm2 = areaM2 > 0 ? (powerLoad * 0.293071) / areaM2 : 0;
                var occupancyDensity = areaM2 > 0 ? areaM2 / Math.Max(occupancy, 1) : 0;

                List<object>? equipmentInRoom = null;
                if (allEquipment is not null && s.Location is LocationPoint lp)
                {
                    var spacePoint = lp.Point;
                    equipmentInRoom = allEquipment
                        .Where(e =>
                        {
                            if (e.Space?.Id == s.Id) return true;
                            if (e.Room?.Id == s.Id) return true;
                            return false;
                        })
                        .Select(e => (object)new
                        {
                            name = e.Name,
                            familyName = e.Symbol?.FamilyName ?? "N/A",
                            id = e.Id.Value
                        })
                        .Take(15)
                        .ToList();
                }

                return new
                {
                    name = s.Name,
                    number = s.Number,
                    level = s.Level?.Name ?? "N/A",
                    areaM2 = Math.Round(areaM2, 2),
                    volumeM3 = Math.Round(volumeM3, 2),
                    ceilingHeightM = areaM2 > 0 ? Math.Round(volumeM3 / areaM2, 2) : 0,
                    cooling = new
                    {
                        kw = Math.Round(coolingKw, 3),
                        ton = Math.Round(coolingKw / 3.517, 3),
                        wPerM2 = areaM2 > 0 ? Math.Round(coolingKw * 1000 / areaM2, 1) : 0
                    },
                    heating = new
                    {
                        kw = Math.Round(heatingKw, 3),
                        wPerM2 = areaM2 > 0 ? Math.Round(heatingKw * 1000 / areaM2, 1) : 0
                    },
                    airflow = new
                    {
                        supplyLps = Math.Round(supplyAirflow * 0.471947, 1),
                        returnLps = Math.Round(returnAirflow * 0.471947, 1),
                        exhaustLps = Math.Round(exhaustAirflow * 0.471947, 1),
                        supplyCfm = Math.Round(supplyAirflow, 1),
                        achSupply = volumeM3 > 0
                            ? Math.Round((supplyAirflow * 0.471947 / 1000.0 * 3600) / volumeM3, 1)
                            : 0
                    },
                    lighting = new
                    {
                        totalW = Math.Round(lightingLoad * 0.293071, 1),
                        wPerM2 = Math.Round(lightingWm2, 1)
                    },
                    power = new
                    {
                        totalW = Math.Round(powerLoad * 0.293071, 1),
                        wPerM2 = Math.Round(powerWm2, 1)
                    },
                    occupancy = new
                    {
                        count = occupancy,
                        m2PerPerson = Math.Round(occupancyDensity, 1)
                    },
                    equipment = equipmentInRoom
                };
            }).ToList();

            var totalCooling = sheets.Sum(s => s.cooling.kw);
            var totalSupplyAir = sheets.Sum(s => s.airflow.supplyLps);

            return new
            {
                totalSpaces = sheets.Count,
                totals = new
                {
                    totalAreaM2 = Math.Round(sheets.Sum(s => s.areaM2), 1),
                    totalCoolingKW = Math.Round(totalCooling, 2),
                    totalCoolingTon = Math.Round(totalCooling / 3.517, 2),
                    totalSupplyAirLps = Math.Round(totalSupplyAir, 1)
                },
                rooms = sheets
            };
        });

        var totalSpaces = (int)((dynamic)result!).totalSpaces;
        var summary = new CalcResultSummary { TotalItems = totalSpaces, IssueCount = 0 };
        var deltaReport = ComputeDelta(context, summary);
        SaveResultForDelta(context, summary);

        var msg = "Room Data Sheet generation completed.";
        if (deltaReport is not null) msg += $"\n{deltaReport.Summary}";

        var followUps = new List<FollowUpSuggestion>
        {
            new() { SkillName = "flow_from_load", Reason = "Calculate water/air flow from these loads" },
            new() { SkillName = "hvac_load_calculation", Reason = "Detailed HVAC load breakdown" }
        };
        msg = AppendFollowUps(msg, followUps);
        return OkPaginated(msg, result, totalSpaces, Math.Min(totalSpaces, 30), "rooms");
    }
}
