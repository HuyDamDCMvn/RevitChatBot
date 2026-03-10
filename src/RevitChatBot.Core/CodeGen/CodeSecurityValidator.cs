using System.Text.RegularExpressions;

namespace RevitChatBot.Core.CodeGen;

/// <summary>
/// Validates generated C# code before compilation to prevent dangerous operations.
/// Uses a whitelist approach: only known-safe namespaces and APIs are permitted.
/// </summary>
public static partial class CodeSecurityValidator
{
    private static readonly HashSet<string> BlockedPatterns =
    [
        "System.IO.File.Delete",
        "System.IO.Directory.Delete",
        "System.IO.File.Move",
        "System.Diagnostics.Process",
        "System.Net.Http",
        "System.Net.WebClient",
        "System.Net.Sockets",
        "System.Runtime.InteropServices",
        "System.Reflection.Emit",
        "System.Environment.Exit",
        "Microsoft.Win32.Registry",
        "System.Security",
        "System.AppDomain",
        "Assembly.Load",
        "DllImport",
        "extern ",
        "unsafe ",
        "stackalloc",
        "fixed (",
        "Marshal.",
        "Process.Start",
        "ProcessStartInfo",
        "Shell32",
        "WScript",
        "PowerShell",
        "cmd.exe"
    ];

    private static readonly HashSet<string> AllowedNamespaces =
    [
        "System",
        "System.Linq",
        "System.Text",
        "System.Collections.Generic",
        "System.Collections",
        "Autodesk.Revit.DB",
        "Autodesk.Revit.DB.Mechanical",
        "Autodesk.Revit.DB.Plumbing",
        "Autodesk.Revit.DB.Electrical",
        "Autodesk.Revit.DB.Architecture",
        "Autodesk.Revit.DB.Structure",
        "Autodesk.Revit.DB.Analysis",
        "Autodesk.Revit.Creation"
    ];

    public static SecurityValidationResult Validate(string code)
    {
        var result = new SecurityValidationResult();

        foreach (var pattern in BlockedPatterns)
        {
            if (code.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                result.Violations.Add($"Blocked API usage: '{pattern}'");
            }
        }

        var usingMatches = UsingPattern().Matches(code);
        foreach (Match match in usingMatches)
        {
            var ns = match.Groups[1].Value.Trim().TrimEnd(';');
            if (!IsNamespaceAllowed(ns))
            {
                result.Violations.Add($"Disallowed namespace: 'using {ns}'");
            }
        }

        if (FileWritePattern().IsMatch(code))
        {
            result.Violations.Add("File write operations are not permitted");
        }

        if (NetworkPattern().IsMatch(code))
        {
            result.Violations.Add("Network operations are not permitted");
        }

        result.IsValid = result.Violations.Count == 0;
        return result;
    }

    private static bool IsNamespaceAllowed(string ns)
    {
        if (ns.StartsWith("static "))
            ns = ns["static ".Length..];

        return AllowedNamespaces.Any(allowed =>
            ns == allowed || ns.StartsWith(allowed + "."));
    }

    [GeneratedRegex(@"using\s+([\w.]+\s*[\w.]*);", RegexOptions.Multiline)]
    private static partial Regex UsingPattern();

    [GeneratedRegex(@"File\.(Write|Append|Create|Copy|Move|Replace)", RegexOptions.IgnoreCase)]
    private static partial Regex FileWritePattern();

    [GeneratedRegex(@"(HttpClient|WebRequest|TcpClient|UdpClient|Socket\b)", RegexOptions.IgnoreCase)]
    private static partial Regex NetworkPattern();
}

public class SecurityValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Violations { get; set; } = [];
}
