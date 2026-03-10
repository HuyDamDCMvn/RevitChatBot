# MEP Routing & Clash Avoidance Knowledge Base

## 1. MEP Clash Detection

### 1.1 Bounding Box Clash Detection
- **Method**: Axis-Aligned Bounding Box (AABB) overlap test
- **Tolerance**: Additional clearance around each element's bounding box
  - Typical values: 25mm (tight), 50mm (standard), 100mm (generous)
  - Convert to Revit internal units: tolerance_feet = tolerance_mm / 304.8
- **Formula**: Two boxes clash if ALL axes overlap:
  - X: min1 - tol ≤ max2 AND max1 + tol ≥ min2
  - Y: min1 - tol ≤ max2 AND max1 + tol ≥ min2
  - Z: min1 - tol ≤ max2 AND max1 + tol ≥ min2
- **Limitations**: AABB is conservative — may report false positives for diagonal elements
- **Alternative**: `ElementIntersectsElementFilter` for precise geometry checks (slower)

### 1.2 Clash Severity Classification
- **CRITICAL**: Overlap volume > threshold (e.g., > 0.001 m³), hard clash
- **MAJOR**: Elements touching or within minimum clearance
- **MINOR**: Soft clash, elements within tolerance but not touching

### 1.3 Connected Component Analysis (BFS Grouping)
- **Purpose**: Group clashing elements into clusters for batch rerouting
- **Algorithm**: Build adjacency graph from clash pairs → BFS/DFS to find connected components
- **Benefit**: Elements in same cluster can share a common reroute path
- **Implementation**: 
  1. Build adjacency: if A clashes with B, add edge A↔B
  2. BFS from unvisited node → collect all reachable nodes = one group
  3. Each group has "shift" elements (to be moved) and "stand" elements (obstacles)

## 2. MEP Rerouting (Dogleg Pattern)

### 2.1 Dogleg Geometry (6-Point / 5-Segment Pattern)
The dogleg reroute replaces one straight segment with 5 segments connected by 4 elbows:

```
P0 ──── P1      P4 ──── P5  (original alignment)
         │      │
         P2 ── P3           (offset bypass)
```

- **P0**: Original start point
- **P1**: Approach point (where element leaves original path)
- **P2**: First offset point (P1 shifted by offset vector)
- **P3**: Second offset point (P2 moved along original direction)
- **P4**: Return point (P3 shifted back to original alignment)
- **P5**: Original end point

### 2.2 Direction Classification
- **Method**: XY-projected dot product of element directions
- **Parallel**: |dot| ≥ 0.98 → reroute perpendicular to flow (typically Up/Down)
- **Perpendicular**: |dot| ≤ 0.10 → reroute along flow direction (typically Left/Right)
- **Fallback**: If direction unreadable, check bounding box Z-span
  - Z-span > 2 feet → vertical riser → treat as perpendicular
  - Otherwise → treat as parallel

### 2.3 Offset Calculation
- **Total offset** = obstacle extent + clearance
- **Obstacle extent**: Maximum projection of obstacle bounding box onto offset direction
- **Clearance**: User-specified minimum gap (typically 100-300mm depending on discipline)
- **Cushion**: Additional buffer at approach/departure (typically 50% of clearance)

### 2.4 MEP Discipline-Specific Rules

#### Duct Routing
- Minimum clearance: 50mm from structure, 100mm from other services
- Preferred offset: vertical (up/down) when parallel, horizontal when crossing
- Standard duct sizes must be preserved in rerouted segments
- Duct insulation adds to effective clearance requirement

#### Pipe Routing
- Maintain slope on drainage pipes (typically 1-2%)
- Hot/cold water pipes: minimum 150mm separation
- Fire sprinkler pipes: maintain per NFPA 13 requirements
- Pipe insulation thickness adds to clearance

#### Cable Tray / Conduit
- Maintain bend radius requirements
- Separation from power lines per electrical code
- Cable tray fill ratio must not exceed 40-50%

## 3. MEP Element Creation (Revit API)

### 3.1 Creating Segments
| Element Type | API Method | Parameters |
|---|---|---|
| Pipe | `Pipe.Create()` | doc, systemTypeId, pipeTypeId, levelId, start, end |
| Duct | `Duct.Create()` | doc, systemTypeId, ductTypeId, levelId, start, end |
| CableTray | `CableTray.Create()` | doc, cableTrayTypeId, start, end, levelId |
| Conduit | `Conduit.Create()` | doc, conduitTypeId, start, end, levelId |

### 3.2 System Type Resolution
- Pipe: Get from `pipe.MEPSystem.GetTypeId()` or query `PipingSystemType`
- Duct: Get from `duct.MEPSystem.GetTypeId()` or query `MechanicalSystemType`
- CableTray/Conduit: No system type needed

### 3.3 Parameter Copying
Critical parameters to copy from original to new segment:
- `RBS_PIPE_DIAMETER_PARAM` (pipe diameter)
- `RBS_CURVE_WIDTH_PARAM` (duct width)
- `RBS_CURVE_HEIGHT_PARAM` (duct height)
- `RBS_CURVE_DIAMETER_PARAM` (round duct/pipe diameter)
- `RBS_REFERENCE_INSULATION_THICKNESS` (insulation)
- `RBS_REFERENCE_LINING_THICKNESS` (lining)

### 3.4 Fitting Connection
| Method | Use Case |
|---|---|
| `doc.Create.NewElbowFitting(c1, c2)` | Generic — works for all MEP types |
| `PlumbingUtils.ConnectPipePlaceholdersAtElbow()` | Pipe-specific (uses routing prefs) |
| `MechanicalUtils.ConnectDuctPlaceholdersAtElbow()` | Duct-specific (uses routing prefs) |
| `connector1.ConnectTo(connector2)` | Direct connection (aligned connectors) |

### 3.5 Connector Management
- `MEPCurve.ConnectorManager.Connectors` — iterate all connectors
- Find nearest unconnected connector to a point for fitting insertion
- Check `Connector.IsConnected` before attempting new connections
- `Connector.Origin` gives position, `Connector.CoordinateSystem.BasisZ` gives direction

## 4. Routing Preferences Validation

### 4.1 Why It Matters
- Revit uses routing preferences to select fitting families for elbows, tees, crosses
- If no valid routing preference exists for a pipe/duct type, fitting creation will fail
- Must validate before attempting rerouting

### 4.2 Checking Routing Preferences
```
PipeType.RoutingPreferenceManager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows)
DuctType.RoutingPreferenceManager.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows)
```
- If elbow rules = 0, automatic fitting creation will fail
- Fallback: use `doc.Create.NewElbowFitting()` which tries to find any compatible fitting

## 5. Best Practices

### 5.1 Transaction Management
- Wrap all model modifications in a single Transaction
- Use `doc.Regenerate()` after creating segments, before connecting fittings
- If any fitting connection fails, still keep successful segments (partial success)
- On total failure, `Transaction.RollBack()` to undo all changes

### 5.2 Error Handling
- Element creation can fail if start == end (zero-length segment)
- Check `ShortCurveTolerance`: `doc.Application.ShortCurveTolerance`
- Fitting creation may fail if routing preferences are missing
- Always provide fallback: try multiple connection methods

### 5.3 Performance
- BoundingBox clash: O(n×m) — acceptable for < 10,000 elements
- For large models: pre-filter by level, use `BoundingBoxIntersectsFilter`
- Connected component BFS: O(V+E) — very fast
- Limit results to avoid overwhelming the user

## 6. Terminology (EN / VI)

| English | Vietnamese | Description |
|---|---|---|
| Clash Detection | Phát hiện va chạm | Finding intersections between elements |
| Clash Avoidance | Tránh va chạm | Rerouting to prevent intersections |
| Reroute | Chuyển tuyến | Changing the path of an MEP element |
| Dogleg | Tuyến gấp khúc | Z-shaped detour around obstacle |
| Offset | Khoảng lệch | Distance shifted from original path |
| Clearance | Khoảng cách an toàn | Minimum gap between elements |
| Tolerance | Dung sai | Acceptable deviation threshold |
| Elbow Fitting | Co nối | Fitting connecting two angled segments |
| Routing Preference | Ưu tiên định tuyến | Rules for automatic fitting selection |
| Connected Component | Nhóm liên thông | Cluster of related clashing elements |
| Bounding Box | Hộp bao | Axis-aligned box enclosing element |
| Parallel | Song song | Same direction elements |
| Perpendicular | Vuông góc | Crossing elements |
