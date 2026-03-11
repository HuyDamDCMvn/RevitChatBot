using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Report;

/// <summary>
/// Generates RFI (Request for Information) documents from detected model issues.
/// Takes clashes, violations, or manual observations and formats them as
/// structured RFI data with location, description, and impact assessment.
/// </summary>
[Skill("generate_rfi",
    "Generate an RFI (Request for Information) from model issues. Takes clashes, " +
    "violations, or observations and formats them as structured RFI data with " +
    "location, description, impact assessment, and suggested resolution.")]
[SkillParameter("issue_description", "string",
    "Description of the issue requiring clarification.",
    isRequired: true)]
[SkillParameter("element_ids", "string",
    "Comma-separated element IDs related to the issue (optional).",
    isRequired: false)]
[SkillParameter("discipline", "string",
    "'mechanical', 'electrical', 'plumbing', 'fire_protection', 'structural'. Default: 'mechanical'.",
    isRequired: false)]
[SkillParameter("priority", "string",
    "'high', 'medium', 'low'. Default: 'medium'.",
    isRequired: false, allowedValues: new[] { "high", "medium", "low" })]
[SkillParameter("rfi_to", "string",
    "Recipient discipline or party for the RFI (e.g. 'Structural Engineer').",
    isRequired: false)]
public class GenerateRfiSkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        var issueDesc = parameters.GetValueOrDefault("issue_description")?.ToString() ?? "";
        var elementIdsStr = parameters.GetValueOrDefault("element_ids")?.ToString() ?? "";
        var discipline = parameters.GetValueOrDefault("discipline")?.ToString() ?? "mechanical";
        var priority = parameters.GetValueOrDefault("priority")?.ToString() ?? "medium";
        var rfiTo = parameters.GetValueOrDefault("rfi_to")?.ToString() ?? "Design Team";

        if (string.IsNullOrWhiteSpace(issueDesc))
            return SkillResult.Fail("issue_description is required.");

        var elementDetails = new List<object>();

        if (context.RevitApiInvoker is not null && !string.IsNullOrWhiteSpace(elementIdsStr))
        {
            var detailsResult = await context.RevitApiInvoker(doc =>
            {
                var document = (Document)doc;
                var ids = elementIdsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var details = new List<object>();

                foreach (var idStr in ids)
                {
                    if (!long.TryParse(idStr, out var idVal)) continue;
                    var elem = document.GetElement(new ElementId(idVal));
                    if (elem is null) continue;

                    var lvlId = elem.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? elem.LevelId;
                    var lvlName = lvlId is not null && lvlId != ElementId.InvalidElementId
                        ? document.GetElement(lvlId)?.Name ?? "N/A" : "N/A";

                    var loc = elem.Location;
                    string locationDesc = "N/A";
                    if (loc is LocationPoint lp)
                        locationDesc = $"({lp.Point.X * 304.8:F0}, {lp.Point.Y * 304.8:F0}, {lp.Point.Z * 304.8:F0}) mm";
                    else if (loc is LocationCurve lc)
                    {
                        var mid = lc.Curve.Evaluate(0.5, true);
                        locationDesc = $"({mid.X * 304.8:F0}, {mid.Y * 304.8:F0}, {mid.Z * 304.8:F0}) mm";
                    }

                    details.Add(new
                    {
                        elementId = idVal,
                        category = elem.Category?.Name ?? "N/A",
                        family = elem.get_Parameter(BuiltInParameter.ELEM_FAMILY_PARAM)?.AsValueString() ?? "N/A",
                        type = elem.get_Parameter(BuiltInParameter.ELEM_TYPE_PARAM)?.AsValueString() ?? "N/A",
                        level = lvlName,
                        location = locationDesc,
                        mark = elem.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? ""
                    });
                }

                return details;
            });

            if (detailsResult is List<object> list)
                elementDetails = list;
        }

        var rfiNumber = $"RFI-{DateTime.Now:yyyyMMdd}-{DateTime.Now:HHmmss}";

        var rfi = new
        {
            rfiNumber,
            date = DateTime.Now.ToString("yyyy-MM-dd"),
            from = $"MEP ({discipline})",
            to = rfiTo,
            priority,
            subject = $"[{discipline.ToUpper()}] {Truncate(issueDesc, 80)}",
            issueDescription = issueDesc,
            affectedElements = elementDetails,
            impactAssessment = priority switch
            {
                "high" => "May impact construction schedule or require design revision.",
                "medium" => "Requires clarification before proceeding with coordination.",
                _ => "Minor clarification needed."
            },
            requestedAction = "Please review and provide clarification or revised design intent.",
            responseRequiredBy = DateTime.Now.AddDays(priority == "high" ? 3 : priority == "medium" ? 7 : 14)
                .ToString("yyyy-MM-dd")
        };

        var formatted = $"""
            ## {rfi.rfiNumber}
            **Date:** {rfi.date}
            **From:** {rfi.from}
            **To:** {rfi.to}
            **Priority:** {rfi.priority.ToUpper()}
            **Subject:** {rfi.subject}
            
            ### Issue Description
            {rfi.issueDescription}
            
            ### Affected Elements
            {(elementDetails.Count > 0
                ? string.Join("\n", elementDetails.Select(e => $"  - Element {((dynamic)e).elementId}: {((dynamic)e).category} at {((dynamic)e).level}"))
                : "  (No specific elements referenced)")}
            
            ### Impact Assessment
            {rfi.impactAssessment}
            
            ### Requested Action
            {rfi.requestedAction}
            
            **Response Required By:** {rfi.responseRequiredBy}
            """;

        return SkillResult.Ok(formatted, rfi);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
