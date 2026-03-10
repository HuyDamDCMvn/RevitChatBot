using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("map_room_to_mep",
    "Map Room/Space parameters to MEP elements based on spatial relationships. " +
    "Detects which Room or Space each MEP element belongs to using point-in-room API, " +
    "with above-space offset and proximity fallback. Can write room number/name to a " +
    "target MEP parameter, or just report the mapping. Supports curve-based and point-based elements.")]
[SkillParameter("spatial_type", "string",
    "Source spatial type: room or space (default: room)",
    isRequired: false, allowedValues: ["room", "space"])]
[SkillParameter("mep_category", "string",
    "Target MEP category (duct/pipe/cabletray/conduit/equipment/fixture/all). Default: all",
    isRequired: false, allowedValues: ["duct", "pipe", "cabletray", "conduit", "equipment", "fixture", "all"])]
[SkillParameter("source_param", "string",
    "Source spatial parameter name to read (default: Number)", isRequired: false)]
[SkillParameter("target_param", "string",
    "Target MEP parameter name to write. If empty, report only (no write). Default: empty",
    isRequired: false)]
[SkillParameter("above_offset_mm", "number",
    "Vertical offset in mm to detect elements above spaces (0 = disabled). Default: 0",
    isRequired: false)]
[SkillParameter("level_name", "string", "Filter by level name (optional)", isRequired: false)]
[SkillParameter("mode", "string",
    "Mode: 'report' = read-only analysis, 'execute' = write parameters. Default: report",
    isRequired: false, allowedValues: ["report", "execute"])]
public class MapRoomToMepSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> MepCats = new()
    {
        ["duct"] = BuiltInCategory.OST_DuctCurves,
        ["pipe"] = BuiltInCategory.OST_PipeCurves,
        ["cabletray"] = BuiltInCategory.OST_CableTray,
        ["conduit"] = BuiltInCategory.OST_Conduit,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fixture"] = BuiltInCategory.OST_PlumbingFixtures,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        string spatialType = (parameters.GetValueOrDefault("spatial_type") as string)?.ToLowerInvariant() ?? "room";
        string mepCat = (parameters.GetValueOrDefault("mep_category") as string)?.ToLowerInvariant() ?? "all";
        string sourceParam = parameters.GetValueOrDefault("source_param") as string ?? "Number";
        string? targetParam = parameters.GetValueOrDefault("target_param") as string;
        double aboveOffsetMm = ParseDouble(parameters.GetValueOrDefault("above_offset_mm"), 0);
        string? levelName = parameters.GetValueOrDefault("level_name") as string;
        string mode = (parameters.GetValueOrDefault("mode") as string)?.ToLowerInvariant() ?? "report";

        if (string.IsNullOrWhiteSpace(targetParam)) mode = "report";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            double aboveOffsetFt = aboveOffsetMm / 304.8;

            var spatials = CollectSpatialElements(document, spatialType, levelName);
            if (spatials.Count == 0)
                return new { error = $"No {spatialType}s found." };

            var mepElements = CollectMepElements(document, mepCat, levelName);
            if (mepElements.Count == 0)
                return new { error = $"No MEP elements found for '{mepCat}'." };

            var mappings = new List<object>();
            int mapped = 0, unmapped = 0, written = 0;

            bool doWrite = mode == "execute" && !string.IsNullOrWhiteSpace(targetParam);
            Transaction? tx = null;
            if (doWrite)
            {
                tx = new Transaction(document, "Map Room/Space to MEP");
                tx.Start();
            }

            try
            {
                foreach (var mep in mepElements)
                {
                    var points = GetDetectionPoints(mep);
                    if (points.Count == 0) { unmapped++; continue; }

                    string? matchedValue = null;
                    string? matchedName = null;
                    long matchedSpatialId = 0;

                    foreach (var spatial in spatials)
                    {
                        bool found = false;

                        foreach (var pt in points)
                        {
                            if (IsPointInSpatial(spatial, spatialType, pt))
                            {
                                found = true;
                                break;
                            }

                            if (aboveOffsetFt > 0)
                            {
                                var below = new XYZ(pt.X, pt.Y, pt.Z - aboveOffsetFt);
                                if (IsPointInSpatial(spatial, spatialType, below))
                                {
                                    found = true;
                                    break;
                                }
                            }
                        }

                        if (found)
                        {
                            matchedValue = GetSpatialParamValue(spatial, sourceParam);
                            matchedName = spatial.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                            matchedSpatialId = spatial.Id.Value;
                            break;
                        }
                    }

                    if (matchedValue is not null)
                    {
                        mapped++;
                        if (doWrite && targetParam is not null)
                        {
                            var p = mep.LookupParameter(targetParam);
                            if (p is not null && !p.IsReadOnly)
                            {
                                try { p.Set(matchedValue); written++; }
                                catch { /* parameter type mismatch */ }
                            }
                        }
                    }
                    else
                    {
                        unmapped++;
                    }

                    mappings.Add(new
                    {
                        mepElementId = mep.Id.Value,
                        mepCategory = mep.Category?.Name ?? "Unknown",
                        spatialId = matchedSpatialId,
                        spatialValue = matchedValue ?? "(unmapped)",
                        spatialName = matchedName ?? ""
                    });
                }

                if (tx is not null)
                {
                    if (written > 0) tx.Commit();
                    else tx.RollBack();
                }
            }
            catch
            {
                tx?.RollBack();
                throw;
            }

            var byRoom = mappings.Cast<dynamic>()
                .Where(m => (string)m.spatialValue != "(unmapped)")
                .GroupBy(m => (string)m.spatialValue)
                .Select(g => new { spatialValue = g.Key, mepCount = g.Count() })
                .OrderByDescending(g => g.mepCount)
                .Take(30).ToList();

            return new
            {
                spatialType,
                sourceParameter = sourceParam,
                targetParameter = targetParam ?? "(report only)",
                mode,
                totalSpatials = spatials.Count,
                totalMepElements = mepElements.Count,
                mapped,
                unmapped,
                written,
                aboveOffsetMm,
                summaryByRoom = byRoom,
                mappings = mappings.Take(100).ToList()
            };
        });

        if (result is IDictionary<string, object> dict && dict.ContainsKey("error"))
            return SkillResult.Fail(dict["error"]?.ToString() ?? "Error");

        string msg = mode == "execute"
            ? "Room-to-MEP mapping executed."
            : "Room-to-MEP mapping analysis completed (report only).";
        return SkillResult.Ok(msg, result);
    }

    private static List<Element> CollectSpatialElements(Document doc, string type, string? levelName)
    {
        var bic = type == "space"
            ? BuiltInCategory.OST_MEPSpaces
            : BuiltInCategory.OST_Rooms;

        var elements = new FilteredElementCollector(doc)
            .OfCategory(bic)
            .WhereElementIsNotElementType()
            .ToList();

        if (!string.IsNullOrEmpty(levelName))
        {
            elements = elements.Where(e =>
            {
                var lvlId = e is Room r ? r.LevelId
                    : e is Space s ? s.LevelId
                    : ElementId.InvalidElementId;
                return doc.GetElement(lvlId)?.Name?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return elements.Where(e =>
        {
            var area = e.get_Parameter(BuiltInParameter.ROOM_AREA)?.AsDouble() ?? 0;
            return area > 0;
        }).ToList();
    }

    private static List<Element> CollectMepElements(Document doc, string cat, string? levelName)
    {
        var cats = cat == "all"
            ? MepCats.Values.ToList()
            : MepCats.TryGetValue(cat, out var bic) ? [bic] : [];

        var elements = new List<Element>();
        foreach (var c in cats)
            elements.AddRange(new FilteredElementCollector(doc)
                .OfCategory(c).WhereElementIsNotElementType().ToList());

        if (!string.IsNullOrEmpty(levelName))
        {
            elements = elements.Where(e =>
            {
                var p = e.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                if (p?.AsElementId() is not { } id || id == ElementId.InvalidElementId) return false;
                return doc.GetElement(id)?.Name?.Equals(levelName, StringComparison.OrdinalIgnoreCase) == true;
            }).ToList();
        }

        return elements;
    }

    private static List<XYZ> GetDetectionPoints(Element elem)
    {
        var points = new List<XYZ>();

        if (elem is FamilyInstance fi)
        {
            if (fi.HasSpatialElementCalculationPoint)
            {
                var calcPt = fi.GetSpatialElementCalculationPoint();
                if (calcPt is not null) points.Add(calcPt);
            }
        }

        if (elem.Location is LocationPoint lp)
            points.Add(lp.Point);
        else if (elem.Location is LocationCurve lc)
        {
            var curve = lc.Curve;
            points.Add(curve.Evaluate(0.5, true));
            points.Add(curve.GetEndPoint(0));
            points.Add(curve.GetEndPoint(1));
        }

        if (points.Count == 0)
        {
            var bb = elem.get_BoundingBox(null);
            if (bb is not null)
                points.Add((bb.Min + bb.Max) / 2);
        }

        return points;
    }

    private static bool IsPointInSpatial(Element spatial, string type, XYZ point)
    {
        try
        {
            if (type == "room" && spatial is Room room)
                return room.IsPointInRoom(point);
            if (type == "space" && spatial is Space space)
                return space.IsPointInSpace(point);
        }
        catch { /* API can throw for degenerate geometry */ }
        return false;
    }

    private static string? GetSpatialParamValue(Element spatial, string paramName)
    {
        if (paramName.Equals("Number", StringComparison.OrdinalIgnoreCase))
            return spatial.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
        if (paramName.Equals("Name", StringComparison.OrdinalIgnoreCase))
            return spatial.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();

        var p = spatial.LookupParameter(paramName);
        return p?.AsValueString() ?? p?.AsString();
    }

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }
}
