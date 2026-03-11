using System.Reflection;
using System.Text;
using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Query;

[Skill("inspect_element",
    "Deep inspect any Revit element: shows all properties, parameter values, types, connectors, and relationships. " +
    "Useful for debugging, understanding unknown elements, or gathering detailed data before code generation.")]
[SkillParameter("element_id", "integer",
    "The element ID to inspect",
    isRequired: true)]
[SkillParameter("include_methods", "boolean",
    "Include parameterless method return values (slower, more data). Default false.",
    isRequired: false)]
[SkillParameter("include_parameters", "boolean",
    "Include all Revit parameters with values. Default true.",
    isRequired: false)]
public class InspectElementSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        if (!parameters.TryGetValue("element_id", out var idObj) || idObj is null)
            return SkillResult.Fail("element_id is required.");

        if (!long.TryParse(idObj.ToString(), out var elementIdValue))
            return SkillResult.Fail($"Invalid element_id: {idObj}");

        var includeMethods = false;
        if (parameters.TryGetValue("include_methods", out var im) && im is not null)
            bool.TryParse(im.ToString(), out includeMethods);

        var includeParams = true;
        if (parameters.TryGetValue("include_parameters", out var ip) && ip is not null)
            bool.TryParse(ip.ToString(), out includeParams);

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;
            var elem = document.GetElement(new ElementId(elementIdValue));
            if (elem == null)
                return $"Element {elementIdValue} not found in document.";

            var sb = new StringBuilder();
            var elemType = elem.GetType();

            sb.AppendLine($"=== Element Deep Inspection: ID {elementIdValue} ===");
            sb.AppendLine();

            AppendBasicInfo(sb, document, elem, elemType);
            AppendProperties(sb, elem, elemType);

            if (includeMethods)
                AppendMethods(sb, elem, elemType);

            if (includeParams)
                AppendRevitParameters(sb, elem);

            AppendConnectors(sb, elem);
            AppendRelationships(sb, document, elem);

            return sb.ToString();
        });

        var text = result?.ToString() ?? "No result.";
        if (text.StartsWith("Element ") && text.Contains("not found"))
            return SkillResult.Fail(text);

        return SkillResult.Ok(text, text);
    }

    private static void AppendBasicInfo(StringBuilder sb, Document doc, Element elem, Type elemType)
    {
        sb.AppendLine("--- Basic Info ---");
        sb.AppendLine($"  Class: {elemType.Name} ({elemType.FullName})");
        sb.AppendLine($"  Category: {elem.Category?.Name ?? "(none)"}");
        sb.AppendLine($"  Name: {elem.Name}");

        string familyName = "", typeName = "";
        if (doc.GetElement(elem.GetTypeId()) is ElementType et)
        {
            typeName = et.Name;
            if (et is FamilySymbol fs)
                familyName = fs.FamilyName;
        }

        if (!string.IsNullOrEmpty(familyName))
            sb.AppendLine($"  Family: {familyName}");
        if (!string.IsNullOrEmpty(typeName))
            sb.AppendLine($"  Type: {typeName}");

        if (elem.LevelId != null && elem.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(elem.LevelId);
            sb.AppendLine($"  Level: {level?.Name ?? elem.LevelId.Value.ToString()}");
        }

        if (elem.Location is LocationPoint lp)
            sb.AppendLine($"  Location: Point ({Mm(lp.Point.X)}, {Mm(lp.Point.Y)}, {Mm(lp.Point.Z)})mm");
        else if (elem.Location is LocationCurve lc)
        {
            var s = lc.Curve.GetEndPoint(0);
            var e = lc.Curve.GetEndPoint(1);
            double lenMm = Math.Round(lc.Curve.Length * 304.8, 1);
            sb.AppendLine($"  Location: Curve from ({Mm(s.X)},{Mm(s.Y)},{Mm(s.Z)}) to ({Mm(e.X)},{Mm(e.Y)},{Mm(e.Z)})mm, L={lenMm}mm");
        }

        try
        {
            var bb = elem.get_BoundingBox(null);
            if (bb != null)
                sb.AppendLine($"  BoundingBox: Min({Mm(bb.Min.X)},{Mm(bb.Min.Y)},{Mm(bb.Min.Z)}) Max({Mm(bb.Max.X)},{Mm(bb.Max.Y)},{Mm(bb.Max.Z)})mm");
        }
        catch { }

        sb.AppendLine();
    }

    private static void AppendProperties(StringBuilder sb, Element elem, Type elemType)
    {
        sb.AppendLine("--- Properties ---");
        var props = elemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var grouped = props
            .Where(p => p.GetIndexParameters().Length == 0)
            .GroupBy(p => p.DeclaringType?.Name ?? "Unknown")
            .OrderByDescending(g => g.Key == elemType.Name);

        foreach (var group in grouped)
        {
            sb.AppendLine($"  [{group.Key}]");
            foreach (var prop in group.OrderBy(p => p.Name))
            {
                string val;
                try
                {
                    var obj = prop.GetValue(elem);
                    val = FormatValue(obj);
                }
                catch (Exception ex)
                {
                    val = $"[{ex.InnerException?.GetType().Name ?? ex.GetType().Name}]";
                }

                sb.AppendLine($"    {prop.Name}: {val}");
            }
        }
        sb.AppendLine();
    }

    private static void AppendMethods(StringBuilder sb, Element elem, Type elemType)
    {
        sb.AppendLine("--- Parameterless Method Results ---");
        var methods = elemType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 0
                        && m.ReturnType != typeof(void)
                        && !m.IsSpecialName
                        && m.DeclaringType != typeof(object)
                        && !m.Name.StartsWith("get_"))
            .OrderBy(m => m.Name)
            .Take(30);

        foreach (var method in methods)
        {
            string val;
            try
            {
                var obj = method.Invoke(elem, null);
                val = FormatValue(obj);
            }
            catch (Exception ex)
            {
                val = $"[{ex.InnerException?.GetType().Name ?? ex.GetType().Name}]";
            }

            sb.AppendLine($"    {method.Name}(): {val}");
        }
        sb.AppendLine();
    }

    private static void AppendRevitParameters(StringBuilder sb, Element elem)
    {
        sb.AppendLine("--- Revit Parameters ---");
        var paramList = new List<(string group, string name, string value, bool readOnly)>();

        foreach (Parameter p in elem.Parameters)
        {
            if (p?.Definition == null) continue;
            string name = p.Definition.Name;
            bool ro = p.IsReadOnly;

            string value;
            if (!p.HasValue)
            {
                value = "(no value)";
            }
            else
            {
                value = p.StorageType switch
                {
                    StorageType.String => p.AsString() ?? "",
                    StorageType.Integer => p.AsInteger().ToString(),
                    StorageType.Double => $"{p.AsDouble():F4} ({p.AsValueString() ?? ""})",
                    StorageType.ElementId => $"ID:{p.AsElementId().Value} ({p.AsValueString() ?? ""})",
                    _ => p.AsValueString() ?? "(unknown)"
                };
            }

            string groupName;
            try
            {
                var groupId = p.Definition.GetGroupTypeId();
                groupName = groupId?.TypeId ?? "Other";
                var lastSlash = groupName.LastIndexOf('/');
                if (lastSlash >= 0) groupName = groupName[(lastSlash + 1)..];
                var dash = groupName.IndexOf('-');
                if (dash >= 0) groupName = groupName[..dash];
            }
            catch { groupName = "Other"; }

            paramList.Add((groupName, name, value, ro));
        }

        foreach (var group in paramList.GroupBy(p => p.group).OrderBy(g => g.Key))
        {
            sb.AppendLine($"  [{group.Key}]");
            foreach (var (_, name, value, readOnly) in group.OrderBy(p => p.name))
            {
                string roMark = readOnly ? " [RO]" : "";
                sb.AppendLine($"    {name}{roMark}: {value}");
            }
        }
        sb.AppendLine();
    }

    private static void AppendConnectors(StringBuilder sb, Element elem)
    {
        ConnectorManager? cm = elem switch
        {
            MEPCurve mc => mc.ConnectorManager,
            FamilyInstance fi => fi.MEPModel?.ConnectorManager,
            _ => null
        };

        if (cm == null) return;

        sb.AppendLine("--- Connectors ---");
        int idx = 0;
        foreach (Connector c in cm.Connectors)
        {
            string conn = c.IsConnected ? "Connected" : "Open";
            var dir = c.CoordinateSystem.BasisZ;
            sb.AppendLine($"  [{idx}] {c.Shape} {c.Domain} {conn}");
            sb.AppendLine($"       Origin: ({Mm(c.Origin.X)}, {Mm(c.Origin.Y)}, {Mm(c.Origin.Z)})mm");
            sb.AppendLine($"       Direction: ({dir.X:F2}, {dir.Y:F2}, {dir.Z:F2})");

            try
            {
                if (c.Shape == ConnectorProfileType.Round)
                    sb.AppendLine($"       Radius: {Math.Round(c.Radius * 304.8, 1)}mm");
                else if (c.Shape is ConnectorProfileType.Rectangular or ConnectorProfileType.Oval)
                    sb.AppendLine($"       Size: {Math.Round(c.Width * 304.8, 1)}×{Math.Round(c.Height * 304.8, 1)}mm");
            }
            catch { }

            try
            {
                if (c.Domain is Domain.DomainHvac or Domain.DomainPiping)
                    sb.AppendLine($"       Flow: {c.Flow:F4}, PressureDrop: {c.PressureDrop:F4}");
            }
            catch { }

            if (c.IsConnected)
            {
                foreach (Connector other in c.AllRefs)
                {
                    if (other?.Owner == null || other.ConnectorType == ConnectorType.Logical) continue;
                    if (other.Owner.Id == elem.Id) continue;
                    sb.AppendLine($"       → Connected to: {other.Owner.Category?.Name} ID:{other.Owner.Id.Value} ({other.Owner.Name})");
                }
            }
            idx++;
        }
        sb.AppendLine();
    }

    private static void AppendRelationships(StringBuilder sb, Document doc, Element elem)
    {
        sb.AppendLine("--- Relationships ---");

        if (elem is FamilyInstance fi)
        {
            if (fi.SuperComponent is Element parent)
                sb.AppendLine($"  Host/Parent: {parent.Category?.Name} ID:{parent.Id.Value}");

            var subIds = fi.GetSubComponentIds();
            if (subIds.Count > 0)
            {
                sb.AppendLine($"  Sub-components ({subIds.Count}):");
                foreach (var id in subIds.Take(10))
                {
                    var sub = doc.GetElement(id);
                    sb.AppendLine($"    ID:{id.Value} {sub?.Category?.Name} {sub?.Name}");
                }
            }
        }

        if (elem is MEPCurve mc && mc.MEPSystem != null)
        {
            var sys = mc.MEPSystem;
            sb.AppendLine($"  MEP System: {sys.Name} (ID:{sys.Id.Value})");
            var classParam = sys.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
            if (classParam != null)
                sb.AppendLine($"  System Classification: {classParam.AsString()}");
        }

        try
        {
            var insIds = InsulationLiningBase.GetInsulationIds(doc, elem.Id);
            if (insIds != null && insIds.Count > 0)
                sb.AppendLine($"  Insulation: {insIds.Count} layer(s) [{string.Join(", ", insIds.Select(id => $"ID:{id.Value}"))}]");
        }
        catch { }

        sb.AppendLine();
    }

    private static string FormatValue(object? obj) => obj switch
    {
        null => "null",
        XYZ xyz => $"({Mm(xyz.X)}, {Mm(xyz.Y)}, {Mm(xyz.Z)})mm",
        ElementId id => $"ID:{id.Value}",
        double d => $"{d:F4} ({Math.Round(d * 304.8, 1)}mm)",
        float f => $"{f:F4}",
        bool b => b.ToString(),
        Enum e => e.ToString(),
        string s => s.Length > 80 ? s[..80] + "..." : s,
        _ => obj.ToString()?.Length > 80 ? obj.ToString()![..80] + "..." : obj.ToString() ?? "null"
    };

    private static double Mm(double feet) => Math.Round(feet * 304.8, 1);
}
