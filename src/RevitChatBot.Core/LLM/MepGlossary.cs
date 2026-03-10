namespace RevitChatBot.Core.LLM;

/// <summary>
/// Bilingual MEP terminology mapping (Vietnamese ↔ English).
/// Used by QueryPreprocessor to normalize user input before LLM processing.
/// </summary>
public static class MepGlossary
{
    private static readonly Dictionary<string, string> ViToEn = new(StringComparer.OrdinalIgnoreCase)
    {
        // Categories
        ["ống gió"] = "duct",
        ["ong gio"] = "duct",
        ["ống nước"] = "pipe",
        ["ong nuoc"] = "pipe",
        ["ống lạnh"] = "chilled_water_pipe",
        ["ong lanh"] = "chilled_water_pipe",
        ["ống nóng"] = "hot_water_pipe",
        ["ống thoát nước"] = "drainage_pipe",
        ["ống thoát"] = "drainage_pipe",
        ["ống cấp nước"] = "supply_pipe",
        ["máng cáp"] = "cable_tray",
        ["mang cap"] = "cable_tray",
        ["ống luồn"] = "conduit",
        ["ong luon"] = "conduit",
        ["ống luồn dây"] = "conduit",
        ["thiết bị"] = "equipment",
        ["thiet bi"] = "equipment",
        ["thiết bị cơ"] = "mechanical_equipment",
        ["thiết bị điện"] = "electrical_equipment",
        ["đèn"] = "lighting",
        ["sprinkler"] = "sprinkler",
        ["đầu phun"] = "sprinkler",

        // Fittings
        ["co"] = "elbow",
        ["tê"] = "tee",
        ["chữ thập"] = "cross",
        ["chuyển tiếp"] = "transition",
        ["thu nhỏ"] = "reducer",
        ["phụ kiện"] = "fitting",
        ["khớp nối"] = "union",

        // Systems
        ["hệ thống"] = "system",
        ["hệ thống lạnh"] = "chilled_water_system",
        ["cấp lạnh"] = "chilled_water",
        ["cấp nóng"] = "hot_water",
        ["thoát nước"] = "sanitary",
        ["thông gió"] = "ventilation",
        ["cấp gió"] = "supply_air",
        ["hồi gió"] = "return_air",
        ["hút gió"] = "exhaust_air",
        ["gió tươi"] = "fresh_air",
        ["chữa cháy"] = "fire_protection",
        ["pccc"] = "fire_protection",
        ["PCCC"] = "fire_protection",

        // Actions / Checks
        ["bảo ôn"] = "insulation",
        ["bao on"] = "insulation",
        ["cách nhiệt"] = "insulation",
        ["cach nhiet"] = "insulation",
        ["va chạm"] = "clash",
        ["va cham"] = "clash",
        ["xung đột"] = "clash",
        ["van chống cháy"] = "fire_damper",
        ["van chong chay"] = "fire_damper",
        ["kiểm tra"] = "check",
        ["kiem tra"] = "check",
        ["đếm"] = "count",
        ["dem"] = "count",
        ["liệt kê"] = "list",
        ["liet ke"] = "list",
        ["tìm"] = "find",
        ["tim"] = "find",
        ["tạo"] = "create",
        ["tao"] = "create",
        ["sửa"] = "modify",
        ["sua"] = "modify",
        ["thay đổi"] = "modify",
        ["xóa"] = "delete",
        ["xoa"] = "delete",
        ["tính toán"] = "calculate",
        ["tinh toan"] = "calculate",
        ["phân tích"] = "analyze",
        ["phan tich"] = "analyze",
        ["báo cáo"] = "report",
        ["bao cao"] = "report",
        ["xuất"] = "export",
        ["xuat"] = "export",
        ["chia"] = "split",
        ["cắt"] = "split",
        ["nối"] = "connect",
        ["kết nối"] = "connect",
        ["ngắt kết nối"] = "disconnect",

        // Properties
        ["vận tốc"] = "velocity",
        ["van toc"] = "velocity",
        ["tốc độ"] = "velocity",
        ["toc do"] = "velocity",
        ["độ dốc"] = "slope",
        ["do doc"] = "slope",
        ["áp suất"] = "pressure",
        ["ap suat"] = "pressure",
        ["lưu lượng"] = "flow_rate",
        ["luu luong"] = "flow_rate",
        ["kích thước"] = "size",
        ["kich thuoc"] = "size",
        ["đường kính"] = "diameter",
        ["duong kinh"] = "diameter",
        ["chiều dài"] = "length",
        ["chieu dai"] = "length",
        ["cao độ"] = "elevation",
        ["cao do"] = "elevation",
        ["diện tích"] = "area",
        ["dien tich"] = "area",

        // Spatial
        ["phòng"] = "room",
        ["phong"] = "room",
        ["không gian"] = "space",
        ["khong gian"] = "space",
        ["tầng"] = "level",
        ["tang"] = "level",
        ["sàn"] = "floor",
        ["san"] = "floor",
        ["tường"] = "wall",
        ["tuong"] = "wall",
        ["trần"] = "ceiling",
        ["tran"] = "ceiling",

        // Misc
        ["dự án"] = "project",
        ["du an"] = "project",
        ["mô hình"] = "model",
        ["mo hinh"] = "model",
        ["tiêu chuẩn"] = "standard",
        ["tieu chuan"] = "standard",
        ["tuyến ống"] = "routing",
        ["tuyen ong"] = "routing",
        ["đồ thị"] = "graph",
        ["mạng lưới"] = "network",
        ["mang luoi"] = "network",
    };

    private static readonly Dictionary<string, string> CategoryMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["duct"] = "duct", ["ống gió"] = "duct", ["ong gio"] = "duct",
        ["pipe"] = "pipe", ["ống nước"] = "pipe", ["ong nuoc"] = "pipe",
        ["conduit"] = "conduit", ["ống luồn"] = "conduit",
        ["cable_tray"] = "cable_tray", ["cable tray"] = "cable_tray", ["máng cáp"] = "cable_tray",
        ["equipment"] = "equipment", ["thiết bị"] = "equipment",
        ["sprinkler"] = "sprinkler", ["đầu phun"] = "sprinkler",
        ["fitting"] = "fitting", ["phụ kiện"] = "fitting",
        ["ống lạnh"] = "pipe", ["chilled_water_pipe"] = "pipe",
        ["ống nóng"] = "pipe", ["hot_water_pipe"] = "pipe",
        ["ống thoát"] = "pipe", ["drainage_pipe"] = "pipe",
    };

    private static readonly Dictionary<string, string> IntentMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // Vietnamese
        ["kiểm tra"] = "check", ["kiem tra"] = "check",
        ["đếm"] = "query", ["dem"] = "query",
        ["liệt kê"] = "query", ["liet ke"] = "query",
        ["tìm"] = "query", ["tim"] = "query",
        ["cho xem"] = "query", ["xem"] = "query",
        ["bao nhiêu"] = "query",
        ["tạo"] = "create", ["tao"] = "create",
        ["thêm"] = "create", ["them"] = "create",
        ["sửa"] = "modify", ["sua"] = "modify",
        ["thay đổi"] = "modify", ["đổi"] = "modify",
        ["xóa"] = "delete", ["xoa"] = "delete",
        ["tính"] = "calculate", ["tinh"] = "calculate",
        ["tính toán"] = "calculate",
        ["phân tích"] = "analyze", ["phan tich"] = "analyze",
        ["báo cáo"] = "report", ["bao cao"] = "report",
        ["xuất"] = "export", ["xuat"] = "export",
        ["giải thích"] = "explain", ["giai thich"] = "explain",
        ["chia"] = "modify", ["cắt"] = "modify",
        ["va chạm"] = "check", ["va cham"] = "check",
        ["clash"] = "check",
        ["trace"] = "query", ["traverse"] = "query",
        ["duyệt"] = "query",
        // English
        ["check"] = "check", ["verify"] = "check", ["validate"] = "check",
        ["count"] = "query", ["list"] = "query", ["show"] = "query",
        ["find"] = "query", ["get"] = "query", ["query"] = "query",
        ["how many"] = "query", ["what"] = "query",
        ["create"] = "create", ["add"] = "create", ["make"] = "create",
        ["modify"] = "modify", ["change"] = "modify", ["set"] = "modify", ["update"] = "modify",
        ["delete"] = "delete", ["remove"] = "delete",
        ["calculate"] = "calculate", ["size"] = "calculate", ["compute"] = "calculate",
        ["analyze"] = "analyze", ["analysis"] = "analyze",
        ["report"] = "report", ["export"] = "export",
        ["explain"] = "explain", ["why"] = "explain", ["how"] = "explain",
        ["split"] = "modify", ["divide"] = "modify",
    };

    /// <summary>
    /// Normalize Vietnamese text to English technical terms.
    /// </summary>
    public static string NormalizeQuery(string query)
    {
        var result = query;
        foreach (var (vi, en) in ViToEn.OrderByDescending(kv => kv.Key.Length))
        {
            if (result.Contains(vi, StringComparison.OrdinalIgnoreCase))
                result = result.Replace(vi, en, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    /// <summary>
    /// Detect the primary intent from user query.
    /// </summary>
    public static string DetectIntent(string query)
    {
        var lower = query.ToLowerInvariant();
        foreach (var (keyword, intent) in IntentMap.OrderByDescending(kv => kv.Key.Length))
        {
            if (lower.Contains(keyword))
                return intent;
        }
        return "query";
    }

    /// <summary>
    /// Extract MEP category from query text.
    /// </summary>
    public static string? DetectCategory(string query)
    {
        var lower = query.ToLowerInvariant();
        foreach (var (keyword, category) in CategoryMap.OrderByDescending(kv => kv.Key.Length))
        {
            if (lower.Contains(keyword))
                return category;
        }
        return null;
    }

    /// <summary>
    /// Detect language (vi or en).
    /// </summary>
    public static string DetectLanguage(string text)
    {
        var viMarkers = new[] {
            "ống", "kiểm", "tra", "bảo", "tầng", "thiết", "bị", "phòng",
            "không", "gian", "lưu", "lượng", "vận", "tốc", "đường", "kính",
            "chạm", "dốc", "đếm", "liệt", "kê", "tạo", "sửa", "xóa",
            "tìm", "phân", "tích", "giải", "thích", "ống gió", "ống nước",
            "à", "ạ", "ơi", "nè", "nhé", "nha", "đi", "hãy", "được"
        };
        int viCount = viMarkers.Count(m =>
            text.Contains(m, StringComparison.OrdinalIgnoreCase));
        return viCount >= 2 ? "vi" : "en";
    }

    /// <summary>
    /// Extract level name from query (e.g., "tầng 2" → "Level 2", "level 3" → "Level 3").
    /// </summary>
    public static string? ExtractLevel(string query)
    {
        var patterns = new[]
        {
            (@"(?:tầng|tang|level|lvl)\s*(\d+)", "Level $1"),
            (@"(?:tầng|tang|level)\s+([\w\s]+?)(?:\s|$|,)", "Level $1"),
        };

        foreach (var (pattern, replacement) in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                query, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var levelNum = match.Groups[1].Value.Trim();
                return $"Level {levelNum}";
            }
        }
        return null;
    }

    /// <summary>
    /// Extract element IDs from query text.
    /// </summary>
    public static List<long> ExtractElementIds(string query)
    {
        var ids = new List<long>();
        var matches = System.Text.RegularExpressions.Regex.Matches(
            query, @"(?:id|ID|Id)\s*[:\s]*(\d{4,})",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (long.TryParse(m.Groups[1].Value, out var id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>
    /// Detect system type from query.
    /// </summary>
    public static string? DetectSystemType(string query)
    {
        var lower = query.ToLowerInvariant();
        if (lower.Contains("lạnh") || lower.Contains("lanh") || lower.Contains("chw") || lower.Contains("chilled"))
            return "ChilledWater";
        if (lower.Contains("nóng") || lower.Contains("nong") || lower.Contains("hw") || lower.Contains("hot water"))
            return "HotWater";
        if (lower.Contains("thoát") || lower.Contains("thoat") || lower.Contains("san") || lower.Contains("sanitary"))
            return "Sanitary";
        if (lower.Contains("cấp gió") || lower.Contains("supply air") || lower.Contains("sa "))
            return "SupplyAir";
        if (lower.Contains("hồi gió") || lower.Contains("return air") || lower.Contains("ra "))
            return "ReturnAir";
        if (lower.Contains("hút gió") || lower.Contains("exhaust") || lower.Contains("ea "))
            return "ExhaustAir";
        if (lower.Contains("pccc") || lower.Contains("chữa cháy") || lower.Contains("fire"))
            return "FireProtection";
        return null;
    }
}
