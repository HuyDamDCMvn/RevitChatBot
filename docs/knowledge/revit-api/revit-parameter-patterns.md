# Revit API Parameter Access Patterns

Source: Extracted from RevitLookup descriptors — correct parameter reading/writing patterns.

## Parameter Access Methods

### 1. BuiltInParameter (Fastest, Most Reliable)
```csharp
Parameter p = elem.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
```
- Fastest lookup method
- Works across all localizations
- Type-safe via enum
- Returns null if parameter doesn't exist on this element

### 2. LookupParameter by Name (Shared/Project Parameters)
```csharp
Parameter p = elem.LookupParameter("Mark");
```
- For shared parameters and project parameters
- Name is localization-dependent!
- Returns null if not found

### 3. Parameter Iteration
```csharp
foreach (Parameter p in elem.Parameters)
{
    string name = p.Definition.Name;
    StorageType type = p.StorageType;
}
```

## Reading Parameter Values

```csharp
// String parameters
string val = param.AsString();

// Double parameters (returns internal units — FEET for length!)
double val = param.AsDouble();

// Integer parameters
int val = param.AsInteger();

// ElementId parameters
ElementId val = param.AsElementId();

// Display value (user-facing, localized, includes units)
string display = param.AsValueString();
```

### Safe Reading Pattern
```csharp
string val = param?.AsString() ?? param?.AsValueString() ?? "";
double num = param?.AsDouble() ?? 0;
```

## Writing Parameter Values

```csharp
// ALWAYS inside Transaction!
// ALWAYS check IsReadOnly first!
if (param != null && !param.IsReadOnly)
{
    switch (param.StorageType)
    {
        case StorageType.String:
            param.Set("new value");
            break;
        case StorageType.Double:
            param.Set(valueFeet); // INTERNAL UNITS (feet)!
            break;
        case StorageType.Integer:
            param.Set(42);
            break;
        case StorageType.ElementId:
            param.Set(newElementId);
            break;
    }
}
```

## ForgeTypeId System (Revit 2025)

### Getting Parameter Data Type
```csharp
ForgeTypeId dataType = param.Definition.GetDataType();
ForgeTypeId groupType = param.Definition.GetGroupTypeId();
```

### Common SpecTypeId Values
| SpecTypeId | What it Represents |
|-----------|-------------------|
| `SpecTypeId.Length` | Length dimension |
| `SpecTypeId.Area` | Area measurement |
| `SpecTypeId.Volume` | Volume measurement |
| `SpecTypeId.Angle` | Angular measurement |
| `SpecTypeId.Flow` | Flow rate (L/s, CFM) |
| `SpecTypeId.HvacVelocity` | HVAC air velocity |
| `SpecTypeId.PipingVelocity` | Pipe water velocity |
| `SpecTypeId.DuctSize` | Duct dimension |
| `SpecTypeId.PipeSize` | Pipe dimension |
| `SpecTypeId.ConduitSize` | Conduit dimension |
| `SpecTypeId.HvacPressure` | HVAC pressure |
| `SpecTypeId.PipingPressure` | Piping pressure |
| `SpecTypeId.PipeSlope` | Pipe slope |
| `SpecTypeId.Number` | Generic number |
| `SpecTypeId.String.Text` | Text string |
| `SpecTypeId.Boolean.YesNo` | Yes/No |
| `SpecTypeId.Reference.Material` | Material reference |

### Unit Conversion (Revit 2025)
```csharp
// To internal units (model stores everything in feet/radians)
double ftValue = UnitUtils.ConvertToInternalUnits(1000, UnitTypeId.Millimeters);
double ftValue = UnitUtils.ConvertToInternalUnits(1.5, UnitTypeId.Meters);
double radians = UnitUtils.ConvertToInternalUnits(90, UnitTypeId.Degrees);

// From internal units (for display)
double mm = UnitUtils.ConvertFromInternalUnits(ftValue, UnitTypeId.Millimeters);
double ms = UnitUtils.ConvertFromInternalUnits(ftPerSec, UnitTypeId.MetersPerSecond);
double lps = UnitUtils.ConvertFromInternalUnits(cuFtPerSec, UnitTypeId.LitersPerSecond);
double pa = UnitUtils.ConvertFromInternalUnits(intPressure, UnitTypeId.Pascals);
```

### Quick Conversion Factors (when UnitUtils not available)
| Conversion | Factor |
|-----------|--------|
| feet → mm | × 304.8 |
| feet → m | × 0.3048 |
| mm → feet | ÷ 304.8 |
| m → feet | × 3.28084 |
| sq ft → m² | × 0.092903 |
| cu ft → m³ | × 0.0283168 |
| ft/s → m/s | × 0.3048 |
| slope ratio → % | × 100 |

## MEP-Specific BuiltInParameters

### Duct Parameters
| BuiltInParameter | Description | StorageType | Units |
|-----------------|-------------|-------------|-------|
| `CURVE_ELEM_LENGTH` | Duct length | Double | feet |
| `RBS_CALCULATED_SIZE` | Size string "400x300" | String | — |
| `RBS_DUCT_FLOW_PARAM` | Airflow | Double | ft³/s |
| `RBS_VELOCITY` | Air velocity | Double | ft/s |
| `RBS_CURVE_WIDTH_PARAM` | Width (rectangular) | Double | feet |
| `RBS_CURVE_HEIGHT_PARAM` | Height (rectangular) | Double | feet |
| `RBS_CURVE_DIAMETER_PARAM` | Diameter (round) | Double | feet |
| `RBS_SYSTEM_NAME_PARAM` | System name | String | — |
| `RBS_SYSTEM_CLASSIFICATION_PARAM` | Classification | String | — |
| `RBS_DUCT_SYSTEM_TYPE_PARAM` | System type ID | ElementId | — |
| `RBS_START_LEVEL_PARAM` | Reference level | ElementId | — |
| `RBS_REFERENCE_INSULATION_TYPE` | Insulation ref | String | — |

### Pipe Parameters
| BuiltInParameter | Description | StorageType | Units |
|-----------------|-------------|-------------|-------|
| `RBS_PIPE_DIAMETER_PARAM` | Pipe diameter | Double | feet |
| `RBS_PIPE_VELOCITY_PARAM` | Water velocity | Double | ft/s |
| `RBS_PIPE_FLOW_PARAM` | Flow rate | Double | ft³/s |
| `RBS_PIPE_SLOPE` | Slope | Double | ratio |
| `RBS_PIPING_SYSTEM_TYPE_PARAM` | System type ID | ElementId | — |

### General MEP Parameters
| BuiltInParameter | Description | Applicable To |
|-----------------|-------------|---------------|
| `ELEM_FAMILY_AND_TYPE_PARAM` | Family & Type | All elements |
| `ALL_MODEL_MARK` | Mark | All elements |
| `ALL_MODEL_INSTANCE_COMMENTS` | Comments | All elements |
| `RBS_SYSTEM_NAME_PARAM` | System name | All MEP |
| `RBS_SYSTEM_CLASSIFICATION_PARAM` | Classification | All MEP |
| `RBS_START_LEVEL_PARAM` | Level | All MEP curves |

### Room/Space Parameters
| BuiltInParameter | Description |
|-----------------|-------------|
| `ROOM_NAME` | Room name |
| `ROOM_NUMBER` | Room number |
| `ROOM_AREA` | Room area (sq ft) |
| `ROOM_VOLUME` | Room volume (cu ft) |
| `ROOM_LEVEL_ID` | Level ElementId |
| `ROOM_UPPER_LEVEL` | Upper level |
| `ROOM_LOWER_OFFSET` | Lower offset |
| `ROOM_UPPER_OFFSET` | Upper offset |

### View Parameters
| BuiltInParameter | Description |
|-----------------|-------------|
| `VIEW_SCALE` | Scale denominator (e.g., 100 for 1:100) |
| `VIEW_DETAIL_LEVEL` | Detail level (Coarse/Medium/Fine) |
| `PLAN_VIEW_RANGE` | PlanViewRange object |

### Structural Parameters
| BuiltInParameter | Description |
|-----------------|-------------|
| `STRUCTURAL_SECTION_COMMON_HEIGHT` | Section height |
| `STRUCTURAL_SECTION_COMMON_WIDTH` | Section width |

## Parameter Groups (ForgeTypeId)

Common parameter group type IDs:
- `GroupTypeId.Dimensions` — Length, Width, Height, Area, Volume
- `GroupTypeId.Mechanical` — HVAC parameters
- `GroupTypeId.MechanicalAirflow` — Airflow specific
- `GroupTypeId.Plumbing` — Piping parameters
- `GroupTypeId.Electrical` — Electrical parameters
- `GroupTypeId.Identity` — Mark, Comments, Description
- `GroupTypeId.Constraints` — Level, Offset
- `GroupTypeId.Construction` — Materials, Layers

## Common Parameter Access Mistakes

1. **Forgetting internal units**: `param.AsDouble()` returns FEET, not mm or meters
2. **Not checking null**: `get_Parameter()` returns null if param doesn't exist
3. **Writing to readonly**: Always check `param.IsReadOnly` before `Set()`
4. **Missing Transaction**: All `Set()` calls need an active Transaction
5. **AsString vs AsValueString**: `AsString()` returns raw string, `AsValueString()` returns formatted display string
6. **Wrong StorageType**: Using `AsString()` on a Double parameter returns null — use `AsValueString()` instead
