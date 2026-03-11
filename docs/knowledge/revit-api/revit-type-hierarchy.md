# Revit API Type Hierarchy Reference

Source: Extracted from RevitLookup DescriptorsMap.cs (~100+ type descriptors)

## Element Inheritance Tree (MEP-Focused)

### Base Element Types

```
Element (abstract base)
├── MEPCurve (linear MEP elements — has ConnectorManager directly)
│   ├── Duct (Autodesk.Revit.DB.Mechanical)
│   ├── Pipe (Autodesk.Revit.DB.Plumbing)
│   ├── FlexDuct (Autodesk.Revit.DB.Mechanical)
│   ├── FlexPipe (Autodesk.Revit.DB.Plumbing)
│   ├── Conduit (Autodesk.Revit.DB.Electrical)
│   ├── CableTray (Autodesk.Revit.DB.Electrical)
│   └── Wire (Autodesk.Revit.DB.Electrical)
├── FamilyInstance (placed instances — has MEPModel.ConnectorManager)
│   └── Panel (curtain wall panel — MUST check before FamilyInstance)
├── HostObject (hosts other elements)
│   ├── Wall
│   ├── Floor
│   ├── Ceiling
│   ├── RoofBase
│   │   ├── FootPrintRoof
│   │   └── ExtrusionRoof
│   └── FaceWall
├── CurveElement (curve-based)
│   ├── ModelCurve
│   ├── DetailCurve
│   └── ModelLine
├── View (document views)
│   ├── TableView
│   │   └── ViewSchedule
│   ├── View3D
│   ├── ViewPlan
│   ├── ViewSection
│   ├── ViewDrafting
│   └── ViewSheet
├── SpatialElement
│   ├── Room (Autodesk.Revit.DB.Architecture)
│   └── Space (Autodesk.Revit.DB.Mechanical)
├── MEPSystem (system grouping)
│   ├── MechanicalSystem (HVAC)
│   ├── PipingSystem (Plumbing/Hydronic)
│   └── ElectricalSystem
├── DatumPlane
│   ├── Level
│   ├── Grid
│   └── ReferencePlane
├── IndependentTag
├── ElevationMarker
├── BasePoint
├── InternalOrigin
├── GlobalParameter
├── SketchPlane
├── Rebar (Autodesk.Revit.DB.Structure)
├── SunAndShadowSettings
├── AssemblyInstance
├── Part
├── PartMaker
└── ElementType (type definitions)
    ├── FamilySymbol
    ├── WallType
    ├── FloorType
    ├── CeilingType
    ├── RoofType
    ├── DuctType (Autodesk.Revit.DB.Mechanical)
    ├── PipeType (Autodesk.Revit.DB.Plumbing)
    ├── ConduitType (Autodesk.Revit.DB.Electrical)
    ├── CableTrayType (Autodesk.Revit.DB.Electrical)
    ├── MechanicalSystemType
    ├── PipingSystemType
    ├── RevitLinkType
    └── AnalyticalLinkType
```

### Non-Element Types (IDisposable)

```
IDisposable (not Element, but commonly accessed)
├── Document
├── ForgeTypeId
├── PlanViewRange
├── CompoundStructure
├── CompoundStructureLayer
├── Entity (ExtensibleStorage)
├── Field (ExtensibleStorage)
├── Schema (ExtensibleStorage)
├── FailureMessage
├── UpdaterInfo
├── Subelement
├── ExternalResourceReference
├── ExternalService
├── PerformanceAdviser
├── SchedulableField
├── ModelPath
├── Workset
├── WorksetTable
├── BoundarySegment
├── AssetProperties (Visual)
├── AssetProperty (Visual)
├── Connector
├── ConnectorManager
├── ScheduleDefinition
├── TableData
├── TableSectionData
├── FamilySizeTableManager
├── FamilySizeTable
├── FamilySizeTableColumn
├── PointCloudFilter
├── TriangulationInterface
├── Units
├── LightFamily (Lighting)
├── Application
├── UIApplication
└── EvaluatedParameter (Revit 2024+)
```

### API Object Types

```
APIObject (base for non-Element geometry/data)
├── BoundingBoxXYZ
├── Category
├── Parameter
├── FamilyParameter
├── Reference
├── Color
├── Curve (Arc, Line, NurbSpline, etc.)
├── Edge
├── EdgeEndPoint
├── Solid
├── Surface
├── Mesh
├── CylindricalFace
├── CurveLoop
├── Face (PlanarFace, CylindricalFace, etc.)
├── GeometryObject
├── City
├── PaperSize
├── PrintManager
├── DefinitionGroup
├── FamilyManager
├── MEPSection
├── LocationCurve
├── CurtainGrid
└── Definition
    ├── ExternalDefinition
    └── InternalDefinition
```

## ConnectorManager Access Patterns

Different element types access ConnectorManager differently:

| Type | Access Pattern | Notes |
|------|---------------|-------|
| `MEPCurve` (Duct, Pipe, etc.) | `mepCurve.ConnectorManager` | Direct access |
| `FamilyInstance` | `fi.MEPModel?.ConnectorManager` | May be null for non-MEP families |
| `FabricationPart` | `fp.ConnectorManager` | Direct access |
| All others | N/A | No connector access |

Universal pattern:
```csharp
ConnectorManager cm = elem switch {
    MEPCurve mc => mc.ConnectorManager,
    FamilyInstance fi => fi.MEPModel?.ConnectorManager,
    FabricationPart fp => fp.ConnectorManager,
    _ => null
};
```

## Type Checking Order

When using pattern matching, always check more specific types first:

1. `Panel` before `FamilyInstance` (Panel inherits FamilyInstance)
2. `Duct`, `Pipe`, `Conduit`, `CableTray` before `MEPCurve`
3. `ViewSchedule` before `TableView` before `View`
4. `Wall` before `HostObject`
5. `FamilySymbol`, `WallType` before `ElementType`
6. `MechanicalSystem` before `MEPSystem`
7. `Room` (Architecture namespace!) before `SpatialElement`
8. `Space` (Mechanical namespace!) before `SpatialElement`

## Namespace Requirements

| Type | Required Using |
|------|---------------|
| Room | `using Autodesk.Revit.DB.Architecture;` |
| Space | `using Autodesk.Revit.DB.Mechanical;` |
| Duct, DuctType, MechanicalSystem | `using Autodesk.Revit.DB.Mechanical;` |
| Pipe, PipeType, PipingSystem | `using Autodesk.Revit.DB.Plumbing;` |
| CableTray, Conduit | `using Autodesk.Revit.DB.Electrical;` |
| Wire | `using Autodesk.Revit.DB.Electrical;` |
| Schema, Entity, Field | `using Autodesk.Revit.DB.ExtensibleStorage;` |
| Rebar | `using Autodesk.Revit.DB.Structure;` |
| AssetProperties | `using Autodesk.Revit.DB.Visual;` |
