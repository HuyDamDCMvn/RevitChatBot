using Autodesk.Revit.DB;

namespace RevitChatBot.RevitServices;

/// <summary>
/// Fluent, prioritized element collector inspired by revitpythonwrapper (rpw).
/// Applies Revit native filters first (fast), then LINQ post-filters (slow).
///
/// Filter priority:
///   0 - SuperQuick : OfCategory, OfClass
///   1 - Quick      : IsNotElementType, Excluding
///   2 - Slow       : OnLevel, WhereParameter, BoundingBox (native Revit filters)
///   3 - SuperSlow  : Where lambda, InSystem, InRoom (C# post-filter)
/// </summary>
public class FluentCollector
{
    private readonly Document _doc;
    private ElementId? _viewId;

    // Priority 0 — SuperQuick
    private BuiltInCategory? _category;
    private Type? _elementClass;

    // Priority 1 — Quick
    private bool? _excludeTypes;
    private ICollection<ElementId>? _excludeIds;

    // Priority 2 — Slow (native Revit ElementFilter)
    private readonly List<ElementFilter> _nativeFilters = [];

    // Priority 3 — SuperSlow (C# lambda)
    private readonly List<Func<Element, bool>> _postFilters = [];

    public FluentCollector(Document doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));
    }

    #region Priority 0 — SuperQuick

    public FluentCollector OfCategory(BuiltInCategory category)
    {
        _category = category;
        return this;
    }

    public FluentCollector OfClass(Type type)
    {
        _elementClass = type;
        return this;
    }

    public FluentCollector OfClass<T>() where T : Element
    {
        _elementClass = typeof(T);
        return this;
    }

    #endregion

    #region Priority 1 — Quick

    public FluentCollector WhereElementIsNotElementType()
    {
        _excludeTypes = true;
        return this;
    }

    public FluentCollector WhereElementIsElementType()
    {
        _excludeTypes = false;
        return this;
    }

    public FluentCollector InView(ElementId viewId)
    {
        _viewId = viewId;
        return this;
    }

    public FluentCollector Excluding(ICollection<ElementId> ids)
    {
        _excludeIds = ids;
        return this;
    }

    #endregion

    #region Priority 2 — Slow (native Revit filters)

    public FluentCollector OnLevel(ElementId levelId)
    {
        _nativeFilters.Add(new ElementLevelFilter(levelId));
        return this;
    }

    /// <summary>
    /// Filter by level name. Resolves name to ElementId(s), then uses native
    /// ElementLevelFilter with a post-filter fallback for elements that store
    /// their level in the "Reference Level" parameter instead of LevelId.
    /// </summary>
    public FluentCollector OnLevel(string levelName)
    {
        var levelIds = new FilteredElementCollector(_doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Where(l => l.Name.Contains(levelName, StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Id.Value)
            .ToHashSet();

        _postFilters.Add(e =>
        {
            if (e.LevelId is { } lid && lid != ElementId.InvalidElementId)
                return levelIds.Contains(lid.Value);
            var refLevel = e.LookupParameter("Reference Level") ?? e.LookupParameter("Level");
            return refLevel?.AsValueString()?.Contains(levelName, StringComparison.OrdinalIgnoreCase) == true;
        });
        return this;
    }

    public FluentCollector WhereParameter(BuiltInParameter param, FilterOperator op, string value)
    {
        var paramId = new ElementId(param);
        FilterRule rule = op switch
        {
            FilterOperator.Equals => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value),
            FilterOperator.NotEquals => ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value),
            FilterOperator.Contains => ParameterFilterRuleFactory.CreateContainsRule(paramId, value),
            FilterOperator.BeginsWith => ParameterFilterRuleFactory.CreateBeginsWithRule(paramId, value),
            FilterOperator.EndsWith => ParameterFilterRuleFactory.CreateEndsWithRule(paramId, value),
            _ => ParameterFilterRuleFactory.CreateContainsRule(paramId, value)
        };

        if (op == FilterOperator.NotContains)
        {
            var containsRule = ParameterFilterRuleFactory.CreateContainsRule(paramId, value);
            _nativeFilters.Add(new ElementParameterFilter(containsRule, true));
        }
        else
        {
            _nativeFilters.Add(new ElementParameterFilter(rule));
        }
        return this;
    }

    public FluentCollector WhereParameter(BuiltInParameter param, FilterOperator op, double value, double tolerance = 1e-6)
    {
        var paramId = new ElementId(param);
        FilterRule rule = op switch
        {
            FilterOperator.Equals => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value, tolerance),
            FilterOperator.NotEquals => ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value, tolerance),
            FilterOperator.Greater => ParameterFilterRuleFactory.CreateGreaterRule(paramId, value, tolerance),
            FilterOperator.GreaterOrEqual => ParameterFilterRuleFactory.CreateGreaterOrEqualRule(paramId, value, tolerance),
            FilterOperator.Less => ParameterFilterRuleFactory.CreateLessRule(paramId, value, tolerance),
            FilterOperator.LessOrEqual => ParameterFilterRuleFactory.CreateLessOrEqualRule(paramId, value, tolerance),
            _ => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value, tolerance)
        };
        _nativeFilters.Add(new ElementParameterFilter(rule));
        return this;
    }

    public FluentCollector WhereParameter(BuiltInParameter param, FilterOperator op, int value)
    {
        var paramId = new ElementId(param);
        FilterRule rule = op switch
        {
            FilterOperator.Equals => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value),
            FilterOperator.NotEquals => ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value),
            FilterOperator.Greater => ParameterFilterRuleFactory.CreateGreaterRule(paramId, value),
            FilterOperator.GreaterOrEqual => ParameterFilterRuleFactory.CreateGreaterOrEqualRule(paramId, value),
            FilterOperator.Less => ParameterFilterRuleFactory.CreateLessRule(paramId, value),
            FilterOperator.LessOrEqual => ParameterFilterRuleFactory.CreateLessOrEqualRule(paramId, value),
            _ => ParameterFilterRuleFactory.CreateEqualsRule(paramId, value)
        };
        _nativeFilters.Add(new ElementParameterFilter(rule));
        return this;
    }

    public FluentCollector IntersectsBoundingBox(Outline outline)
    {
        _nativeFilters.Add(new BoundingBoxIntersectsFilter(outline));
        return this;
    }

    public FluentCollector InsideBoundingBox(Outline outline)
    {
        _nativeFilters.Add(new BoundingBoxIsInsideFilter(outline));
        return this;
    }

    #endregion

    #region Priority 2→3 — Smart resolvers (name-based → post-filter)

    /// <summary>
    /// Filter by parameter name (project/shared/family params). Uses post-filter
    /// because named parameters can't always be resolved to native ElementParameterFilter.
    /// </summary>
    public FluentCollector WhereParameter(string paramName, FilterOperator op, string value)
    {
        _postFilters.Add(e =>
        {
            var param = e.LookupParameter(paramName);
            if (param is null || !param.HasValue) return false;
            var actual = param.AsValueString() ?? param.AsString() ?? "";
            return op switch
            {
                FilterOperator.Equals => actual.Equals(value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.NotEquals => !actual.Equals(value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.Contains => actual.Contains(value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.NotContains => !actual.Contains(value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.BeginsWith => actual.StartsWith(value, StringComparison.OrdinalIgnoreCase),
                FilterOperator.EndsWith => actual.EndsWith(value, StringComparison.OrdinalIgnoreCase),
                _ => actual.Contains(value, StringComparison.OrdinalIgnoreCase)
            };
        });
        return this;
    }

    public FluentCollector WhereParameter(string paramName, FilterOperator op, double value)
    {
        _postFilters.Add(e =>
        {
            var param = e.LookupParameter(paramName);
            if (param is null || !param.HasValue) return false;
            double actual;
            if (param.StorageType == StorageType.Double)
                actual = param.AsDouble();
            else if (!double.TryParse(ExtractNumber(param.AsValueString() ?? ""), out actual))
                return false;
            return op switch
            {
                FilterOperator.Equals => Math.Abs(actual - value) < 1e-6,
                FilterOperator.NotEquals => Math.Abs(actual - value) >= 1e-6,
                FilterOperator.Greater => actual > value,
                FilterOperator.GreaterOrEqual => actual >= value,
                FilterOperator.Less => actual < value,
                FilterOperator.LessOrEqual => actual <= value,
                _ => Math.Abs(actual - value) < 1e-6
            };
        });
        return this;
    }

    /// <summary>
    /// Convenience overload accepting string operator names (backward-compatible
    /// with existing skill parameter formats like "equals", "contains", "greater_than").
    /// </summary>
    public FluentCollector WhereParameter(string paramName, string op, string value)
    {
        var filterOp = ParseOperator(op);

        if (filterOp is FilterOperator.Greater or FilterOperator.GreaterOrEqual
                or FilterOperator.Less or FilterOperator.LessOrEqual
            && double.TryParse(ExtractNumber(value), out var numValue))
        {
            return WhereParameter(paramName, filterOp, numValue);
        }

        return WhereParameter(paramName, filterOp, value);
    }

    #endregion

    #region Priority 3 — SuperSlow (lambda post-filters)

    public FluentCollector Where(Func<Element, bool> predicate)
    {
        _postFilters.Add(predicate);
        return this;
    }

    /// <summary>
    /// Filter by MEP system name. Always a post-filter because Revit has
    /// no native filter for MEP system membership.
    /// </summary>
    public FluentCollector InSystem(string systemName)
    {
        _postFilters.Add(e =>
        {
            var sysParam = e.LookupParameter("System Name")
                           ?? e.LookupParameter("System Type")
                           ?? e.LookupParameter("System Classification");
            var val = sysParam?.AsString() ?? sysParam?.AsValueString() ?? "";
            return val.Contains(systemName, StringComparison.OrdinalIgnoreCase);
        });
        return this;
    }

    /// <summary>
    /// Filter elements whose bounding box overlaps with a named Room or MEP Space.
    /// Falls back to MEP Spaces if no Rooms match.
    /// </summary>
    public FluentCollector InRoom(string roomName)
    {
        var boxes = FindRoomBoundingBoxes(roomName);

        if (boxes.Count > 0)
        {
            _postFilters.Add(e =>
            {
                var ebb = e.get_BoundingBox(null);
                if (ebb is null) return false;
                return boxes.Any(box =>
                    ebb.Min.X <= box.Max.X && ebb.Max.X >= box.Min.X &&
                    ebb.Min.Y <= box.Max.Y && ebb.Max.Y >= box.Min.Y &&
                    ebb.Min.Z <= box.Max.Z && ebb.Max.Z >= box.Min.Z);
            });
        }
        else
        {
            _postFilters.Add(_ => false);
        }
        return this;
    }

    #endregion

    #region Annotation shortcuts

    /// <summary>
    /// Shortcut: collect all IndependentTag instances visible in the given view.
    /// </summary>
    public FluentCollector OfTags()
    {
        _elementClass = typeof(IndependentTag);
        return this;
    }

    /// <summary>
    /// Shortcut: collect all TextNote instances visible in the given view.
    /// </summary>
    public FluentCollector OfTextNotes()
    {
        _elementClass = typeof(TextNote);
        return this;
    }

    /// <summary>
    /// Shortcut: collect all Viewport instances (views placed on sheets).
    /// </summary>
    public FluentCollector OfViewports()
    {
        _elementClass = typeof(Viewport);
        return this;
    }

    /// <summary>
    /// Filter IndependentTag instances that tag elements of the specified category.
    /// Post-filter because Revit has no native filter for tagged-element category.
    /// </summary>
    public FluentCollector WhereTaggedCategory(BuiltInCategory taggedCategory)
    {
        _postFilters.Add(e =>
        {
            if (e is not IndependentTag tag) return false;
            var taggedIds = tag.GetTaggedLocalElementIds();
            if (taggedIds.Count == 0) return false;
            var doc = e.Document;
            return taggedIds.Any(id =>
            {
                var taggedElem = doc.GetElement(id);
                return taggedElem?.Category?.BuiltInCategory == taggedCategory;
            });
        });
        return this;
    }

    /// <summary>
    /// Shortcut: collect all Dimension instances.
    /// </summary>
    public FluentCollector OfDimensions()
    {
        _category = BuiltInCategory.OST_Dimensions;
        return this;
    }

    /// <summary>
    /// Shortcut: collect all detail lines / detail curves.
    /// </summary>
    public FluentCollector OfDetailLines()
    {
        _category = BuiltInCategory.OST_Lines;
        return this;
    }

    /// <summary>
    /// Shortcut: collect all detail items (detail components placed in views).
    /// </summary>
    public FluentCollector OfDetailItems()
    {
        _category = BuiltInCategory.OST_DetailComponents;
        return this;
    }

    /// <summary>
    /// Collect elements of the given category that have no IndependentTag in the view.
    /// Useful for finding untagged elements before placing tags.
    /// Must call InView() before or after this method for correct results.
    /// </summary>
    public FluentCollector WhereUntagged(BuiltInCategory elementCategory)
    {
        _postFilters.Add(e =>
        {
            if (e.Category?.BuiltInCategory != elementCategory) return false;
            var doc = e.Document;
            var viewId = _viewId;
            if (viewId is null) return true;

            var tags = new FilteredElementCollector(doc, viewId)
                .OfClass(typeof(IndependentTag))
                .Cast<IndependentTag>();

            foreach (var tag in tags)
            {
                if (tag.GetTaggedLocalElementIds().Any(id => id.Value == e.Id.Value))
                    return false;
            }
            return true;
        });
        return this;
    }

    /// <summary>
    /// Shortcut: collect all RevitLinkInstance elements in the document.
    /// </summary>
    public FluentCollector OfLinks()
    {
        _elementClass = typeof(RevitLinkInstance);
        return this;
    }

    #endregion

    #region Terminal operations

    public List<Element> ToList()
    {
        return Execute().ToList();
    }

    public List<T> ToList<T>() where T : Element
    {
        return Execute().OfType<T>().ToList();
    }

    public int Count()
    {
        if (_postFilters.Count == 0)
            return BuildCollector().GetElementCount();
        return Execute().Count();
    }

    public Element? FirstOrDefault()
    {
        return Execute().FirstOrDefault();
    }

    public T? FirstOrDefault<T>() where T : Element
    {
        return Execute().OfType<T>().FirstOrDefault();
    }

    public IEnumerable<Element> AsEnumerable()
    {
        return Execute();
    }

    public ICollection<ElementId> GetElementIds()
    {
        if (_postFilters.Count == 0)
            return BuildCollector().ToElementIds();
        return Execute().Select(e => e.Id).ToList();
    }

    #endregion

    #region Internal

    private FilteredElementCollector BuildCollector()
    {
        var collector = _viewId is not null
            ? new FilteredElementCollector(_doc, _viewId)
            : new FilteredElementCollector(_doc);

        if (_category.HasValue)
            collector.OfCategory(_category.Value);
        if (_elementClass is not null)
            collector.OfClass(_elementClass);

        if (_excludeTypes == true)
            collector.WhereElementIsNotElementType();
        else if (_excludeTypes == false)
            collector.WhereElementIsElementType();

        if (_excludeIds is not null && _excludeIds.Count > 0)
            collector.WherePasses(new ExclusionFilter(_excludeIds));

        foreach (var filter in _nativeFilters)
            collector.WherePasses(filter);

        return collector;
    }

    private IEnumerable<Element> Execute()
    {
        var collector = BuildCollector();

        if (_postFilters.Count == 0)
            return collector.ToElements();

        IEnumerable<Element> result = collector.ToElements();
        foreach (var predicate in _postFilters)
            result = result.Where(predicate);
        return result;
    }

    private List<BoundingBoxXYZ> FindRoomBoundingBoxes(string roomName)
    {
        var rooms = new FilteredElementCollector(_doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Where(r => r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                        ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        if (rooms.Count == 0)
        {
            rooms = new FilteredElementCollector(_doc)
                .OfCategory(BuiltInCategory.OST_MEPSpaces)
                .WhereElementIsNotElementType()
                .Where(s => s.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString()
                            ?.Contains(roomName, StringComparison.OrdinalIgnoreCase) == true)
                .ToList();
        }

        return rooms
            .Select(r => r.get_BoundingBox(null))
            .Where(bb => bb is not null)
            .ToList()!;
    }

    private static string ExtractNumber(string s)
    {
        var num = "";
        foreach (var c in s)
        {
            if (char.IsDigit(c) || c == '.' || c == '-') num += c;
            else if (num.Length > 0) break;
        }
        return num;
    }

    #endregion

    #region Operator parsing

    public static FilterOperator ParseOperator(string op) => op.ToLowerInvariant() switch
    {
        "equals" or "=" or "==" => FilterOperator.Equals,
        "not_equals" or "!=" or "<>" => FilterOperator.NotEquals,
        "contains" => FilterOperator.Contains,
        "not_contains" => FilterOperator.NotContains,
        "begins_with" or "starts_with" => FilterOperator.BeginsWith,
        "ends_with" => FilterOperator.EndsWith,
        "greater_than" or ">" or "gt" => FilterOperator.Greater,
        "greater_equal" or ">=" or "gte" => FilterOperator.GreaterOrEqual,
        "less_than" or "<" or "lt" => FilterOperator.Less,
        "less_equal" or "<=" or "lte" => FilterOperator.LessOrEqual,
        _ => FilterOperator.Contains
    };

    #endregion
}

public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    BeginsWith,
    EndsWith,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual
}
