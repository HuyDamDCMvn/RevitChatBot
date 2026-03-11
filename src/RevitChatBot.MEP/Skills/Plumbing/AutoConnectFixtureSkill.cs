using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;
using RevitChatBot.MEP.Skills.Coordination.Routing;

namespace RevitChatBot.MEP.Skills.Plumbing;

/// <summary>
/// Auto-connect plumbing fixtures with drainage piping via tree-topology routing.
///
/// Three modes:
///   preview — compute route and render in DirectContext3D (no model changes)
///   create  — build actual pipes and fittings with slope
///   clear   — remove DirectContext3D preview overlay
///
/// Typical agent flow: preview → user confirms → create → clear
/// </summary>
[Skill("auto_connect_fixtures",
    "Auto-connect plumbing fixtures with drainage piping. " +
    "Mode 'preview' shows proposed route in 3D view via DirectContext3D. " +
    "Mode 'create' builds actual pipes and fittings with gravity slope. " +
    "Mode 'clear' removes preview. " +
    "Computes tree-topology layout with slope for gravity drainage. " +
    "Pipe sizing uses UPC/IPC fixture unit (DFU) method.")]
[SkillParameter("mode", "string",
    "Operation mode. Default: 'preview'.",
    isRequired: false,
    allowedValues: new[] { "preview", "create", "clear" })]
[SkillParameter("level", "string",
    "Filter fixtures by level name. Required for preview/create.",
    isRequired: false)]
[SkillParameter("slope_percent", "number",
    "Drain slope in percent. Default: 1.0 (1%).", isRequired: false)]
[SkillParameter("pipe_type_name", "string",
    "Pipe type name for creation. If omitted, uses first available PipeType.",
    isRequired: false)]
[SkillParameter("system_type_name", "string",
    "Piping system type name (e.g. 'Sanitary'). If omitted, auto-detects.",
    isRequired: false)]
public class AutoConnectFixtureSkill : ISkill
{
    private const string RouteDataKey = "fixture_route_data";
    private const string PreviewTag = "fixture_preview";

    /// <summary>Default DFU per fixture category when connector data unavailable.</summary>
    private static readonly Dictionary<string, double> DefaultDfu = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lavatory"] = 1, ["basin"] = 1,
        ["sink"] = 2, ["kitchen"] = 2,
        ["water closet"] = 4, ["toilet"] = 4, ["wc"] = 4,
        ["urinal"] = 2,
        ["bathtub"] = 2, ["bath"] = 2,
        ["shower"] = 2,
        ["floor drain"] = 2,
        ["washing machine"] = 3, ["laundry"] = 3,
        ["dishwasher"] = 2,
    };

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
        var slopePercent = ParseDouble(parameters.GetValueOrDefault("slope_percent"), 1.0);

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Document)docObj;
            var fixtures = CollectFixtures(doc, levelFilter);

            if (fixtures.Count == 0)
                return new { error = $"No plumbing fixtures found{(levelFilter != null ? $" on level '{levelFilter}'" : "")}." };

            var routeData = DrainageRoutingAlgorithm.ComputeRoute(fixtures, slopePercent, levelFilter);
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
                branchDN = $"DN{routeData.BranchSizeMm}",
                mainDN = $"DN{routeData.MainSizeMm}",
                slopePercent,
                routeData.LevelName,
                previewRendered = rendered >= 0,
                renderedGeometries = rendered
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to compute fixture routing.");

        dynamic r = result;
        if (r.error is not null)
            return SkillResult.Fail((string)r.error);

        var preview = (bool)r.previewRendered;
        var msg = $"Fixture drainage routing preview computed: " +
                  $"{(int)r.TotalEndpoints} fixtures, " +
                  $"{(int)r.TotalSegments} pipe segments ({r.totalLengthM}m), " +
                  $"{(int)r.TotalFittings} fittings. " +
                  $"Branch {r.branchDN}, Main {r.mainDN}. " +
                  $"Slope: {r.slopePercent}%.";

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

        var routeData = context.Extra.GetValueOrDefault(RouteDataKey) as MepAutoRouteData;
        if (routeData is null || routeData.TotalSegments == 0)
        {
            var previewResult = await HandlePreview(context, parameters, cancellationToken);
            if (!previewResult.Success) return previewResult;
            routeData = context.Extra.GetValueOrDefault(RouteDataKey) as MepAutoRouteData;
            if (routeData is null || routeData.TotalSegments == 0)
                return SkillResult.Fail("No route data available. Run preview first.");
        }

        var pipeTypeName = parameters.GetValueOrDefault("pipe_type_name")?.ToString();
        var systemTypeName = parameters.GetValueOrDefault("system_type_name")?.ToString();

        var result = await context.RevitApiInvoker(docObj =>
        {
            var doc = (Document)docObj;

            var pipeTypeId = FindPipeTypeId(doc, pipeTypeName);
            var systemTypeId = FindSanitarySystemTypeId(doc, systemTypeName);
            var levelId = FindLevelId(doc, routeData.LevelName);

            if (pipeTypeId == ElementId.InvalidElementId)
                return new { error = "No PipeType found in model.", created = 0, fittings = 0, errors = new List<string>() };
            if (systemTypeId == ElementId.InvalidElementId)
                return new { error = "No PipingSystemType found in model.", created = 0, fittings = 0, errors = new List<string>() };

            using var tx = new Transaction(doc, "Auto-Connect Fixtures");
            tx.Start();

            var createdPipes = new List<Pipe>();
            var errors = new List<string>();

            var allSegments = routeData.MainSegments
                .Select(s => (seg: s, diamMm: routeData.MainSizeMm))
                .Concat(routeData.BranchSegments.Select(s => (seg: s, diamMm: routeData.BranchSizeMm)));

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

                    var slopeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                    if (slopeParam is not null && !slopeParam.IsReadOnly && routeData.SlopeRatio > 0)
                        slopeParam.Set(routeData.SlopeRatio);

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
            TryClearPreview(context);

            return new
            {
                error = (string?)null,
                created = createdPipes.Count,
                fittings = fittingsConnected,
                errors
            };
        });

        if (result is null)
            return SkillResult.Fail("Failed to create drainage piping.");

        dynamic res = result;
        if (res.error is not null)
            return SkillResult.Fail((string)res.error);

        int count = res.created;
        int fits = res.fittings;
        List<string> errs = res.errors;
        var msg = $"Created {count} pipe segments and {fits} fittings for {routeData.TotalEndpoints} fixtures.";
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
        return SkillResult.Ok("Fixture drainage routing preview cleared.");
    }

    #region Revit Helpers

    private static List<(XYZ Position, double Dfu)> CollectFixtures(Document doc, string? levelFilter)
    {
        var fixtures = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_PlumbingFixtures)
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .ToList();

        if (!string.IsNullOrWhiteSpace(levelFilter))
        {
            fixtures = fixtures.Where(f =>
            {
                var levelName = (doc.GetElement(f.LevelId) as Level)?.Name;
                return levelName?.Contains(levelFilter, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return fixtures
            .Select(f =>
            {
                var pos = (f.Location as LocationPoint)?.Point;
                if (pos is null) return ((XYZ?)null, 0.0);

                double dfu = EstimateDfu(f, doc);
                return (pos, dfu);
            })
            .Where(x => x.Item1 is not null)
            .Select(x => (x.Item1!, x.Item2))
            .ToList();
    }

    private static double EstimateDfu(FamilyInstance fixture, Document doc)
    {
        string familyName = "";
        if (doc.GetElement(fixture.GetTypeId()) is FamilySymbol fs)
            familyName = fs.FamilyName;

        string typeName = fixture.Name;
        string combined = $"{familyName} {typeName}".ToLowerInvariant();

        foreach (var (keyword, dfu) in DefaultDfu)
        {
            if (combined.Contains(keyword))
                return dfu;
        }

        return 2.0; // generic fixture fallback
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

    private static ElementId FindSanitarySystemTypeId(Document doc, string? systemName)
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

        var sanitaryMatch = systemTypes.FirstOrDefault(t =>
            t.Name.Contains("Sanitary", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Drain", StringComparison.OrdinalIgnoreCase) ||
            t.Name.Contains("Waste", StringComparison.OrdinalIgnoreCase));
        if (sanitaryMatch is not null) return sanitaryMatch.Id;

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
        Document doc, List<Pipe> pipes, MepAutoRouteData routeData,
        ref int fittingsConnected, List<string> errors)
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
