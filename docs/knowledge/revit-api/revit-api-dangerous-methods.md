# Revit API Dangerous & Context-Sensitive Methods

Source: Extracted from RevitLookup Descriptor patterns — methods that crash, throw, or behave unexpectedly.

## DISABLED Methods — NEVER Call

| Class | Method | Risk | Notes |
|-------|--------|------|-------|
| Document | `Close()` | **CRASH** | Crashes Revit process. Never call programmatically. |
| Document | `Save()` | **Data Loss** | May overwrite without user consent. Only with explicit confirmation. |
| Document | `SaveAs()` | **Data Loss** | Same as Save. |
| Application | `OpenAndActivateDocument()` | **Context Switch** | Unreliable in ExternalEvent handlers. |

## Methods That Throw Based on Domain

### Connector Domain Restrictions

Accessing wrong-domain properties throws `InvalidOperationException`:

| Property | DomainHvac | DomainPiping | DomainElectrical | DomainCableTrayConduit |
|----------|:----------:|:------------:|:----------------:|:---------------------:|
| `Flow` | OK | OK | THROWS | THROWS |
| `PressureDrop` | OK | OK | THROWS | THROWS |
| `VelocityPressure` | OK | OK | THROWS | THROWS |
| `Coefficient` | OK | OK | THROWS | THROWS |
| `DuctSystemType` | OK | THROWS | THROWS | THROWS |
| `PipeSystemType` | THROWS | OK | THROWS | THROWS |
| `ElectricalSystemType` | THROWS | THROWS | OK | THROWS |
| `Demand` | THROWS | THROWS | OK | THROWS |

**Safe pattern:**
```csharp
if (c.Domain == Domain.DomainHvac)
{
    double flow = c.Flow;
    double pressure = c.PressureDrop;
    DuctSystemType sysType = c.DuctSystemType;
}
else if (c.Domain == Domain.DomainPiping)
{
    double flow = c.Flow;
    PipeSystemType sysType = c.PipeSystemType;
}
else if (c.Domain == Domain.DomainElectrical)
{
    double demand = c.Demand;
    ElectricalSystemType sysType = c.ElectricalSystemType;
}
```

## Methods That Return Null (Must Check)

| Class | Method/Property | When Null |
|-------|----------------|-----------|
| `Element` | `MEPSystem` | Element not assigned to any system |
| `FamilyInstance` | `MEPModel` | Non-MEP family (furniture, structural) |
| `FamilyInstance.MEPModel` | `ConnectorManager` | Family has no connectors defined |
| `Element` | `get_BoundingBox(view)` | Element invisible in that view |
| `Element` | `get_Parameter(bip)` | Parameter doesn't exist on this element |
| `Element` | `LookupParameter(name)` | Shared/project parameter not present |
| `WallType` | `GetCompoundStructure()` | Curtain wall, stacked wall |
| `Schema` | `Lookup(guid)` | Schema not registered in document |
| `Entity` | `GetEntity(schema)` | Element has no data for this schema |
| `Element` | `Category` | Some system elements have no category |
| `Element` | `LevelId` | Element not associated with a level |
| `MEPCurve` | `MEPSystem` | Unassigned MEP element |
| `FamilyInstance` | `SuperComponent` | Not a nested family |

**Safe pattern:**
```csharp
var sys = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
var level = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsValueString() ?? "";
```

## Methods That Need Transaction

Any modification to the Revit model requires an active Transaction:

```csharp
using var tx = new Transaction(doc, "Operation Name");
tx.Start();
// ... modifications ...
tx.Commit();
```

Operations requiring Transaction:
- `Parameter.Set()` — setting any parameter value
- `doc.Delete(elemId)` — deleting elements
- `doc.Create.NewElbowFitting()` — creating fittings
- `Pipe.Create()`, `Duct.Create()` — creating elements
- `MechanicalUtils.BreakCurve()` — splitting elements
- `ElementTransformUtils.CopyElement()` — copying
- `view.SetElementOverrides()` — changing view overrides
- `element.GetEntity(schema).Set<T>()` — ExtensibleStorage write

Operations NOT requiring Transaction:
- All read operations (get_Parameter, LookupParameter)
- FilteredElementCollector queries
- BoundingBox access
- Connector traversal
- Schema.Lookup, entity.Get<T>

## Fitting Creation Failures

| Fitting | Common Failure | Solution |
|---------|---------------|----------|
| `NewElbowFitting` | Connectors already connected | Use only unconnected connectors |
| `NewElbowFitting` | Connectors not at same point | Ensure connectors are co-located |
| `NewTeeFitting` | Wrong connector order | Main pair (dot ≈ -1) first, branch last |
| `NewCrossFitting` | Wrong connector order | Main pair first, then two branches |
| `NewTransitionFitting` | Not colinear | Connectors must be on same axis |
| `NewTakeoffFitting` | Wrong parameter type | 1st param = branch Connector, 2nd = MEPCurve (not connector!) |
| `NewUnionFitting` | Not coincident | Connectors must be at exact same position (dist < 0.001) |

## BreakCurve Failures

| Issue | Cause | Solution |
|-------|-------|----------|
| `BreakCurve` fails | Split point not on curve | Verify point lies on the curve line |
| `BreakCurve` fails | Point at endpoint | Point must be between (not at) endpoints |
| `BreakCurve` fails | Short curve tolerance | Resulting segments too short |
| `BreakCurve` not available | Conduit/CableTray | Use CopyElement + LocationCurve workaround |

## ReadOnly Parameters

Always check `parameter.IsReadOnly` before `Set()`:
```csharp
var param = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
if (param != null && !param.IsReadOnly)
    param.Set("NewValue");
```

Common read-only parameters:
- `ELEM_TYPE_PARAM` — Element type (change via ChangeTypeId)
- `ELEM_CATEGORY_PARAM` — Category
- Most calculated parameters (velocity, flow when auto-calculated)
- `CURVE_ELEM_LENGTH` — Length (change via LocationCurve)

## Revit 2025 Deprecated APIs

| Deprecated | Replacement |
|-----------|-------------|
| `ParameterType` enum | `ForgeTypeId` / `SpecTypeId` |
| `UnitType` enum | `UnitTypeId` |
| `parameter.Definition.ParameterGroup` | `param.Definition.GetGroupTypeId()` |
| `ElementSet.Size` | `.Count` property or LINQ `.Count()` |
| `DuctSettings.AirViscosity` | `AirDynamicViscosity` |
| `PipeSystemType.Storm` | `PipeSystemType.OtherPipe` |
| `DisplayUnitType` | `ForgeTypeId` |
