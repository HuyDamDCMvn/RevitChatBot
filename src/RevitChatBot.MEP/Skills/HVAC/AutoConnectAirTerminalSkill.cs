using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Coordination.Routing;

namespace RevitChatBot.MEP.Skills.HVAC;

/// <summary>
/// Auto-connect air terminals (diffusers/grilles) with ductwork via tree-topology routing.
///
/// Three modes:
///   preview — compute route and render in DirectContext3D (no model changes)
///   create  — build actual ducts and fittings in the model
///   clear   — remove DirectContext3D preview overlay
///
/// Typical agent flow: preview → user confirms → create → clear
/// </summary>
[Skill("auto_connect_air_terminals",
    "Auto-connect air terminals (diffusers/grilles) with ductwork. " +
    "Mode 'preview' shows proposed route in 3D view via DirectContext3D without modifying the model. " +
    "Mode 'create' builds actual ducts and fittings. " +
    "Mode 'clear' removes the preview overlay. " +
    "Computes tree-topology layout: branch ducts per row → main duct → fittings. " +
    "Sizing uses ASHRAE equal-velocity method.")]
[SkillParameter("mode", "string",
    "Operation mode. Default: 'preview'.",
    isRequired: false,
    allowedValues: new[] { "preview", "create", "clear" })]
[SkillParameter("level", "string",
    "Filter terminals by level name. Required for preview/create.",
    isRequired: false)]
[SkillParameter("system_type", "string",
    "HVAC system type to filter: supply, return, exhaust, or all. Default: 'all'.",
    isRequired: false,
    allowedValues: new[] { "supply", "return", "exhaust", "all" })]
[SkillParameter("branch_velocity_mps", "number",
    "Branch duct velocity in m/s. Default: 4.", isRequired: false)]
[SkillParameter("main_velocity_mps", "number",
    "Main duct velocity in m/s. Default: 7.", isRequired: false)]
[SkillParameter("prefer_rectangular", "boolean",
    "Use rectangular duct sizing instead of round. Default: false.", isRequired: false)]
[SkillParameter("max_aspect_ratio", "number",
    "Max W:H aspect ratio for rectangular ducts. Default: 3.", isRequired: false)]
[SkillParameter("duct_type_name", "string",
    "Duct type name to use for creation. If omitted, uses the first available DuctType.",
    isRequired: false)]
[SkillParameter("system_type_name", "string",
    "Mechanical system type name (e.g. 'Supply Air'). If omitted, uses first available.",
    isRequired: false)]
public class AutoConnectAirTerminalSkill : ISkill
{
    private const string RouteDataKey = "air_terminal_route_data";
    private const string PreviewTag = "air_terminal_preview";

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var mode = parameters.GetValueOrDefault("mode")?.ToString() ?? "preview";

        return mode switch
        {
            "clear" => HandleClear(context),
            "create" => await HandleCreate(context, parameters, cancellationToken),
            _ => await HandlePreview(context, parameters, cancellationToken)
        };
    }

    private async Task<SkillResult> HandlePreview(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var levelFilter = parameters.GetValueOrDefault("level")?.ToString();
        var sysFilter = parameters.GetValueOrDefault("system_type")?.ToString() ?? "all";
        var branchVel = ParseDouble(parameters.GetValueOrDefault("branch_velocity_mps"), 4.0);
        var mainVel = ParseDouble(parameters.GetValueOrDefault("main_velocity_mps"), 7.0);
        var preferRect = parameters.GetValueOrDefault("prefer_rectangular") is true or "true";
        var maxAR = ParseDouble(parameters.GetValueOrDefault("max_aspect_ratio"), 3.0);

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Document)docObj;
            var terminals = CollectAirTerminals(doc, levelFilter, sysFilter);

            if (terminals.Count == 0)
                return new { error = $"No air terminals found{(levelFilter != null ? $" on level '{levelFilter}'" : "")}{(sysFilter != "all" ? $" for '{sysFilter}' system" : "")}." };

            var routeData = DuctTerminalRoutingAlgorithm.ComputeRoute(
                terminals, branchVel, mainVel, maxAR, preferRect, levelFilter);

            context.Extra[RouteDataKey] = routeData;

            int rendered = 0;
            var vizManager = context.VisualizationManager;
            if (vizManager is not null)
            {
                try
                {
                    rendered = (int)((dynamic)vizManager).PreviewMepRoute(routeData, PreviewTag);
                    ((dynamic)vizManager).RefreshViews();
                }
                catch { rendered = -1; }
            }

            return new
            {
                error = (string?)null,
                routeData.TotalEndpoints,
                routeData.TotalSegments,
                routeData.TotalFittings,
                totalLengthM = Math.Round(routeData.TotalLengthFeet * 0.3048, 1),
                totalFlowLps = Math.Round(routeData.TotalFlowLps, 1),
                branchSize = FormatSize(routeData, isBranch: true),
                mainSize = FormatSize(routeData, isBranch: false),
                routeData.IsRectangular,
                routeData.LevelName,
                previewRendered = rendered >= 0,
                renderedGeometries = rendered
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to compute duct terminal routing.");

        dynamic r = result;
        if (r.error is not null)
            return SkillResult.Fail((string)r.error);

        var preview = (bool)r.previewRendered;
        var msg = $"Air terminal routing preview computed: " +
                  $"{(int)r.TotalEndpoints} terminals, " +
                  $"{(int)r.TotalSegments} duct segments ({r.totalLengthM}m), " +
                  $"{(int)r.TotalFittings} fittings. " +
                  $"Total airflow: {r.totalFlowLps} L/s. " +
                  $"Branch {r.branchSize}, Main {r.mainSize}.";

        if (preview)
            msg += "\nPreview shown in 3D view (cyan=branch, gold=main, white=fittings). " +
                   "Say 'create' to build actual ducts or 'clear' to remove preview.";
        else
            msg += "\nVisualization not available — use mode='create' to build ducts directly.";

        return SkillResult.Ok(msg, result);
    }

    private async Task<SkillResult> HandleCreate(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var routeData = context.Extra.GetValueOrDefault(RouteDataKey) as MepAutoRouteData;
        if (routeData is null || routeData.TotalSegments == 0)
        {
            var previewResult = await HandlePreview(context, parameters, cancellationToken);
            if (!previewResult.Success) return previewResult;
            routeData = context.Extra.GetValueOrDefault(RouteDataKey) as MepAutoRouteData;
            if (routeData is null || routeData.TotalSegments == 0)
                return SkillResult.Fail("No route data available. Run preview first.");
        }

        var ductTypeName = parameters.GetValueOrDefault("duct_type_name")?.ToString();
        var systemTypeName = parameters.GetValueOrDefault("system_type_name")?.ToString();

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Document)docObj;

            var ductTypeId = FindDuctTypeId(doc, ductTypeName);
            var systemTypeId = FindMechanicalSystemTypeId(doc, systemTypeName);
            var levelId = FindLevelId(doc, routeData.LevelName);

            if (ductTypeId == ElementId.InvalidElementId)
                return new { error = "No DuctType found in model.", created = 0, fittings = 0, errors = new List<string>() };
            if (systemTypeId == ElementId.InvalidElementId)
                return new { error = "No MechanicalSystemType found in model.", created = 0, fittings = 0, errors = new List<string>() };

            bool isRectType = IsRectangularDuctType(doc, ductTypeId);

            using var tx = new Transaction(doc, "Auto-Connect Air Terminals");
            tx.Start();

            var createdDucts = new List<Duct>();
            var errors = new List<string>();

            var allSegments = routeData.MainSegments
                .Select(s => (seg: s, isMain: true))
                .Concat(routeData.BranchSegments.Select(s => (seg: s, isMain: false)));

            foreach (var (seg, isMain) in allSegments)
            {
                try
                {
                    var start = new XYZ(seg.Start.X, seg.Start.Y, seg.Start.Z);
                    var end = new XYZ(seg.End.X, seg.End.Y, seg.End.Z);
                    if (start.DistanceTo(end) < doc.Application.ShortCurveTolerance)
                        continue;

                    var duct = Duct.Create(doc, systemTypeId, ductTypeId, levelId, start, end);
                    SetDuctSize(duct, routeData, isMain, isRectType);
                    createdDucts.Add(duct);
                }
                catch (Exception ex)
                {
                    errors.Add($"Segment: {ex.Message}");
                }
            }

            doc.Regenerate();

            int fittingsConnected = 0;
            for (int i = 0; i < createdDucts.Count - 1; i++)
            {
                try
                {
                    var endPt = GetEndPoint(createdDucts[i]);
                    var nextStart = GetStartPoint(createdDucts[i + 1]);
                    if (endPt is not null && nextStart is not null && endPt.DistanceTo(nextStart) < 0.5)
                    {
                        if (FittingConnector.ConnectWithFitting(doc, createdDucts[i], createdDucts[i + 1], endPt))
                            fittingsConnected++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Fitting: {ex.Message}");
                }
            }

            ConnectToBranchJunctions(doc, createdDucts, routeData, ref fittingsConnected, errors);

            tx.Commit();

            TryClearPreview(context);

            return new
            {
                error = (string?)null,
                created = createdDucts.Count,
                fittings = fittingsConnected,
                errors
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to create ductwork.");

        dynamic res = result;
        if (res.error is not null)
            return SkillResult.Fail((string)res.error);

        int count = res.created;
        int fits = res.fittings;
        List<string> errs = res.errors;
        var msg = $"Created {count} duct segments and {fits} fittings for {routeData.TotalEndpoints} air terminals.";
        if (errs.Count > 0)
            msg += $"\n{errs.Count} warning(s): {string.Join("; ", errs.Take(5))}";

        context.Extra.Remove(RouteDataKey);
        return SkillResult.Ok(msg, result);
    }

    private static SkillResult HandleClear(SkillContext context)
    {
        var vizManager = context.VisualizationManager;
        if (vizManager is null)
            return SkillResult.Fail("Visualization not available.");

        try
        {
            ((dynamic)vizManager).ClearByTag(PreviewTag);
            ((dynamic)vizManager).ClearByTag(PreviewTag + "_branch");
            ((dynamic)vizManager).ClearByTag(PreviewTag + "_main");
            ((dynamic)vizManager).ClearByTag(PreviewTag + "_fittings");
            ((dynamic)vizManager).RefreshViews();
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Failed to clear preview: {ex.Message}");
        }

        context.Extra.Remove(RouteDataKey);
        return SkillResult.Ok("Air terminal routing preview cleared.");
    }

    #region Revit Helpers

    private static List<(XYZ Position, double FlowLps)> CollectAirTerminals(
        Document doc, string? levelFilter, string sysFilter)
    {
        var terminals = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DuctTerminal)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            terminals = terminals.Where(t =>
            {
                var levelName = (doc.GetElement(t.LevelId) as Level)?.Name;
                return levelName?.Contains(levelFilter, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        if (sysFilter != "all")
        {
            terminals = terminals.Where(t =>
            {
                var sysClass = t.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
                return sysFilter switch
                {
                    "supply" => sysClass.Contains("Supply", StringComparison.OrdinalIgnoreCase),
                    "return" => sysClass.Contains("Return", StringComparison.OrdinalIgnoreCase),
                    "exhaust" => sysClass.Contains("Exhaust", StringComparison.OrdinalIgnoreCase),
                    _ => true
                };
            }).ToList();
        }

        return terminals
            .Select(t =>
            {
                var pos = (t.Location as LocationPoint)?.Point;
                if (pos is null) return ((XYZ?)null, 0.0);

                double flowLps = GetTerminalFlowLps(t);
                return (pos, flowLps);
            })
            .Where(x => x.Item1 is not null)
            .Select(x => (x.Item1!, x.Item2))
            .ToList();
    }

    private static double GetTerminalFlowLps(FamilyInstance terminal)
    {
        var connectors = terminal.MEPModel?.ConnectorManager;
        if (connectors is null) return DefaultFlowLps;

        foreach (Connector c in connectors.Connectors)
        {
            if (c.Domain != Domain.DomainHvac) continue;
            try
            {
                double flowCfs = c.Flow;
                if (flowCfs > 0)
                    return flowCfs * 28.3168; // ft³/s → L/s
            }
            catch { }
        }

        return DefaultFlowLps;
    }

    private const double DefaultFlowLps = 50.0; // ~100 CFM fallback

    private static void SetDuctSize(Duct duct, MepAutoRouteData routeData, bool isMain, bool isRectType)
    {
        if (isRectType && routeData.IsRectangular)
        {
            double wMm = isMain ? routeData.MainWidthMm!.Value : (routeData.BranchWidthMm ?? routeData.MainWidthMm!.Value);
            double hMm = isMain ? routeData.MainHeightMm!.Value : (routeData.BranchHeightMm ?? routeData.MainHeightMm!.Value);

            var wp = duct.get_Parameter(BuiltInParameter.RBS_CURVE_WIDTH_PARAM);
            var hp = duct.get_Parameter(BuiltInParameter.RBS_CURVE_HEIGHT_PARAM);
            if (wp is not null && !wp.IsReadOnly) wp.Set(wMm / 304.8);
            if (hp is not null && !hp.IsReadOnly) hp.Set(hMm / 304.8);
        }
        else
        {
            double diamMm = isMain ? routeData.MainSizeMm : routeData.BranchSizeMm;
            var dp = duct.get_Parameter(BuiltInParameter.RBS_CURVE_DIAMETER_PARAM);
            if (dp is not null && !dp.IsReadOnly) dp.Set(diamMm / 304.8);
        }
    }

    private static bool IsRectangularDuctType(Document doc, ElementId ductTypeId)
    {
        if (doc.GetElement(ductTypeId) is not DuctType ductType)
            return false;

        var name = ductType.Name.ToLowerInvariant();
        return name.Contains("rect") || name.Contains("chữ nhật");
    }

    private static ElementId FindDuctTypeId(Document doc, string? typeName)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(DuctType))
            .Cast<DuctType>()
            .ToList();

        if (typeName is not null)
        {
            var match = types.FirstOrDefault(t =>
                t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }

        return types.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private static ElementId FindMechanicalSystemTypeId(Document doc, string? systemName)
    {
        var systemTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystemType))
            .Cast<MechanicalSystemType>()
            .ToList();

        if (systemName is not null)
        {
            var match = systemTypes.FirstOrDefault(t =>
                t.Name.Contains(systemName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }

        var supplyMatch = systemTypes.FirstOrDefault(t =>
            t.Name.Contains("Supply", StringComparison.OrdinalIgnoreCase));
        if (supplyMatch is not null) return supplyMatch.Id;

        return systemTypes.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private static ElementId FindLevelId(Document doc, string? levelName)
    {
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        if (levelName is not null)
        {
            var match = levels.FirstOrDefault(l =>
                l.Name.Contains(levelName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }

        return levels.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private static XYZ? GetStartPoint(MEPCurve curve)
        => (curve.Location as LocationCurve)?.Curve.GetEndPoint(0);

    private static XYZ? GetEndPoint(MEPCurve curve)
        => (curve.Location as LocationCurve)?.Curve.GetEndPoint(1);

    private static void ConnectToBranchJunctions(
        Document doc, List<Duct> ducts, MepAutoRouteData routeData,
        ref int fittingsConnected, List<string> errors)
    {
        if (routeData.FittingPositions.Count == 0 || ducts.Count < 2)
            return;

        foreach (var fp in routeData.FittingPositions)
        {
            var junctionPt = new XYZ(fp.X, fp.Y, fp.Z);
            var nearby = ducts
                .Where(d =>
                {
                    var loc = d.Location as LocationCurve;
                    if (loc?.Curve is not Line line) return false;
                    var proj = line.Project(junctionPt);
                    return proj is not null && proj.Distance < 0.5;
                })
                .OrderBy(d =>
                {
                    var loc = (LocationCurve)d.Location;
                    return loc.Curve.Project(junctionPt)?.Distance ?? double.MaxValue;
                })
                .Take(3)
                .ToList();

            for (int i = 0; i < nearby.Count - 1; i++)
            {
                try
                {
                    if (FittingConnector.ConnectWithFitting(doc, nearby[i], nearby[i + 1], junctionPt))
                        fittingsConnected++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Junction fitting: {ex.Message}");
                }
            }
        }
    }

    private static void TryClearPreview(SkillContext context)
    {
        var vizManager = context.VisualizationManager;
        if (vizManager is null) return;
        try
        {
            ((dynamic)vizManager).ClearByTag(PreviewTag);
            ((dynamic)vizManager).ClearByTag(PreviewTag + "_branch");
            ((dynamic)vizManager).ClearByTag(PreviewTag + "_main");
            ((dynamic)vizManager).ClearByTag(PreviewTag + "_fittings");
            ((dynamic)vizManager).RefreshViews();
        }
        catch { }
    }

    private static string FormatSize(MepAutoRouteData data, bool isBranch)
    {
        if (data.IsRectangular)
        {
            double w = isBranch ? (data.BranchWidthMm ?? data.MainWidthMm ?? 0) : (data.MainWidthMm ?? 0);
            double h = isBranch ? (data.BranchHeightMm ?? data.MainHeightMm ?? 0) : (data.MainHeightMm ?? 0);
            return $"{w}×{h}mm";
        }
        double d = isBranch ? data.BranchSizeMm : data.MainSizeMm;
        return $"Ø{d}mm";
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    #endregion
}
