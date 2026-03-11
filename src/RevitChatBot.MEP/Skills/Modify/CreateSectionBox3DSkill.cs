using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("create_section_box_3d",
    "Create a focused 3D view with a section box tightly fitted around specified elements. " +
    "The section box is sized to fit the elements' bounding boxes plus adjustable padding. " +
    "Useful for inspecting equipment rooms, riser shafts, or specific MEP systems in 3D. " +
    "Accepts element IDs, a room/space name, or level+system_name combination.")]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to focus on.",
    isRequired: false)]
[SkillParameter("room_name", "string",
    "Focus on elements within a room/space (e.g. 'Mechanical Room', 'Riser'). " +
    "Finds the room bounding box and all MEP elements within it.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Level name. Combined with system_name to focus on a system segment.",
    isRequired: false)]
[SkillParameter("system_name", "string",
    "System name filter (e.g. 'Supply Air', 'Chilled Water'). Combined with level.",
    isRequired: false)]
[SkillParameter("padding_feet", "number",
    "Extra padding around bounding box in feet. Default: 3.0 (~1m).",
    isRequired: false)]
[SkillParameter("view_name", "string",
    "Name for the new 3D view. Default: auto-generated 'CB3D_{timestamp}'.",
    isRequired: false)]
[SkillParameter("base_3d_view", "string",
    "Name of existing 3D view to copy visual style from. If omitted, uses default settings.",
    isRequired: false)]
public class CreateSectionBox3DSkill : ISkill
{
    private static readonly BuiltInCategory[] MepCategories =
    [
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
        BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_FlexPipeCurves,
        BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting,
        BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_MechanicalEquipment,
        BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures,
        BuiltInCategory.OST_PlumbingFixtures, BuiltInCategory.OST_Sprinklers,
        BuiltInCategory.OST_CableTray, BuiltInCategory.OST_Conduit,
    ];

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var roomName = parameters.GetValueOrDefault("room_name")?.ToString();
        var level = parameters.GetValueOrDefault("level")?.ToString();
        var systemName = parameters.GetValueOrDefault("system_name")?.ToString();
        var padding = ParseDouble(parameters.GetValueOrDefault("padding_feet"), 3.0);
        var viewName = parameters.GetValueOrDefault("view_name")?.ToString();
        var base3dView = parameters.GetValueOrDefault("base_3d_view")?.ToString();

        if (string.IsNullOrWhiteSpace(idsStr) && string.IsNullOrWhiteSpace(roomName)
            && string.IsNullOrWhiteSpace(level) && string.IsNullOrWhiteSpace(systemName))
            return SkillResult.Fail("Provide element_ids, room_name, or level/system_name to define the focus area.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var elements = ResolveElements(document, idsStr, roomName, level, systemName);
            if (elements.Count == 0)
                return new SectionBoxResult { Error = "No elements found for the given criteria." };

            var (bbMin, bbMax) = ComputeUnionBoundingBox(elements);
            if (bbMin is null || bbMax is null)
                return new SectionBoxResult { Error = "Could not compute bounding box for the selected elements." };

            var paddingVec = new XYZ(padding, padding, padding);
            var sectionBox = new BoundingBoxXYZ
            {
                Min = bbMin - paddingVec,
                Max = bbMax + paddingVec,
                Enabled = true
            };

            var viewFamilyTypeId = GetThreeDViewFamilyTypeId(document);
            if (viewFamilyTypeId == ElementId.InvalidElementId)
                return new SectionBoxResult { Error = "No 3D view family type found in the model." };

            using var tx = new Transaction(document, "Create 3D section box view");
            tx.Start();

            var newView = View3D.CreateIsometric(document, viewFamilyTypeId);

            if (!string.IsNullOrWhiteSpace(base3dView))
            {
                var baseView = new FilteredElementCollector(document)
                    .OfClass(typeof(View3D))
                    .Cast<View3D>()
                    .FirstOrDefault(v => v.Name.Contains(base3dView, StringComparison.OrdinalIgnoreCase) && !v.IsTemplate);
                if (baseView?.ViewTemplateId is { } vtId && vtId != ElementId.InvalidElementId)
                    newView.ViewTemplateId = vtId;
            }

            newView.SetSectionBox(sectionBox);

            var finalName = viewName ?? $"CB3D_{DateTime.Now:HHmmss}";
            finalName = EnsureUniqueName(document, finalName);
            newView.Name = finalName;

            tx.Commit();

            return new SectionBoxResult
            {
                Success = true,
                ViewId = newView.Id.Value,
                ViewName = newView.Name,
                ElementCount = elements.Count,
                PaddingFeet = padding,
                SectionBoxMin = FormatXyz(sectionBox.Min),
                SectionBoxMax = FormatXyz(sectionBox.Max)
            };
        });

        var res = result as SectionBoxResult;
        if (res is null || !res.Success)
            return SkillResult.Fail(res?.Error ?? "Failed to create 3D section box view.");

        return SkillResult.Ok(
            $"Created 3D view '{res.ViewName}' with section box around {res.ElementCount} elements " +
            $"(padding: {res.PaddingFeet} ft).",
            result);
    }

    private static List<Element> ResolveElements(Document doc, string? idsStr, string? roomName, string? level, string? systemName)
    {
        if (!string.IsNullOrWhiteSpace(idsStr))
        {
            return idsStr
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var id) ? id : -1)
                .Where(id => id > 0)
                .Select(id => doc.GetElement(new ElementId(id)))
                .Where(e => e is not null)
                .ToList()!;
        }

        if (!string.IsNullOrWhiteSpace(roomName))
        {
            var allInRoom = new List<Element>();
            foreach (var cat in MepCategories)
            {
                var elems = new FluentCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType()
                    .InRoom(roomName)
                    .ToList();
                allInRoom.AddRange(elems);
            }

            if (allInRoom.Count == 0)
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Where(r => r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                                ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true)
                    .ToList();
                if (rooms.Count == 0)
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WhereElementIsNotElementType()
                        .Where(s => s.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                                    ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                }
                return rooms;
            }
            return allInRoom;
        }

        var results = new List<Element>();
        foreach (var cat in MepCategories)
        {
            var collector = new FluentCollector(doc)
                .OfCategory(cat)
                .WhereElementIsNotElementType();

            if (!string.IsNullOrWhiteSpace(level))
                collector.OnLevel(level);
            if (!string.IsNullOrWhiteSpace(systemName))
                collector.InSystem(systemName);

            results.AddRange(collector.ToList());
        }
        return results;
    }

    private static (XYZ? Min, XYZ? Max) ComputeUnionBoundingBox(List<Element> elements)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        bool found = false;

        foreach (var elem in elements)
        {
            var bb = elem.get_BoundingBox(null);
            if (bb is null) continue;
            found = true;

            minX = Math.Min(minX, bb.Min.X);
            minY = Math.Min(minY, bb.Min.Y);
            minZ = Math.Min(minZ, bb.Min.Z);
            maxX = Math.Max(maxX, bb.Max.X);
            maxY = Math.Max(maxY, bb.Max.Y);
            maxZ = Math.Max(maxZ, bb.Max.Z);
        }

        if (!found) return (null, null);
        return (new XYZ(minX, minY, minZ), new XYZ(maxX, maxY, maxZ));
    }

    private static ElementId GetThreeDViewFamilyTypeId(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(vft => vft.ViewFamily == ViewFamily.ThreeDimensional)
            ?.Id ?? ElementId.InvalidElementId;
    }

    private static string EnsureUniqueName(Document doc, string baseName)
    {
        var existingNames = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Select(v => v.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName)) return baseName;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!existingNames.Contains(candidate)) return candidate;
        }
        return $"{baseName}_{Guid.NewGuid().ToString()[..6]}";
    }

    private static string FormatXyz(XYZ pt) =>
        $"({pt.X:F2}, {pt.Y:F2}, {pt.Z:F2})";

    private static double ParseDouble(object? value, double fallback)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is long l) return l;
        if (value is string s && double.TryParse(s, out var parsed)) return parsed;
        return fallback;
    }

    private class SectionBoxResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public long ViewId { get; set; }
        public string ViewName { get; set; } = "";
        public int ElementCount { get; set; }
        public double PaddingFeet { get; set; }
        public string SectionBoxMin { get; set; } = "";
        public string SectionBoxMax { get; set; } = "";
    }
}
