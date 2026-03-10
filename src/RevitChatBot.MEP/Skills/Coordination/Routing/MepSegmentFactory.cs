using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;

namespace RevitChatBot.MEP.Skills.Coordination.Routing;

/// <summary>
/// Creates new MEP curve elements matching the type and parameters of an original element.
/// Supports Pipe, Duct, CableTray, and Conduit.
/// </summary>
public static class MepSegmentFactory
{
    public static MEPCurve? CreateSegmentLike(Document doc, Element original, XYZ start, XYZ end)
    {
        if (start.DistanceTo(end) < doc.Application.ShortCurveTolerance)
            return null;

        return original switch
        {
            Pipe pipe => CreatePipe(doc, pipe, start, end),
            Duct duct => CreateDuct(doc, duct, start, end),
            CableTray tray => CreateCableTray(doc, tray, start, end),
            Conduit conduit => CreateConduit(doc, conduit, start, end),
            _ => null
        };
    }

    /// <summary>
    /// Copy dimension parameters (Width, Height, Diameter, Insulation, etc.) from source to target.
    /// </summary>
    public static void CopyMepParameters(MEPCurve source, MEPCurve target)
    {
        var paramsToCopy = new[]
        {
            BuiltInParameter.RBS_PIPE_DIAMETER_PARAM,
            BuiltInParameter.RBS_PIPE_INNER_DIAM_PARAM,
            BuiltInParameter.RBS_CURVE_WIDTH_PARAM,
            BuiltInParameter.RBS_CURVE_HEIGHT_PARAM,
            BuiltInParameter.RBS_CURVE_DIAMETER_PARAM,
            BuiltInParameter.RBS_REFERENCE_INSULATION_THICKNESS,
            BuiltInParameter.RBS_REFERENCE_LINING_THICKNESS,
        };

        foreach (var bip in paramsToCopy)
        {
            var sourceParam = source.get_Parameter(bip);
            var targetParam = target.get_Parameter(bip);
            if (sourceParam is null || targetParam is null) continue;
            if (sourceParam.StorageType != StorageType.Double) continue;
            if (targetParam.IsReadOnly) continue;

            try
            {
                targetParam.Set(sourceParam.AsDouble());
            }
            catch
            {
                // Some parameters may not be settable depending on element state
            }
        }
    }

    private static Pipe? CreatePipe(Document doc, Pipe original, XYZ start, XYZ end)
    {
        var systemTypeId = original.MEPSystem is PipingSystem ps
            ? ps.GetTypeId()
            : GetDefaultSystemTypeId(doc, original);

        var pipe = Pipe.Create(
            doc,
            systemTypeId,
            original.GetTypeId(),
            GetLevelId(doc, original),
            start,
            end);

        CopyMepParameters(original, pipe);
        return pipe;
    }

    private static Duct? CreateDuct(Document doc, Duct original, XYZ start, XYZ end)
    {
        var systemTypeId = original.MEPSystem is MechanicalSystem ms
            ? ms.GetTypeId()
            : GetDefaultSystemTypeId(doc, original);

        var duct = Duct.Create(
            doc,
            systemTypeId,
            original.GetTypeId(),
            GetLevelId(doc, original),
            start,
            end);

        CopyMepParameters(original, duct);
        return duct;
    }

    private static CableTray? CreateCableTray(Document doc, CableTray original, XYZ start, XYZ end)
    {
        var tray = CableTray.Create(
            doc,
            original.GetTypeId(),
            start,
            end,
            GetLevelId(doc, original));

        CopyMepParameters(original, tray);
        return tray;
    }

    private static Conduit? CreateConduit(Document doc, Conduit original, XYZ start, XYZ end)
    {
        var conduit = Conduit.Create(
            doc,
            original.GetTypeId(),
            start,
            end,
            GetLevelId(doc, original));

        CopyMepParameters(original, conduit);
        return conduit;
    }

    private static ElementId GetLevelId(Document doc, Element element)
    {
        var levelParam = element.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM);
        if (levelParam?.AsElementId() is { } id && id != ElementId.InvalidElementId)
            return id;

        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        return levels.FirstOrDefault()?.Id ?? ElementId.InvalidElementId;
    }

    private static ElementId GetDefaultSystemTypeId(Document doc, Element element)
    {
        if (element is Pipe)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(PipingSystemType))
                .FirstElementId();
        }

        if (element is Duct)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(MechanicalSystemType))
                .FirstElementId();
        }

        return ElementId.InvalidElementId;
    }
}
