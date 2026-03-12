using Autodesk.Revit.DB;
using RevitChatBot.Core.Skills;

namespace RevitChatBot.MEP.Skills.Modify;

[Skill("load_family",
    "Load a Revit family (.rfa) into the current project from a known path or the default " +
    "family library. If the family already exists, optionally overwrite. " +
    "Returns the loaded family name and its types.")]
[SkillParameter("family_name", "string",
    "Family name to search for (e.g. 'FCU-600', 'Round Duct'). Partial match in library.",
    isRequired: true)]
[SkillParameter("family_path", "string",
    "Full path to .rfa file. If omitted, searches in default Revit library folders.",
    isRequired: false)]
[SkillParameter("overwrite", "string",
    "'true' to overwrite if family already exists. Default: 'false'.",
    isRequired: false)]
public class LoadFamilySkill : ISkill
{
    public async Task<SkillResult> ExecuteAsync(
        SkillContext context,
        Dictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        if (context.RevitApiInvoker is null)
            return SkillResult.Fail("Revit API not available.");

        var familyName = parameters.GetValueOrDefault("family_name")?.ToString();
        if (string.IsNullOrWhiteSpace(familyName))
            return SkillResult.Fail("'family_name' is required.");

        var familyPath = parameters.GetValueOrDefault("family_path")?.ToString();
        var overwrite = parameters.GetValueOrDefault("overwrite")?.ToString()?.ToLower() == "true";

        var result = await context.RevitApiInvoker(doc =>
        {
            var document = (Document)doc;

            var existing = new FilteredElementCollector(document)
                .OfClass(typeof(Autodesk.Revit.DB.Family))
                .Cast<Autodesk.Revit.DB.Family>()
                .FirstOrDefault(f => f.Name.Contains(familyName!, StringComparison.OrdinalIgnoreCase));

            if (existing is not null && !overwrite)
            {
                var types = GetFamilyTypes(document, existing);
                return new
                {
                    status = "already_loaded",
                    familyName = existing.Name,
                    familyId = existing.Id.Value,
                    types,
                    message = $"Family '{existing.Name}' already exists. Set overwrite=true to reload."
                };
            }

            string? resolvedPath = familyPath;
            if (string.IsNullOrWhiteSpace(resolvedPath))
                resolvedPath = SearchFamilyInLibrary(familyName!);

            if (string.IsNullOrWhiteSpace(resolvedPath) || !System.IO.File.Exists(resolvedPath))
                return new { status = "not_found", familyName, message = $"Family file not found. Provide explicit 'family_path'." };

            Autodesk.Revit.DB.Family? loadedFamily = null;
            using var tx = new Transaction(document, "Load family");
            tx.Start();
            try
            {
                if (document.LoadFamily(resolvedPath, new FamilyLoadOptions(overwrite), out loadedFamily))
                {
                    tx.Commit();
                    var types = loadedFamily is not null ? GetFamilyTypes(document, loadedFamily) : [];
                    return new
                    {
                        status = "loaded",
                        familyName = loadedFamily?.Name ?? familyName,
                        familyId = loadedFamily?.Id.Value ?? -1L,
                        types,
                        path = resolvedPath
                    };
                }

                tx.RollBack();
                return new { status = "failed", familyName, message = "LoadFamily returned false." };
            }
            catch (Exception ex)
            {
                if (tx.HasStarted()) tx.RollBack();
                return new { status = "error", familyName, message = ex.Message };
            }
        });

        dynamic res = result!;
        var status = res.status?.ToString() ?? "unknown";
        return status switch
        {
            "loaded" => SkillResult.Ok($"Family '{res.familyName}' loaded successfully.", result),
            "already_loaded" => SkillResult.Ok($"Family '{res.familyName}' already in project.", result),
            _ => SkillResult.Fail(res.message?.ToString() ?? "Load failed.")
        };
    }

    private static List<string> GetFamilyTypes(Document doc, Autodesk.Revit.DB.Family family)
    {
        return family.GetFamilySymbolIds()
            .Select(id => doc.GetElement(id)?.Name ?? "")
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    private static string? SearchFamilyInLibrary(string name)
    {
        var searchPaths = new[]
        {
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.CommonApplicationData) +
                @"\Autodesk\RVT 2025\Libraries",
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData) +
                @"\Autodesk\Revit\Addins\2025\Libraries",
        };

        foreach (var basePath in searchPaths)
        {
            if (!System.IO.Directory.Exists(basePath)) continue;
            try
            {
                var files = System.IO.Directory.GetFiles(basePath, "*.rfa", System.IO.SearchOption.AllDirectories);
                var match = files.FirstOrDefault(f =>
                    System.IO.Path.GetFileNameWithoutExtension(f).Contains(name, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
            catch { }
        }
        return null;
    }

    private class FamilyLoadOptions(bool overwrite) : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = overwrite;
            return overwrite;
        }

        public bool OnSharedFamilyFound(Autodesk.Revit.DB.Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = overwrite;
            return overwrite;
        }
    }
}
