using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using Nice3point.Revit.Extensions;
using Autodesk.Revit.DB.Plumbing;
using RevitChatBot.Core.Context;

namespace RevitChatBot.MEP.Context;

/// <summary>
/// Provides a snapshot of the current model's MEP inventory:
/// loaded families, types, available system types, and common parameter names.
/// This helps the LLM generate accurate code by using exact names from the model.
/// </summary>
public class ModelInventoryProvider : IContextProvider
{
    public string Name => "model_inventory";
    public int Priority => 15;

    private static readonly (string Label, BuiltInCategory Cat)[] MepCategories =
    [
        ("Ducts", BuiltInCategory.OST_DuctCurves),
        ("Pipes", BuiltInCategory.OST_PipeCurves),
        ("Duct Fittings", BuiltInCategory.OST_DuctFitting),
        ("Pipe Fittings", BuiltInCategory.OST_PipeFitting),
        ("Duct Accessories", BuiltInCategory.OST_DuctAccessory),
        ("Pipe Accessories", BuiltInCategory.OST_PipeAccessory),
        ("Flex Ducts", BuiltInCategory.OST_FlexDuctCurves),
        ("Flex Pipes", BuiltInCategory.OST_FlexPipeCurves),
        ("Mech Equipment", BuiltInCategory.OST_MechanicalEquipment),
        ("Elec Equipment", BuiltInCategory.OST_ElectricalEquipment),
        ("Elec Fixtures", BuiltInCategory.OST_ElectricalFixtures),
        ("Lighting", BuiltInCategory.OST_LightingFixtures),
        ("Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures),
        ("Sprinklers", BuiltInCategory.OST_Sprinklers),
        ("Cable Trays", BuiltInCategory.OST_CableTray),
        ("Conduits", BuiltInCategory.OST_Conduit),
        ("MEP Spaces", BuiltInCategory.OST_MEPSpaces),
        ("Rooms", BuiltInCategory.OST_Rooms)
    ];

    public Task<ContextData> GatherAsync(object? revitDocument = null)
    {
        var data = new ContextData();
        if (revitDocument is not Document doc)
        {
            data.Add("model_inventory", "No active document.");
            return Task.FromResult(data);
        }

        var sb = new StringBuilder();

        AppendElementCounts(doc, sb);
        AppendFamilyTypes(doc, sb);
        AppendSystemTypes(doc, sb);
        AppendLevels(doc, sb);
        AppendSampleParameters(doc, sb);

        data.Add("model_inventory", sb.ToString());
        return Task.FromResult(data);
    }

    private static void AppendElementCounts(Document doc, StringBuilder sb)
    {
        sb.AppendLine("MODEL INVENTORY - MEP Element Counts:");
        foreach (var (label, cat) in MepCategories)
        {
            int count = doc.GetInstances(cat).Count;
            if (count > 0)
                sb.AppendLine($"  {label}: {count}");
        }
    }

    private static void AppendFamilyTypes(Document doc, StringBuilder sb)
    {
        sb.AppendLine("\nLOADED FAMILY TYPES (by category):");
        var typeCats = new (string Label, BuiltInCategory Cat)[]
        {
            ("Mech Equipment", BuiltInCategory.OST_MechanicalEquipment),
            ("Elec Equipment", BuiltInCategory.OST_ElectricalEquipment),
            ("Plumbing Fixtures", BuiltInCategory.OST_PlumbingFixtures),
            ("Duct Types", BuiltInCategory.OST_DuctCurves),
            ("Pipe Types", BuiltInCategory.OST_PipeCurves),
            ("Lighting", BuiltInCategory.OST_LightingFixtures),
            ("Sprinklers", BuiltInCategory.OST_Sprinklers)
        };

        foreach (var (label, cat) in typeCats)
        {
            var types = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .OfCategory(cat)
                .Cast<FamilySymbol>()
                .GroupBy(s => s.FamilyName)
                .OrderBy(g => g.Key)
                .Take(10)
                .ToList();

            if (types.Count == 0) continue;
            sb.AppendLine($"  [{label}]");
            foreach (var family in types)
            {
                var typeNames = family.Select(t => t.Name).Take(5);
                sb.AppendLine($"    Family: \"{family.Key}\" → Types: {string.Join(", ", typeNames.Select(n => $"\"{n}\""))}");
                if (family.Count() > 5)
                    sb.AppendLine($"      ... and {family.Count() - 5} more types");
            }
        }

        var ductTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(DuctType)).Cast<DuctType>()
            .Select(t => t.Name).Take(8).ToList();
        if (ductTypes.Count > 0)
            sb.AppendLine($"  [DuctType]: {string.Join(", ", ductTypes.Select(n => $"\"{n}\""))}");

        var pipeTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(PipeType)).Cast<PipeType>()
            .Select(t => t.Name).Take(8).ToList();
        if (pipeTypes.Count > 0)
            sb.AppendLine($"  [PipeType]: {string.Join(", ", pipeTypes.Select(n => $"\"{n}\""))}");
    }

    private static void AppendSystemTypes(Document doc, StringBuilder sb)
    {
        sb.AppendLine("\nMEP SYSTEM TYPES:");

        var mechSystems = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystemType))
            .Cast<MechanicalSystemType>()
            .Select(s => $"\"{s.Name}\" (class={s.SystemClassification})")
            .Take(10).ToList();
        if (mechSystems.Count > 0)
            sb.AppendLine($"  Mechanical: {string.Join(", ", mechSystems)}");

        var pipeSystems = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .Cast<PipingSystemType>()
            .Select(s => $"\"{s.Name}\" (class={s.SystemClassification})")
            .Take(10).ToList();
        if (pipeSystems.Count > 0)
            sb.AppendLine($"  Piping: {string.Join(", ", pipeSystems)}");
    }

    private static void AppendLevels(Document doc, StringBuilder sb)
    {
        var levels = doc.EnumerateInstances<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => $"\"{l.Name}\" (elev={Math.Round(l.Elevation * 0.3048, 2)}m)")
            .Take(20).ToList();

        if (levels.Count > 0)
            sb.AppendLine($"\nLEVELS: {string.Join(", ", levels)}");
    }

    private static void AppendSampleParameters(Document doc, StringBuilder sb)
    {
        var sample = doc.GetInstances(BuiltInCategory.OST_DuctCurves).FirstOrDefault()
            ?? doc.GetInstances(BuiltInCategory.OST_PipeCurves).FirstOrDefault();

        if (sample is null) return;

        sb.AppendLine($"\nSAMPLE INSTANCE PARAMETERS (from {sample.Category?.Name} ID:{sample.Id}):");
        var paramNames = new List<string>();
        foreach (Parameter p in sample.Parameters)
        {
            if (p.Definition is null) continue;
            string readOnly = p.IsReadOnly ? " [RO]" : "";
            string storage = p.StorageType.ToString();
            paramNames.Add($"  \"{p.Definition.Name}\" ({storage}{readOnly})");
        }

        foreach (var pn in paramNames.OrderBy(x => x).Take(30))
            sb.AppendLine(pn);

        if (paramNames.Count > 30)
            sb.AppendLine($"  ... and {paramNames.Count - 30} more parameters");
    }
}
