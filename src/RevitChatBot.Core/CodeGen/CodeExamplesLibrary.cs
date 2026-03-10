namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Working code examples (few-shot) that the LLM references when generating
/// dynamic Revit API code. Patterns are sourced from production MEP tools.
/// </summary>
public static class CodeExamplesLibrary
{
    public static string GetExamples() => """
        === WORKING CODE EXAMPLES (use as reference) ===

        ### EXAMPLE 1: Count all MEP elements by category
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var categories = new Dictionary<string, BuiltInCategory>
                {
                    ["Ducts"] = BuiltInCategory.OST_DuctCurves,
                    ["Pipes"] = BuiltInCategory.OST_PipeCurves,
                    ["Flex Ducts"] = BuiltInCategory.OST_FlexDuctCurves,
                    ["Flex Pipes"] = BuiltInCategory.OST_FlexPipeCurves,
                    ["Duct Fittings"] = BuiltInCategory.OST_DuctFitting,
                    ["Pipe Fittings"] = BuiltInCategory.OST_PipeFitting,
                    ["Duct Accessories"] = BuiltInCategory.OST_DuctAccessory,
                    ["Pipe Accessories"] = BuiltInCategory.OST_PipeAccessory,
                    ["Mech Equipment"] = BuiltInCategory.OST_MechanicalEquipment,
                    ["Elec Equipment"] = BuiltInCategory.OST_ElectricalEquipment,
                    ["Plumbing Fixtures"] = BuiltInCategory.OST_PlumbingFixtures,
                    ["Sprinklers"] = BuiltInCategory.OST_Sprinklers,
                    ["Cable Trays"] = BuiltInCategory.OST_CableTray,
                    ["Conduits"] = BuiltInCategory.OST_Conduit,
                    ["Lighting"] = BuiltInCategory.OST_LightingFixtures,
                    ["Duct Insulation"] = BuiltInCategory.OST_DuctInsulations,
                    ["Pipe Insulation"] = BuiltInCategory.OST_PipeInsulations
                };
                var lines = new List<string> { "MEP Element Summary:" };
                int total = 0;
                foreach (var (name, cat) in categories)
                {
                    int count = new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType().GetElementCount();
                    if (count > 0) { lines.Add($"  {name}: {count}"); total += count; }
                }
                lines.Add($"  TOTAL: {total}");
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 2: List ducts grouped by system with size details
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Mechanical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var ducts = new FilteredElementCollector(doc)
                    .OfClass(typeof(Duct)).Cast<Duct>().ToList();
                if (ducts.Count == 0) return "No ducts found in model.";

                var grouped = ducts.GroupBy(d =>
                {
                    var sysParam = d.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM);
                    return sysParam?.AsString() ?? "Unassigned";
                });
                var lines = new List<string> { $"Found {ducts.Count} ducts:" };
                foreach (var group in grouped.OrderBy(g => g.Key))
                {
                    var totalLen = group.Sum(d =>
                        d.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble() * 0.3048);
                    lines.Add($"\n  System: {group.Key} ({group.Count()} ducts, {Math.Round(totalLen, 1)}m)");
                    foreach (var d in group.Take(5))
                    {
                        string size = d.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "N/A";
                        double len = Math.Round(d.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble() * 0.3048, 2);
                        lines.Add($"    - ID:{d.Id} {size} L={len}m");
                    }
                    if (group.Count() > 5) lines.Add($"    ... and {group.Count() - 5} more");
                }
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 3: List pipes grouped by system type with summary
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Plumbing;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();
                if (pipes.Count == 0) return "No pipes found in model.";

                var grouped = pipes.GroupBy(p =>
                {
                    var sys = p.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM);
                    return sys?.AsString() ?? "Unassigned";
                });
                var lines = new List<string> { $"Total pipes: {pipes.Count}" };
                foreach (var g in grouped.OrderBy(x => x.Key))
                {
                    var totalLen = g.Sum(p =>
                        p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble() * 0.3048);
                    lines.Add($"  {g.Key}: {g.Count()} pipes, total {Math.Round(totalLen, 1)}m");
                }
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 4: Check duct velocity violations
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                double maxVelocityMs = 8.0;
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
                var violations = new List<string>();
                foreach (var duct in ducts)
                {
                    var velParam = duct.get_Parameter(BuiltInParameter.RBS_VELOCITY);
                    if (velParam == null || !velParam.HasValue) continue;
                    double velocityMs = velParam.AsDouble() * 0.3048; // ft/s -> m/s
                    if (velocityMs > maxVelocityMs)
                    {
                        string size = duct.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "";
                        string sys = duct.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
                        string lvl = duct.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsValueString() ?? "";
                        violations.Add($"  ID:{duct.Id} {size} v={Math.Round(velocityMs, 2)}m/s sys={sys} lvl={lvl}");
                    }
                }
                if (violations.Count == 0) return $"All {ducts.Count} ducts within velocity limit ({maxVelocityMs} m/s).";
                return $"Velocity violations ({violations.Count}/{ducts.Count}):\n" + string.Join("\n", violations.Take(30));
            }
        }
        ```

        ### EXAMPLE 5: Check pipe slope violations
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                double minSlopePct = 0.5;
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().ToList();
                var violations = new List<string>();
                foreach (var pipe in pipes)
                {
                    var slopeParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                    if (slopeParam == null || !slopeParam.HasValue) continue;
                    double slopePct = slopeParam.AsDouble() * 100.0; // ratio -> %
                    if (slopePct < minSlopePct && slopePct >= 0)
                    {
                        string size = pipe.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "";
                        string sys = pipe.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
                        violations.Add($"  ID:{pipe.Id} {size} slope={Math.Round(slopePct, 3)}% sys={sys}");
                    }
                }
                if (violations.Count == 0) return $"All pipes meet minimum slope ({minSlopePct}%).";
                return $"Slope violations ({violations.Count}):\n" + string.Join("\n", violations.Take(30));
            }
        }
        ```

        ### EXAMPLE 6: Check disconnected MEP elements
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var cats = new[] {
                    BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_PipeAccessory,
                    BuiltInCategory.OST_MechanicalEquipment
                };
                var disconnected = new List<string>();
                foreach (var cat in cats)
                {
                    foreach (var elem in new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType())
                    {
                        ConnectorManager cm = null;
                        if (elem is MEPCurve mc) cm = mc.ConnectorManager;
                        else if (elem is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
                        if (cm == null) continue;

                        bool hasOpen = false;
                        foreach (Connector c in cm.Connectors)
                            if (!c.IsConnected) { hasOpen = true; break; }
                        if (hasOpen)
                            disconnected.Add($"  ID:{elem.Id} {elem.Category?.Name} {elem.Name}");
                    }
                }
                if (disconnected.Count == 0) return "All MEP elements are fully connected.";
                return $"Disconnected elements ({disconnected.Count}):\n" + string.Join("\n", disconnected.Take(40));
            }
        }
        ```

        ### EXAMPLE 7: Check missing insulation
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().ToList();

                var missing = new List<string>();
                foreach (var elem in ducts.Concat(pipes))
                {
                    try
                    {
                        var insIds = InsulationLiningBase.GetInsulationIds(doc, elem.Id);
                        if (insIds == null || insIds.Count == 0)
                        {
                            string sys = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
                            string size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "";
                            missing.Add($"  ID:{elem.Id} {elem.Category?.Name} {size} sys={sys}");
                        }
                    }
                    catch { }
                }
                if (missing.Count == 0) return "All ducts and pipes have insulation.";
                int total = ducts.Count + pipes.Count;
                return $"Missing insulation ({missing.Count}/{total}):\n" + string.Join("\n", missing.Take(40));
            }
        }
        ```

        ### EXAMPLE 8: Traverse MEP network from element (BFS)
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                // Replace with actual element ID
                long startId = 123456;
                var startElem = doc.GetElement(new ElementId(startId));
                if (startElem == null) return $"Element {startId} not found.";

                var visited = new HashSet<long>();
                var queue = new Queue<(Element elem, int depth)>();
                queue.Enqueue((startElem, 0));
                visited.Add(startElem.Id.Value);
                var path = new List<string>();
                int openEnds = 0;

                while (queue.Count > 0 && visited.Count < 200)
                {
                    var (elem, depth) = queue.Dequeue();
                    path.Add($"  [{depth}] ID:{elem.Id} {elem.Category?.Name} {elem.Name}");

                    ConnectorManager cm = null;
                    if (elem is MEPCurve mc) cm = mc.ConnectorManager;
                    else if (elem is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
                    if (cm == null) continue;

                    foreach (Connector c in cm.Connectors)
                    {
                        if (!c.IsConnected) { openEnds++; continue; }
                        foreach (Connector other in c.AllRefs)
                        {
                            if (other?.Owner != null && visited.Add(other.Owner.Id.Value))
                                queue.Enqueue((other.Owner, depth + 1));
                        }
                    }
                }
                return $"Network from ID:{startId}: {visited.Count} elements, {openEnds} open ends\n" +
                       string.Join("\n", path.Take(50));
            }
        }
        ```

        ### EXAMPLE 9: Get MEP spaces with airflow data
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Mechanical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType().Cast<Space>()
                    .Where(s => s.Area > 0).OrderBy(s => s.Number).ToList();
                if (spaces.Count == 0) return "No MEP Spaces found.";

                var lines = new List<string>
                {
                    $"MEP Spaces ({spaces.Count}):",
                    "| # | Name | Area m² | Vol m³ | Supply CFM | Return CFM |",
                    "|---|------|---------|--------|------------|------------|"
                };
                foreach (var s in spaces.Take(30))
                {
                    string name = s.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    double area = Math.Round(s.Area * 0.092903, 1);
                    double vol = Math.Round(s.Volume * 0.0283168, 1);
                    double supply = s.LookupParameter("Actual Supply Airflow")?.AsDouble() ?? 0;
                    double ret = s.LookupParameter("Actual Return Airflow")?.AsDouble() ?? 0;
                    lines.Add($"| {s.Number} | {name} | {area} | {vol} | {Math.Round(supply, 1)} | {Math.Round(ret, 1)} |");
                }
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 10: MEP systems summary with element counts
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var mepCats = new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_FlexPipeCurves };
                var systems = new Dictionary<string, (string classification, int count, double totalLenFt)>();

                foreach (var cat in mepCats)
                {
                    foreach (var elem in new FilteredElementCollector(doc)
                        .OfCategory(cat).WhereElementIsNotElementType())
                    {
                        string sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "(Unassigned)";
                        string sysClass = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "-";
                        double length = elem.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
                        if (systems.TryGetValue(sysName, out var ex))
                            systems[sysName] = (sysClass, ex.count + 1, ex.totalLenFt + length);
                        else
                            systems[sysName] = (sysClass, 1, length);
                    }
                }
                if (systems.Count == 0) return "No MEP systems found.";
                var lines = new List<string>
                {
                    $"MEP Systems ({systems.Count}):",
                    "| System | Classification | Elements | Length (m) |",
                    "|--------|---------------|----------|------------|"
                };
                foreach (var kv in systems.OrderByDescending(x => x.Value.count))
                    lines.Add($"| {kv.Key} | {kv.Value.classification} | {kv.Value.count} | {Math.Round(kv.Value.totalLenFt * 0.3048, 1)} |");
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 11: Clash detection between two categories
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var catA = BuiltInCategory.OST_DuctCurves;
                var catB = BuiltInCategory.OST_PipeCurves;
                var elemsA = new FilteredElementCollector(doc).OfCategory(catA).WhereElementIsNotElementType().ToList();
                var elemsB = new FilteredElementCollector(doc).OfCategory(catB).WhereElementIsNotElementType().ToList();

                var reported = new HashSet<(long, long)>();
                var clashes = new List<string>();
                foreach (var a in elemsA)
                {
                    var bbA = a.get_BoundingBox(null);
                    if (bbA == null) continue;
                    foreach (var b in elemsB)
                    {
                        if (a.Id == b.Id) continue;
                        long lo = Math.Min(a.Id.Value, b.Id.Value);
                        long hi = Math.Max(a.Id.Value, b.Id.Value);
                        if (!reported.Add((lo, hi))) continue;
                        var bbB = b.get_BoundingBox(null);
                        if (bbB == null) continue;
                        if (bbA.Max.X >= bbB.Min.X && bbA.Min.X <= bbB.Max.X &&
                            bbA.Max.Y >= bbB.Min.Y && bbA.Min.Y <= bbB.Max.Y &&
                            bbA.Max.Z >= bbB.Min.Z && bbA.Min.Z <= bbB.Max.Z)
                        {
                            clashes.Add($"  {a.Category?.Name} {a.Id} <-> {b.Category?.Name} {b.Id}");
                            if (clashes.Count >= 50) break;
                        }
                    }
                    if (clashes.Count >= 50) break;
                }
                if (clashes.Count == 0) return "No clashes found.";
                return $"Clashes ({clashes.Count}):\n" + string.Join("\n", clashes);
            }
        }
        ```

        ### EXAMPLE 12: Check elevation/clearance height
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                double minHeightM = 2.4;
                double minHeightFt = minHeightM / 0.3048;
                var levels = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>().ToDictionary(l => l.Id, l => l);
                var cats = new[] { BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_PipeCurves };
                var issues = new List<string>();

                foreach (var cat in cats)
                {
                    foreach (var elem in new FilteredElementCollector(doc).OfCategory(cat).WhereElementIsNotElementType())
                    {
                        if (elem.Location is not LocationCurve lc) continue;
                        double midZ = (lc.Curve.GetEndPoint(0).Z + lc.Curve.GetEndPoint(1).Z) / 2;
                        var lvlParam = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
                        if (lvlParam == null) continue;
                        var lvlId = lvlParam.AsElementId();
                        if (!levels.TryGetValue(lvlId, out var refLevel)) continue;
                        double heightAbove = midZ - refLevel.Elevation;
                        if (heightAbove >= 0 && heightAbove < minHeightFt)
                        {
                            string size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "";
                            issues.Add($"  ID:{elem.Id} {elem.Category?.Name} {size} h={Math.Round(heightAbove * 0.3048, 2)}m lvl={refLevel.Name}");
                        }
                    }
                }
                if (issues.Count == 0) return $"All elements above {minHeightM}m clearance.";
                return $"Clearance issues ({issues.Count}):\n" + string.Join("\n", issues.Take(30));
            }
        }
        ```

        ### EXAMPLE 13: Model audit — warnings summary
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var warnings = doc.GetWarnings();
                var total = new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount();
                var byType = warnings
                    .GroupBy(w => w.GetDescriptionText())
                    .Select(g => new { desc = g.Key, count = g.Count() })
                    .OrderByDescending(x => x.count).Take(15).ToList();

                var lines = new List<string>
                {
                    $"Model Audit: {doc.Title}",
                    $"  Total elements: {total}",
                    $"  Total warnings: {warnings.Count}",
                    $"\n  Top warnings:"
                };
                foreach (var w in byType)
                    lines.Add($"    [{w.count}] {w.desc.Substring(0, Math.Min(w.desc.Length, 80))}");
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 14: Create pipes with system type
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Plumbing;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                using var tx = new Transaction(doc, "Create Pipes");
                tx.Start();

                var level = new FilteredElementCollector(doc)
                    .OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).First();
                var pipeType = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType)).Cast<PipeType>().First();
                var systemType = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>()
                    .FirstOrDefault(s => s.SystemClassification == MEPSystemClassification.DomesticColdWater)
                    ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().First();

                int count = 0;
                double spacingFt = 3.0 / 0.3048;
                double lengthFt = 10.0 / 0.3048;
                for (int i = 0; i < 5; i++)
                {
                    Pipe.Create(doc, systemType.Id, pipeType.Id, level.Id,
                        new XYZ(0, i * spacingFt, 0), new XYZ(lengthFt, i * spacingFt, 0));
                    count++;
                }
                tx.Commit();
                return $"Created {count} pipes on {level.Name}, each 10m long, spacing 3m.";
            }
        }
        ```

        ### EXAMPLE 15: Modify element parameters in batch
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
                if (ducts.Count == 0) return "No ducts found to modify.";

                using var tx = new Transaction(doc, "Batch Set Comments");
                tx.Start();
                int modified = 0;
                foreach (var duct in ducts)
                {
                    var param = duct.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                    if (param != null && !param.IsReadOnly) { param.Set("Reviewed"); modified++; }
                }
                tx.Commit();
                return $"Updated comments on {modified}/{ducts.Count} ducts.";
            }
        }
        ```

        ### EXAMPLE 16: Export pipe data to markdown table
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Plumbing;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var pipes = new FilteredElementCollector(doc)
                    .OfClass(typeof(Pipe)).Cast<Pipe>().Take(30).ToList();
                if (pipes.Count == 0) return "No pipes found.";
                var lines = new List<string>
                {
                    "| ID | DN (mm) | Length (m) | System | Level |",
                    "|---|---|---|---|---|"
                };
                foreach (var p in pipes)
                {
                    double dia = Math.Round(p.Diameter * 304.8);
                    double len = Math.Round(p.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble() * 0.3048, 2);
                    string sys = p.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "N/A";
                    string lvl = p.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsValueString() ?? "N/A";
                    lines.Add($"| {p.Id} | {dia} | {len} | {sys} | {lvl} |");
                }
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 17: Duct sizing calculation
        ```csharp
        using System;
        using System.Collections.Generic;

        public static class DynamicAction
        {
            static double[] StandardDuctSizes = { 100,125,150,200,250,300,350,400,450,500,550,600,650,700,750,800,900,1000,1100,1200,1400,1500,1600,1800,2000 };
            static double RoundToStandard(double mm) =>
                Array.MinBy(StandardDuctSizes, v => Math.Abs(v - mm));

            public static string Execute(Autodesk.Revit.DB.Document doc)
            {
                double airflowLps = 500; // L/s
                double targetVelocity = 6.0; // m/s
                double aspectRatio = 2.0;
                double areaM2 = (airflowLps / 1000.0) / targetVelocity;
                double heightM = Math.Sqrt(areaM2 / aspectRatio);
                double widthM = heightM * aspectRatio;
                double widthMm = RoundToStandard(widthM * 1000);
                double heightMm = RoundToStandard(heightM * 1000);
                double actualArea = (widthMm / 1000.0) * (heightMm / 1000.0);
                double actualV = (airflowLps / 1000.0) / actualArea;
                return $"Duct sizing for {airflowLps} L/s at {targetVelocity} m/s:\n" +
                       $"  Recommended: {widthMm}x{heightMm}mm\n" +
                       $"  Actual velocity: {Math.Round(actualV, 2)} m/s\n" +
                       $"  Area: {Math.Round(actualArea, 4)} m²";
            }
        }
        ```

        ### EXAMPLE 18: Set pipe slope for drainage pipes
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                // Target: set 1% slope on all sanitary pipes
                double targetSlopePct = 1.0;
                double slopeRatio = targetSlopePct / 100.0;
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().ToList()
                    .Where(p => {
                        var sys = p.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
                        return sys.Contains("Sanitary", StringComparison.OrdinalIgnoreCase);
                    }).ToList();
                if (pipes.Count == 0) return "No sanitary pipes found.";

                using var tx = new Transaction(doc, "Set Pipe Slope");
                tx.Start();
                int success = 0, failed = 0;
                foreach (var pipe in pipes)
                {
                    var param = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE);
                    if (param != null && !param.IsReadOnly) { param.Set(slopeRatio); success++; }
                    else failed++;
                }
                tx.Commit();
                return $"Set {targetSlopePct}% slope: {success} success, {failed} failed out of {pipes.Count} sanitary pipes.";
            }
        }
        ```

        ### EXAMPLE 19: Fire damper check
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var accessories = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctAccessory)
                    .WhereElementIsNotElementType().ToList();
                var dampers = new List<string>();
                int connected = 0, disconnected = 0;
                foreach (var elem in accessories)
                {
                    if (elem is not FamilyInstance fi) continue;
                    string family = fi.Symbol?.FamilyName?.ToLower() ?? "";
                    string type = fi.Symbol?.Name?.ToLower() ?? "";
                    if (!family.Contains("fire") && !family.Contains("damper") &&
                        !type.Contains("fire") && !type.Contains("damper")) continue;

                    var cm = fi.MEPModel?.ConnectorManager;
                    bool allConnected = true;
                    if (cm != null)
                        foreach (Connector c in cm.Connectors)
                            if (!c.IsConnected) { allConnected = false; break; }
                    if (allConnected) connected++; else disconnected++;
                    string status = allConnected ? "OK" : "DISCONNECTED";
                    string lvl = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsValueString() ?? "";
                    dampers.Add($"  ID:{elem.Id} [{status}] {fi.Symbol?.FamilyName} lvl={lvl}");
                }
                if (dampers.Count == 0) return "No fire dampers found in model.";
                return $"Fire dampers ({dampers.Count}): {connected} connected, {disconnected} disconnected\n" +
                       string.Join("\n", dampers.Take(30));
            }
        }
        ```

        ### EXAMPLE 20: Parameter completeness check
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                string paramName = "Mark"; // Change to target parameter
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();
                int filled = 0, missing = 0;
                foreach (var elem in ducts)
                {
                    var p = elem.LookupParameter(paramName);
                    if (p != null && p.HasValue && !string.IsNullOrWhiteSpace(p.AsValueString() ?? p.AsString()))
                        filled++;
                    else missing++;
                }
                double rate = ducts.Count > 0 ? Math.Round((double)filled / ducts.Count * 100, 1) : 0;
                return $"Parameter '{paramName}' on Ducts:\n  Filled: {filled}\n  Missing: {missing}\n  Completion: {rate}%";
            }
        }
        ```

        ### EXAMPLE 21: Clash detection with tolerance and grouping (connected components)
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                double toleranceMm = 50;
                double tolFt = toleranceMm / 304.8;
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves).WhereElementIsNotElementType().ToList();
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves).WhereElementIsNotElementType().ToList();

                var adj = new Dictionary<long, HashSet<long>>();
                var elemMap = new Dictionary<long, Element>();
                foreach (var d in ducts) elemMap[d.Id.Value] = d;
                foreach (var p in pipes) elemMap[p.Id.Value] = p;

                foreach (var d in ducts)
                {
                    var bb1 = d.get_BoundingBox(null); if (bb1 == null) continue;
                    foreach (var p in pipes)
                    {
                        var bb2 = p.get_BoundingBox(null); if (bb2 == null) continue;
                        if (bb1.Min.X - tolFt <= bb2.Max.X && bb1.Max.X + tolFt >= bb2.Min.X &&
                            bb1.Min.Y - tolFt <= bb2.Max.Y && bb1.Max.Y + tolFt >= bb2.Min.Y &&
                            bb1.Min.Z - tolFt <= bb2.Max.Z && bb1.Max.Z + tolFt >= bb2.Min.Z)
                        {
                            long a = d.Id.Value, b = p.Id.Value;
                            if (!adj.ContainsKey(a)) adj[a] = new();
                            if (!adj.ContainsKey(b)) adj[b] = new();
                            adj[a].Add(b); adj[b].Add(a);
                        }
                    }
                }

                var visited = new HashSet<long>();
                var groups = new List<List<long>>();
                foreach (var id in adj.Keys)
                {
                    if (visited.Contains(id)) continue;
                    var q = new Queue<long>(); q.Enqueue(id); visited.Add(id);
                    var group = new List<long>();
                    while (q.Count > 0) { var c = q.Dequeue(); group.Add(c);
                        foreach (var n in adj[c]) if (visited.Add(n)) q.Enqueue(n); }
                    groups.Add(group);
                }

                var lines = new List<string> { $"Clash groups: {groups.Count} (tol={toleranceMm}mm)" };
                foreach (var (g, i) in groups.Select((g, i) => (g, i)))
                    lines.Add($"  Group {i+1}: {g.Count} elements [{string.Join(",", g.Take(5))}]");
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 22: Create MEP segment matching original (Pipe/Duct/CableTray/Conduit)
        ```csharp
        using System;
        using System.Linq;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Plumbing;
        using Autodesk.Revit.DB.Mechanical;
        using Autodesk.Revit.DB.Electrical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                long sourceId = 123456; // Replace with actual ID
                var source = doc.GetElement(new ElementId(sourceId));
                if (source == null) return "Element not found.";
                if (source.Location is not LocationCurve lc) return "Not a curve-based MEP element.";

                var start = lc.Curve.GetEndPoint(0);
                var end = lc.Curve.GetEndPoint(1);
                var dir = (end - start).Normalize();
                double offsetFt = 300 / 304.8; // 300mm offset downward
                var newStart = start + new XYZ(0, 0, -offsetFt);
                var newEnd = end + new XYZ(0, 0, -offsetFt);

                using var tx = new Transaction(doc, "Duplicate MEP Segment");
                tx.Start();
                MEPCurve newSeg = null;
                var lvl = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .OrderBy(l => l.Elevation).First();

                if (source is Pipe pipe)
                {
                    var sysId = pipe.MEPSystem?.GetTypeId() ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(PipingSystemType)).FirstElementId();
                    newSeg = Pipe.Create(doc, sysId, pipe.GetTypeId(), lvl.Id, newStart, newEnd);
                }
                else if (source is Duct duct)
                {
                    var sysId = duct.MEPSystem?.GetTypeId() ?? new FilteredElementCollector(doc)
                        .OfClass(typeof(MechanicalSystemType)).FirstElementId();
                    newSeg = Duct.Create(doc, sysId, duct.GetTypeId(), lvl.Id, newStart, newEnd);
                }
                else if (source is CableTray tray)
                    newSeg = CableTray.Create(doc, tray.GetTypeId(), newStart, newEnd, lvl.Id);
                else if (source is Conduit cond)
                    newSeg = Conduit.Create(doc, cond.GetTypeId(), newStart, newEnd, lvl.Id);

                if (newSeg != null && source is MEPCurve srcCurve)
                {
                    foreach (var bip in new[] { BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
                        BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM })
                    {
                        var sp = srcCurve.get_Parameter(bip); var tp = newSeg.get_Parameter(bip);
                        if (sp != null && tp != null && !tp.IsReadOnly && sp.StorageType == StorageType.Double)
                            try { tp.Set(sp.AsDouble()); } catch { }
                    }
                }
                tx.Commit();
                return newSeg != null ? $"Created {newSeg.Category?.Name} ID:{newSeg.Id}" : "Failed to create segment.";
            }
        }
        ```

        ### EXAMPLE 23: Create elbow fitting between two MEP segments
        ```csharp
        using System;
        using System.Linq;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Plumbing;
        using Autodesk.Revit.DB.Mechanical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                long id1 = 111111, id2 = 222222; // Replace with actual IDs
                var e1 = doc.GetElement(new ElementId(id1)) as MEPCurve;
                var e2 = doc.GetElement(new ElementId(id2)) as MEPCurve;
                if (e1 == null || e2 == null) return "Elements not found.";

                var c1 = GetNearestConnector(e1, e2);
                var c2 = GetNearestConnector(e2, e1);
                if (c1 == null || c2 == null) return "No suitable connectors found.";

                using var tx = new Transaction(doc, "Create Elbow");
                tx.Start();
                try
                {
                    var fitting = doc.Create.NewElbowFitting(c1, c2);
                    tx.Commit();
                    return fitting != null ? $"Elbow created: ID:{fitting.Id}" : "Failed to create elbow.";
                }
                catch (Exception ex) { tx.RollBack(); return $"Error: {ex.Message}"; }
            }

            static Connector GetNearestConnector(MEPCurve from, MEPCurve to)
            {
                var toCm = to.ConnectorManager;
                XYZ toCenter = XYZ.Zero;
                int cnt = 0;
                foreach (Connector c in toCm.Connectors) { toCenter += c.Origin; cnt++; }
                if (cnt > 0) toCenter /= cnt;

                Connector best = null; double minDist = double.MaxValue;
                foreach (Connector c in from.ConnectorManager.Connectors)
                {
                    if (c.IsConnected) continue;
                    double d = c.Origin.DistanceTo(toCenter);
                    if (d < minDist) { minDist = d; best = c; }
                }
                return best;
            }
        }
        ```

        ### EXAMPLE 24: Get element direction and classify parallel/perpendicular
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                long id1 = 111111, id2 = 222222; // Replace with actual IDs
                var e1 = doc.GetElement(new ElementId(id1));
                var e2 = doc.GetElement(new ElementId(id2));
                if (e1 == null || e2 == null) return "Elements not found.";

                var d1 = GetDirection(e1);
                var d2 = GetDirection(e2);
                if (d1 == null || d2 == null) return "Cannot determine direction (not curve-based elements).";

                var xy1 = new XYZ(d1.X, d1.Y, 0).Normalize();
                var xy2 = new XYZ(d2.X, d2.Y, 0).Normalize();
                double dot = Math.Abs(xy1.DotProduct(xy2));

                string relation = dot >= 0.98 ? "PARALLEL" : dot <= 0.10 ? "PERPENDICULAR" : $"ANGLED ({Math.Round(Math.Acos(dot) * 180 / Math.PI, 1)}°)";
                return $"Element {id1} dir=({d1.X:F2},{d1.Y:F2},{d1.Z:F2})\n" +
                       $"Element {id2} dir=({d2.X:F2},{d2.Y:F2},{d2.Z:F2})\n" +
                       $"Relation: {relation} (dot={dot:F4})";
            }

            static XYZ GetDirection(Element e)
            {
                if (e.Location is LocationCurve lc)
                {
                    var s = lc.Curve.GetEndPoint(0); var t = lc.Curve.GetEndPoint(1);
                    var d = t - s; return d.GetLength() > 1e-9 ? d.Normalize() : null;
                }
                return null;
            }
        }
        ```

        ### EXAMPLE 25: Validate MEP routing preferences
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Plumbing;
        using Autodesk.Revit.DB.Mechanical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var lines = new List<string> { "MEP Routing Preferences:" };

                var pipeTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(PipeType)).Cast<PipeType>().ToList();
                foreach (var pt in pipeTypes.Take(5))
                {
                    var rp = pt.RoutingPreferenceManager;
                    int elbows = rp.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                    int junctions = rp.GetNumberOfRules(RoutingPreferenceRuleGroupType.Junctions);
                    lines.Add($"  Pipe: {pt.Name} — Elbows:{elbows} rules, Junctions:{junctions} rules");
                }

                var ductTypes = new FilteredElementCollector(doc)
                    .OfClass(typeof(DuctType)).Cast<DuctType>().ToList();
                foreach (var dt in ductTypes.Take(5))
                {
                    var rp = dt.RoutingPreferenceManager;
                    int elbows = rp.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
                    lines.Add($"  Duct: {dt.Name} — Elbows:{elbows} rules");
                }

                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 26: Directional clearance check with ReferenceIntersector (ray casting)
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                double thresholdMm = 300;
                double thresholdFt = thresholdMm / 304.8;
                var view3d = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D)).Cast<View3D>()
                    .FirstOrDefault(v => !v.IsTemplate);
                if (view3d == null) return "No 3D view found (required for ray casting).";

                var refFilter = new LogicalOrFilter(new List<ElementFilter> {
                    new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                    new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                    new ElementCategoryFilter(BuiltInCategory.OST_Ceilings) });
                var intersector = new ReferenceIntersector(refFilter, FindReferenceTarget.Element, view3d);
                intersector.FindReferencesInRevitLinks = true;

                var directions = new Dictionary<string, XYZ> {
                    ["Top"] = XYZ.BasisZ, ["Bottom"] = -XYZ.BasisZ,
                    ["Left"] = -XYZ.BasisX, ["Right"] = XYZ.BasisX };

                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().Take(50).ToList();
                var violations = new List<string>();

                foreach (var d in ducts)
                {
                    if (d.Location is not LocationCurve lc) continue;
                    var mid = lc.Curve.Evaluate(0.5, true);
                    foreach (var (dir, vec) in directions)
                    {
                        var hit = intersector.FindNearest(mid, vec);
                        if (hit == null) continue;
                        double dist = mid.DistanceTo(hit.GetReference().GlobalPoint) * 304.8;
                        if (dist < thresholdMm)
                            violations.Add($"  ID:{d.Id} {dir} dist={Math.Round(dist)}mm < {thresholdMm}mm");
                    }
                }
                if (violations.Count == 0) return $"All ducts have ≥{thresholdMm}mm clearance.";
                return $"Clearance violations ({violations.Count}):\n" + string.Join("\n", violations.Take(30));
            }
        }
        ```

        ### EXAMPLE 27: Identify which Room each MEP element belongs to
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Architecture;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().Cast<Room>()
                    .Where(r => r.Area > 0).ToList();
                var pipes = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_PipeCurves)
                    .WhereElementIsNotElementType().Take(100).ToList();

                var mappings = new List<string>();
                int mapped = 0;
                foreach (var pipe in pipes)
                {
                    XYZ pt = pipe.Location is LocationCurve lc
                        ? lc.Curve.Evaluate(0.5, true) : null;
                    if (pt == null) continue;
                    string roomInfo = "(no room)";
                    foreach (var room in rooms)
                    {
                        if (room.IsPointInRoom(pt))
                        {
                            string num = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                            string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                            roomInfo = $"Room {num} - {name}";
                            mapped++;
                            break;
                        }
                    }
                    mappings.Add($"  Pipe {pipe.Id}: {roomInfo}");
                }
                return $"Pipe-Room mapping ({mapped}/{pipes.Count} mapped):\n" +
                       string.Join("\n", mappings.Take(30));
            }
        }
        ```

        ### EXAMPLE 28: Split duct/pipe and create union fittings
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Mechanical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                long targetId = 123456; // Replace with actual element ID
                double splitMm = 1000; // 1 meter segments
                double splitFt = splitMm / 304.8;
                var elem = doc.GetElement(new ElementId(targetId));
                if (elem == null) return "Element not found.";
                if (elem.Location is not LocationCurve lc) return "Not a curve element.";

                double totalLen = lc.Curve.Length;
                int numSplits = (int)(totalLen / splitFt);
                if (numSplits <= 0) return "Element shorter than split distance.";

                using var tx = new Transaction(doc, "Split Duct");
                tx.Start();
                var segments = new List<ElementId>();
                var currentId = elem.Id;

                for (int i = 1; i <= numSplits; i++)
                {
                    var curve = ((LocationCurve)doc.GetElement(currentId).Location).Curve;
                    var start = curve.GetEndPoint(0); var end = curve.GetEndPoint(1);
                    var dir = end - start; double len = dir.GetLength();
                    double ratio = splitFt / len;
                    var pt = start + dir * ratio;
                    var newId = MechanicalUtils.BreakCurve(doc, currentId, pt);
                    segments.Add(newId);
                }
                segments.Add(currentId);

                int fittings = 0;
                for (int i = 0; i < segments.Count - 1; i++)
                {
                    var e1 = doc.GetElement(segments[i]) as MEPCurve;
                    var e2 = doc.GetElement(segments[i + 1]) as MEPCurve;
                    if (e1 == null || e2 == null) continue;
                    foreach (Connector c1 in e1.ConnectorManager.Connectors)
                        foreach (Connector c2 in e2.ConnectorManager.Connectors)
                            if (c1.Origin.DistanceTo(c2.Origin) < 0.001)
                            { try { doc.Create.NewUnionFitting(c1, c2); fittings++; } catch {} goto next; }
                    next:;
                }
                tx.Commit();
                return $"Split into {segments.Count} segments, {fittings} union fittings created.";
            }
        }
        ```

        ### EXAMPLE 29: MEP elements in Space with airflow summary
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Mechanical;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                var spaces = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_MEPSpaces)
                    .WhereElementIsNotElementType().Cast<Space>()
                    .Where(s => s.Area > 0).ToList();
                var terminals = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctTerminal)
                    .WhereElementIsNotElementType().Cast<FamilyInstance>().ToList();

                var lines = new List<string> { $"Spaces: {spaces.Count}, Air Terminals: {terminals.Count}" };
                foreach (var space in spaces.Take(20))
                {
                    string num = space.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                    string name = space.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "";
                    int termCount = 0;
                    foreach (var t in terminals)
                    {
                        XYZ pt = null;
                        if (t.HasSpatialElementCalculationPoint)
                            pt = t.GetSpatialElementCalculationPoint();
                        else if (t.Location is LocationPoint lp) pt = lp.Point;
                        if (pt != null && space.IsPointInSpace(pt)) termCount++;
                    }
                    double area = Math.Round(space.Area * 0.092903, 1);
                    lines.Add($"  Space {num} {name}: {area}m², {termCount} terminals");
                }
                return string.Join("\n", lines);
            }
        }
        ```

        ### EXAMPLE 30: Write room number to MEP parameter
        ```csharp
        using System;
        using System.Linq;
        using System.Collections.Generic;
        using Autodesk.Revit.DB;
        using Autodesk.Revit.DB.Architecture;

        public static class DynamicAction
        {
            public static string Execute(Document doc)
            {
                string targetParam = "Comments"; // Parameter to write room number
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType().Cast<Room>()
                    .Where(r => r.Area > 0).ToList();
                var ducts = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_DuctCurves)
                    .WhereElementIsNotElementType().ToList();

                using var tx = new Transaction(doc, "Write Room to Ducts");
                tx.Start();
                int written = 0;
                foreach (var d in ducts)
                {
                    if (d.Location is not LocationCurve lc) continue;
                    var mid = lc.Curve.Evaluate(0.5, true);
                    foreach (var room in rooms)
                    {
                        if (room.IsPointInRoom(mid))
                        {
                            string roomNum = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "";
                            var p = d.LookupParameter(targetParam);
                            if (p != null && !p.IsReadOnly) { p.Set(roomNum); written++; }
                            break;
                        }
                    }
                }
                tx.Commit();
                return $"Written room numbers to {written}/{ducts.Count} ducts via '{targetParam}'.";
            }
        }
        ```
        """;
}
