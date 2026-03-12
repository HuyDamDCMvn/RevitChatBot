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

        // Architectural / Structural elements
        ["cửa"] = "door",
        ["cua"] = "door",
        ["cửa sổ"] = "window",
        ["cua so"] = "window",
        ["lộn ngược"] = "mirrored",
        ["lon nguoc"] = "mirrored",
        ["gương"] = "mirrored",
        ["guong"] = "mirrored",
        ["đảo chiều"] = "mirrored",
        ["bị lật"] = "mirrored",

        // Revision
        ["bản sửa"] = "revision",
        ["phát hành"] = "issued",
        ["đám mây sửa đổi"] = "revision_cloud",

        // Sheet / View
        ["bản vẽ"] = "sheet",
        ["ban ve"] = "sheet",
        ["trang"] = "sheet",
        ["vùng cắt"] = "crop_region",
        ["vung cat"] = "crop_region",
        ["khung nhìn"] = "viewport",

        // Move / Transform
        ["di chuyển"] = "move",
        ["di chuyen"] = "move",
        ["dịch chuyển"] = "move",
        ["dich chuyen"] = "move",
        ["dời"] = "move",
        ["doi"] = "move",
        ["nâng lên"] = "move_up",
        ["hạ xuống"] = "move_down",
        ["offset"] = "offset",

        // Copy / Aggregate
        ["sao chép"] = "copy",
        ["sao chep"] = "copy",
        ["nhân bản"] = "copy",
        ["nhan ban"] = "copy",
        ["thống kê"] = "report",
        ["thong ke"] = "report",
        ["theo category"] = "by_category",
        ["theo loại"] = "by_category",
        ["theo loai"] = "by_category",
        ["phân loại"] = "categorize",
        ["phan loai"] = "categorize",
        ["nhóm theo"] = "group_by",
        ["nhom theo"] = "group_by",
        ["bảng thống kê"] = "schedule",
        ["bang thong ke"] = "schedule",
        ["khoảng trống"] = "clearance",
        ["khong tro"] = "clearance",
        ["thông thủy"] = "clearance",
        ["thong thuy"] = "clearance",
        ["dầm"] = "beam",
        ["dam"] = "beam",
        ["cột"] = "column",
        ["cot"] = "column",

        // Vague / lazy-user
        ["nặng"] = "heavy",
        ["nang"] = "heavy",
        ["tóm tắt"] = "summary",
        ["tom tat"] = "summary",
        ["tổng quan"] = "overview",
        ["tong quan"] = "overview",
        ["ổn"] = "ok",
        ["vấn đề"] = "issue",
        ["van de"] = "issue",
        ["bị sai"] = "error",
        ["bi sai"] = "error",
        ["lỗi"] = "error",
        ["loi"] = "error",
        ["hết"] = "all",
        ["het"] = "all",
        ["tất cả"] = "all",
        ["tat ca"] = "all",

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
        ["tổng"] = "sum",
        ["tong"] = "sum",
        ["trọng tâm"] = "centroid",
        ["trong tam"] = "centroid",
        ["lỗ mở"] = "opening",
        ["lo mo"] = "opening",

        // Load / Mirror / Rotate
        ["tải family"] = "load_family",
        ["tai family"] = "load_family",
        ["nạp family"] = "load_family",
        ["nap family"] = "load_family",
        ["thêm family"] = "load_family",
        ["them family"] = "load_family",
        ["xoay"] = "rotate",
        ["quay"] = "rotate",
        ["đối xứng"] = "mirror",
        ["doi xung"] = "mirror",

        // Model health / coordination
        ["sức khỏe model"] = "model_health",
        ["suc khoe model"] = "model_health",
        ["dung lượng"] = "file_size",
        ["dung luong"] = "file_size",
        ["tiến độ"] = "progress",
        ["tien do"] = "progress",
        ["phần trăm"] = "percent",
        ["phan tram"] = "percent",
        ["view template"] = "view_template",
        ["mẫu view"] = "view_template",
        ["mau view"] = "view_template",
        ["shared parameter"] = "shared_parameter",
        ["tham số chia sẻ"] = "shared_parameter",
        ["tham so chia se"] = "shared_parameter",
        ["coordination"] = "coordination",
        ["phối hợp"] = "coordination",
        ["phoi hop"] = "coordination",

        // Workset / Pin
        ["workset"] = "workset",
        ["ghim"] = "pin",
        ["bỏ ghim"] = "unpin",
        ["bo ghim"] = "unpin",
        ["giai đoạn"] = "phase",
        ["giai doan"] = "phase",
        ["phá dỡ"] = "demolish",
        ["pha do"] = "demolish",

        // View / Annotation
        ["callout"] = "callout",
        ["phóng to"] = "enlarged",
        ["phong to"] = "enlarged",
        ["chi tiết"] = "detail",
        ["chi tiet"] = "detail",
        ["dependent view"] = "dependent_view",
        ["scope box"] = "scope_box",
        ["vùng nhìn"] = "scope_box",
        ["vung nhin"] = "scope_box",
        ["ghi chú"] = "text_note",
        ["ghi chu"] = "text_note",
        ["đám mây"] = "revision_cloud",
        ["dam may"] = "revision_cloud",
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
        ["beam"] = "beam", ["dầm"] = "beam", ["dam"] = "beam",
        ["column"] = "column", ["cột"] = "column", ["cot"] = "column",
        ["wall"] = "wall", ["tường"] = "wall", ["tuong"] = "wall",
        ["floor"] = "floor", ["sàn"] = "floor", ["san"] = "floor",
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
        ["đâm xuyên"] = "check", ["dam xuyen"] = "check",
        ["xuyên qua"] = "check", ["xuyen qua"] = "check",
        ["chạm vào"] = "check", ["cham vao"] = "check",
        ["ổn chưa"] = "check", ["on chua"] = "check",
        ["ok chưa"] = "check", ["ok chua"] = "check",
        ["đúng chưa"] = "check", ["dung chua"] = "check",
        ["đạt chưa"] = "check", ["dat chua"] = "check",
        ["đủ chưa"] = "check", ["du chua"] = "check",
        ["hợp lệ"] = "check", ["hop le"] = "check",
        ["đúng không"] = "check", ["dung khong"] = "check",
        ["có vấn đề"] = "check", ["co van de"] = "check",
        ["lỗi"] = "check", ["sai"] = "check",
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
        ["connect"] = "modify", ["nối"] = "modify", ["kết nối"] = "modify",
        ["disconnect"] = "check", ["ngắt"] = "modify",
        ["select"] = "query", ["similar"] = "query",
        ["mirrored"] = "check", ["mirror"] = "check",
        ["revision"] = "query", ["rev"] = "query",
        ["sheet"] = "query", ["sheets"] = "query",
        ["sum"] = "calculate", ["total"] = "calculate", ["tổng"] = "calculate",
        ["thống kê"] = "report", ["thong ke"] = "report",
        ["theo category"] = "report", ["theo loại"] = "report",
        ["tóm tắt"] = "query", ["tổng quan"] = "query", ["overview"] = "query",
        ["nặng"] = "check", ["heavy"] = "check",
        ["bị sai"] = "check", ["lỗi"] = "check", ["vấn đề"] = "check",
        ["ổn"] = "check", ["ok không"] = "check",
        ["centroid"] = "calculate", ["center"] = "calculate",
        ["crop"] = "modify",
        ["ifc"] = "export", ["nwc"] = "export",
        ["place"] = "modify", ["restore"] = "modify",
        ["save"] = "modify", ["viewport"] = "modify",
        ["purge"] = "delete", ["cleanup"] = "delete", ["dọn"] = "delete",
        ["compare"] = "analyze", ["so sánh"] = "analyze",
        ["render"] = "query", ["hiển thị"] = "query",
        ["viết code"] = "create", ["write code"] = "create",
        ["đánh số"] = "modify", ["danh so"] = "modify",
        ["đổi tên"] = "modify", ["rename"] = "modify",
        ["copy"] = "modify", ["paste"] = "modify",
        ["sao chép"] = "modify", ["sao chep"] = "modify",
        ["nhân bản"] = "modify", ["nhan ban"] = "modify",
        ["schedule"] = "export", ["bảng thống kê"] = "export",
        ["di chuyển"] = "modify", ["di chuyen"] = "modify",
        ["dời"] = "modify", ["nâng"] = "modify", ["hạ"] = "modify",
        ["đổi type"] = "modify", ["doi type"] = "modify",
        ["swap"] = "modify", ["convert"] = "modify",
        ["đo"] = "query", ["measure"] = "query",
        ["khoảng cách"] = "query", ["distance"] = "query",
        ["highlight"] = "query", ["tô màu"] = "modify",
        ["lock"] = "modify", ["ghim"] = "modify",
        ["bind"] = "create", ["gán"] = "modify",
        ["load"] = "create", ["tải"] = "create", ["nạp"] = "create",
        ["rotate"] = "modify", ["xoay"] = "modify", ["quay"] = "modify",
        ["mirror"] = "modify", ["đối xứng"] = "modify", ["lật"] = "modify",
        ["health"] = "check", ["sức khỏe"] = "check",
        ["file size"] = "query", ["dung lượng"] = "query",
        ["warning"] = "check", ["warnings"] = "check",
        ["progress"] = "query", ["tiến độ"] = "query", ["tien do"] = "query",
        ["hoàn thành"] = "query", ["xong chưa"] = "query",
        ["view template"] = "check", ["mẫu view"] = "check",
        ["shared parameter"] = "check", ["tham số chia sẻ"] = "check",
        ["coordination"] = "analyze", ["phối hợp"] = "analyze",
        ["workset"] = "modify", ["chuyển workset"] = "modify",
        ["pin"] = "modify", ["unpin"] = "modify",
        ["ghim"] = "modify", ["bỏ ghim"] = "modify",
        ["phase"] = "query", ["giai đoạn"] = "query",
        ["existing"] = "query", ["demolish"] = "query",
        ["callout"] = "create", ["phóng to"] = "create",
        ["dependent"] = "create",
        ["scope box"] = "modify",
        ["text note"] = "create", ["ghi chú"] = "create",
        ["revision cloud"] = "create", ["đám mây"] = "create",
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
