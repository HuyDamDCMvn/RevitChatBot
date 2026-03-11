# Revit MEP Type Details Reference

Source: Extracted from RevitLookup descriptors for MEP-specific types.

## Duct (Autodesk.Revit.DB.Mechanical)

Inherits: MEPCurve → Element

### Key Properties
| Property | Type | Notes |
|----------|------|-------|
| `Width` | double (ft) | Rectangular ducts only, 0 for round |
| `Height` | double (ft) | Rectangular ducts only, 0 for round |
| `Diameter` | double (ft) | Round ducts only, 0 for rectangular |
| `ConnectorManager` | ConnectorManager | Direct access (MEPCurve) |
| `MEPSystem` | MEPSystem | MechanicalSystem, may be null |
| `DuctType` | DuctType | Access via `doc.GetElement(duct.GetTypeId())` |

### Key Parameters (BuiltInParameter)
| Parameter | Value |
|-----------|-------|
| `CURVE_ELEM_LENGTH` | Length in feet |
| `RBS_CALCULATED_SIZE` | String like "400x300" or "Ø250" |
| `RBS_DUCT_FLOW_PARAM` | Airflow in ft³/s |
| `RBS_VELOCITY` | Velocity in ft/s |
| `RBS_SYSTEM_NAME_PARAM` | System name string |
| `RBS_SYSTEM_CLASSIFICATION_PARAM` | Classification string |
| `RBS_START_LEVEL_PARAM` | Reference level ElementId |
| `RBS_REFERENCE_INSULATION_TYPE` | Insulation type string |
| `RBS_CURVE_WIDTH_PARAM` | Width (ft) — for rectangular |
| `RBS_CURVE_HEIGHT_PARAM` | Height (ft) — for rectangular |
| `RBS_CURVE_DIAMETER_PARAM` | Diameter (ft) — for round |

### Creation
```csharp
Duct.Create(doc, mechSystemTypeId, ductTypeId, levelId, startXYZ, endXYZ);
// mechSystemTypeId: from MechanicalSystemType
// ductTypeId: from DuctType
```

### Splitting
```csharp
ElementId newId = MechanicalUtils.BreakCurve(doc, ductId, splitPoint);
// using Autodesk.Revit.DB.Mechanical;
```

## Pipe (Autodesk.Revit.DB.Plumbing)

Inherits: MEPCurve → Element

### Key Properties
| Property | Type | Notes |
|----------|------|-------|
| `Diameter` | double (ft) | Pipe diameter |
| `ConnectorManager` | ConnectorManager | Direct access |
| `MEPSystem` | MEPSystem | PipingSystem, may be null |
| `FlexPipeType` / `PipeType` | Type | Access via GetTypeId() |

### Key Parameters
| Parameter | Value |
|-----------|-------|
| `RBS_PIPE_DIAMETER_PARAM` | Diameter in feet |
| `RBS_PIPE_VELOCITY_PARAM` | Velocity in ft/s |
| `RBS_PIPE_FLOW_PARAM` | Flow rate in ft³/s |
| `RBS_PIPE_SLOPE` | Slope as ratio (0.01 = 1%) |
| `CURVE_ELEM_LENGTH` | Length in feet |
| `RBS_PIPING_SYSTEM_TYPE_PARAM` | System type ElementId |

### Splitting
```csharp
ElementId newId = PlumbingUtils.BreakCurve(doc, pipeId, splitPoint);
// using Autodesk.Revit.DB.Plumbing;
```

## Conduit & CableTray (Autodesk.Revit.DB.Electrical)

Inherits: MEPCurve → Element

### Important: No BreakCurve!
Conduit and CableTray do NOT support BreakCurve. Use CopyElement workaround:
```csharp
var copiedIds = ElementTransformUtils.CopyElement(doc, elem.Id, XYZ.Zero);
var copy = doc.GetElement(copiedIds.First());
((LocationCurve)elem.Location).Curve = Line.CreateBound(start, splitPoint);
((LocationCurve)copy.Location).Curve = Line.CreateBound(splitPoint, end);
```

## Connector

Not an Element. Accessed via ConnectorManager.

### Properties by Domain

| Property | DomainHvac | DomainPiping | DomainElectrical |
|----------|------------|--------------|------------------|
| `Flow` | airflow (ft³/s) | flow (ft³/s) | THROWS! |
| `PressureDrop` | pressure | pressure | THROWS! |
| `DuctSystemType` | enum | THROWS! | THROWS! |
| `PipeSystemType` | THROWS! | enum | THROWS! |
| `ElectricalSystemType` | THROWS! | THROWS! | enum |
| `Demand` | THROWS! | THROWS! | demand |
| `Velocity` / `VelocityPressure` | available | available | THROWS! |

### Universal Properties (all domains)
| Property | Type | Notes |
|----------|------|-------|
| `Origin` | XYZ | Connector position |
| `CoordinateSystem` | Transform | BasisZ = outward direction |
| `Shape` | ConnectorProfileType | Round, Rectangular, Oval |
| `Radius` | double | Round only |
| `Width` / `Height` | double | Rectangular/Oval only |
| `IsConnected` | bool | Has connections? |
| `AllRefs` | ConnectorSet | Connected connectors |
| `Owner` | Element | Parent element |
| `Domain` | Domain | HVAC, Piping, Electrical, CableTrayConduit |
| `ConnectorType` | ConnectorType | End, Curve, Physical, Logical |

## MEP Systems

### MechanicalSystem (HVAC)
```csharp
MechanicalSystem mechSys = duct.MEPSystem as MechanicalSystem;
ElementSet network = mechSys.DuctNetwork;    // all connected elements
DuctSystemType sysType = mechSys.SystemType; // SupplyAir, ReturnAir, ExhaustAir
```

### PipingSystem (Plumbing/Hydronic)
```csharp
PipingSystem pipeSys = pipe.MEPSystem as PipingSystem;
PipeSystemType sysType = pipeSys.SystemType; // DomesticColdWater, Sanitary, etc.
```

### System Classification Complete List
| Enum Value | Vietnamese | Abbreviation |
|-----------|-----------|-------------|
| SupplyAir | Cấp gió | SA |
| ReturnAir | Hồi gió | RA |
| ExhaustAir | Thải gió | EA |
| OtherAir | Gió khác | OA |
| SupplyHydronic | Cấp nước kỹ thuật (CHW/HW supply) | CHS, HWS |
| ReturnHydronic | Hồi nước kỹ thuật (CHW/HW return) | CHR, HWR |
| DomesticColdWater | Nước lạnh sinh hoạt | DCW |
| DomesticHotWater | Nước nóng sinh hoạt | DHW |
| Sanitary | Nước thải | SAN |
| Vent | Thông hơi | V |
| FireProtectWet | PCCC ướt | FPW |
| FireProtectDry | PCCC khô | FPD |
| FireProtectPreaction | PCCC pre-action | FPP |
| FireProtectOther | PCCC khác | FPO |
| OtherPipe | Ống khác (storm water in 2025) | OP |

## Document

### Safe Properties
| Property | Type | Notes |
|----------|------|-------|
| `Title` | string | Document title |
| `PathName` | string | File path |
| `IsWorkshared` | bool | Worksharing enabled? |
| `ActiveView` | View | Current active view |
| `GetWarnings()` | IList | Model warnings |

### DANGEROUS — Do NOT Call
| Method | Risk |
|--------|------|
| `Close()` | Crashes Revit |
| `Save()` | Data loss without consent |

## ForgeTypeId (Revit 2025 Parameter System)

Replaces deprecated ParameterType and UnitType.

### Common SpecTypeId Values
| SpecTypeId | Description | Unit |
|-----------|-------------|------|
| `SpecTypeId.Length` | Length | UnitTypeId.Millimeters, Meters, Feet |
| `SpecTypeId.Area` | Area | UnitTypeId.SquareMeters |
| `SpecTypeId.Volume` | Volume | UnitTypeId.CubicMeters |
| `SpecTypeId.Angle` | Angle | UnitTypeId.Degrees |
| `SpecTypeId.Flow` | Flow rate | UnitTypeId.LitersPerSecond |
| `SpecTypeId.HvacVelocity` | HVAC velocity | UnitTypeId.MetersPerSecond |
| `SpecTypeId.PipingVelocity` | Piping velocity | UnitTypeId.MetersPerSecond |
| `SpecTypeId.DuctSize` | Duct dimension | UnitTypeId.Millimeters |
| `SpecTypeId.PipeSize` | Pipe dimension | UnitTypeId.Millimeters |
| `SpecTypeId.HvacPressure` | HVAC pressure | UnitTypeId.Pascals |
| `SpecTypeId.PipingPressure` | Piping pressure | UnitTypeId.Pascals |
| `SpecTypeId.PipeSlope` | Pipe slope | ratio (0.01 = 1%) |

### Usage
```csharp
ForgeTypeId dataType = param.Definition.GetDataType();
ForgeTypeId groupType = param.Definition.GetGroupTypeId();
bool isLength = dataType == SpecTypeId.Length;
```

## RoutingPreferenceManager

Available on DuctType and PipeType for auto-routing configuration.

```csharp
var pipeType = doc.GetElement(pipe.GetTypeId()) as PipeType;
var rpm = pipeType.RoutingPreferenceManager;

// Rule groups
int elbows = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
int junctions = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Junctions);
int crosses = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Crosses);
int transitions = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Transitions);
int segments = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);

// Preferred junction type
var juncType = rpm.PreferredJunctionType; // Tee or Tap

// Get fitting family for a rule
var rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Elbows, 0);
var fittingSymbol = doc.GetElement(rule.MEPPartId); // FamilySymbol
```

## ExtensibleStorage (Schema/Entity)

### Schema Discovery
```csharp
var schemas = Schema.ListSchemas();
var schema = Schema.Lookup(guid);
var elements = new FilteredElementCollector(doc)
    .WherePasses(new ExtensibleStorageFilter(schema.GUID))
    .ToElements();
```

### Entity Read
```csharp
var entity = element.GetEntity(schema);
if (entity.IsValid())
{
    foreach (var field in schema.ListFields())
    {
        string name = field.FieldName;
        Type valueType = field.ValueType;
        // Read based on type
    }
}
```

## CompoundStructure (Wall/Floor Layers)

```csharp
if (wallType.GetCompoundStructure() is CompoundStructure cs)
{
    int layerCount = cs.LayerCount;
    foreach (var layer in cs.GetLayers())
    {
        double width = layer.Width;                    // feet
        MaterialFunctionAssignment func = layer.Function; // Structure, Finish, etc.
        ElementId materialId = layer.MaterialId;
    }
}
// Returns null for: Curtain walls, Stacked walls
```

## FamilySizeTable

```csharp
var mgr = FamilySizeTableManager.GetFamilySizeTableManager(doc, family.Id);
var tableNames = mgr.GetAllSizeTableNames();
var table = mgr.GetSizeTable(tableName);
int cols = table.NumberOfColumns;
int rows = table.NumberOfRows;
var header = table.GetColumnHeader(colIndex);
```
