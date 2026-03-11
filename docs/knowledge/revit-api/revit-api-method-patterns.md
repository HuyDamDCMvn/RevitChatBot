# Revit API Method Patterns Reference

Source: Extracted from RevitLookup Descriptor Resolve() methods — correct API call patterns for ~200+ methods.

## Methods Requiring View Parameter

These methods need an active View. Passing null may give incorrect results or throw exceptions.

| Class | Method | Correct Usage |
|-------|--------|---------------|
| Element | `IsHidden(View)` | `elem.IsHidden(activeView)` — returns true if hidden in that view |
| Element | `CanBeHidden(View)` | `elem.CanBeHidden(activeView)` — check before hiding |
| Element | `get_BoundingBox(View)` | `null` = model coords, `activeView` = view-clipped |
| Element | `get_Geometry(Options)` | `new Options { View = activeView }` for view-specific |
| View | `SetElementOverrides` | `view.SetElementOverrides(elemId, ogs)` |
| View | `GetElementOverrides` | `view.GetElementOverrides(elemId)` |
| View | `IsolateElementsTemporary` | `view.IsolateElementsTemporary(idList)` |

## Methods Requiring Document Parameter

| Class | Method | Correct Usage |
|-------|--------|---------------|
| InsulationLiningBase | `GetInsulationIds` | `InsulationLiningBase.GetInsulationIds(doc, elemId)` — TWO params |
| Reference | `ConvertToStableRepresentation` | `ref.ConvertToStableRepresentation(doc)` |
| Schema | `Lookup` | `Schema.Lookup(guid)` — static, may return null |
| ElementId | → Element resolution | `doc.GetElement(elementId)` |
| GlobalParameter | `GetAllGlobalParameters` | `GlobalParameter.GetAllGlobalParameters(doc)` |

## Methods with Multiple Overloads

### Element.GetMaterialIds
```csharp
element.GetMaterialIds(true);   // returns paint material IDs
element.GetMaterialIds(false);  // returns non-paint material IDs (most common)
```

### Element.get_BoundingBox
```csharp
element.get_BoundingBox(null);        // model BoundingBox (all coordinates)
element.get_BoundingBox(activeView);  // view-specific (clipped to view range)
```

### Entity.Get (ExtensibleStorage)
```csharp
entity.Get<string>("FieldName");                           // 1-param: string field
entity.Get<string>("FieldName", UnitTypeId.Millimeters);   // 2-param: with unit conversion
entity.Get<T>(field);                                       // using Field object
```

### Connector — Domain-Specific Properties
```csharp
// HVAC connector — has flow, pressure
if (c.Domain == Domain.DomainHvac)
{
    DuctSystemType sysType = c.DuctSystemType;
    double flow = c.Flow;
    double pressure = c.PressureDrop;
}
// Piping connector — has flow, pressure
if (c.Domain == Domain.DomainPiping)
{
    PipeSystemType sysType = c.PipeSystemType;
    double flow = c.Flow;
    double pressure = c.PressureDrop;
}
// Electrical connector — has demand, NO flow/pressure
if (c.Domain == Domain.DomainElectrical)
{
    ElectricalSystemType sysType = c.ElectricalSystemType;
    double demand = c.Demand;
    // c.Flow → throws InvalidOperationException!
}
```

## Disabled / Dangerous Methods

These methods should NEVER be called in add-in code:

| Method | Risk | Alternative |
|--------|------|-------------|
| `Document.Close()` | Crashes Revit | Let user close manually |
| `Document.Save()` | Data loss risk | Only with user consent |
| `Application.OpenAndActivateDocument()` | Context switch issues | Use UIDocument approach |
| `Element.Delete()` | N/A in Revit API | Use `doc.Delete(elemId)` |

## Safe Access Patterns

### Null-Safe Property Access
```csharp
// Parameter reading — always null-check
string val = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
double num = elem.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0;

// LookupParameter — may return null for missing shared params
Parameter p = elem.LookupParameter("Mark");
string mark = p?.AsString() ?? p?.AsValueString() ?? "";
```

### Type Info Extraction
```csharp
string familyName = "", typeName = "";
if (doc.GetElement(elem.GetTypeId()) is ElementType et)
{
    typeName = et.Name;
    if (et is FamilySymbol fs) familyName = fs.FamilyName;
}
```

### MEPSystem Access
```csharp
// Not all MEP elements belong to a system
if (elem is MEPCurve mc && mc.MEPSystem is MechanicalSystem mechSys)
{
    string sysName = mechSys.Name;
    ElementSet network = mechSys.DuctNetwork;
}
// For pipes:
if (elem is Pipe pipe && pipe.MEPSystem is PipingSystem pipeSys)
{
    string sysName = pipeSys.Name;
}
```

### CompoundStructure Access
```csharp
// Not all WallTypes have CompoundStructure (curtain walls don't)
if (wallType.GetCompoundStructure() is CompoundStructure cs)
{
    foreach (var layer in cs.GetLayers())
    {
        double widthFt = layer.Width;
        MaterialFunctionAssignment func = layer.Function;
        ElementId matId = layer.MaterialId;
    }
}
```

### ExtensibleStorage Safe Pattern
```csharp
var schema = Schema.Lookup(guid);
if (schema != null)
{
    var entity = element.GetEntity(schema);
    if (entity.IsValid())
    {
        var field = schema.GetField("FieldName");
        if (field != null)
        {
            var value = entity.Get<string>(field);
        }
    }
}
```

## Element Location Patterns

```csharp
// Universal element location resolution
XYZ GetLocation(Element elem)
{
    if (elem.Location is LocationPoint lp) return lp.Point;
    if (elem.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
    if (elem is FamilyInstance fi && fi.HasSpatialElementCalculationPoint)
        return fi.GetSpatialElementCalculationPoint();
    var bb = elem.get_BoundingBox(null);
    return bb != null ? (bb.Min + bb.Max) / 2 : XYZ.Zero;
}
```

## Connector Relationship Classification

```csharp
// Classify connector pair using dot product of directions
Transform t1 = c1.CoordinateSystem, t2 = c2.CoordinateSystem;
double dot = t1.BasisZ.DotProduct(t2.BasisZ);

// dot ≈ -1.0 → facing each other (main-to-main, inline)
// dot ≈  0.0 → perpendicular (main-to-branch, for Tee)
// dot ≈ +1.0 → same direction (parallel, usually wrong)
```

## Fitting Creation Order Rules

| Fitting | Connector Order | Rule |
|---------|----------------|------|
| Elbow | `(c1, c2)` | Both unconnected, at same point |
| Tee | `(main1, main2, branch)` | Main pair colinear (dot ≈ -1), branch perpendicular |
| Cross | `(main1, main2, branch1, branch2)` | Main pair colinear, both branches perpendicular |
| Transition | `(larger, smaller)` | Same axis, different sizes |
| Takeoff | `(branchConn, mepCurve)` | Branch connector + MEPCurve (not connector!) |
| Union | `(c1, c2)` | Coincident position (DistanceTo < 0.001) |
