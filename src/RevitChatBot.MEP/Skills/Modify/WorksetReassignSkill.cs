using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("workset_reassign",
    "Move elements to a different workset. Supports filtering by category and level, " +
    "or specifying explicit element IDs. Useful for organizing elements into correct worksets " +
    "after import, copy-paste, or team coordination.")]
[SkillParameter("target_workset", "string",
    "Target workset name (partial match supported). Required.",
    isRequired: true)]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs to reassign. Optional — use category/level filter instead.",
    isRequired: false)]
[SkillParameter("category", "string",
    "Category filter: 'ducts', 'pipes', 'equipment', 'fittings', 'cable_trays', 'conduits', " +
    "'sprinklers', 'air_terminals'.",
    isRequired: false)]
[SkillParameter("level", "string",
    "Level name filter (partial match). Optional.",
    isRequired: false)]
[SkillParameter("source_workset", "string",
    "Only move elements currently in this workset (partial match). Optional.",
    isRequired: false)]
public class WorksetReassignSkill : ISkill
{
    private static readonly Dictionary<string, BuiltInCategory> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ducts"] = BuiltInCategory.OST_DuctCurves,
        ["pipes"] = BuiltInCategory.OST_PipeCurves,
        ["equipment"] = BuiltInCategory.OST_MechanicalEquipment,
        ["fittings"] = BuiltInCategory.OST_DuctFitting,
        ["pipe_fittings"] = BuiltInCategory.OST_PipeFitting,
        ["cable_trays"] = BuiltInCategory.OST_CableTray,
        ["conduits"] = BuiltInCategory.OST_Conduit,
        ["sprinklers"] = BuiltInCategory.OST_Sprinklers,
        ["air_terminals"] = BuiltInCategory.OST_DuctTerminal,
    };

    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var targetWsName = parameters.GetValueOrDefault("target_workset")?.ToString();
        if (string.IsNullOrWhiteSpace(targetWsName))
            return SkillResult.Fail("'target_workset' is required.");

        var idsStr = parameters.GetValueOrDefault("element_ids")?.ToString();
        var categoryStr = parameters.GetValueOrDefault("category")?.ToString();
        var levelStr = parameters.GetValueOrDefault("level")?.ToString();
        var sourceWsName = parameters.GetValueOrDefault("source_workset")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            if (!document.IsWorkshared)
                return new { status = "error", message = "Model is not workshared.", moved = 0 };

            var targetWs = new FilteredWorksetCollector(document)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .FirstOrDefault(ws => ws.Name.Contains(targetWsName!, StringComparison.OrdinalIgnoreCase));

            if (targetWs is null)
                return new { status = "error", message = $"Workset '{targetWsName}' not found.", moved = 0 };

            List<Element> elements;
            if (!string.IsNullOrWhiteSpace(idsStr))
            {
                elements = idsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => document.GetElement(new ElementId(long.Parse(s.Trim()))))
                    .Where(e => e is not null)
                    .ToList()!;
            }
            else
            {
                var collector = new FilteredElementCollector(document)
                    .WhereElementIsNotElementType();

                if (!string.IsNullOrWhiteSpace(categoryStr) && CategoryMap.TryGetValue(categoryStr, out var bic))
                    collector = collector.OfCategory(bic);

                if (!string.IsNullOrWhiteSpace(levelStr))
                {
                    var level = new FilteredElementCollector(document)
                        .OfClass(typeof(Level))
                        .Cast<Level>()
                        .FirstOrDefault(l => l.Name.Contains(levelStr, StringComparison.OrdinalIgnoreCase));
                    if (level is not null)
                        collector = collector.WherePasses(new ElementLevelFilter(level.Id));
                }

                elements = collector.ToElements().ToList();
            }

            if (!string.IsNullOrWhiteSpace(sourceWsName))
            {
                var sourceWs = new FilteredWorksetCollector(document)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksets()
                    .FirstOrDefault(ws => ws.Name.Contains(sourceWsName, StringComparison.OrdinalIgnoreCase));
                if (sourceWs is not null)
                    elements = elements.Where(e => e.WorksetId == sourceWs.Id).ToList();
            }

            elements = elements.Where(e => e.WorksetId != targetWs.Id).ToList();
            if (elements.Count == 0)
                return new { status = "ok", message = "No elements need reassignment.", moved = 0 };

            using var tx = new Transaction(document, "Reassign worksets");
            tx.Start();
            try
            {
                var wsParam = elements[0].get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                int moved = 0;
                foreach (var elem in elements)
                {
                    var p = elem.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                    if (p is not null && !p.IsReadOnly)
                    {
                        p.Set(targetWs.Id.IntegerValue);
                        moved++;
                    }
                }
                tx.Commit();
                return new
                {
                    status = "ok",
                    message = $"Moved {moved} elements to workset '{targetWs.Name}'.",
                    moved
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message, moved = 0 };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }
}
