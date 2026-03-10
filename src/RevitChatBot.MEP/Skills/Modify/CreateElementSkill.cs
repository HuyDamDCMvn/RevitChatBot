using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("create_element",
    "Create a new MEP element in the Revit model. " +
    "Supports creating ducts, pipes, and placing equipment.")]
[SkillParameter("element_type", "string",
    "Type of element to create: duct, pipe",
    isRequired: true,
    allowedValues: new[] { "duct", "pipe" })]
[SkillParameter("start_x", "number", "Start point X coordinate in feet", isRequired: true)]
[SkillParameter("start_y", "number", "Start point Y coordinate in feet", isRequired: true)]
[SkillParameter("start_z", "number", "Start point Z coordinate in feet", isRequired: true)]
[SkillParameter("end_x", "number", "End point X coordinate in feet", isRequired: true)]
[SkillParameter("end_y", "number", "End point Y coordinate in feet", isRequired: true)]
[SkillParameter("end_z", "number", "End point Z coordinate in feet", isRequired: true)]
[SkillParameter("system_type", "string",
    "MEP system type (e.g. 'Supply Air', 'Return Air', 'Hydronic Supply')",
    isRequired: false)]
public class CreateElementSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var elementType = parameters.GetValueOrDefault("element_type")?.ToString() ?? "duct";

        if (!TryParsePoint(parameters, "start", out var startPt) ||
            !TryParsePoint(parameters, "end", out var endPt))
            return SkillResult.Fail("Invalid start/end coordinates.");

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            using var tx = new Transaction(document, $"Create {elementType}");
            tx.Start();

            ElementId? createdId = elementType switch
            {
                "duct" => CreateDuct(document, startPt, endPt),
                "pipe" => CreatePipe(document, startPt, endPt),
                _ => null
            };

            if (createdId is null)
            {
                tx.RollBack();
                return (object?)null;
            }

            tx.Commit();
            return new { id = createdId.Value, type = elementType };
        });

        return result is not null
            ? SkillResult.Ok($"Created {elementType} successfully.", result)
            : SkillResult.Fail($"Failed to create {elementType}. Check parameters and available types.");
    }

    private static ElementId? CreateDuct(Document doc, XYZ start, XYZ end)
    {
        var ductType = new FilteredElementCollector(doc)
            .OfClass(typeof(DuctType))
            .FirstOrDefault();
        if (ductType is null) return null;

        var level = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .FirstOrDefault();
        if (level is null) return null;

        var systemType = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystemType))
            .FirstOrDefault();
        if (systemType is null) return null;

        var duct = Duct.Create(doc, systemType.Id, ductType.Id, level.Id, start, end);
        return duct?.Id;
    }

    private static ElementId? CreatePipe(Document doc, XYZ start, XYZ end)
    {
        var pipeType = new FilteredElementCollector(doc)
            .OfClass(typeof(PipeType))
            .FirstOrDefault();
        if (pipeType is null) return null;

        var level = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .FirstOrDefault();
        if (level is null) return null;

        var systemType = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .FirstOrDefault();
        if (systemType is null) return null;

        var pipe = Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id, start, end);
        return pipe?.Id;
    }

    private static bool TryParsePoint(
        Dictionary<string, object?> parameters,
        string prefix,
        out XYZ point)
    {
        point = XYZ.Zero;
        if (!TryGetDouble(parameters, $"{prefix}_x", out var x) ||
            !TryGetDouble(parameters, $"{prefix}_y", out var y) ||
            !TryGetDouble(parameters, $"{prefix}_z", out var z))
            return false;

        point = new XYZ(x, y, z);
        return true;
    }

    private static bool TryGetDouble(
        Dictionary<string, object?> parameters,
        string key,
        out double value)
    {
        value = 0;
        if (!parameters.TryGetValue(key, out var v) || v is null) return false;
        return double.TryParse(v.ToString(), out value);
    }
}
