# MEP Spatial Mapping, Clearance & Split Knowledge Base

## 1. Directional Clearance Checking

### 1.1 Ray Casting Method (ReferenceIntersector)
- **Requirement**: Requires a 3D view (View3D) — cannot use in plan/section views
- **API**: `ReferenceIntersector(filter, FindReferenceTarget.Element, view3d)`
- **Linked Models**: Set `intersector.FindReferencesInRevitLinks = true`
- **Method**: `intersector.FindNearest(origin, direction)` returns nearest hit

### 1.2 Six Directions
| Direction | Vector | Typical Check |
|---|---|---|
| Top | (0, 0, 1) | Ceiling clearance, structure above |
| Bottom | (0, 0, -1) | Floor clearance, structure below |
| Left | (-1, 0, 0) | Wall/column distance (X-axis) |
| Right | (1, 0, 0) | Wall/column distance (X-axis) |
| Front | (0, 1, 0) | Wall distance (Y-axis) |
| Back | (0, -1, 0) | Wall distance (Y-axis) |

### 1.3 Reference Origin Selection (for curve-based elements)
- **Top check**: Use endpoint with higher Z
- **Bottom check**: Use endpoint with lower Z
- **Left/Right**: Use endpoint with min/max X
- **Front/Back**: Use endpoint with min/max Y
- **Point-based elements**: Use LocationPoint directly
- **Fallback**: Bounding box center

### 1.4 Clearance Standards
| Location | Minimum Clearance | Standard |
|---|---|---|
| Corridor ceiling | 2400mm (2.4m) | Building code |
| Office ceiling | 2600mm (2.6m) | Building code |
| Lobby ceiling | 2800-3000mm | Building code |
| Basement ceiling | 2100mm (2.1m) | Building code |
| Wall offset (duct) | 50-100mm | SMACNA |
| Wall offset (pipe) | 25-50mm | Good practice |
| Pipe-to-pipe | 25mm minimum | Good practice |
| Duct-to-duct | 50mm minimum | Good practice |
| MEP-to-structure | 50mm minimum | Good practice |
| Fire sprinkler to ceiling | 25-305mm | NFPA 13 |

### 1.5 Reference Categories
| Category | BuiltInCategory | Use Case |
|---|---|---|
| Wall | OST_Walls | Side clearance |
| Floor | OST_Floors | Bottom clearance |
| Ceiling | OST_Ceilings | Top clearance |
| Column | OST_StructuralColumns | Side clearance |
| Beam | OST_StructuralFraming | Top/bottom clearance |
| Roof | OST_Roofs | Top clearance |

## 2. MEP-Room/Space Spatial Mapping

### 2.1 Detection Methods (Priority Order)
1. **Room Calculation Point** (most accurate for FamilyInstance)
   - `fi.HasSpatialElementCalculationPoint` → `fi.GetSpatialElementCalculationPoint()`
   - Set in family editor, represents the "spatial calculation" location
2. **IsPointInRoom / IsPointInSpace** (native Revit API)
   - `Room.IsPointInRoom(XYZ)` — returns bool
   - `Space.IsPointInSpace(XYZ)` — returns bool
   - Requires `using Autodesk.Revit.DB.Architecture;` for Room
   - Requires `using Autodesk.Revit.DB.Mechanical;` for Space
3. **LocationPoint / LocationCurve midpoint**
   - For curve-based MEP: use curve midpoint + start + end
   - For point-based MEP: use LocationPoint.Point
4. **Bounding box center** (fallback)
   - `(bb.Min + bb.Max) / 2`

### 2.2 Above-Space Detection
- **Purpose**: Detect MEP elements installed above ceiling (e.g., duct runs, cable trays)
- **Method**: Translate detection point downward by offset
  - `new XYZ(pt.X, pt.Y, pt.Z - offsetFeet)`
- **Typical offset**: 500-2000mm depending on ceiling void height
- **Use case**: Ceiling-mounted equipment, above-ceiling ductwork, sprinkler pipes

### 2.3 MEP Element Types for Spatial Detection
| Type | Location | Detection Points |
|---|---|---|
| Duct/Pipe/CableTray/Conduit | LocationCurve | Midpoint, start, end |
| Air Terminal / Sprinkler | LocationPoint | Point, spatial calc point |
| Mechanical Equipment | LocationPoint | Point, spatial calc point |
| Plumbing Fixture | LocationPoint | Point, spatial calc point |
| Lighting Fixture | LocationPoint | Point, spatial calc point |
| Electrical Equipment | LocationPoint | Point, spatial calc point |

### 2.4 Common Parameters to Map
| Source (Room/Space) | Target (MEP) | BuiltInParameter |
|---|---|---|
| Room Number | Comments/Mark | ROOM_NUMBER |
| Room Name | - | ROOM_NAME |
| Room Area | - | ROOM_AREA |
| Level | - | ROOM_LEVEL_ID |
| Space Number | Comments | ROOM_NUMBER |
| Space Name | - | ROOM_NAME |

## 3. Duct/Pipe Splitting

### 3.1 BreakCurve API
- **Duct**: `MechanicalUtils.BreakCurve(doc, ductId, splitPoint)`
  - Requires: `using Autodesk.Revit.DB.Mechanical;`
  - Returns: `ElementId` of newly created segment
  - Original element retains one part, new element is the other
- **Pipe**: `PlumbingUtils.BreakCurve(doc, pipeId, splitPoint)`
  - Requires: `using Autodesk.Revit.DB.Plumbing;`
  - Same return behavior

### 3.2 Split Point Calculation
- **From Start to End**: `point = start + direction * (splitDistance * i)`
- **From End to Start**: `point = end + reverseDirection * (splitDistance * i)`
- **Important**: Split point must be ON the curve, not at endpoints
- **Check**: Skip if remaining length < ShortCurveTolerance

### 3.3 Sequential Splitting
When splitting multiple times from the same element:
- **Start→End direction**: After each break, the NEW element is the far part. Continue splitting the original.
- **End→Start direction**: After each break, the NEW element ID becomes the current element for next split.

### 3.4 Union Fitting Creation
- **API**: `doc.Create.NewUnionFitting(connector1, connector2)`
- **Requirement**: Connectors must be at the exact same position (coincident)
- **Finding coincident connectors**:
  - Iterate ConnectorManager.Connectors of both adjacent segments
  - Match by `c1.Origin.DistanceTo(c2.Origin) < 0.001`
- **Union vs Elbow**: Union = straight connection (same direction), Elbow = angled connection

### 3.5 Sequential Numbering
- After splitting, assign order number to each segment via parameter
- Common parameters: Comments, Mark, or custom shared parameter
- Handle different StorageType: String → `Set("1")`, Integer → `Set(1)`, Double → `Set(1.0)`

## 4. Terminology (EN / VI)

| English | Vietnamese | Description |
|---|---|---|
| Clearance | Khoảng thông thủy | Minimum free distance |
| Ray Casting | Phép chiếu tia | Shooting ray from point in direction |
| Room | Phòng | Architectural room boundary |
| Space | Không gian | MEP space for HVAC calculations |
| Spatial Element | Phần tử không gian | Room or Space element |
| Split | Chia cắt | Break element into segments |
| Union Fitting | Phụ kiện nối thẳng | Fitting connecting aligned segments |
| Segment | Đoạn | Part of element after splitting |
| Above-space | Trên không gian | Element above ceiling void |
| Proximity | Lân cận | Near but not inside spatial element |
| Detection Point | Điểm kiểm tra | Point used for spatial detection |
| Calculation Point | Điểm tính toán | Family's spatial calculation reference |
