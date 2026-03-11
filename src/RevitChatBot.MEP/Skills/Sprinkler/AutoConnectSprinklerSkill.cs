using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Coordination.Routing;

namespace RevitChatBot.MEP.Skills.Sprinkler;

/// <summary>
/// Auto-connect sprinkler heads with piping via tree-topology routing.
///
/// Three modes:
///   preview — compute route and render in DirectContext3D (no model changes)
///   create  — build actual pipes and fittings in the model
///   clear   — remove DirectContext3D preview overlay
///
/// Typical agent flow: preview → user confirms → create → clear
/// </summary>
[Skill("auto_connect_sprinklers",
    "Auto-connect sprinkler heads with piping. " +
    "Mode 'preview' shows proposed route in 3D view via DirectContext3D without modifying the model. " +
    "Mode 'create' builds actual pipes and fittings. " +
    "Mode 'clear' removes the preview overlay. " +
    "Computes tree-topology layout: branch lines per row → cross main → fittings.")]
[SkillParameter("mode", "string",
    "Operation mode. Default: 'preview'.",
    isRequired: false,
    allowedValues: new[] { "preview", "create", "clear" })]
[SkillParameter("level", "string",
    "Filter sprinklers by level name. Required for preview/create.",
    isRequired: false)]
[SkillParameter("hazard_class", "string",
    "NFPA 13 hazard classification. Default: 'light'.",
    isRequired: false,
    allowedValues: new[] { "light", "oh1", "oh2", "extra" })]
[SkillParameter("k_factor", "number",
    "K-factor of sprinkler heads (default: 80 for K80).",
    isRequired: false)]
[SkillParameter("min_pressure_bar", "number",
    "Minimum pressure at sprinkler head in bar. Default: 0.5.",
    isRequired: false)]
[SkillParameter("pipe_type_name", "string",
    "Pipe type name to use for creation. If omitted, uses the first available PipeType.",
    isRequired: false)]
[SkillParameter("system_type_name", "string",
    "Piping system type name (e.g. 'Fire Protection'). If omitted, uses first available.",
    isRequired: false)]
public class AutoConnectSprinklerSkill : ISkill
{
    private const string RouteDataKey = "sprinkler_route_data";
    private const string PreviewTag = "sprinkler_preview";

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
        var hazardClass = parameters.GetValueOrDefault("hazard_class")?.ToString() ?? "light";
        var kFactor = ParseDouble(parameters.GetValueOrDefault("k_factor"), 80);
        var minPressure = ParseDouble(parameters.GetValueOrDefault("min_pressure_bar"), 0.5);

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Document)docObj;
            var heads = CollectSprinklerPositions(doc, levelFilter);

            if (heads.Count == 0)
                return new { error = $"No sprinkler heads found{(levelFilter is not null ? $" on level '{levelFilter}'" : "")}." };

            var routeData = SprinklerRoutingAlgorithm.ComputeRoute(
                heads, hazardClass, kFactor, minPressure, levelFilter);

            context.Extra[RouteDataKey] = routeData;

            int rendered = 0;
            var vizManager = context.VisualizationManager;
            if (vizManager is not null)
            {
                try
                {
                    rendered = (int)((dynamic)vizManager).PreviewSprinklerRoute(routeData, PreviewTag);
                    ((dynamic)vizManager).RefreshViews();
                }
                catch
                {
                    rendered = -1;
                }
            }

            return new
            {
                error = (string?)null,
                routeData.TotalHeads,
                routeData.TotalSegments,
                routeData.TotalFittings,
                totalLengthM = Math.Round(routeData.TotalLengthFeet * 0.3048, 1),
                branchDN = $"DN{routeData.BranchDiameterMm}",
                mainDN = $"DN{routeData.MainDiameterMm}",
                branchSegments = routeData.BranchSegments.Count,
                mainSegments = routeData.MainSegments.Count,
                routeData.HazardClass,
                routeData.LevelName,
                previewRendered = rendered >= 0,
                renderedGeometries = rendered
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to compute sprinkler routing.");

        dynamic r = result;
        if (r.error is not null)
            return SkillResult.Fail((string)r.error);

        var preview = (bool)r.previewRendered;
        var msg = $"Sprinkler routing preview computed: " +
                  $"{(int)r.TotalHeads} heads, " +
                  $"{(int)r.TotalSegments} pipe segments ({r.totalLengthM}m), " +
                  $"{(int)r.TotalFittings} fittings. " +
                  $"Branch {r.branchDN}, Main {r.mainDN}. " +
                  $"Hazard: {r.HazardClass}.";

        if (preview)
            msg += "\nPreview shown in 3D view (cyan=branch, gold=main, white=fittings). " +
                   "Say 'create' to build actual pipes or 'clear' to remove preview.";
        else
            msg += "\nVisualization not available — use mode='create' to build pipes directly.";

        return SkillResult.Ok(msg, result);
    }

    private async Task<SkillResult> HandleCreate(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var routeData = context.Extra.GetValueOrDefault(RouteDataKey) as SprinklerRouteData;
        if (routeData is null || routeData.TotalSegments == 0)
        {
            var previewResult = await HandlePreview(context, parameters, cancellationToken);
            if (!previewResult.Success) return previewResult;
            routeData = context.Extra.GetValueOrDefault(RouteDataKey) as SprinklerRouteData;
            if (routeData is null || routeData.TotalSegments == 0)
                return SkillResult.Fail("No route data available. Run preview first.");
        }

        var pipeTypeName = parameters.GetValueOrDefault("pipe_type_name")?.ToString();
        var systemTypeName = parameters.GetValueOrDefault("system_type_name")?.ToString();

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Document)docObj;

            var pipeTypeId = FindPipeTypeId(doc, pipeTypeName);
            var systemTypeId = FindPipingSystemTypeId(doc, systemTypeName);
            var levelId = FindLevelId(doc, routeData.LevelName);

            if (pipeTypeId == ElementId.InvalidElementId)
                return new { error = "No PipeType found in model.", created = 0, fittings = 0, errors = new List<string>() };
            if (systemTypeId == ElementId.InvalidElementId)
                return new { error = "No PipingSystemType found in model.", created = 0, fittings = 0, errors = new List<string>() };

            using var tx = new Transaction(doc, "Auto-Connect Sprinklers");
            tx.Start();

            var createdPipes = new List<Pipe>();
            var errors = new List<string>();

            var allSegments = routeData.MainSegments
                .Select(s => (s, routeData.MainDiameterMm))
                .Concat(routeData.BranchSegments.Select(s => (s, routeData.BranchDiameterMm)));

            foreach (var (seg, diamMm) in allSegments)
            {
                try
                {
                    var start = new XYZ(seg.Start.X, seg.Start.Y, seg.Start.Z);
                    var end = new XYZ(seg.End.X, seg.End.Y, seg.End.Z);
                    if (start.DistanceTo(end) < doc.Application.ShortCurveTolerance)
                        continue;

                    var pipe = Pipe.Create(doc, systemTypeId, pipeTypeId, levelId, start, end);
                    var diamParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    if (diamParam is not null && !diamParam.IsReadOnly)
                        diamParam.Set(diamMm / 304.8);

                    createdPipes.Add(pipe);
                }
                catch (Exception ex)
                {
                    errors.Add($"Segment: {ex.Message}");
                }
            }

            doc.Regenerate();

            int fittingsConnected = 0;
            for (int i = 0; i < createdPipes.Count - 1; i++)
            {
                try
                {
                    var endPt = GetEndPoint(createdPipes[i]);
                    var nextStart = GetStartPoint(createdPipes[i + 1]);
                    if (endPt is not null && nextStart is not null && endPt.DistanceTo(nextStart) < 0.5)
                    {
                        if (FittingConnector.ConnectWithFitting(doc, createdPipes[i], createdPipes[i + 1], endPt))
                            fittingsConnected++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Fitting: {ex.Message}");
                }
            }

            ConnectToBranchJunctions(doc, createdPipes, routeData, ref fittingsConnected, errors);

            tx.Commit();

            var vizManager = context.VisualizationManager;
            if (vizManager is not null)
            {
                try
                {
                    ((dynamic)vizManager).ClearSprinklerPreview(PreviewTag);
                    ((dynamic)vizManager).RefreshViews();
                }
                catch { }
            }

            return new
            {
                error = (string?)null,
                created = createdPipes.Count,
                fittings = fittingsConnected,
                errors
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to create sprinkler piping.");

        dynamic res = result;
        if (res.error is not null)
            return SkillResult.Fail((string)res.error);

        int count = res.created;
        int fits = res.fittings;
        List<string> errs = res.errors;
        var msg = $"Created {count} pipe segments and {fits} fittings for {routeData.TotalHeads} sprinkler heads.";
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
            ((dynamic)vizManager).ClearSprinklerPreview(PreviewTag);
            ((dynamic)vizManager).RefreshViews();
        }
        catch (Exception ex)
        {
            return SkillResult.Fail($"Failed to clear preview: {ex.Message}");
        }

        context.Extra.Remove(RouteDataKey);
        return SkillResult.Ok("Sprinkler routing preview cleared.");
    }

    #region Revit Helpers

    private static List<XYZ> CollectSprinklerPositions(Document doc, string? levelFilter)
    {
        var sprinklers = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Sprinklers)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            sprinklers = sprinklers.Where(s =>
            {
                var levelName = (doc.GetElement(s.LevelId) as Level)?.Name;
                return levelName?.Contains(levelFilter, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return sprinklers
            .Select(s => (s.Location as LocationPoint)?.Point)
            .Where(p => p is not null)
            .Cast<XYZ>()
            .ToList();
    }

    private static ElementId FindPipeTypeId(Document doc, string? typeName)
    {
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(PipeType))
            .Cast<PipeType>()
            .ToList();

        if (typeName is not null)
        {
            var match = types.FirstOrDefault(t =>
                t.Name.Contains(typeName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }

        return types.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private static ElementId FindPipingSystemTypeId(Document doc, string? systemName)
    {
        var systemTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .Cast<PipingSystemType>()
            .ToList();

        if (systemName is not null)
        {
            var match = systemTypes.FirstOrDefault(t =>
                t.Name.Contains(systemName, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Id;
        }

        var fireMatch = systemTypes.FirstOrDefault(t =>
            t.Name.Contains("Fire", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Sprinkler", StringComparison.OrdinalIgnoreCase));
        if (fireMatch is not null) return fireMatch.Id;

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
    {
        return (curve.Location as LocationCurve)?.Curve.GetEndPoint(0);
    }

    private static XYZ? GetEndPoint(MEPCurve curve)
    {
        return (curve.Location as LocationCurve)?.Curve.GetEndPoint(1);
    }

    private static void ConnectToBranchJunctions(
        Document doc,
        List<Pipe> pipes,
        SprinklerRouteData routeData,
        ref int fittingsConnected,
        List<string> errors)
    {
        if (routeData.FittingPositions.Count == 0 || pipes.Count < 2)
            return;

        foreach (var fp in routeData.FittingPositions)
        {
            var junctionPt = new XYZ(fp.X, fp.Y, fp.Z);

            var nearby = pipes
                .Where(p =>
                {
                    var loc = p.Location as LocationCurve;
                    if (loc?.Curve is not Line line) return false;
                    var proj = line.Project(junctionPt);
                    return proj is not null && proj.Distance < 0.5;
                })
                .OrderBy(p =>
                {
                    var loc = (LocationCurve)p.Location;
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
