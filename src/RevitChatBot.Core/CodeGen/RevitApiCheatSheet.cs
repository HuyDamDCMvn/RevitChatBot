namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Revit API quick-reference injected into the LLM context for code generation.
/// Patterns sourced from production MEP tools (DCMvn, HDD_Support).
/// </summary>
public static class RevitApiCheatSheet
{
    public static string GetCheatSheet() => """
        === REVIT 2025 API CHEAT SHEET (for code generation) ===

        ## UNITS — CRITICAL (OpenMEP robust patterns)
        - Revit internal unit = FEET. Always convert: meters * 3.28084 = feet, mm / 304.8 = feet
        - UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters)
        - UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters)
        - Area: 1 sq ft = 0.092903 m². Volume: 1 cu ft = 0.0283168 m³
        - Velocity: Revit stores ft/s. Multiply by 0.3048 for m/s.
        - Slope: Revit stores as ratio (0.01 = 1%)

        ## UNIT CONVERSION — SpecTypeId MAPPING (Revit 2025)
        ```
        // Length
        UnitUtils.ConvertToInternalUnits(1000, UnitTypeId.Millimeters); // mm → ft
        UnitUtils.ConvertToInternalUnits(1.5, UnitTypeId.Meters);      // m  → ft
        UnitUtils.ConvertFromInternalUnits(val, UnitTypeId.Millimeters); // ft → mm

        // Area & Volume
        UnitUtils.ConvertToInternalUnits(10, UnitTypeId.SquareMeters);   // m² → ft²
        UnitUtils.ConvertFromInternalUnits(val, UnitTypeId.SquareMeters);
        UnitUtils.ConvertToInternalUnits(1, UnitTypeId.CubicMeters);     // m³ → ft³

        // Flow
        UnitUtils.ConvertToInternalUnits(100, UnitTypeId.LitersPerSecond); // L/s → ft³/s
        UnitUtils.ConvertFromInternalUnits(val, UnitTypeId.CubicFeetPerMinute); // → CFM

        // Velocity
        UnitUtils.ConvertToInternalUnits(3, UnitTypeId.MetersPerSecond);  // m/s → ft/s

        // Pressure
        UnitUtils.ConvertToInternalUnits(100, UnitTypeId.Pascals);        // Pa → internal
        UnitUtils.ConvertFromInternalUnits(val, UnitTypeId.Pascals);

        // Angle
        UnitUtils.ConvertToInternalUnits(90, UnitTypeId.Degrees);  // deg → radians

        // SpecTypeId for parameter ForgeTypeId (Revit 2025 replacement for ParameterType)
        // SpecTypeId.Length, SpecTypeId.Area, SpecTypeId.Volume,
        // SpecTypeId.Flow, SpecTypeId.HvacVelocity, SpecTypeId.PipingVelocity,
        // SpecTypeId.DuctSize, SpecTypeId.PipeSize, SpecTypeId.ConduitSize,
        // SpecTypeId.HvacPressure, SpecTypeId.PipingPressure,
        // SpecTypeId.Angle, SpecTypeId.PipeSlope
        ```

        ## QUERY ELEMENTS
        ```
        // By class (strongly typed)
        var ducts = new FilteredElementCollector(doc)
            .OfClass(typeof(Duct)).Cast<Duct>().ToList();
        var pipes = new FilteredElementCollector(doc)
            .OfClass(typeof(Pipe)).Cast<Pipe>().ToList();

        // By category (general)
        var elements = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MechanicalEquipment)
            .WhereElementIsNotElementType().ToElements();

        // Family instances of specific category
        var instances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .OfCategory(BuiltInCategory.OST_DuctFitting)
            .Cast<FamilyInstance>().ToList();

        // Get all levels sorted
        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level)).Cast<Level>()
            .OrderBy(l => l.Elevation).ToList();

        // Get family types
        var types = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(BuiltInCategory.OST_PipeFitting)
            .Cast<FamilySymbol>().ToList();

        // On specific level
        var levelFilter = new ElementLevelFilter(levelId);
        var onLevel = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_DuctCurves)
            .WherePasses(levelFilter).ToElements();

        // Rooms and Spaces
        var spaces = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType().Cast<Space>().ToList();
        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType().Cast<Room>().ToList();

        // System types
        var mechSystemTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystemType)).Cast<MechanicalSystemType>().ToList();
        var pipeSystemTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().ToList();
        ```

        ## PARAMETERS — READING
        ```
        // BuiltInParameter (fastest, most reliable)
        double diameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
        string size = elem.get_Parameter(BuiltInParameter.RBS_CALCULATED_SIZE)?.AsString() ?? "";
        string systemName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
        string systemClass = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";
        string levelName = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsValueString() ?? "";

        // By name (for shared/project params)
        Parameter p = elem.LookupParameter("Mark");
        string val = p?.AsString() ?? p?.AsValueString() ?? "";

        // Set parameter (inside Transaction)
        elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK).Set("NewValue");
        ```

        ## MEP-SPECIFIC PROPERTIES
        ```
        // Duct properties
        Duct duct = ...;
        double width = duct.Width;     // feet (rectangular)
        double height = duct.Height;   // feet (rectangular)
        double diameter = duct.Diameter; // feet (round, 0 if rect)
        double length = duct.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH).AsDouble(); // feet
        double velocity = duct.get_Parameter(BuiltInParameter.RBS_VELOCITY)?.AsDouble() ?? 0; // ft/s
        double flow = duct.get_Parameter(BuiltInParameter.RBS_DUCT_FLOW_PARAM)?.AsDouble() ?? 0;

        // Pipe properties
        Pipe pipe = ...;
        double pipeDia = pipe.Diameter; // feet
        double pipeVel = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_VELOCITY_PARAM)?.AsDouble() ?? 0;
        double slope = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_SLOPE)?.AsDouble() ?? 0; // ratio
        ```

        ## CONNECTOR — ACCESS (universal pattern from OpenMEP)
        ```
        // Universal ConnectorManager access (MEPCurve, FamilyInstance, FabricationPart)
        ConnectorManager cm = null;
        if (elem is MEPCurve mc) cm = mc.ConnectorManager;
        else if (elem is FamilyInstance fi) cm = fi.MEPModel?.ConnectorManager;
        else if (elem is FabricationPart fp) cm = fp.ConnectorManager;

        // Get all connectors as list
        var connectors = new List<Connector>();
        if (cm != null) foreach (Connector c in cm.Connectors) connectors.Add(c);

        // Unused (open) vs Used (connected) connectors
        var unused = connectors.Where(c => !c.IsConnected).ToList();
        var used = connectors.Where(c => c.IsConnected).ToList();
        ```

        ## CONNECTOR — PROPERTIES (flow, pressure, area)
        ```
        // Connector properties
        var origin = c.Origin;            // XYZ position
        var domain = c.Domain;            // DomainHvac, DomainPiping, DomainElectrical
        double flow = c.Flow;             // current flow
        double assignedFlow = c.AssignedFlow;
        double pressureDrop = c.PressureDrop;
        double velocityPressure = c.VelocityPressure;
        double assignedPressureDrop = c.AssignedPressureDrop;
        double coefficient = c.Coefficient;
        double kCoeff = c.AssignedKCoefficient;
        double demand = c.Demand;

        // Connector shape and size
        ConnectorProfileType shape = c.Shape; // Round, Rectangular, Oval
        double radius = c.Radius;   // Round only
        double width = c.Width;     // Rectangular/Oval
        double height = c.Height;   // Rectangular/Oval

        // Connector area by shape (OpenMEP pattern)
        double area = c.Shape switch {
            ConnectorProfileType.Round => Math.PI * c.Radius * c.Radius,
            ConnectorProfileType.Rectangular => c.Width * c.Height,
            ConnectorProfileType.Oval => Math.PI * c.Height * c.Width / 4,
            _ => 0
        };

        // Direction via CoordinateSystem
        Transform t = c.CoordinateSystem;
        XYZ connDir = t.BasisZ; // outward direction of connector
        XYZ connOrigin = t.Origin;

        // Domain-based system type
        if (c.Domain == Domain.DomainHvac)        var sysType = c.DuctSystemType;
        if (c.Domain == Domain.DomainPiping)       var sysType = c.PipeSystemType;
        if (c.Domain == Domain.DomainElectrical)   var sysType = c.ElectricalSystemType;
        ```

        ## CONNECTOR — CLOSEST/FARTHEST (OpenMEP pattern)
        ```
        // Find closest connector of elem1 to elem2
        static Connector GetClosestConnector(Element e1, Element e2)
        {
            var cm1 = GetCM(e1); var cm2 = GetCM(e2);
            if (cm1 == null || cm2 == null) return null;
            XYZ center2 = XYZ.Zero; int cnt = 0;
            foreach (Connector c in cm2.Connectors) { center2 += c.Origin; cnt++; }
            if (cnt > 0) center2 /= cnt;
            Connector best = null; double minDist = double.MaxValue;
            foreach (Connector c in cm1.Connectors)
            {
                double d = c.Origin.DistanceTo(center2);
                if (d < minDist) { minDist = d; best = c; }
            }
            return best;
        }

        // Find closest connector pair between two elements
        static (Connector, Connector) GetClosestPair(Element e1, Element e2)
        {
            var c1 = GetClosestConnector(e1, e2);
            var c2 = GetClosestConnector(e2, e1);
            return (c1, c2);
        }
        ```

        ## CONNECTOR — CONNECT / DISCONNECT
        ```
        // Connect two connectors
        connector1.ConnectTo(connector2);

        // Disconnect
        connector1.DisconnectFrom(connector2);

        // Check if connected to specific connector
        bool isConnected = c1.IsConnectedTo(c2);
        ```

        ## CONNECTOR — GRAPH TRAVERSAL (recursive/BFS)
        ```
        // Get directly connected element via connector
        static Element GetConnectedElement(Connector c)
        {
            if (!c.IsConnected) return null;
            foreach (Connector other in c.AllRefs)
                if (other.Owner.Id != c.Owner.Id && other.ConnectorType != ConnectorType.Logical)
                    return other.Owner;
            return null;
        }

        // BFS network traversal (OpenMEP recursive pattern)
        var visited = new HashSet<long>();
        var queue = new Queue<(Element elem, int depth)>();
        queue.Enqueue((startElem, 0));
        visited.Add(startElem.Id.Value);
        while (queue.Count > 0 && visited.Count < 500)
        {
            var (elem, depth) = queue.Dequeue();
            ConnectorManager cm2 = GetCM(elem);
            if (cm2 == null) continue;
            foreach (Connector c in cm2.Connectors)
            {
                if (!c.IsConnected) continue;
                foreach (Connector other in c.AllRefs)
                {
                    if (other?.Owner == null) continue;
                    if (other.ConnectorType == ConnectorType.Logical) continue;
                    if (visited.Add(other.Owner.Id.Value))
                        queue.Enqueue((other.Owner, depth + 1));
                }
            }
        }
        ```

        ## INSULATION CHECK
        ```
        // Check if element has insulation
        var insIds = InsulationLiningBase.GetInsulationIds(doc, elem.Id);
        bool hasInsulation = insIds != null && insIds.Count > 0;

        // Get insulation type reference
        string insType = elem.get_Parameter(BuiltInParameter.RBS_REFERENCE_INSULATION_TYPE)?.AsValueString() ?? "";
        ```

        ## MEP SYSTEM INFO
        ```
        // System name and classification
        string sysName = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
        string sysClass = elem.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)?.AsString() ?? "";

        // Cast to typed system
        MechanicalSystem mechSys = duct.MEPSystem as MechanicalSystem;
        PipingSystem pipeSys = pipe.MEPSystem as PipingSystem;
        ElementSet network = mechSys?.DuctNetwork; // all connected elements

        // System classification enum
        MEPSystemClassification.DomesticColdWater
        MEPSystemClassification.SupplyAir
        MEPSystemClassification.ReturnAir
        MEPSystemClassification.ExhaustAir
        MEPSystemClassification.Sanitary
        MEPSystemClassification.SupplyHydronic
        MEPSystemClassification.ReturnHydronic
        ```

        ## ELEMENT INFO EXTRACTION (production pattern)
        ```
        string familyName = "";
        string typeName = "";
        if (doc.GetElement(elem.GetTypeId()) is ElementType et)
        {
            typeName = et.Name;
            if (et is FamilySymbol fs) familyName = fs.FamilyName;
        }
        ```

        ## BUILT-IN CATEGORIES (MEP — complete list)
        ```
        OST_DuctCurves, OST_PipeCurves, OST_Conduit, OST_CableTray,
        OST_FlexDuctCurves, OST_FlexPipeCurves,
        OST_DuctFitting, OST_PipeFitting, OST_ConduitFitting, OST_CableTrayFitting,
        OST_DuctAccessory, OST_PipeAccessory,
        OST_DuctTerminal,
        OST_MechanicalEquipment, OST_ElectricalEquipment, OST_ElectricalFixtures,
        OST_LightingFixtures, OST_LightingDevices,
        OST_PlumbingFixtures, OST_Sprinklers,
        OST_MEPSpaces, OST_Rooms,
        OST_DuctInsulations, OST_PipeInsulations
        ```

        ## BUILT-IN PARAMETERS (MEP)
        ```
        CURVE_ELEM_LENGTH                 // Length of any MEP curve
        RBS_CALCULATED_SIZE               // Calculated size string (e.g. "400x300")
        RBS_PIPE_DIAMETER_PARAM           // Pipe diameter
        RBS_PIPE_VELOCITY_PARAM           // Pipe velocity
        RBS_PIPE_FLOW_PARAM               // Pipe flow rate
        RBS_PIPE_SLOPE                    // Pipe slope (ratio)
        RBS_CURVE_DIAMETER_PARAM          // Duct diameter (round)
        RBS_CURVE_WIDTH_PARAM             // Duct width
        RBS_CURVE_HEIGHT_PARAM            // Duct height
        RBS_DUCT_FLOW_PARAM               // Duct airflow
        RBS_VELOCITY                      // Duct velocity
        RBS_SYSTEM_NAME_PARAM             // System name
        RBS_SYSTEM_CLASSIFICATION_PARAM   // System classification
        RBS_DUCT_SYSTEM_TYPE_PARAM        // Duct system type ID
        RBS_PIPING_SYSTEM_TYPE_PARAM      // Pipe system type ID
        RBS_START_LEVEL_PARAM             // Reference level
        RBS_REFERENCE_INSULATION_TYPE     // Insulation type ref
        ELEM_FAMILY_AND_TYPE_PARAM        // Family and Type
        ALL_MODEL_MARK                    // Mark
        ALL_MODEL_INSTANCE_COMMENTS       // Comments
        ROOM_NAME, ROOM_NUMBER, ROOM_AREA, ROOM_VOLUME
        ```

        ## TRANSACTIONS
        ```
        using var tx = new Transaction(doc, "My Operation");
        tx.Start();
        // ... modify model ...
        tx.Commit();
        ```

        ## CREATE ELEMENTS
        ```
        // Pipe
        Pipe.Create(doc, pipingSystemTypeId, pipeTypeId, levelId, startXYZ, endXYZ);
        // Duct
        Duct.Create(doc, mechSystemTypeId, ductTypeId, levelId, startXYZ, endXYZ);
        // Family instance
        if (!symbol.IsActive) symbol.Activate();
        doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.NonStructural);
        ```

        ## CLASH / INTERSECTION
        ```
        // BoundingBox overlap check
        BoundingBoxXYZ bb = element.get_BoundingBox(null);
        Outline outline = new Outline(bb.Min, bb.Max);
        var intersectFilter = new BoundingBoxIntersectsFilter(outline);
        var clashes = new FilteredElementCollector(doc)
            .WherePasses(intersectFilter).WhereElementIsNotElementType()
            .Where(e => e.Id != element.Id).ToList();

        // More precise geometry intersection
        var solidFilter = new ElementIntersectsElementFilter(element);

        // BoundingBox overlap with tolerance (mm → ft)
        double tolFt = toleranceMm / 304.8;
        bool clash = bb1.Min.X - tolFt <= bb2.Max.X && bb1.Max.X + tolFt >= bb2.Min.X
                  && bb1.Min.Y - tolFt <= bb2.Max.Y && bb1.Max.Y + tolFt >= bb2.Min.Y
                  && bb1.Min.Z - tolFt <= bb2.Max.Z && bb1.Max.Z + tolFt >= bb2.Min.Z;
        ```

        ## MEP ELEMENT CREATION
        ```
        // Pipe — requires system type, pipe type, level
        var sysId = existingPipe.MEPSystem?.GetTypeId()
            ?? new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).FirstElementId();
        var newPipe = Pipe.Create(doc, sysId, pipeTypeId, levelId, startXYZ, endXYZ);

        // Duct — requires system type, duct type, level
        var mechSysId = existingDuct.MEPSystem?.GetTypeId()
            ?? new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).FirstElementId();
        var newDuct = Duct.Create(doc, mechSysId, ductTypeId, levelId, startXYZ, endXYZ);

        // CableTray — type + start/end + level
        var tray = CableTray.Create(doc, cableTrayTypeId, startXYZ, endXYZ, levelId);

        // Conduit — type + start/end + level
        var conduit = Conduit.Create(doc, conduitTypeId, startXYZ, endXYZ, levelId);

        // Copy dimension params from source to new segment
        foreach (var bip in new[] { BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
            BuiltInParameter.RBS_CURVE_WIDTH_PARAM, BuiltInParameter.RBS_CURVE_HEIGHT_PARAM,
            BuiltInParameter.RBS_CURVE_DIAMETER_PARAM })
        {
            var sp = source.get_Parameter(bip); var tp = target.get_Parameter(bip);
            if (sp != null && tp != null && !tp.IsReadOnly && sp.StorageType == StorageType.Double)
                tp.Set(sp.AsDouble());
        }
        ```

        ## FITTING CREATION — ELBOW / TEE / CROSS / TRANSITION / TAKEOFF / UNION
        ```
        // ELBOW — between 2 open connectors
        var fitting = doc.Create.NewElbowFitting(connector1, connector2);

        // TEE — 3 connectors: 2 on main, 1 on branch (ORDER MATTERS!)
        // OpenMEP pattern: main connectors first, branch last
        var tee = doc.Create.NewTeeFitting(mainConn1, mainConn2, branchConn);

        // CROSS — 4 connectors: main pair + 2 branches (ORDER MATTERS!)
        var cross = doc.Create.NewCrossFitting(mainC1, mainC2, branchC1, branchC2);

        // TRANSITION — between 2 connectors of different size (same axis)
        var transition = doc.Create.NewTransitionFitting(largerConn, smallerConn);

        // TAKEOFF — branch from existing duct/pipe
        var takeoff = doc.Create.NewTakeoffFitting(branchConn, mepCurve);

        // UNION — between 2 coincident connectors (split segments)
        var union = doc.Create.NewUnionFitting(conn1, conn2);
        // conn1 and conn2 must be at same position (DistanceTo < 0.001)

        // For pipes: PlumbingUtils alternative
        PlumbingUtils.ConnectPipePlaceholdersAtElbow(doc, conn1, conn2);
        // For ducts: MechanicalUtils alternative
        MechanicalUtils.ConnectDuctPlaceholdersAtElbow(doc, conn1, conn2);
        // Connect air terminal directly on duct
        MechanicalUtils.ConnectAirTerminalOnDuct(doc, airTerminal, ductConnector);

        // CRITICAL: Connector ordering for Tee/Cross (OpenMEP insight)
        // Main connectors: colinear (dot product ≈ -1)
        // Branch connectors: perpendicular to main (dot product ≈ 0)
        static (Connector main1, Connector main2, Connector branch) OrderForTee(
            Connector c1, Connector c2, Connector c3)
        {
            var d1 = c1.CoordinateSystem.BasisZ;
            var d2 = c2.CoordinateSystem.BasisZ;
            var d3 = c3.CoordinateSystem.BasisZ;
            if (Math.Abs(d1.DotProduct(d2) + 1) < 0.1) return (c1, c2, c3);
            if (Math.Abs(d1.DotProduct(d3) + 1) < 0.1) return (c1, c3, c2);
            return (c2, c3, c1);
        }

        // Find nearest unconnected connector to a point
        Connector nearest = null; double minDist = double.MaxValue;
        foreach (Connector c in mepCurve.ConnectorManager.Connectors)
        {
            if (c.IsConnected) continue;
            double d = c.Origin.DistanceTo(targetPoint);
            if (d < minDist) { minDist = d; nearest = c; }
        }
        ```

        ## ELEMENT DIRECTION / GEOMETRY
        ```
        // Get direction of curve-based element
        if (elem.Location is LocationCurve lc)
        {
            var dir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();
            // XY projection for parallel/perpendicular check
            var xyDir = new XYZ(dir.X, dir.Y, 0).Normalize();
        }

        // Classify parallel vs perpendicular (dot product)
        double dot = Math.Abs(dir1.DotProduct(dir2));
        bool isParallel = dot >= 0.98;
        bool isPerpendicular = dot <= 0.10;

        // Detect vertical riser (Z-span dominant)
        var bb = elem.get_BoundingBox(null);
        bool isRiser = (bb.Max.Z - bb.Min.Z) > 2.0; // > 2 feet
        ```

        ## ROUTING PREFERENCES (OpenMEP pattern)
        ```
        // Get RoutingPreferenceManager from pipe/duct type
        var pipeType = doc.GetElement(pipe.GetTypeId()) as PipeType;
        var rpm = pipeType.RoutingPreferenceManager;

        var ductType = doc.GetElement(duct.GetTypeId()) as DuctType;
        var drpm = ductType.RoutingPreferenceManager;

        // Query rules by group
        int elbowRules = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Elbows);
        int junctionRules = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Junctions);
        int crossRules = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Crosses);
        int transitionRules = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Transitions);
        int segmentRules = rpm.GetNumberOfRules(RoutingPreferenceRuleGroupType.Segments);

        // Get the preferred fitting for a rule group (e.g., elbow fitting family)
        if (elbowRules > 0)
        {
            var rule = rpm.GetRule(RoutingPreferenceRuleGroupType.Elbows, 0);
            var fittingId = rule.MEPPartId; // ElementId of the fitting type
            var fittingElem = doc.GetElement(fittingId); // FamilySymbol
        }

        // Lookup preferred fitting for specific conditions (OpenMEP pattern)
        // RoutingConditions: encapsulate size for preference lookup
        // var conditions = new RoutingConditions(RoutingPreferenceErrorLevel.None);
        // var partId = rpm.GetMEPPartId(RoutingPreferenceRuleGroupType.Elbows, conditions);

        // Preferred junction type
        var junctionType = rpm.PreferredJunctionType;
        // RoutingPreferenceJunctionType.Tee or RoutingPreferenceJunctionType.Tap
        ```

        ## RAY CASTING / REFERENCE INTERSECTOR
        ```
        // Setup ReferenceIntersector (requires 3D view)
        var view3d = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D)).Cast<View3D>()
            .FirstOrDefault(v => !v.IsTemplate);
        var refFilter = new LogicalOrFilter(new List<ElementFilter> {
            new ElementCategoryFilter(BuiltInCategory.OST_Walls),
            new ElementCategoryFilter(BuiltInCategory.OST_Floors),
            new ElementCategoryFilter(BuiltInCategory.OST_Ceilings) });
        var intersector = new ReferenceIntersector(refFilter, FindReferenceTarget.Element, view3d);
        intersector.FindReferencesInRevitLinks = true; // include linked models

        // Cast ray from point in direction
        var hit = intersector.FindNearest(originPoint, directionVector);
        if (hit != null)
        {
            XYZ hitPoint = hit.GetReference().GlobalPoint;
            double distance = originPoint.DistanceTo(hitPoint);
            ElementId hitElemId = hit.GetReference().ElementId;
        }

        // Direction vectors
        XYZ.BasisZ       // Top (0,0,1)
        -XYZ.BasisZ      // Bottom (0,0,-1)
        XYZ.BasisX       // Right (1,0,0)
        -XYZ.BasisX      // Left (-1,0,0)
        XYZ.BasisY       // Front (0,1,0)
        -XYZ.BasisY      // Back (0,-1,0)
        ```

        ## ROOM / SPACE — SPATIAL QUERIES
        ```
        // Rooms
        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType().Cast<Room>()
            .Where(r => r.Area > 0).ToList();
        // using Autodesk.Revit.DB.Architecture; for Room

        // Check if point is inside room
        bool inside = room.IsPointInRoom(point); // XYZ point

        // Spaces (using Autodesk.Revit.DB.Mechanical;)
        bool inSpace = space.IsPointInSpace(point);

        // Room calculation point for FamilyInstance
        if (fi.HasSpatialElementCalculationPoint)
        {
            XYZ calcPt = fi.GetSpatialElementCalculationPoint();
        }

        // Room parameters
        string roomNum = room.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString();
        string roomName = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
        double roomArea = room.Area; // sq feet
        ```

        ## SPLIT / BREAK CURVE
        ```
        // Split duct at point
        ElementId newId = MechanicalUtils.BreakCurve(doc, ductId, splitPoint);
        // using Autodesk.Revit.DB.Mechanical;

        // Split pipe at point
        ElementId newId = PlumbingUtils.BreakCurve(doc, pipeId, splitPoint);
        // using Autodesk.Revit.DB.Plumbing;

        // Create union fitting between adjacent split segments
        // Find coincident connectors:
        foreach (Connector c1 in mep1.ConnectorManager.Connectors)
            foreach (Connector c2 in mep2.ConnectorManager.Connectors)
                if (c1.Origin.DistanceTo(c2.Origin) < 0.001)
                    doc.Create.NewUnionFitting(c1, c2);

        // CONDUIT / CABLE TRAY — BreakCurve NOT available! (OpenMEP workaround)
        // Conduit and CableTray do NOT support BreakCurve.
        // Workaround: CopyElement + shorten both copies via LocationCurve
        if (elem is Conduit || elem is CableTray)
        {
            var lc = (LocationCurve)elem.Location;
            var start = lc.Curve.GetEndPoint(0);
            var end = lc.Curve.GetEndPoint(1);
            // Create copy
            var copiedIds = ElementTransformUtils.CopyElement(doc, elem.Id, XYZ.Zero);
            var copy = doc.GetElement(copiedIds.First());
            // Shorten original to [start, splitPoint]
            lc.Curve = Line.CreateBound(start, splitPoint);
            // Set copy to [splitPoint, end]
            ((LocationCurve)copy.Location).Curve = Line.CreateBound(splitPoint, end);
        }
        ```

        ## ELEMENT LOCATION — UNIVERSAL RESOLUTION (OpenMEP pattern)
        ```
        // Get element location as XYZ (covers all cases)
        static XYZ GetElementLocation(Element elem)
        {
            if (elem.Location is LocationPoint lp) return lp.Point;
            if (elem.Location is LocationCurve lc)
                return lc.Curve.Evaluate(0.5, true); // midpoint
            if (elem is FamilyInstance fi)
            {
                if (fi.HasSpatialElementCalculationPoint)
                    return fi.GetSpatialElementCalculationPoint();
            }
            var bb = elem.get_BoundingBox(null);
            return bb != null ? (bb.Min + bb.Max) / 2 : XYZ.Zero;
        }

        // Get element's level (covers multiple fallbacks)
        static Level GetElementLevel(Document doc, Element elem)
        {
            // Method 1: RBS_START_LEVEL_PARAM
            var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId();
            if (lvlId != null && lvlId != ElementId.InvalidElementId)
                return doc.GetElement(lvlId) as Level;
            // Method 2: LevelId property
            if (elem.LevelId != null && elem.LevelId != ElementId.InvalidElementId)
                return doc.GetElement(elem.LevelId) as Level;
            // Method 3: Closest level by elevation
            var z = GetElementLocation(elem).Z;
            return new FilteredElementCollector(doc).OfClass(typeof(Level))
                .Cast<Level>().OrderBy(l => Math.Abs(l.Elevation - z)).FirstOrDefault();
        }
        ```

        ## NESTED ELEMENTS — SUPER/SUB COMPONENTS
        ```
        // Get parent (host) of a sub-component
        if (fi.SuperComponent is FamilyInstance parent)
        {
            // parent is the host element
        }

        // Get child sub-components
        var subIds = fi.GetSubComponentIds();
        foreach (var id in subIds)
        {
            var subElem = doc.GetElement(id);
        }
        ```

        ## VIEW OPERATIONS
        ```
        // Override element color in view
        var ogs = new OverrideGraphicSettings();
        ogs.SetProjectionLineColor(new Color(255, 0, 0));
        ogs.SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
        view.SetElementOverrides(elemId, ogs);

        // Isolate elements in active view
        view.IsolateElementsTemporary(elementIdList);

        // Create 3D view
        var vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .First(v => v.ViewFamily == ViewFamily.ThreeDimensional);
        var view3d = View3D.CreateIsometric(doc, vft.Id);
        view3d.Name = "MEP - System Name";
        ```

        ## MODEL AUDIT
        ```
        var warnings = doc.GetWarnings();
        var byType = warnings.GroupBy(w => w.GetDescriptionText())
            .Select(g => new { desc = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count).ToList();
        ```

        ## STANDARD SIZES
        ```
        // Standard duct sizes (mm)
        double[] ductSizes = { 100,125,150,200,250,300,350,400,450,500,550,600,650,700,750,800,900,1000,1100,1200,1400,1500,1600,1800,2000 };
        // Standard pipe DN (mm)
        double[] pipeDN = { 15,20,25,32,40,50,65,80,100,125,150,200,250,300,350,400,450,500 };
        // Round to nearest standard
        double nearest = sizes.OrderBy(s => Math.Abs(s - targetMm)).First();
        ```

        ## REVIT 2025 GOTCHAS
        - .NET 8 required (not .NET Framework)
        - PipeSystemType enum: NO "Storm" value → Use PipeSystemType.OtherPipe
        - ParameterType is DEPRECATED → Use ForgeTypeId / SpecTypeId
        - UnitType is DEPRECATED → Use UnitTypeId
        - parameter.Definition.ParameterGroup is DEPRECATED → Use GetGroupTypeId()
        - ElementSet.Size is DEPRECATED → Use .Count or LINQ .Count()
        - DuctSettings.AirViscosity is DEPRECATED → Use AirDynamicViscosity
        - LinearDimension inherits Dimension (affects typeof checks)
        - Electrical params renamed: "Total Estimated Demand" → "Total Demand Apparent Power"

        ## RESULT STRING PATTERN
        ```
        return $"Found {count} elements. Total length: {Math.Round(totalLen * 0.3048, 1)}m.";
        // For tables:
        "| ID | Size | System | Level |\n|---|---|---|---|"
        ```
        """;

    public static string GetCommonErrorFixes() => """
        === COMMON COMPILE ERRORS AND FIXES ===

        ERROR: "'PipeSystemType' does not contain a definition for 'Storm'"
        FIX: Use PipeSystemType.OtherPipe instead of PipeSystemType.Storm (removed in 2025)

        ERROR: "'ParameterType' is obsolete"
        FIX: Use ForgeTypeId. e.g., SpecTypeId.Length instead of ParameterType.Length

        ERROR: "'UnitType' is obsolete"
        FIX: Use UnitTypeId. e.g., UnitTypeId.Millimeters instead of UnitType.UT_Millimeters

        ERROR: "'ElementSet' does not contain a definition for 'Size'"
        FIX: Use .Count property or LINQ .Count() instead of .Size

        ERROR: "Cannot implicitly convert type 'LinearDimension' to 'Dimension'"
        FIX: LinearDimension inherits from Dimension. Use 'is Dimension' or cast.

        ERROR: "The type or namespace name 'xxx' could not be found"
        FIX: Add: using Autodesk.Revit.DB; using Autodesk.Revit.DB.Mechanical; using Autodesk.Revit.DB.Plumbing;

        ERROR: "Cannot convert from 'double' to 'Autodesk.Revit.DB.XYZ'"
        FIX: Create XYZ: new XYZ(x, y, z). Values in FEET.

        ERROR: "Modifications are not permitted outside Transaction"
        FIX: Wrap: using var tx = new Transaction(doc, "name"); tx.Start(); ... tx.Commit();

        ERROR: "The symbol is not active"
        FIX: if (!symbol.IsActive) symbol.Activate(); (inside Transaction)

        ERROR: "'Document' does not contain a definition for 'Create'"
        FIX: Use doc.Create (exists). For Pipe: Pipe.Create(doc, ...).

        ERROR: "'ConnectorManager' does not contain a definition for 'Connectors'"
        FIX: ConnectorManager.Connectors returns ConnectorSet. Iterate with foreach.

        ERROR: "InsulationLiningBase.GetInsulationIds requires Document"
        FIX: InsulationLiningBase.GetInsulationIds(doc, elementId) — two parameters.

        ERROR: "Cannot access 'MEPModel' on non-FamilyInstance"
        FIX: Check: if (elem is FamilyInstance fi) { var cm = fi.MEPModel?.ConnectorManager; }

        ERROR: "'Space' does not exist in Autodesk.Revit.DB"
        FIX: Add: using Autodesk.Revit.DB.Mechanical; (Space is in Mechanical namespace)

        ERROR: "Pipe.Create requires 6 parameters"
        FIX: Pipe.Create(doc, pipingSystemTypeId, pipeTypeId, levelId, startXYZ, endXYZ)

        ERROR: "Duct.Create requires 6 parameters"
        FIX: Duct.Create(doc, mechSystemTypeId, ductTypeId, levelId, startXYZ, endXYZ)

        ERROR: "CableTray.Create requires 5 parameters"
        FIX: CableTray.Create(doc, cableTrayTypeId, startXYZ, endXYZ, levelId)

        ERROR: "'ConnectorManager' is null"
        FIX: MEPCurve.ConnectorManager. For FamilyInstance: fi.MEPModel?.ConnectorManager

        ERROR: "NewElbowFitting failed"
        FIX: Connectors must be unconnected and at the same point. Try PlumbingUtils or MechanicalUtils alternatives.

        ERROR: "ReferenceIntersector requires View3D"
        FIX: Get a 3D view: new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().First(v => !v.IsTemplate)

        ERROR: "'Room' does not exist in Autodesk.Revit.DB"
        FIX: Add: using Autodesk.Revit.DB.Architecture; (Room is in Architecture namespace)

        ERROR: "IsPointInRoom is not a member of Element"
        FIX: Cast to Room: if (element is Room room) room.IsPointInRoom(point)

        ERROR: "BreakCurve failed"
        FIX: Split point must be ON the curve and not at endpoints. Check ShortCurveTolerance.

        ERROR: "NewUnionFitting requires coincident connectors"
        FIX: Connectors must be at exact same position (DistanceTo < 0.001). Use connectors from adjacent split segments.

        ERROR: "NewTeeFitting failed" or "NewCrossFitting failed"
        FIX: Connector order matters! Main connectors (colinear, dot≈-1) first, then branch. Use CoordinateSystem.BasisZ to classify.

        ERROR: "NewTransitionFitting failed"
        FIX: Connectors must be on same axis (colinear). Larger connector first, smaller second.

        ERROR: "NewTakeoffFitting failed"
        FIX: First param is branch connector, second is the MEPCurve (not connector). Branch must be perpendicular to curve.

        ERROR: "BreakCurve not available for Conduit/CableTray"
        FIX: Use CopyElement workaround: copy element + adjust LocationCurve on both original and copy.

        ERROR: "'FabricationPart' does not contain 'ConnectorManager'"
        FIX: FabricationPart.ConnectorManager exists in 2025. Add: using Autodesk.Revit.DB;

        ERROR: "UnitTypeId does not contain 'LitersPerSecond'"
        FIX: Use UnitTypeId.LitersPerSecond (no underscore). Check exact name in API docs.

        ERROR: "'RoutingPreferenceManager' not found"
        FIX: Access via PipeType.RoutingPreferenceManager or DuctType.RoutingPreferenceManager. Cast GetTypeId() first.
        """;
}
