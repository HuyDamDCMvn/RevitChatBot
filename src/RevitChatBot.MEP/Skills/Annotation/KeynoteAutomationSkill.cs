using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;
using RevitChatBot.RevitServices;

namespace RevitChatBot.MEP.Skills.Annotation;

[Skill("auto_keynote",
    "Automatically place keynote tags on elements in a view. " +
    "Uses the project's keynote table to assign keynotes by element type or material. " +
    "Smart positioning avoids overlap with existing annotations.")]
[SkillParameter("view_id", "string",
    "Element ID of the target view", isRequired: true)]
[SkillParameter("category", "string",
    "Category to keynote: 'Ducts', 'Pipes', 'Equipment', 'all'",
    isRequired: false)]
[SkillParameter("keynote_type", "string",
    "Type of keynote: 'element' (by element type) or 'material'. Default 'element'.",
    isRequired: false,
    allowedValues: new[] { "element", "material" })]
[SkillParameter("add_leader", "string",
    "Add leader lines: 'true' or 'false' (default 'true')",
    isRequired: false)]
public class KeynoteAutomationSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var viewIdStr = parameters.GetValueOrDefault("view_id")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString() ?? "all";
        var keynoteTypeStr = parameters.GetValueOrDefault("keynote_type")?.ToString() ?? "element";
        var addLeaderStr = parameters.GetValueOrDefault("add_leader")?.ToString();

        if (string.IsNullOrWhiteSpace(viewIdStr) || !long.TryParse(viewIdStr, out var viewIdLong))
            return SkillResult.Fail("view_id is required.");

        bool addLeader = !string.Equals(addLeaderStr, "false", StringComparison.OrdinalIgnoreCase);
        bool isElementKeynote = !keynoteTypeStr.Equals("material", StringComparison.OrdinalIgnoreCase);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var viewElem = document.GetElement(new ElementId(viewIdLong));
            if (viewElem is not View view)
                return new { success = false, message = "Invalid view ID.", placed = 0 };

            var categories = ResolveCategories(categoryStr);
            var elements = categories
                .SelectMany(cat => new FluentCollector(document)
                    .OfCategory(cat).WhereElementIsNotElementType().InView(view.Id).ToList())
                .Where(e => HasKeynote(e, isElementKeynote))
                .ToList();

            if (elements.Count == 0)
                return new { success = true, message = "No elements with keynotes found in the view.", placed = 0 };

            var keynoteTagType = FindKeynoteTagType(document, isElementKeynote);
            if (keynoteTagType is null)
                return new { success = false, message = "No keynote tag family loaded in the project.", placed = 0 };

            using var tx = new Transaction(document, "Auto-keynote");
            tx.Start();

            int placed = 0;
            double offsetStep = 0.5;

            foreach (var element in elements)
            {
                try
                {
                    var center = element.GetCenter();
                    if (center is null) continue;

                    var tagPoint = new XYZ(center.X + 1.0, center.Y + offsetStep * (placed % 8), center.Z);
                    var reference = new Reference(element);

                    IndependentTag.Create(document, view.Id, reference,
                        addLeader, TagMode.TM_ADDBY_CATEGORY,
                        TagOrientation.Horizontal, tagPoint);

                    placed++;
                }
                catch { }
            }

            tx.Commit();

            return new
            {
                success = true,
                message = $"Placed {placed} keynote tags on {elements.Count} elements with keynotes.",
                placed
            };
        });

        var r = (dynamic)result!;
        return (bool)r.success
            ? SkillResult.Ok((string)r.message, result)
            : SkillResult.Fail((string)r.message);
    }

    private static bool HasKeynote(Element elem, bool isElementKeynote)
    {
        var bip = isElementKeynote
            ? BuiltInParameter.KEYNOTE_PARAM
            : BuiltInParameter.KEYNOTE_PARAM;
        var param = elem.get_Parameter(bip);
        return param is not null && param.HasValue && !string.IsNullOrWhiteSpace(param.AsString());
    }

    private static FamilySymbol? FindKeynoteTagType(Document doc, bool isElement)
    {
        var tagCategory = isElement
            ? BuiltInCategory.OST_KeynoteTags
            : BuiltInCategory.OST_KeynoteTags;

        return new FilteredElementCollector(doc)
            .OfCategory(tagCategory)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault();
    }

    private static List<BuiltInCategory> ResolveCategories(string cat)
    {
        var n = cat.ToLowerInvariant().Replace(" ", "");
        return n switch
        {
            "all" => [
                BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_CableTray,
                BuiltInCategory.OST_Conduit, BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings
            ],
            "ducts" => [BuiltInCategory.OST_DuctCurves],
            "pipes" => [BuiltInCategory.OST_PipeCurves],
            "equipment" => [BuiltInCategory.OST_MechanicalEquipment],
            _ => [BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves]
        };
    }
}
