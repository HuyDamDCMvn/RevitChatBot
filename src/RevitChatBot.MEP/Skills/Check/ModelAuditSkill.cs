using Autodesk.Revit.DB;
using Nice3point.Revit.Extensions;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Check;

[Skill("model_audit",
    "Comprehensive model audit. Counts warnings, MEP elements by category, detects duplicate room numbers, " +
    "in-place families, and views with excessive detail lines. Returns a full health report.")]
[SkillParameter("scope", "string",
    "Scope: 'active_view' to limit to elements visible in the current view, " +
    "'entire_model' to include all (default: entire_model)",
    isRequired: false, allowedValues: new[] { "active_view", "entire_model" })]
public class ModelAuditSkill : ISkill
{
    private static readonly (string Label, BuiltInCategory Cat)[] MepCategories =
    {
        ("Ducts", BuiltInCategory.OST_DuctCurves),
        ("Pipes", BuiltInCategory.OST_PipeCurves),
        ("Flex Ducts", BuiltInCategory.OST_FlexDuctCurves),
        ("Flex Pipes", BuiltInCategory.OST_FlexPipeCurves),
        ("Duct Fittings", BuiltInCategory.OST_DuctFitting),
        ("Pipe Fittings", BuiltInCategory.OST_PipeFitting),
        ("Duct Accessories", BuiltInCategory.OST_DuctAccessory),
        ("Pipe Accessories", BuiltInCategory.OST_PipeAccessory),
        ("Mechanical Equipment", BuiltInCategory.OST_MechanicalEquipment),
        ("Electrical Equipment", BuiltInCategory.OST_ElectricalEquipment),
        ("Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures),
        ("Sprinklers", BuiltInCategory.OST_Sprinklers)
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var scope = ViewScopeHelper.ParseScope(parameters, ViewScopeHelper.EntireModel);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            // --- Warnings ---
            var warnings = document.GetWarnings();
            var warningDescriptions = new List<string>();
            foreach (FailureMessage fm in warnings)
            {
                if (fm?.GetDescriptionText() is { } desc)
                    warningDescriptions.Add(desc);
            }

            var topWarnings = warningDescriptions
                .GroupBy(d => d)
                .OrderByDescending(g => g.Count())
                .Take(20)
                .Select(g => new { description = g.Key, count = g.Count() })
                .ToList();

            // --- Element counts ---
            var totalElements = ViewScopeHelper.CreateCollector(document, scope)
                .WhereElementIsNotElementType().GetElementCount();

            var mepCounts = new List<object>();
            foreach (var (label, category) in MepCategories)
            {
                var count = ViewScopeHelper.CreateCollector(document, scope)
                    .OfCategory(category).WhereElementIsNotElementType().GetElementCount();
                mepCounts.Add(new { category = label, count });
            }

            // --- Duplicate Room Numbers ---
            var duplicateRooms = FindDuplicateRooms(document);

            // --- In-Place Families ---
            var inPlaceFamilies = FindInPlaceFamilies(document);

            // --- Views with excessive lines (top 10) ---
            var heavyViews = FindViewsWithExcessiveLines(document);

            return new
            {
                totalWarnings = warningDescriptions.Count,
                topWarnings,
                totalElements,
                mepCounts,
                duplicateRooms,
                inPlaceFamilies,
                heavyViews
            };
        });

        return SkillResult.Ok("Model audit completed.", result);
    }

    private static object FindDuplicateRooms(Document doc)
    {
        try
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToList();

            var duplicates = rooms
                .Select(r => new
                {
                    Number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                    Name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                    Id = r.Id.Value
                })
                .Where(r => !string.IsNullOrWhiteSpace(r.Number))
                .GroupBy(r => r.Number)
                .Where(g => g.Count() > 1)
                .Select(g => new
                {
                    roomNumber = g.Key,
                    count = g.Count(),
                    rooms = g.Select(r => new { r.Id, r.Name }).ToList()
                })
                .ToList();

            return new { totalRooms = rooms.Count, duplicateCount = duplicates.Count, duplicates };
        }
        catch { return new { totalRooms = 0, duplicateCount = 0, duplicates = Array.Empty<object>() }; }
    }

    private static object FindInPlaceFamilies(Document doc)
    {
        try
        {
            var inPlace = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .Where(fi => fi.Symbol?.Family?.IsInPlace == true)
                .GroupBy(fi => fi.Symbol.Family.Name)
                .Select(g => new { familyName = g.Key, instanceCount = g.Count() })
                .OrderByDescending(g => g.instanceCount)
                .Take(20)
                .ToList();

            return new { inPlaceCount = inPlace.Sum(f => f.instanceCount), families = inPlace };
        }
        catch { return new { inPlaceCount = 0, families = Array.Empty<object>() }; }
    }

    private static object FindViewsWithExcessiveLines(Document doc)
    {
        try
        {
            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && v is not ViewSchedule
                            && v.ViewType is ViewType.FloorPlan or ViewType.CeilingPlan
                               or ViewType.Section or ViewType.Elevation
                               or ViewType.EngineeringPlan)
                .Take(50)
                .ToList();

            var viewLineCounts = views
                .Select(v =>
                {
                    try
                    {
                        var lineCount = new FilteredElementCollector(doc, v.Id)
                            .OfCategory(BuiltInCategory.OST_Lines)
                            .GetElementCount();
                        return new { viewName = v.Name, viewType = v.ViewType.ToString(), lineCount };
                    }
                    catch { return new { viewName = v.Name, viewType = v.ViewType.ToString(), lineCount = 0 }; }
                })
                .OrderByDescending(v => v.lineCount)
                .Take(10)
                .Where(v => v.lineCount > 100)
                .ToList();

            return new { checkedViews = views.Count, heavyViews = viewLineCounts };
        }
        catch { return new { checkedViews = 0, heavyViews = Array.Empty<object>() }; }
    }
}
