using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("create_workset",
    "Create a new user workset in a workshared model, or list existing worksets. " +
    "Use for organizing elements by discipline, zone, or team.")]
[SkillParameter("action", "string",
    "'create' to create a new workset, 'list' to list all worksets.",
    isRequired: true,
    allowedValues: new[] { "create", "list" })]
[SkillParameter("workset_name", "string",
    "Name for the new workset. Required when action='create'.",
    isRequired: false)]
public class CreateWorksetSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var action = parameters.GetValueOrDefault("action")?.ToString()?.ToLower() ?? "list";
        var wsName = parameters.GetValueOrDefault("workset_name")?.ToString();

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            if (!document.IsWorkshared)
                return new { status = "error", message = "Model is not workshared.", worksets = new List<object>() };

            if (action == "list")
            {
                var wsList = new FilteredWorksetCollector(document)
                    .OfKind(WorksetKind.UserWorkset)
                    .ToWorksets()
                    .Select(ws =>
                    {
                        var elemCount = new FilteredElementCollector(document)
                            .WhereElementIsNotElementType()
                            .WherePasses(new ElementWorksetFilter(ws.Id))
                            .GetElementCount();
                        return new
                        {
                            id = ws.Id.IntegerValue,
                            name = ws.Name,
                            isOpen = ws.IsOpen,
                            isDefault = ws.IsDefaultWorkset,
                            elementCount = elemCount
                        };
                    })
                    .OrderBy(w => w.name)
                    .Cast<object>()
                    .ToList();

                return new { status = "ok", message = $"Found {wsList.Count} worksets.", worksets = wsList };
            }

            if (string.IsNullOrWhiteSpace(wsName))
                return new { status = "error", message = "'workset_name' is required for 'create'.", worksets = new List<object>() };

            var existing = new FilteredWorksetCollector(document)
                .OfKind(WorksetKind.UserWorkset)
                .ToWorksets()
                .Any(ws => ws.Name.Equals(wsName, StringComparison.OrdinalIgnoreCase));

            if (existing)
                return new { status = "error", message = $"Workset '{wsName}' already exists.", worksets = new List<object>() };

            using var tx = new Transaction(document, "Create workset");
            tx.Start();
            try
            {
                var newWs = Workset.Create(document, wsName!);
                tx.Commit();
                return new
                {
                    status = "ok",
                    message = $"Workset '{newWs.Name}' created (ID: {newWs.Id.IntegerValue}).",
                    worksets = new List<object> { new { id = newWs.Id.IntegerValue, name = newWs.Name } }
                };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", message = ex.Message, worksets = new List<object>() };
            }
        });

        dynamic res = result!;
        return res.status == "ok"
            ? SkillResult.Ok(res.message, result)
            : SkillResult.Fail(res.message);
    }
}
