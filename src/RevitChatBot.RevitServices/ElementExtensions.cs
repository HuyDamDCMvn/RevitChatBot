using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices;

/// <summary>
/// Extension methods for Revit Element — reduces boilerplate for parameter
/// access, MEP property queries, and geometric operations.
/// </summary>
public static class ElementExtensions
{
    #region Parameter access — by name

    public static string? GetParamString(this Element e, string paramName)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || !p.HasValue) return null;
        return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
    }

    public static double? GetParamDouble(this Element e, string paramName)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || !p.HasValue) return null;
        return p.StorageType == StorageType.Double ? p.AsDouble() : null;
    }

    public static int? GetParamInt(this Element e, string paramName)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || !p.HasValue) return null;
        return p.StorageType == StorageType.Integer ? p.AsInteger() : null;
    }

    /// <summary>
    /// Returns the best display string for a parameter, regardless of storage type.
    /// </summary>
    public static string GetParamDisplay(this Element e, string paramName)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || !p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("F2"),
            StorageType.Integer => p.AsValueString() ?? p.AsInteger().ToString(),
            StorageType.ElementId => p.AsValueString() ?? p.AsElementId().ToString(),
            _ => p.AsValueString() ?? ""
        };
    }

    #endregion

    #region Parameter access — by BuiltInParameter

    public static string? GetParamString(this Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        if (p is null || !p.HasValue) return null;
        return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
    }

    public static double? GetParamDouble(this Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        if (p is null || !p.HasValue) return null;
        return p.StorageType == StorageType.Double ? p.AsDouble() : null;
    }

    public static int? GetParamInt(this Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        if (p is null || !p.HasValue) return null;
        return p.StorageType == StorageType.Integer ? p.AsInteger() : null;
    }

    public static string GetParamDisplay(this Element e, BuiltInParameter bip)
    {
        var p = e.get_Parameter(bip);
        if (p is null || !p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("F2"),
            StorageType.Integer => p.AsValueString() ?? p.AsInteger().ToString(),
            StorageType.ElementId => p.AsValueString() ?? p.AsElementId().ToString(),
            _ => p.AsValueString() ?? ""
        };
    }

    #endregion

    #region Set parameter

    public static bool TrySetParam(this Element e, string paramName, string value)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        try { p.Set(value); return true; } catch { return false; }
    }

    public static bool TrySetParam(this Element e, string paramName, double value)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
        try { p.Set(value); return true; } catch { return false; }
    }

    public static bool TrySetParam(this Element e, string paramName, int value)
    {
        var p = e.LookupParameter(paramName);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.Integer) return false;
        try { p.Set(value); return true; } catch { return false; }
    }

    public static bool TrySetParam(this Element e, BuiltInParameter bip, string value)
    {
        var p = e.get_Parameter(bip);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
        try { p.Set(value); return true; } catch { return false; }
    }

    public static bool TrySetParam(this Element e, BuiltInParameter bip, double value)
    {
        var p = e.get_Parameter(bip);
        if (p is null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
        try { p.Set(value); return true; } catch { return false; }
    }

    #endregion

    #region Common MEP properties

    public static string GetLevelName(this Element e, Document doc)
    {
        if (e.LevelId is { } lid && lid != ElementId.InvalidElementId)
            return doc.GetElement(lid)?.Name ?? "";
        return e.LookupParameter("Reference Level")?.AsValueString()
               ?? e.LookupParameter("Level")?.AsValueString()
               ?? "";
    }

    public static string GetSystemName(this Element e)
    {
        return e.GetParamString(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)
               ?? e.GetParamString("System Name")
               ?? e.GetParamString("System Type")
               ?? "";
    }

    public static string GetTypeName(this Element e, Document doc)
    {
        var typeId = e.GetTypeId();
        return typeId != ElementId.InvalidElementId
            ? doc.GetElement(typeId)?.Name ?? ""
            : "";
    }

    public static string GetFamilyName(this Element e)
    {
        return e is FamilyInstance fi ? fi.Symbol?.FamilyName ?? "" : "";
    }

    public static string GetSize(this Element e)
    {
        return e.GetParamString(BuiltInParameter.RBS_CALCULATED_SIZE)
               ?? e.GetParamString("Size")
               ?? e.GetParamString("Diameter")
               ?? "";
    }

    /// <summary>Returns curve length in meters (converts from internal feet).</summary>
    public static double GetLengthMeters(this Element e)
    {
        var lenFt = e.GetParamDouble(BuiltInParameter.CURVE_ELEM_LENGTH);
        return lenFt.HasValue ? lenFt.Value * 0.3048 : 0;
    }

    #endregion

    #region Connectors

    public static ConnectorManager? GetConnectorManager(this Element e)
    {
        return e switch
        {
            MEPCurve mc => mc.ConnectorManager,
            FamilyInstance fi => fi.MEPModel?.ConnectorManager,
            _ => null
        };
    }

    public static bool HasOpenConnectors(this Element e)
    {
        var cm = e.GetConnectorManager();
        if (cm is null) return false;
        foreach (Connector c in cm.Connectors)
            if (!c.IsConnected) return true;
        return false;
    }

    public static List<Connector> GetOpenConnectors(this Element e)
    {
        var result = new List<Connector>();
        var cm = e.GetConnectorManager();
        if (cm is null) return result;
        foreach (Connector c in cm.Connectors)
            if (!c.IsConnected) result.Add(c);
        return result;
    }

    public static List<Element> GetConnectedElements(this Element e)
    {
        var result = new List<Element>();
        var cm = e.GetConnectorManager();
        if (cm is null) return result;
        foreach (Connector c in cm.Connectors)
        {
            if (!c.IsConnected) continue;
            foreach (Connector other in c.AllRefs)
            {
                if (other?.Owner != null && other.Owner.Id != e.Id)
                    result.Add(other.Owner);
            }
        }
        return result;
    }

    #endregion

    #region Geometry helpers

    public static XYZ? GetCenter(this Element e)
    {
        var bb = e.get_BoundingBox(null);
        if (bb is null) return null;
        return (bb.Min + bb.Max) / 2;
    }

    /// <summary>
    /// Returns the midpoint of a curve element, the point of a point-based element,
    /// or the bounding box center as a fallback.
    /// </summary>
    public static XYZ? GetMidpoint(this Element e)
    {
        if (e.Location is LocationCurve lc)
            return lc.Curve.Evaluate(0.5, true);
        if (e.Location is LocationPoint lp)
            return lp.Point;
        return e.GetCenter();
    }

    public static XYZ? GetDirection(this Element e)
    {
        if (e.Location is not LocationCurve lc) return null;
        var s = lc.Curve.GetEndPoint(0);
        var t = lc.Curve.GetEndPoint(1);
        var d = t - s;
        return d.GetLength() > 1e-9 ? d.Normalize() : null;
    }

    #endregion

    #region Movement helpers

    /// <summary>
    /// Translates the element by the given vector.
    /// Must be called inside a Transaction.
    /// </summary>
    public static void MoveBy(this Element e, XYZ translation)
    {
        ElementTransformUtils.MoveElement(e.Document, e.Id, translation);
    }

    /// <summary>
    /// Moves the element so its center (bounding box) lands at <paramref name="target"/>.
    /// Must be called inside a Transaction.
    /// </summary>
    public static bool MoveTo(this Element e, XYZ target)
    {
        var current = e.GetCenter();
        if (current is null) return false;
        e.MoveBy(target - current);
        return true;
    }

    /// <summary>
    /// Moves the element so its LocationPoint lands at <paramref name="target"/>.
    /// Preferred for point-based elements (equipment, tags without leaders, etc.).
    /// Must be called inside a Transaction.
    /// </summary>
    public static bool MovePointTo(this Element e, XYZ target)
    {
        if (e.Location is LocationPoint lp)
        {
            e.MoveBy(target - lp.Point);
            return true;
        }
        return e.MoveTo(target);
    }

    #endregion

    #region Annotation helpers

    /// <summary>
    /// Returns the position of an annotation element in view coordinates.
    /// Works for IndependentTag (TagHeadPosition), TextNote (Coord),
    /// and falls back to bounding box center.
    /// </summary>
    public static XYZ? GetAnnotationPosition(this Element e)
    {
        if (e is IndependentTag tag)
            return tag.TagHeadPosition;
        if (e is TextNote tn)
            return tn.Coord;
        return e.GetCenter();
    }

    /// <summary>
    /// Moves an annotation element to the given position.
    /// Uses type-specific APIs for IndependentTag and TextNote,
    /// falls back to ElementTransformUtils.MoveElement.
    /// Must be called inside a Transaction.
    /// </summary>
    public static bool SetAnnotationPosition(this Element e, XYZ target)
    {
        if (e is IndependentTag tag)
        {
            tag.TagHeadPosition = target;
            return true;
        }
        if (e is TextNote tn)
        {
            var delta = target - tn.Coord;
            ElementTransformUtils.MoveElement(e.Document, e.Id, delta);
            return true;
        }
        return e.MoveTo(target);
    }

    /// <summary>
    /// Returns the bounding box of the element projected into the given view,
    /// or null if not visible.
    /// </summary>
    public static BoundingBoxXYZ? GetBoundingBoxInView(this Element e, View? view)
    {
        return e.get_BoundingBox(view);
    }

    /// <summary>
    /// Returns the first tagged element for an IndependentTag, or null.
    /// </summary>
    public static Element? GetTaggedElement(this IndependentTag tag)
    {
        var ids = tag.GetTaggedLocalElementIds();
        if (ids.Count == 0) return null;
        return tag.Document.GetElement(ids.First());
    }

    /// <summary>
    /// Returns the tag's width and height in the given view, in feet.
    /// Falls back to a default estimate if bounding box is unavailable.
    /// </summary>
    public static (double Width, double Height) GetTagSize(this IndependentTag tag, View view)
    {
        var bb = tag.get_BoundingBox(view);
        if (bb is not null)
            return (bb.Max.X - bb.Min.X, bb.Max.Y - bb.Min.Y);
        return (0.4, 0.15);
    }

    /// <summary>
    /// Returns whether the tag has a visible leader line.
    /// </summary>
    public static bool HasLeader(this IndependentTag tag)
    {
        try { return tag.HasLeader; }
        catch { return false; }
    }

    /// <summary>
    /// Returns the crop region bounding box of a view in model coordinates,
    /// or null if cropping is not active.
    /// </summary>
    public static BoundingBoxXYZ? GetCropRegionBounds(this View view)
    {
        try
        {
            if (!view.CropBoxActive) return null;
            return view.CropBox;
        }
        catch { return null; }
    }

    /// <summary>
    /// Returns the leader end point for a tag (the point closest to the tagged element).
    /// Falls back to the tagged element's center if the leader endpoint is not directly accessible.
    /// </summary>
    public static XYZ? GetLeaderEnd(this IndependentTag tag)
    {
        try
        {
            if (!tag.HasLeader) return null;
            var tagged = tag.GetTaggedElement();
            return tagged?.GetCenter();
        }
        catch { return null; }
    }

    #endregion
}
