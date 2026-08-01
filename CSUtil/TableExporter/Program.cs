using System.Text;

namespace TableExporter;

/// <summary>命令行入口。</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

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

        Directory.CreateDirectory(options.OutputDir);

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
                if (string.IsNullOrWhiteSpace(options.FontConfigPath))
                {
                    throw new ExportException(
                        $"[{table.Name}] 存在 text 列，但未通过 --font-config 指定字体图集配置");
                }
                Console.WriteLine($"    调用 FontAtlasGenerator 转码文本: {raw}");
                return bridge.Value.TranscodeOne(raw, options.FontConfigPath!);
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
            var outPath = Path.Combine(options.OutputDir, $"{outName}_table.smt");
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
    public string OutputDir { get; private set; } = "out";
    public string? FontAtlasPath { get; private set; }
    public string? FontConfigPath { get; private set; }
    public string? Prefix { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool RunSelfTest { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();

        if (args.Length == 0)
        {
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
              TableExporter <表文件|目录|通配符>... [选项]

            选项:
              -o, --out <dir>          输出目录 (默认 out)
                  --font-atlas <path>  显式指定 FontAtlasGenerator 可执行文件
                  --font-config <path> 字体图集配置 json (含 text 列时必填)
                  --prefix <name>      覆盖生成脚本的函数名前缀
                  --self-test          运行内置自检
              -h, --help               显示本帮助

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
              类型后加 [] 为数组, 如 i[] 或 s[;]

            必需列:
              id  - 每行的唯一标识, 作为查表主键
            """);
    }
}
