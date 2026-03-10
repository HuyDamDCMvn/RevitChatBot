# MEP Connector & System Graph Knowledge

Reference knowledge for Revit MEP connector operations, fitting creation, routing preferences,
unit conversions, and system graph traversal. Sourced from OpenMEP patterns and production tools.

## 1. Connector Access — Universal Pattern

Revit MEP elements expose connectors via `ConnectorManager`:

| Element Type | Access Pattern |
|---|---|
| `MEPCurve` (Duct, Pipe, FlexDuct, FlexPipe, Conduit, CableTray) | `mepCurve.ConnectorManager` |
| `FamilyInstance` (equipment, fittings, terminals) | `fi.MEPModel?.ConnectorManager` |
| `FabricationPart` | `fp.ConnectorManager` |

Always check for null: `FamilyInstance.MEPModel` can be null for non-MEP families.

### Connector Properties

| Property | Type | Description |
|---|---|---|
| `Origin` | XYZ | Position of connector center |
| `CoordinateSystem.BasisZ` | XYZ | Outward direction of the connector |
| `Domain` | Domain | `DomainHvac`, `DomainPiping`, `DomainElectrical` |
| `Shape` | ConnectorProfileType | `Round`, `Rectangular`, `Oval` |
| `IsConnected` | bool | Whether the connector has a connection |
| `AllRefs` | ConnectorSet | All connected connectors |
| `Flow` | double | Current flow rate |
| `AssignedFlow` | double | User-assigned flow |
| `PressureDrop` | double | Pressure drop at connector |
| `VelocityPressure` | double | Velocity pressure |
| `Coefficient` | double | Loss coefficient |
| `Demand` | double | Electrical demand |
| `Radius` | double | For Round connectors |
| `Width`, `Height` | double | For Rectangular/Oval connectors |

### Connector Area Calculation

```
Round:       A = π × r²
Rectangular: A = W × H
Oval:        A = π × W × H / 4
```

All dimensions in Revit internal units (feet). Convert to mm²: multiply by 92903.04.

### Connector Direction

Use `CoordinateSystem.BasisZ` to get the outward-facing direction of a connector.
This is critical for:
- Determining main vs branch connectors (for Tee/Cross fitting creation)
- Checking if connectors are colinear (dot product ≈ -1) or perpendicular (dot product ≈ 0)

## 2. Fitting Creation — Connector Ordering

### Elbow (2 connectors)
```csharp
doc.Create.NewElbowFitting(conn1, conn2);
```
Both connectors must be unconnected and at different angles.

### Tee (3 connectors — ORDER MATTERS)
```csharp
doc.Create.NewTeeFitting(mainConn1, mainConn2, branchConn);
```
- `mainConn1` and `mainConn2`: colinear (BasisZ dot product ≈ -1)
- `branchConn`: perpendicular to main (BasisZ dot product with main ≈ 0)
- If order is wrong, the API throws an exception.

### Cross (4 connectors — ORDER MATTERS)
```csharp
doc.Create.NewCrossFitting(mainConn1, mainConn2, branchConn1, branchConn2);
```
- Main pair: colinear
- Branch pair: perpendicular to main

### Transition (2 connectors — different sizes, same axis)
```csharp
doc.Create.NewTransitionFitting(largerConn, smallerConn);
```

### Takeoff (branch from MEPCurve)
```csharp
doc.Create.NewTakeoffFitting(branchConnector, mepCurve);
```
Note: second parameter is the MEPCurve itself, not a connector.

### Union (coincident connectors from split)
```csharp
doc.Create.NewUnionFitting(conn1, conn2);
```
Connectors must be at same position (distance < 0.001 ft).

### Air Terminal on Duct
```csharp
MechanicalUtils.ConnectAirTerminalOnDuct(doc, airTerminalInstance, ductConnector);
```

## 3. Routing Preferences

Accessed via `PipeType.RoutingPreferenceManager` or `DuctType.RoutingPreferenceManager`.

### Rule Groups (RoutingPreferenceRuleGroupType)
| Group | Description |
|---|---|
| `Segments` | Preferred pipe/duct segment types |
| `Elbows` | Elbow fitting families for turns |
| `Junctions` | Tee/Tap fitting families for branches |
| `Crosses` | Cross fitting families |
| `Transitions` | Reducer/Increaser fitting families |

### Junction Type
`rpm.PreferredJunctionType` returns either:
- `RoutingPreferenceJunctionType.Tee` — classic tee fitting
- `RoutingPreferenceJunctionType.Tap` — saddle tap fitting

### Rule Lookup
```csharp
var rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Elbows, 0);
var fittingId = rule.MEPPartId; // ElementId of the FamilySymbol
```

## 4. Unit Conversion — SpecTypeId Mapping (Revit 2025)

Revit 2025 uses `UnitTypeId` for unit conversion and `SpecTypeId` for parameter specifications.

### Common Conversions
| Quantity | To Internal | From Internal | UnitTypeId |
|---|---|---|---|
| Length (mm) | `ConvertToInternalUnits(v, UnitTypeId.Millimeters)` | `ConvertFromInternalUnits(v, UnitTypeId.Millimeters)` | `UnitTypeId.Millimeters` |
| Length (m) | `ConvertToInternalUnits(v, UnitTypeId.Meters)` | same pattern | `UnitTypeId.Meters` |
| Area (m²) | `ConvertToInternalUnits(v, UnitTypeId.SquareMeters)` | same pattern | `UnitTypeId.SquareMeters` |
| Volume (m³) | `ConvertToInternalUnits(v, UnitTypeId.CubicMeters)` | same pattern | `UnitTypeId.CubicMeters` |
| Flow (L/s) | `ConvertToInternalUnits(v, UnitTypeId.LitersPerSecond)` | same pattern | `UnitTypeId.LitersPerSecond` |
| Velocity (m/s) | `ConvertToInternalUnits(v, UnitTypeId.MetersPerSecond)` | same pattern | `UnitTypeId.MetersPerSecond` |
| Pressure (Pa) | `ConvertToInternalUnits(v, UnitTypeId.Pascals)` | same pattern | `UnitTypeId.Pascals` |
| Angle (°) | `ConvertToInternalUnits(v, UnitTypeId.Degrees)` | same pattern | `UnitTypeId.Degrees` |

### Quick Constants (no API needed)
| Conversion | Factor |
|---|---|
| mm → feet | ÷ 304.8 |
| m → feet | × 3.28084 |
| ft/s → m/s | × 0.3048 |
| sq ft → m² | × 0.092903 |
| cu ft → m³ | × 0.0283168 |
| sq ft → mm² | × 92903.04 |

### SpecTypeId (Revit 2025 parameter specification)
Used with `ForgeTypeId` to identify parameter types:
- `SpecTypeId.Length`, `SpecTypeId.Area`, `SpecTypeId.Volume`
- `SpecTypeId.Flow`, `SpecTypeId.HvacVelocity`, `SpecTypeId.PipingVelocity`
- `SpecTypeId.DuctSize`, `SpecTypeId.PipeSize`, `SpecTypeId.ConduitSize`
- `SpecTypeId.HvacPressure`, `SpecTypeId.PipingPressure`
- `SpecTypeId.Angle`, `SpecTypeId.PipeSlope`

## 5. Conduit / CableTray — BreakCurve Workaround

`MechanicalUtils.BreakCurve` and `PlumbingUtils.BreakCurve` do NOT work for Conduit or CableTray.

### Workaround: CopyElement + Shorten
```csharp
// 1. Copy the element in place
var copiedIds = ElementTransformUtils.CopyElement(doc, elem.Id, XYZ.Zero);
var copy = doc.GetElement(copiedIds.First());

// 2. Shorten original: [start, splitPoint]
var origLc = (LocationCurve)elem.Location;
origLc.Curve = Line.CreateBound(origLc.Curve.GetEndPoint(0), splitPoint);

// 3. Set copy: [splitPoint, end]
var copyLc = (LocationCurve)copy.Location;
copyLc.Curve = Line.CreateBound(splitPoint, copyLc.Curve.GetEndPoint(1));
```

This preserves system assignments, type, and most parameters. Union fittings are not typically
used for conduit/cable tray splits.

## 6. MEP System Graph Traversal

### BFS Algorithm
1. Start from a seed element.
2. Get `ConnectorManager` (universal access pattern).
3. For each `Connector`, check `IsConnected`.
4. For connected connectors, iterate `AllRefs` to find neighbors.
5. Skip `ConnectorType.Logical` (non-physical connections).
6. Track visited elements by `ElementId.Value` to avoid cycles.

### Key Statistics to Collect
- **Total elements**: count of unique visited elements
- **Max depth**: maximum BFS depth from start
- **Open ends**: unconnected connectors (system endpoints)
- **Total length**: sum of `LocationCurve.Length` for curve elements
- **Category breakdown**: count by `Category.Name`
- **Domain distribution**: count by `Connector.Domain`

### Practical Limits
- Set max_elements to 500-2000 to prevent long traversals in large models.
- Skip `ConnectorType.Logical` to avoid phantom connections.
- Consider domain filtering (hvac/piping/electrical) to stay within one system type.

## 7. Element Location — Universal Resolution

Different element types store location differently. Use this priority:

1. `LocationPoint.Point` — for equipment, fittings, terminals
2. `LocationCurve.Evaluate(0.5, true)` — midpoint of curve elements
3. `FamilyInstance.GetSpatialElementCalculationPoint()` — if `HasSpatialElementCalculationPoint`
4. `BoundingBox center` — fallback: `(bb.Min + bb.Max) / 2`

### Level Resolution
1. `RBS_START_LEVEL_PARAM` — most MEP elements
2. `Element.LevelId` — generic fallback
3. Closest level by elevation — last resort

## 8. Nested Elements

Some MEP families contain sub-components:
- `FamilyInstance.SuperComponent` — the parent host element (null if top-level)
- `FamilyInstance.GetSubComponentIds()` — child elements

This is important when analyzing equipment assemblies or multi-part fixtures.

---

## Bilingual Terminology / Thuật ngữ song ngữ

| English | Vietnamese |
|---|---|
| Connector | Đầu nối |
| Fitting | Phụ kiện (co, tê, chữ thập, chuyển tiếp) |
| Elbow | Co (phụ kiện uốn cong) |
| Tee | Tê (phụ kiện nhánh chữ T) |
| Cross | Chữ thập (phụ kiện 4 nhánh) |
| Transition / Reducer | Chuyển tiếp / Thu nhỏ |
| Takeoff | Cổ ngỗng / Nhánh rẽ |
| Union | Khớp nối (giữa các đoạn cắt) |
| Routing Preference | Tùy chọn định tuyến |
| Graph Traversal | Duyệt đồ thị hệ thống |
| Open End | Đầu hở (đầu nối chưa kết nối) |
| Conduit | Ống luồn dây điện |
| Cable Tray | Máng cáp |
| FabricationPart | Phần chế tạo |
