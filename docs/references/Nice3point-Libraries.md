# Nice3point Revit Libraries - Reference

> Author: Roman (Nice3point) - https://github.com/Nice3point
> Integrated: March 2026

## Packages Used in RevitChatBot

### 1. Nice3point.Revit.Toolkit

- **NuGet:** `Nice3point.Revit.Toolkit`
- **Version:** `2025.*`
- **Repo:** https://github.com/Nice3point/RevitToolkit
- **Purpose:** Base classes and infrastructure for Revit add-in development

#### Key Features Used

| Feature | Description |
|---------|-------------|
| `ExternalApplication` | Base class for `IExternalApplication` with auto dependency resolution |
| `ExternalCommand` | Base class for `IExternalCommand` with `Document`, `UiDocument`, `Application` properties |
| `AsyncEventHandler` | Async external event handler - awaitable `RaiseAsync()` |
| `AsyncEventHandler<T>` | Async handler with return value |
| `ActionEventHandler` | Queue-based external event handler |
| `Context` | Global access to `ActiveDocument`, `ActiveView`, `Application`, `UiApplication` |
| `Context.SuppressDialogs()` | Suppress Revit dialog popups during batch operations |
| `Context.SuppressFailures()` | Suppress failure handling for automated operations |
| `DuplicateTypeNamesHandler` | Built-in handler for `IDuplicateTypeNamesHandler` |
| `FamilyLoadOptions` | Built-in handler for `IFamilyLoadOptions` |
| `SelectionConfiguration` | Fluent `ISelectionFilter` builder |
| `DockablePaneProvider` | Simplified dockable pane registration |
| `ResolveHelper` | Dependency resolution for Revit 2025 and older |

#### AsyncEventHandler Usage Pattern

```csharp
private readonly AsyncEventHandler _handler = new();

// Fire-and-forget from non-Revit thread
await _handler.RaiseAsync(app => {
    var doc = app.ActiveUIDocument.Document;
    using var tx = new Transaction(doc, "Operation");
    tx.Start();
    // ... Revit API calls ...
    tx.Commit();
});

// With return value
private readonly AsyncEventHandler<int> _handler = new();
var count = await _handler.RaiseAsync(app => {
    return app.ActiveUIDocument.Document.GetInstances(BuiltInCategory.OST_Walls).Count;
});
```

#### Context Usage

```csharp
var doc = Context.ActiveDocument;
var view = Context.ActiveView;
var username = Context.Application.Username;

if (Context.IsRevitInApiMode) { /* direct API call */ }
else { /* use external event handler */ }
```

---

### 2. Nice3point.Revit.Extensions

- **NuGet:** `Nice3point.Revit.Extensions`
- **Version:** `2025.*`
- **Repo:** https://github.com/Nice3point/RevitExtensions
- **Purpose:** Extension methods that simplify Revit API operations

#### FilteredElementCollector Extensions

```csharp
var instances = document.GetInstances(BuiltInCategory.OST_Walls);
var instanceIds = document.GetInstanceIds(BuiltInCategory.OST_DuctCurves);
var types = document.GetTypes(BuiltInCategory.OST_PipeCurves);
var typeIds = document.GetTypeIds();

// With filters
var filtered = document.GetInstances(BuiltInCategory.OST_Walls, new ElementParameterFilter(...));

// Generic typed
var ducts = document.EnumerateInstances<Duct>(BuiltInCategory.OST_DuctCurves);

// Raw collector
var collector = document.GetElements().WhereElementIsViewIndependent().ToElements();
```

#### Parameter Extensions

```csharp
// FindParameter searches instance AND type
var param = element.FindParameter("Height");
var param = element.FindParameter(BuiltInParameter.ALL_MODEL_URL);
var param = element.FindParameter(ParameterTypeId.AllModelUrl);

// Typed access
bool val = element.FindParameter("IsClosed").AsBool();
Color color = element.FindParameter("Color").AsColor();
Element mat = element.FindParameter("Material").AsElement<Material>();

// Set with Color
parameter.Set(new Color(66, 69, 96));
parameter.Set(true);
```

#### Unit Extensions

```csharp
var mm = value.ToMillimeters();
var feet = mmValue.FromMillimeters();
var meters = value.ToMeters();
var degrees = radians.ToDegrees();
var radians = degrees.FromDegrees();
var custom = value.FromUnit(UnitTypeId.Celsius);
```

#### ElementId Extensions

```csharp
Element element = wallId.ToElement(document);
Wall wall = wallId.ToElement<Wall>(document);
IList<Element> elements = wallIds.ToElements(document);
IList<Wall> walls = wallIds.ToElements<Wall>(document);
categoryId.AreEquals(BuiltInCategory.OST_Walls);
```

#### Ribbon Extensions

```csharp
var panel = application.CreatePanel("Commands", "TabName");
panel.AddPushButton<Command>("Button Text")
    .SetImage("/Resources/Icons/icon16.png")
    .SetLargeImage("/Resources/Icons/icon32.png")
    .SetToolTip("Tooltip text")
    .SetLongDescription("Long description");
```

#### Element Transform Extensions

```csharp
element.Copy(new XYZ(1, 0, 0));
element.Move(new XYZ(0, 0, 1));
element.Mirror(plane);
element.Rotate(axis, angle);
```

#### Geometry Extensions

```csharp
boundingBox.Contains(point);
boundingBox.Overlaps(otherBox);
boundingBox.ComputeCentroid();
boundingBox.ComputeVolume();
line.Distance(otherLine);
```

#### Plumbing Extensions (MEP-specific)

```csharp
pipe.PlaceCapOnOpenEnds();
pipe.HasOpenConnector();
pipe.BreakCurve(point);
connector1.ConnectPipePlaceholdersAtElbow(connector2);
connector1.ConnectPipePlaceholdersAtTee(connector2, connector3);
```

#### Label Extensions

```csharp
var label = BuiltInCategory.OST_Walls.ToLabel();         // "Walls"
var label = BuiltInParameter.WALL_TOP_OFFSET.ToLabel();   // "Top Offset"
```

---

### 3. Nice3point.Revit.Api

- **NuGet:** `Nice3point.Revit.Api`
- **Version:** `2025.*`
- **Repo:** https://github.com/Nice3point/RevitApi
- **Purpose:** NuGet-packaged Revit API references (replaces manual DLL paths)

```xml
<!-- Replaces manual HintPath references to RevitAPI.dll / RevitAPIUI.dll -->
<PackageReference Include="Nice3point.Revit.Api" Version="$(RevitVersion).*" />
```

---

## Other Repos (Reference Only)

| Repository | URL | Notes |
|---|---|---|
| RevitTemplates | https://github.com/Nice3point/RevitTemplates | Project templates, CI/CD patterns |
| RevitUnit | https://github.com/Nice3point/RevitUnit | Testing framework for Revit |
| RevitBenchmark | https://github.com/Nice3point/RevitBenchmark | Performance benchmarking |
| FamilyUpdater | https://github.com/Nice3point/FamilyUpdater | Family batch operations |
