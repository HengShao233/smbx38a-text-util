using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TableExporter;

/// <summary>批处理模式的参数集合。</summary>
public sealed class BatchOptions
{
    public string TableDir = string.Empty;
    public string LvlDir = string.Empty;
    public string OutputDir = "out";
    public string? FontAtlasPath;
    public string? FontConfigPath;   // FontAtlasGenerator 的 .cfg.json 路径
    public string TargetScriptName = "TableExport";
    public string TxtDecoderScriptName = "TxtDecoder";
    public string? DepsDir;
    public string? CommonUtilsDir;
}

/// <summary>
/// 批处理：递归扫描 <c>[lvl名]-[脚本名].xlsx</c> 表文件，按关卡分组，
/// 逐个导出并在每个表的变量名前加表名前缀后合并为整合脚本，写入对应 .lvl，
/// 并据扫描结果更新 FontAtlasGenerator 的 .cfg.json 的 script 数组。
/// </summary>
public static class BatchExporter
{
    // [lvl名]-[脚本名].xlsx / .xlsm / .csv
    private static readonly Regex TableNameRe = new(
        @"^\[(?<lvl>.+?)\]-\[(?<script>.+?)\]\.(xlsx|xlsm|csv)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static int Run(BatchOptions bo)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (string.IsNullOrWhiteSpace(bo.TableDir) || !Directory.Exists(bo.TableDir))
        {
            throw new ExportException($"表目录不存在或未指定: {bo.TableDir}");
        }

        if (string.IsNullOrWhiteSpace(bo.LvlDir) || !Directory.Exists(bo.LvlDir))
        {
            throw new ExportException($"lvl 目录不存在或未指定: {bo.LvlDir}");
        }

        Directory.CreateDirectory(bo.OutputDir);

        // 1) 递归扫描所有 [lvl]-[script].xlsx
        var tableFiles = ScanTables(bo.TableDir);
        if (tableFiles.Count == 0)
        {
            throw new ExportException(
                $"未在 {bo.TableDir} 及其子目录中找到形如 [lvl]-[script].xlsx 的表文件");
        }

        Console.WriteLine($"扫描到 {tableFiles.Count} 个表文件, 分布在 " +
                          $"{tableFiles.Select(t => t.LvlName).Distinct(StringComparer.OrdinalIgnoreCase).Count()} 个关卡。");

        // 2) 按关卡分组
        var groups = tableFiles
            .GroupBy(t => t.LvlName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var diag = new Diagnostics();
        var bridge = new Lazy<FontAtlasBridge>(() => FontAtlasBridge.Locate(bo.FontAtlasPath));

        var generatedScripts = new List<string>();

        foreach (var grp in groups)
        {
            var lvlName = grp.Key;
            Console.WriteLine();
            Console.WriteLine($"==> 处理关卡 [{lvlName}] (表 {grp.Count()})");

            var perTable =
                new List<(string ScriptName, string LvlName, string SourcePath, string PrefixedScript)>();
            var usedPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tf in grp.OrderBy(t => t.ScriptName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    - 表 [{tf.LvlName}]-[{tf.ScriptName}]  <- {Path.GetFileName(tf.FullPath)}");

                var table = TableReader.Read(tf.FullPath, diag, tf.ScriptName);
                diag.FlushWarnings(Console.Out);

                // 构建完美哈希（与单表流程一致）
                var ids = table.Rows.Select(r => r.Id).ToList();
                var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
                var hash = PerfectHash.Build(ids, table.Name, tableSize);

                var idToRow = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    idToRow.TryAdd(table.Rows[i].Id, i + 1);
                }

                bool hasText = table.Fields.Any(f => f.Type == FieldType.Text);

                string TextResolver(string raw)
                {
                    if (string.IsNullOrEmpty(raw)) return string.Empty;
                    if (!hasText) return raw; // 无 text 列时不会进入此分支
                    return bridge.Value.TranscodeOne(raw, bo.FontConfigPath);
                }

                int RefResolver(string raw)
                {
                    if (string.IsNullOrEmpty(raw)) return 0;
                    return idToRow.TryGetValue(raw, out var row) ? row : 0;
                }

                var packed = DataPacker.Pack(table, hash, TextResolver, RefResolver);
                diag.FlushWarnings(Console.Out);

                var emitter = new TeaScriptEmitter(packed, TeaScriptEmitter.ToPascal(table.Name));
                var script = emitter.Emit();

                // 变量加表名前缀，避免合并冲突
                var prefix = ScriptIntegrator.SanitizePrefix(tf.ScriptName);
                var uniquePrefix = prefix;
                int dup = 2;
                while (!usedPrefixes.Add(uniquePrefix))
                {
                    uniquePrefix = prefix + dup++;
                }

                var prefixed = ScriptIntegrator.PrefixTable(script, uniquePrefix);

                var perTablePath = Path.Combine(bo.OutputDir, $"{tf.ScriptName}_table.smt");
                File.WriteAllText(perTablePath, prefixed, new UTF8Encoding(false));

                var relSource = RelativePath(bo.TableDir, tf.FullPath);
                perTable.Add((tf.ScriptName, tf.LvlName, relSource, prefixed));
            }

            // 3) 整合为单个脚本
            var integrated = ScriptIntegrator.Integrate(perTable, bo.TargetScriptName);
            var integratedPath = Path.Combine(bo.OutputDir, $"{lvlName}_table.smt");
            File.WriteAllText(integratedPath, integrated, new UTF8Encoding(false));
            generatedScripts.Add(integratedPath);

            // 4) 写入对应 .lvl
            var lvlPath = Path.Combine(bo.LvlDir, lvlName + ".lvl");
            WriteToLvl(lvlPath, bo, integrated, lvlName);

            var integratedSize = new FileInfo(integratedPath).Length;
            Console.WriteLine($"    -> 整合脚本: {integratedPath} ({integratedSize} 字节)");
            Console.WriteLine($"    -> 写入关卡: {lvlPath}");
        }

        diag.ThrowIfError();

        // 5) 更新 FontAtlasGenerator 的 .cfg.json 的 script 数组
        if (!string.IsNullOrWhiteSpace(bo.FontConfigPath) && File.Exists(bo.FontConfigPath))
        {
            UpdateCfgScriptArray(bo.FontConfigPath, generatedScripts);
        }

        Console.WriteLine();
        Console.WriteLine("批处理完成。");
        return 0;
    }

    /// <summary>递归扫描目录树，匹配 [lvl]-[script].xlsx，返回 (关卡名, 脚本名, 完整路径)。</summary>
    private static List<(string LvlName, string ScriptName, string FullPath)> ScanTables(string dir)
    {
        var result = new List<(string, string, string)>();
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith('~')) continue; // 排除 Excel 临时文件

            var m = TableNameRe.Match(name);
            if (!m.Success) continue;

            var lvl = m.Groups["lvl"].Value.Trim();
            var script = m.Groups["script"].Value.Trim();
            if (lvl.Length == 0 || script.Length == 0) continue;

            result.Add((lvl, script, file));
        }

        return result;
    }

    /// <summary>
    /// 把整合脚本写入 .lvl：先删除同名旧表脚本再新增，注入/补充运行期依赖脚本
    /// （TxtDecoder 始终刷新；bmp_utils / cumath_utils 缺失才补），
    /// 随后按规范重排脚本顺序：lib/utils 类 → TxtDecoder → 表脚本 → 其它游戏逻辑。
    /// </summary>
    private static void WriteToLvl(string lvlPath, BatchOptions bo, string integratedBody, string lvlName)
    {
        LvlFile lvl;
        bool created = false;
        if (File.Exists(lvlPath))
        {
            lvl = LvlFile.Load(lvlPath);
        }
        else
        {
            lvl = LvlFile.CreateMinimal();
            created = true;
            Console.WriteLine($"    [warn] 关卡文件不存在, 已创建最小占位: {lvlPath}");
        }

        var tableScript = bo.TargetScriptName;
        var txtScript = bo.TxtDecoderScriptName;

        // TxtDecoder：同名先删后加（始终刷新到最新）
        InjectScript(lvl, bo, "TxtDecoder.smt", txtScript, replaceAlways: true);

        // bmp_utils / cumath_utils：缺失才自动补充（内容来自 common-utils 目录）
        InjectScript(lvl, bo, "bmp_utils.smt", "bmp_utils", replaceAlways: false);
        InjectScript(lvl, bo, "cumath_utils.smt", "cumath_utils", replaceAlways: false);

        // 表整合脚本：遍历删除同名旧脚本直到彻底干净（F3），再注入
        int removedTable = 0;
        while (lvl.HasScript(tableScript))
        {
            lvl.RemoveScript(tableScript);
            removedTable++;
        }
        if (removedTable > 0)
        {
            Console.WriteLine($"    [info] 删除旧表脚本 {tableScript} 共 {removedTable} 处, 准备重新注入。");
        }
        lvl.SetScript(tableScript, integratedBody, "SU");

        // 脚本顺序重整（lib/utils → TxtDecoder → 表脚本 → 其它）
        lvl.ReorderScripts(tableScript, txtScript);

        lvl.Save(lvlPath);

        if (created)
        {
            Console.WriteLine("    [warn] 新建的关卡仅含脚本, 可能需在 SMBX 编辑器中补全其它字段才能正常加载。");
        }
    }

    /// <summary>
    /// 查找依赖脚本正文：依次在 --deps-dir / --common-utils-dir /
    /// exe 上级 smbx38a-tescript-common-utils / exe 同目录 / 若干 Teascripts/Release 路径中查找。
    /// </summary>
    private static string? FindDependencyBody(BatchOptions bo, string fileName)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(bo.DepsDir))
            candidates.Add(Path.Combine(bo.DepsDir, fileName));
        if (!string.IsNullOrWhiteSpace(bo.CommonUtilsDir))
            candidates.Add(Path.Combine(bo.CommonUtilsDir, fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "..", "smbx38a-tescript-common-utils", fileName));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));
        candidates.Add(Path.Combine(bo.TableDir, "..", "Teascripts", "Release", fileName));
        candidates.Add(Path.Combine(bo.LvlDir, "..", "Teascripts", "Release", fileName));
        candidates.Add(Path.Combine(bo.LvlDir, "..", "..", "Teascripts", "Release", fileName));

        var found = candidates.FirstOrDefault(File.Exists);
        if (found is null) return null;

        try
        {
            return File.ReadAllText(found, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [warn] 读取依赖脚本 {fileName} 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 注入/补充一个脚本：
    /// replaceAlways=true 时同名先删后加（始终刷新）；
    /// replaceAlways=false 时仅当关卡中缺失该脚本才补充。
    /// </summary>
    private static void InjectScript(LvlFile lvl, BatchOptions bo, string fileName, string scriptName, bool replaceAlways)
    {
        if (!replaceAlways && lvl.HasScript(scriptName))
        {
            // 保留关卡中原本的脚本：不补充，但清理历史遗留的重复条目（仅保留首条），
            // 并把旧版本写入的"半编码"脚本名（如 bmp%5Futils）改写为 38A 规范形式，随后仍按规范重排位置。
            int dup = lvl.KeepOnlyFirst(scriptName);
            int renamed = lvl.NormalizeScriptName(scriptName);
            Console.WriteLine($"    [info] 已存在脚本 {scriptName}, 跳过补充"
                + (dup > 0 ? $", 并清理历史重复 {dup} 处" : "")
                + (renamed > 0 ? $", 并修正脚本名存储形式 {renamed} 处" : "") + "。");
            return;
        }

        var body = FindDependencyBody(bo, fileName);
        if (body is null)
        {
            Console.WriteLine($"    [warn] 未找到依赖脚本 {fileName}, 跳过注入 {scriptName}。");
            return;
        }

        // 同名检测：遍历删除同名旧脚本直到彻底干净（F3），再注入
        int removedSame = 0;
        while (lvl.HasScript(scriptName))
        {
            lvl.RemoveScript(scriptName);
            removedSame++;
        }
        if (removedSame > 0)
        {
            Console.WriteLine($"    [info] 删除旧脚本 {scriptName} 共 {removedSame} 处, 准备重新注入。");
        }

        lvl.SetScript(scriptName, body, "SU");
        Console.WriteLine($"    [info] 已注入脚本: {scriptName} (来自 {fileName})");
    }

    /// <summary>把生成的整合脚本路径合并进 .cfg.json 的 script 数组（仅追加, 不删除已有项）。</summary>
    private static void UpdateCfgScriptArray(string cfgPath, List<string> generated)
    {
        string json;
        try
        {
            json = File.ReadAllText(cfgPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [warn] 无法读取 cfg.json, 跳过更新: {ex.Message}");
            return;
        }

        var cfgDir = Path.GetDirectoryName(Path.GetFullPath(cfgPath)) ?? ".";
        List<string> existing = new();
        try
        {
            var stripped = CliOptions.StripJsonComments(json);
            using var doc = JsonDocument.Parse(stripped);
            if (doc.RootElement.TryGetProperty("script", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        existing.Add(item.GetString()!);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    [warn] 解析 cfg.json 的 script 数组失败, 仅尝试原地替换: {ex.Message}");
        }

        var unioned = new List<string>(existing);
        foreach (var g in generated)
        {
            var rel = RelativePath(cfgDir, g);
            if (!unioned.Contains(rel, StringComparer.OrdinalIgnoreCase))
            {
                unioned.Add(rel);
            }
        }

        var arrayLiteral = StringifyScriptArray(unioned);

        // 优先原地替换已有的 "script": [...]；否则在最后 } 之前插入。
        var re = new Regex(@"""(?:script|Script)""\s*:\s*\[[^\]]*\]");
        if (re.IsMatch(json))
        {
            json = re.Replace(json, "\"script\": " + arrayLiteral);
        }
        else
        {
            var idx = json.LastIndexOf('}');
            if (idx >= 0)
            {
                var before = json.Substring(0, idx).TrimEnd();
                var needComma = before.Length > 0 && !before.EndsWith(',') && !before.EndsWith('\n');
                var insert = (needComma ? ",\n" : "\n") + "  \"script\": " + arrayLiteral + "\n";
                json = before + insert + json.Substring(idx);
            }
        }

        File.WriteAllText(cfgPath, json, new UTF8Encoding(false));
        Console.WriteLine($"    [info] 已更新 cfg.json 的 script 数组 ({unioned.Count} 项): {cfgPath}");
    }

    private static string StringifyScriptArray(List<string> items)
    {
        if (items.Count == 0) return "[]";
        var sb = new StringBuilder();
        sb.Append("[\n");
        for (int i = 0; i < items.Count; i++)
        {
            var escaped = items[i].Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.Append("    \"").Append(escaped).Append('"');
            sb.Append(i < items.Count - 1 ? ",\n" : "\n");
        }

        sb.Append("  ]");
        return sb.ToString();
    }

    private static string RelativePath(string baseDir, string path)
    {
        try
        {
            return Path.GetRelativePath(baseDir, path);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
