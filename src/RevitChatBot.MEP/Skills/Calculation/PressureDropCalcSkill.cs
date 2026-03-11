using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Calculation;

/// <summary>
/// Calculates pressure drop from first principles using Darcy-Weisbach:
/// ΔP = f × (L/D) × (ρV²/2) plus fitting K-factors.
/// Unlike the existing PressureDropSkill which reads Revit params,
/// this skill computes from geometry and flow data.
/// </summary>
[Skill("pressure_drop_calc",
    "Calculate pressure drop from first principles using Darcy-Weisbach equation. " +
    "Computes friction loss from duct/pipe geometry and velocity, plus fitting losses " +
    "with K-factors. More accurate than reading Revit's built-in pressure drop params.")]
[SkillParameter("system_type", "string",
    "'duct' or 'pipe'. Default: 'duct'.",
    isRequired: false, allowedValues: new[] { "duct", "pipe" })]
[SkillParameter("system_name", "string", "Filter by system name (optional)", isRequired: false)]
[SkillParameter("friction_factor", "number",
    "Darcy friction factor (default: 0.02 for sheet metal duct, 0.03 for galvanized pipe)",
    isRequired: false)]
[SkillParameter("fluid_density", "number",
    "Fluid density in kg/m³ (default: 1.2 for air, 998 for water)", isRequired: false)]
public class PressureDropCalcSkill : CalculationSkillBase
{
    protected override string SkillName => "pressure_drop_calc";

    public override async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var systemType = GetParamString(parameters, context, "system_type", "duct");
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var isDuct = systemType.Equals("duct", StringComparison.OrdinalIgnoreCase);

        var defaultF = isDuct ? 0.02 : 0.03;
        var defaultRho = isDuct ? 1.2 : 998.0;
        var frictionFactor = GetParamDouble(parameters, context, "friction_factor", defaultF);
        var rho = GetParamDouble(parameters, context, "fluid_density", defaultRho);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var elements = isDuct
                ? new FilteredElementCollector(document)
                    .OfClass(typeof(Duct)).WhereElementIsNotElementType().ToList()
                : new FilteredElementCollector(document)
                    .OfClass(typeof(Pipe)).WhereElementIsNotElementType().ToList();

            var systemData = new Dictionary<string, SystemCalcData>();

            foreach (var elem in elements)
            {
                var sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned";
                if (systemName is not null &&
                    !sysName.Contains(systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!systemData.TryGetValue(sysName, out var data))
                {
                    data = new SystemCalcData { SystemName = sysName };
                    systemData[sysName] = data;
                }

                var lengthFt = elem.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
                var lengthM = lengthFt * 0.3048;

                double velocityMps;
                double dHydM;

                if (isDuct)
                {
                    var velFpm = elem.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0;
                    velocityMps = velFpm * 0.00508;
                    var widthFt = elem.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM)?.AsDouble() ?? 0;
                    var heightFt = elem.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM)?.AsDouble() ?? 0;
                    var wM = widthFt * 0.3048;
                    var hM = heightFt * 0.3048;
                    dHydM = (wM > 0 && hM > 0) ? 2 * wM * hM / (wM + hM) : 0;
                    if (dHydM == 0)
                    {
                        var diaFt = elem.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                        dHydM = diaFt * 0.3048;
                    }
                }
                else
                {
                    var velFps = elem.get_Parameter(BuiltInParameter.RBS_PIPE_VELOCITY_PARAM)?.AsDouble() ?? 0;
                    velocityMps = velFps * 0.3048;
                    var diaFt = elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
                    dHydM = diaFt * 0.3048;
                }

                if (dHydM <= 0 || lengthM <= 0) continue;

                // Darcy-Weisbach: ΔP = f × (L/D) × (ρV²/2)
                var dP = frictionFactor * (lengthM / dHydM) * (rho * velocityMps * velocityMps / 2.0);
                data.TotalFrictionPa += dP;
                data.TotalLengthM += lengthM;
                data.SegmentCount++;
            }

            // Fitting losses: simplified K-factor approach
            var fittingCat = isDuct ? BuiltInCategory.OST_DuctFitting : BuiltInCategory.OST_PipeFitting;
            var fittings = new FilteredElementCollector(document)
                .OfCategory(fittingCat)
                .WhereElementIsNotElementType()
                .ToList();

            foreach (var fit in fittings)
            {
                var sysName = fit.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "Unassigned";
                if (systemName is not null &&
                    !sysName.Contains(systemName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!systemData.TryGetValue(sysName, out var data)) continue;

                double kFactor = 0.5; // average for elbows/tees
                double velMps = 0;

                if (isDuct)
                {
                    var connectors = (fit as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
                    if (connectors is not null)
                    {
                        foreach (Connector c in connectors)
                        {
                            if (c.Domain == Autodesk.Revit.DB.Domain.DomainHvac)
                            {
                                try
                                {
                                    var area = c.Shape == ConnectorProfileType.Round
                                        ? Math.PI * c.Radius * c.Radius
                                        : c.Width * c.Height;
                                    var areaM2 = area * 0.092903;
                                    velMps = Math.Max(velMps, c.Flow * 0.000471947 / (areaM2 + 0.0001));
                                }
                                catch { }
                                break;
                            }
                        }
                    }
                }
                else
                {
                    var connectors = (fit as FamilyInstance)?.MEPModel?.ConnectorManager?.Connectors;
                    if (connectors is not null)
                    {
                        foreach (Connector c in connectors)
                        {
                            if (c.Domain == Autodesk.Revit.DB.Domain.DomainPiping)
                            {
                                try
                                {
                                    var areaM2 = Math.PI * c.Radius * c.Radius * 0.092903;
                                    velMps = Math.Max(velMps, c.Flow * 0.0000631 / (areaM2 + 0.0001));
                                }
                                catch { }
                                break;
                            }
                        }
                    }
                }

                var dPFitting = kFactor * (rho * velMps * velMps / 2.0);
                data.TotalFittingPa += dPFitting;
                data.FittingCount++;
            }

            var systems = systemData.Values
                .OrderByDescending(s => s.TotalPressureDropPa)
                .Select(s => new
                {
                    system = s.SystemName,
                    segments = s.SegmentCount,
                    fittings = s.FittingCount,
                    totalLengthM = Math.Round(s.TotalLengthM, 1),
                    frictionLossPa = Math.Round(s.TotalFrictionPa, 1),
                    fittingLossPa = Math.Round(s.TotalFittingPa, 1),
                    totalPressureDropPa = Math.Round(s.TotalPressureDropPa, 1),
                    pressureDropPerMeter = s.TotalLengthM > 0
                        ? Math.Round(s.TotalFrictionPa / s.TotalLengthM, 2) : 0
                })
                .ToList();

            return new
            {
                method = "Darcy-Weisbach",
                systemType,
                parameters = new
                {
                    frictionFactor,
                    fluidDensity = rho,
                    averageKFactor = 0.5
                },
                totalSystems = systems.Count,
                systems
            };
        });

        var totalSystems = (int)((dynamic)result!).totalSystems;
        var calcSummary = new CalcResultSummary { TotalItems = totalSystems, IssueCount = 0 };
        var delta = ComputeDelta(context, calcSummary);
        SaveResultForDelta(context, calcSummary);

        var msg = "Pressure drop calculation (Darcy-Weisbach) completed.";
        if (delta is not null) msg += $"\n{delta.Summary}";

        var followUps = new List<FollowUpSuggestion>
        {
            new()
            {
                SkillName = isDuct ? "duct_network_sizing" : "pipe_network_sizing",
                Reason = "Resize elements to optimize pressure drop"
            }
        };
        msg = AppendFollowUps(msg, followUps);
        return SkillResult.Ok(msg, result);
    }

    private class SystemCalcData
    {
        public string SystemName { get; set; } = "";
        public int SegmentCount { get; set; }
        public int FittingCount { get; set; }
        public double TotalLengthM { get; set; }
        public double TotalFrictionPa { get; set; }
        public double TotalFittingPa { get; set; }
        public double TotalPressureDropPa => TotalFrictionPa + TotalFittingPa;
    }
}
