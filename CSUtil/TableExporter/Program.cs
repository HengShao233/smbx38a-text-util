using System.Text;
using System.Text.Json;

namespace TableExporter;

/// <summary>命令行入口。</summary>
public static class Program
{
    private static bool RegisterGbkProvider()
    {
        try
        {
            Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[warn] 注册 GBK 代码页失败: {ex.Message}");
            return false;
        }
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // 注册 CodePages（GBK / gb2312 等 ANSI 代码页），供 lvl 脚本按 GBK 字节编 base64 使用。
        // 注意：类型全名为 System.Text.CodePagesEncodingProvider（命名空间 System.Text），
        // 与 System.Text.Encoding 类同处一个命名空间，可直接引用。
        if (!RegisterGbkProvider())
        {
            Console.Error.WriteLine("[warn] 注册 GBK 代码页失败，lvl 脚本中文可能无法按 GBK 编 base64。");
        }

        try
        {
            var options = CliOptions.Parse(args);

            if (options.ShowHelp)
            {
                CliOptions.PrintUsage();
                return 0;
            }

            if (options.RunSelfTest)
            {
                return SelfTest.Run() ? 0 : 1;
            }

            if (options.Batch)
            {
                return BatchExporter.Run(BuildBatchOptions(options));
            }

            return Export(options);
        }
        catch (ExportException ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"未预期的错误: {ex}");
            return 2;
        }
    }

    private static int Export(CliOptions options)
    {
        var inputs = ResolveInputs(options);
        if (inputs.Count == 0)
        {
            throw new ExportException("未找到任何输入表文件");
        }

        var outDir = options.OutputDir ?? "out";
        Directory.CreateDirectory(outDir);

        var diag = new Diagnostics();

        // 只有确实存在 text 列时才去定位 FontAtlasGenerator，避免无谓的硬依赖。
        var bridge = new Lazy<FontAtlasBridge>(
            () => FontAtlasBridge.Locate(options.FontAtlasPath));

        foreach (var input in inputs)
        {
            Console.WriteLine($"==> 处理 {Path.GetFileName(input)}");

            var table = TableReader.Read(input, diag);
            diag.FlushWarnings(Console.Out);

            // 构建完美哈希：表大小取大于行数两倍的质数，留出足够空槽。
            var ids = table.Rows.Select(r => r.Id).ToList();
            var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
            var hash = PerfectHash.Build(ids, table.Name, tableSize);

            // id -> 1-based 行号（供 ref 字段解析目标行）。
            var idToRow = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < table.Rows.Count; i++)
            {
                idToRow.TryAdd(table.Rows[i].Id, i + 1);
            }

            bool hasText = table.Fields.Any(f => f.Type == FieldType.Text);

            string TextResolver(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return "";
                if (!hasText) return raw; // 无 text 列时不会进入此分支
                // --font-config 未指定时，FontAtlasBridge 自动使用空配置（仅转码模式，不需要字体图集）
                Console.WriteLine($"    调用 FontAtlasGenerator 转码文本: {raw}");
                return bridge.Value.TranscodeOne(raw, options.FontConfigPath);
            }

            int RefResolver(string raw)
            {
                if (string.IsNullOrEmpty(raw)) return 0;
                return idToRow.TryGetValue(raw, out var row) ? row : 0;
            }

            var packed = DataPacker.Pack(table, hash, TextResolver, RefResolver);
            diag.FlushWarnings(Console.Out);

            var emitter = new TeaScriptEmitter(packed, options.Prefix);
            var script = emitter.Emit();

            var outName = (options.Prefix ?? TeaScriptEmitter.ToPascal(table.Name)).ToLowerInvariant();
            var outPath = Path.Combine(outDir, $"{outName}_table.smt");
            File.WriteAllText(outPath, script, new UTF8Encoding(false));

            Console.WriteLine(
                $"    行数 {packed.Table.Rows.Count}, 数据块 {packed.DataChunks.Count}, " +
                $"{hash.Describe()}");
            Console.WriteLine($"    -> {outPath}");
        }

        diag.ThrowIfError();
        Console.WriteLine();
        Console.WriteLine("导表完成。");
        return 0;
    }

    /// <summary>由解析后的命令行参数构建批处理选项：命令行优先，其次读取批处理配置文件。</summary>
    private static BatchOptions BuildBatchOptions(CliOptions o)
    {
        var cfgPath = o.BatchConfigPath ?? CliOptions.FindDefaultCfg();
        if (cfgPath is not null && File.Exists(cfgPath))
        {
            CliOptions.LoadBatchFromConfig(o, cfgPath);
        }

        return new BatchOptions
        {
            // 多目录支持：单个路径串里可用 "|" 分隔多个目录（命令行与 JSON 配置均可）
            TableDirs = CliOptions.SplitPaths(o.TableDir),
            LvlDirs = CliOptions.SplitPaths(o.LvlDir),
            OutputDir = o.OutputDir ?? "out",
            FontAtlasPath = o.FontAtlasPath,
            FontConfigPath = o.FontConfigPath
                ?? (cfgPath is not null && File.Exists(cfgPath) ? cfgPath : null),
            TargetScriptName = o.TargetScriptName ?? "TableExport",
            TxtDecoderScriptName = o.TxtDecoderScriptName ?? "TxtDecoder",
            DepsDir = o.DepsDir,
            CommonUtilsDir = o.CommonUtilsDir,
        };
    }

    /// <summary>展开输入路径（支持目录与通配符）。</summary>
    private static List<string> ResolveInputs(CliOptions options)
    {
        var result = new List<string>();

        foreach (var raw in options.Inputs)
        {
            if (Directory.Exists(raw))
            {
                result.AddRange(Directory
                    .EnumerateFiles(raw)
                    .Where(IsSupportedTable));
                continue;
            }

            if (File.Exists(raw))
            {
                result.Add(raw);
                continue;
            }

            // 尝试按通配符解释。
            var dir = Path.GetDirectoryName(raw);
            var pattern = Path.GetFileName(raw);
            dir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;

            if (Directory.Exists(dir) && pattern.Contains('*'))
            {
                result.AddRange(Directory
                    .EnumerateFiles(dir, pattern)
                    .Where(IsSupportedTable));
                continue;
            }

            throw new ExportException($"输入不存在: {raw}");
        }

        return result
            .Where(p => !Path.GetFileName(p).StartsWith('~')) // 排除 Excel 临时文件
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsSupportedTable(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" or ".csv";
    }
}

/// <summary>命令行参数。</summary>
public sealed class CliOptions
{
    public List<string> Inputs { get; } = [];
    public string? OutputDir { get; private set; }
    public string? FontAtlasPath { get; private set; }
    public string? FontConfigPath { get; private set; }
    public string? Prefix { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool RunSelfTest { get; private set; }

    // 批处理相关
    public bool Batch { get; private set; }

    /// <summary>表目录原始取值：可以是单个路径，也可以是 "|" 分隔的多个路径。</summary>
    public string? TableDir { get; private set; }

    /// <summary>lvl 目录原始取值：可以是单个路径，也可以是 "|" 分隔的多个路径。</summary>
    public string? LvlDir { get; private set; }

    public string? BatchConfigPath { get; private set; }
    public string? TargetScriptName { get; private set; }
    public string? DepsDir { get; private set; }
    public string? TxtDecoderScriptName { get; private set; }
    public string? CommonUtilsDir { get; private set; }

    /// <summary>多目录分隔符。</summary>
    public const char PathSeparator = '|';

    /// <summary>
    /// 把 "|" 分隔的路径串解析为路径列表：去除空项与重复项，保留原有顺序，
    /// 相对路径按当前工作目录展开为绝对路径。null / 空白返回空列表。
    /// </summary>
    internal static List<string> SplitPaths(string? raw)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return list;

        foreach (var part in raw.Split(PathSeparator))
        {
            var p = part.Trim().Trim('"');
            if (p.Length == 0) continue;

            string full;
            try
            {
                full = Path.GetFullPath(p);
            }
            catch (Exception)
            {
                // 非法路径（如含 * 等通配符字符）：保留原值，交由后续的存在性检查报错
                full = p;
            }

            if (!list.Contains(full, StringComparer.OrdinalIgnoreCase)) list.Add(full);
        }

        return list;
    }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();

        if (args.Length == 0)
        {
            // 无参数（双击启动）：尝试从同目录 .cfg.json 读取默认配置
            if (TryLoadFromConfig(o))
            {
                return o;
            }

            o.ShowHelp = true;
            return o;
        }

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];

            switch (a)
            {
                case "-h" or "--help" or "/?":
                    o.ShowHelp = true;
                    return o;

                case "--self-test":
                    o.RunSelfTest = true;
                    return o;

                case "-o" or "--out":
                    o.OutputDir = Next(args, ref i, a);
                    break;

                case "--font-atlas":
                    o.FontAtlasPath = Next(args, ref i, a);
                    break;

                case "--font-config":
                    o.FontConfigPath = Next(args, ref i, a);
                    break;

                case "--prefix":
                    o.Prefix = Next(args, ref i, a);
                    break;

                // ---- 批处理相关 ----
                case "--batch":
                    o.Batch = true;
                    break;

                case "--table-dir":
                    o.TableDir = Next(args, ref i, a);
                    break;

                case "--lvl-dir":
                    o.LvlDir = Next(args, ref i, a);
                    break;

                case "--batch-config":
                    o.BatchConfigPath = Next(args, ref i, a);
                    break;

                case "--target-script":
                    o.TargetScriptName = Next(args, ref i, a);
                    break;

                case "--deps-dir":
                    o.DepsDir = Next(args, ref i, a);
                    break;

                case "--txtdecoder-script":
                    o.TxtDecoderScriptName = Next(args, ref i, a);
                    break;

                case "--common-utils-dir":
                    o.CommonUtilsDir = Next(args, ref i, a);
                    break;

                default:
                    if (a.StartsWith('-'))
                    {
                        throw new ExportException($"未知参数: {a}");
                    }

                    o.Inputs.Add(a);
                    break;
            }
        }

        return o;
    }

    /// <summary>
    /// 从 exe 同目录下的 .cfg.json 读取默认配置。
    /// 支持 table (字符串或数组, 文件/目录路径) 和 table-output (输出目录)。
    /// 也支持 font-atlas 和 font-config 的相对路径。
    /// </summary>
    private static bool TryLoadFromConfig(CliOptions o)
    {
        var exeDir = AppContext.BaseDirectory;
        var cfgPath = Path.Combine(exeDir, ".cfg.json");
        if (!File.Exists(cfgPath)) return false;

        string jsonText;
        try
        {
            jsonText = File.ReadAllText(cfgPath);
        }
        catch
        {
            return false;
        }

        // 宽松解析：去掉注释（cfg.json 含 // 注释）
        jsonText = StripJsonComments(jsonText);

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;

        // table: 字符串或数组
        if (root.TryGetProperty("table", out var tableEl))
        {
            if (tableEl.ValueKind == JsonValueKind.String)
            {
                var p = tableEl.GetString()!;
                o.Inputs.Add(ResolvePath(exeDir, p));
            }
            else if (tableEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in tableEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var p = item.GetString()!;
                        o.Inputs.Add(ResolvePath(exeDir, p));
                    }
                }
            }
        }

        // table-output: 输出目录
        if (root.TryGetProperty("table-output", out var outEl) && outEl.ValueKind == JsonValueKind.String)
        {
            o.OutputDir = ResolvePath(exeDir, outEl.GetString()!);
        }

        // font-atlas: FontAtlasGenerator exe 路径
        if (root.TryGetProperty("font-atlas-gen-exe", out var faEl) && faEl.ValueKind == JsonValueKind.String)
        {
            o.FontAtlasPath = ResolvePath(exeDir, faEl.GetString()!);
        }

        // font-config: 字体配置 json 路径（默认就是 .cfg.json 本身）
        o.FontConfigPath ??= cfgPath;

        // prefix
        if (root.TryGetProperty("table-prefix", out var pfEl) && pfEl.ValueKind == JsonValueKind.String)
        {
            o.Prefix = pfEl.GetString();
        }

        return o.Inputs.Count > 0;
    }

    /// <summary>把相对路径解析为相对于 exe 目录的绝对路径。</summary>
    private static string ResolvePath(string baseDir, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDir, path));
    }

    /// <summary>去掉 JSON 中的 // 行注释（FontAtlasGenerator 的 cfg.json 含注释）。</summary>
    internal static string StripJsonComments(string json)
    {
        var sb = new StringBuilder(json.Length);
        bool inString = false;
        bool escaped = false;
        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                sb.Append(c);
                continue;
            }

            // 非字符串内的 // 注释
            if (!inString && c == '/' && i + 1 < json.Length && json[i + 1] == '/')
            {
                // 跳过到行尾
                while (i < json.Length && json[i] != '\n') i++;
                sb.Append('\n');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 查找默认的批处理配置文件（与 exe 同目录或当前目录下的 .cfg.json，
    /// 该文件同时是 FontAtlasGenerator 的字体配置）。返回 null 表示未找到。
    /// </summary>
    internal static string? FindDefaultCfg()
    {
        var exeDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(exeDir, ".cfg.json"),
            Path.Combine(Directory.GetCurrentDirectory(), ".cfg.json"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    /// <summary>
    /// 从批处理配置文件读取 table-dir / lvl-dir 等设置，仅填充当前为 null 的字段
    /// （命令行参数优先）。
    /// </summary>
    internal static void LoadBatchFromConfig(CliOptions o, string cfgPath)
    {
        string json;
        try
        {
            json = File.ReadAllText(cfgPath);
        }
        catch (Exception)
        {
            return;
        }

        json = StripJsonComments(json);

        string? ReadStr(string key)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }

            return null;
        }

        var exeDir = Path.GetDirectoryName(Path.GetFullPath(cfgPath)) ?? ".";
        string? Resolve(string? p) =>
            string.IsNullOrEmpty(p) ? null : (Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(exeDir, p)));

        // 读取目录类配置：支持字符串（可用 "|" 分隔多个目录）或字符串数组；
        // 每项经 Resolve 展开后再用 "|" 拼回原始取值串。
        string? ReadDirs(string key)
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(key, out var el)) return null;

            var parts = new List<string>();
            if (el.ValueKind == JsonValueKind.String)
            {
                var raw = el.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    parts.AddRange(raw.Split(PathSeparator));
                }
            }
            else if (el.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String) parts.Add(item.GetString()!);
                }
            }
            else
            {
                return null;
            }

            var resolved = parts
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .Select(p => Resolve(p))
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            return resolved.Count == 0 ? null : string.Join(PathSeparator, resolved);
        }

        o.TableDir ??= ReadDirs("table-dir");
        o.LvlDir ??= ReadDirs("lvl-dir");
        o.OutputDir ??= Resolve(ReadStr("table-output"));
        o.FontAtlasPath ??= Resolve(ReadStr("font-atlas-gen-exe"));
        o.FontConfigPath ??= Resolve(ReadStr("font-config"));
        o.TargetScriptName ??= ReadStr("target-script");
        o.DepsDir ??= Resolve(ReadStr("deps-dir"));
        o.TxtDecoderScriptName ??= ReadStr("txtdecoder-script");
        o.CommonUtilsDir ??= Resolve(ReadStr("common-utils-dir"));
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ExportException($"参数 {flag} 缺少取值");
        }

        return args[++i];
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            TableExporter - SMBX 38A 配置表导出工具

            用法:
              TableExporter                       双击模式: 读同目录 .cfg.json
              TableExporter <表文件|目录|通配符>... [选项]

            选项:
              -o, --out <dir>          输出目录 (默认 out)
                  --font-atlas <path>  显式指定 FontAtlasGenerator 可执行文件
                  --font-config <path> 字体图集配置 json (默认 .cfg.json)
                  --prefix <name>      覆盖生成脚本的函数名前缀
                  --self-test          运行内置自检
              -h, --help               显示本帮助

            双击模式 (.cfg.json):
              无参数启动时自动读取 exe 同目录的 .cfg.json:
              "table": "npc.csv"         输入表 (字符串或数组, 文件/目录路径)
              "table-output": "./out"    输出目录
              "font-atlas-gen-exe": "..."    FontAtlasGenerator exe (可选)
              "table-prefix": "Npc"      函数名前缀 (可选)

            批处理模式 (递归扫描 + 整合写入 lvl):
              TableExporter --batch
                  --table-dir <dir>   存放 [lvl]-[脚本].xlsx 的目录 (递归扫描子目录)
                  --lvl-dir <dir>     存放 <lvl名>.lvl 的目录
                  --batch-config <p>  批处理配置 json (默认 exe 同目录/.cfg.json)
                  --target-script <n> 写入 lvl 的整合脚本名 (默认 TableExport)
                  --deps-dir <dir>    额外依赖脚本目录 (可选)
                  --txtdecoder-script <n> 写入 lvl 的 TxtDecoder 脚本名 (默认 TxtDecoder)
                  --common-utils-dir <dir> bmp_utils/cumath_utils 脚本目录
                                        (默认 exe 上级 smbx38a-tescript-common-utils)

              多目录: --table-dir / --lvl-dir 可一次指定多个目录, 之间用 '|' 分隔, 如
                  --table-dir "tables|..\shared\tables"
                  --lvl-dir   "levels|..\common\levels"
                  多个表目录会被全部递归扫描并合并 (同一关卡的同名表以首个为准);
                  多个 lvl 目录会按序查找 <lvl名>.lvl, 均未找到时在第一个目录中新建。
              配置 json 字段 (与 .cfg.json 共用, 命令行优先):
                "table-dir", "lvl-dir", "table-output", "target-script",
                "txtdecoder-script", "deps-dir", "common-utils-dir"
                table-dir / lvl-dir 支持两种写法:
                  "table-dir": "tables|..\shared\tables"   // '|' 分隔的字符串
                  "table-dir": ["tables", "..\shared\tables"]  // 字符串数组
              行为: 按 [lvl]-[脚本] 分组 -> 逐表导出 -> 变量加表名前缀后合并
                    为一个整合脚本 -> 写入 <lvl>.lvl (SU 脚本)
                    -> 据扫描到的表自动更新 .cfg.json 的 script 数组
                    -> 关卡内脚本顺序重整:
                       lib/utils 类 -> TxtDecoder -> 表脚本 -> 其它脚本
                       其中"其它脚本"里带 [编号] 的按编号升序排在前, 无编号的保持原序排在其后

            表头格式 (文档三行头):
              第1行: 备注/表名 (可含 [sheetName])
              第2行: 字段名 (如 id, name, hp)
              第3行: 字段类型 (如 s, i, text, i[|], s[;])
              第4行起: 数据行
              以 # 开头的行会被忽略

            支持类型:
              int (i/i32/integer)  - 32位整数, 对应 Long
              str (s/string)       - ASCII 字符串, 对应 String
              text (t/txt)         - 富文本, 需 FontAtlasGenerator 转码
              skip (none/x/comment/remark) - 跳过列: 不导出, 仅保留源表内容用于写备注
              类型后加 [] 为数组, 如 i[] 或 s[;]

            必需列:
              id  - 每行的唯一标识, 作为查表主键
            """);
    }
}
