using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("workset_reassign",
    "Move elements to a different workset, or audit workset assignments. " +
    "Use action='audit' to get a breakdown of which elements are on which workset " +
    "(helps find misplaced elements). Use action='move' (default) to reassign.")]
[SkillParameter("action", "string",
    "'move' to reassign elements (default), 'audit' to report element counts per workset per category.",
    isRequired: false, allowedValues: new[] { "move", "audit" })]
[SkillParameter("target_workset", "string",
    "Target workset name (partial match). Required for action='move'. Ignored for 'audit'.",
    isRequired: false)]
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

        var actionParam = parameters.GetValueOrDefault("action")?.ToString()?.ToLowerInvariant() ?? "move";

        if (actionParam == "audit")
            return await AuditWorksetsAsync(context);

        var targetWsName = parameters.GetValueOrDefault("target_workset")?.ToString();
        if (string.IsNullOrWhiteSpace(targetWsName))
            return SkillResult.Fail("'target_workset' is required for action='move'.");

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

    private static async Task<SkillResult> AuditWorksetsAsync(SkillContext context)
    {
        var result = await context.RevitApiInvoker!(doc =>
        {
            var document = (Document)doc;
            if (!document.IsWorkshared)
                return new { isWorkshared = false, worksets = Array.Empty<object>() };

            var worksets = new FilteredWorksetCollector(document)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .ToList();

            var allElements = new FilteredElementCollector(document)
                .WhereElementIsNotElementType()
                .ToElements();

            var wsData = worksets.Select(ws =>
            {
                var elemsOnWs = allElements.Where(e => e.WorksetId == ws.Id).ToList();
                var byCat = elemsOnWs
                    .Where(e => e.Category is not null)
                    .GroupBy(e => e.Category!.Name)
                    .Select(g => new { category = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count)
                    .Take(10)
                    .ToList();

                return new
                {
                    worksetName = ws.Name,
                    worksetId = ws.Id.IntegerValue,
                    isOpen = ws.IsOpen,
                    owner = ws.Owner,
                    elementCount = elemsOnWs.Count,
                    topCategories = byCat
                };
            })
            .OrderByDescending(w => w.elementCount)
            .ToList();

            return new { isWorkshared = true, worksets = wsData.Cast<object>().ToArray() };
        });

        return SkillResult.Ok("Workset audit completed.", result);
    }
}
