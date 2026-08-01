using System.Diagnostics;
using System.Text;

namespace TableExporter;

/// <summary>
/// 以子进程方式调用 FontAtlasGenerator 完成 <c>text</c> 列的字模转码。
/// <para>
/// 不直接引用其工程，避免两个工具的版本/依赖耦合；exe 通过宽松匹配在若干候选目录中查找。
/// </para>
/// </summary>
public sealed class FontAtlasBridge
{
    /// <summary>可执行文件名（不含扩展名）的候选，按优先级排列。</summary>
    private static readonly string[] ExeNameCandidates =
    [
        "FontAtlasGenerator",
        "fontatlasgenerator",
        "FontAtlasGen",
        "font-atlas-generator",
    ];

    private readonly string _exePath;

    private FontAtlasBridge(string exePath) => _exePath = exePath;

    /// <summary>已定位到的 exe 路径。</summary>
    public string ExePath => _exePath;

    /// <summary>
    /// 定位 FontAtlasGenerator。查找顺序：显式指定 -> 本 exe 同目录 -> 常见相对构建目录 -> PATH。
    /// </summary>
    public static FontAtlasBridge Locate(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var resolved = Path.GetFullPath(explicitPath);
            if (File.Exists(resolved))
            {
                return new FontAtlasBridge(resolved);
            }

            throw new ExportException($"指定的 FontAtlasGenerator 不存在: {resolved}");
        }

        foreach (var dir in CandidateDirectories())
        {
            var hit = ProbeDirectory(dir);
            if (hit is not null)
            {
                return new FontAtlasBridge(hit);
            }
        }

        var fromPath = ProbeSystemPath();
        if (fromPath is not null)
        {
            return new FontAtlasBridge(fromPath);
        }

        throw new ExportException(
            "未找到 FontAtlasGenerator 可执行文件。\n" +
            "请将其与 TableExporter 放在同一目录，或使用 --font-atlas <path> 显式指定。\n" +
            "已搜索目录:\n  " + string.Join("\n  ", CandidateDirectories()));
    }

    /// <summary>枚举候选目录。</summary>
    private static IEnumerable<string> CandidateDirectories()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return baseDir;

        // 与 build/ 平级或位于其中的常见布局。
        yield return Path.Combine(baseDir, "FontAtlasGenerator");
        yield return Path.Combine(baseDir, "tools");

        var current = Directory.GetCurrentDirectory();
        if (!PathEquals(current, baseDir))
        {
            yield return current;
        }

        // 开发期从 bin/<cfg>/<tfm>/ 回溯到解决方案目录，再进入 FontAtlasGenerator 的输出目录。
        var dir = new DirectoryInfo(baseDir);
        for (var depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
        {
            var sibling = Path.Combine(dir.FullName, "FontAtlasGenerator", "bin");
            if (Directory.Exists(sibling))
            {
                // 优先 Debug，其次 Release。
                foreach (var cfg in new[] { "Debug", "Release" })
                {
                    var cfgDir = Path.Combine(sibling, cfg);
                    if (Directory.Exists(cfgDir))
                    {
                        foreach (var tfm in Directory.EnumerateDirectories(cfgDir))
                        {
                            yield return tfm;
                        }
                    }
                }
            }

            var buildDir = Path.Combine(dir.FullName, "build");
            if (Directory.Exists(buildDir))
            {
                yield return buildDir;
            }
        }
    }

    /// <summary>在目录内按候选名宽松匹配可执行文件。</summary>
    private static string? ProbeDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var exeExt = OperatingSystem.IsWindows() ? ".exe" : string.Empty;

        // 先精确匹配。
        foreach (var name in ExeNameCandidates)
        {
            var exact = Path.Combine(dir, name + exeExt);
            if (File.Exists(exact))
            {
                return exact;
            }
        }

        // 再做大小写无关的模糊匹配：文件名去掉分隔符后包含 fontatlas。
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (OperatingSystem.IsWindows() &&
                !file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(file)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .Replace(".", string.Empty);

            if (stem.Contains("fontatlas", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    /// <summary>在 PATH 中查找。</summary>
    private static string? ProbeSystemPath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
        {
            return null;
        }

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            string? hit = null;
            try
            {
                hit = ProbeDirectory(dir.Trim());
            }
            catch (Exception)
            {
                // PATH 中可能存在无权限或非法路径，忽略即可。
            }

            if (hit is not null)
            {
                return hit;
            }
        }

        return null;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 生成一个仅转码用的临时空配置（不含字体图集路径），供 FontAtlasGenerator 在
    /// 「只做 $"..." 字模转码、不生成图集」的场景下使用。
    /// </summary>
    private string WriteEphemeralConfig()
    {
        var dir = Path.Combine(
            Path.GetTempPath(), "tableexporter_cfg_" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);

        var cfgPath = Path.Combine(dir, "transcode-only.json");
        var cfg = new StringBuilder();
        cfg.AppendLine("{");
        cfg.AppendLine("  \"font-path\": \"\",");      // 空：不生成图集
        cfg.AppendLine("  \"output-path\": \"\",");
        cfg.AppendLine("  \"size\": 0,");
        cfg.AppendLine("  \"force\": false");
        cfg.AppendLine("}");
        File.WriteAllText(cfgPath, cfg.ToString(), new UTF8Encoding(false));

        return cfgPath;
    }

    /// <summary>
    /// 调用 FontAtlasGenerator 对一批 <c>text</c> 文本做字模转码。
    /// </summary>
    /// <param name="configPath">字体图集配置 json。</param>
    /// <param name="texts">待转码文本，按序返回结果。</param>
    /// <returns>与输入等长的转码结果数组。</returns>
    /// <summary>对单条文本转码，返回其 PAYLOAD 原文（即 TXT(D("&lt;payload&gt;")) 中 payload 部分）。</summary>
    public string TranscodeOne(string text, string? configPath = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var results = Transcode(configPath, new[] { text });
        return results.Count > 0 ? results[0] : string.Empty;
    }

    public List<string> Transcode(string? configPath, IReadOnlyList<string> texts)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        string fullConfigPath;
        if (string.IsNullOrWhiteSpace(configPath))
        {
            // 仅转码模式：使用内置空配置（不需要字体图集）。
            fullConfigPath = WriteEphemeralConfig();
        }
        else
        {
            fullConfigPath = Path.GetFullPath(configPath);
            if (!File.Exists(fullConfigPath))
            {
                throw new ExportException($"字体图集配置不存在: {fullConfigPath}");
            }
        }

        // 关键: 配置里的 font-path / output-path 多为相对路径 (如 ./xxx.ttf),
        // FontAtlasGenerator 按当前工作目录解析它们, 因此必须在配置所在目录下运行,
        // 否则会读不到字体而以全零配置继续, 最终除零崩溃。
        var configDir = Path.GetDirectoryName(fullConfigPath);
        if (string.IsNullOrEmpty(configDir))
        {
            configDir = Directory.GetCurrentDirectory();
        }

        // FontAtlasGenerator 以「扫描 .smt 文件、重写 $"..." 字面量」的方式工作，
        // 因此这里构造一个临时 smt：每行一条待转码文本，转码后再按行取回。
        // 临时文件放在配置目录下的子目录中，避免污染用户目录同时保持相对路径可用。
        var workDir = Path.Combine(
            configDir, ".tableexporter_tmp_" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(workDir);

        try
        {
            var inputName = "tableexporter_text";
            var inputPath = Path.Combine(workDir, inputName + ".smt");

            var sb = new StringBuilder();
            foreach (var t in texts)
            {
                // 每条文本占一行，写成 FontAtlasGenerator 能识别的 $"..." 字面量。
                sb.Append("Call __TE_Text($\"").Append(EscapeForSmt(t)).AppendLine("\")");
            }

            File.WriteAllText(inputPath, sb.ToString(), new UTF8Encoding(false));

            // 产物固定写在进程的工作目录下 (即配置目录), 而非输入文件旁边。
            var outputPath = Path.Combine(configDir, "encoded-" + inputName + ".smt");

            // 先清掉可能存在的同名旧产物, 避免误把上一次的结果当成本次输出。
            TryDeleteFile(outputPath);

            // 工作目录设为配置目录, 传入绝对路径以保证 File.Exists(arg) 判定成立。
            var run = RunProcess(fullConfigPath, configDir, inputPath);

            if (!File.Exists(outputPath))
            {
                throw new ExportException(
                    $"FontAtlasGenerator 未生成预期输出: {outputPath}\n" +
                    "常见原因: 配置中的 font-path 指向的字体文件不存在。\n" +
                    $"退出码 {run.ExitCode}\nstdout:\n{run.StdOut}\nstderr:\n{run.StdErr}");
            }

            var lines = File.ReadAllLines(outputPath)
                .Where(l => l.TrimStart().StartsWith("Call __TE_Text(", StringComparison.Ordinal))
                .ToList();

            if (lines.Count != texts.Count)
            {
                throw new ExportException(
                    $"FontAtlasGenerator 返回行数 {lines.Count} 与输入 {texts.Count} 不一致");
            }

            var result = lines.Select(ExtractEncodedPayload).ToList();

            // 产物落在了用户的配置目录, 用完即删, 避免留下中间文件。
            TryDeleteFile(outputPath);

            return result;
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception)
        {
            // 清理失败不影响主流程。
        }
    }

    /// <summary>
    /// 从 <c>Call __TE_Text(TXT(D("&lt;payload&gt;")))</c> 中取出最内层的 <c>&lt;payload&gt;</c>。
    /// <para>
    /// 必须剥到最内层字符串: 外层的 <c>TXT(D(...))</c> 是「代码」而非「数据」。
    /// 若把整个表达式文本当作字段值存起来, 运行时拿到的只是一串源码文本, 永远不会被求值；
    /// 而且其中真实存在的双引号也无法安全地嵌回 TeaScript 字面量。
    /// 正确做法是只存 payload, 由生成的访问函数在运行时执行 <c>TXT(D(seg))</c>。
    /// </para>
    /// </summary>
    private static string ExtractEncodedPayload(string line)
    {
        // 定位最内层的一对双引号。
        var first = line.IndexOf('"');
        var last = line.LastIndexOf('"');

        if (first < 0 || last <= first)
        {
            throw new ExportException(
                $"无法从 FontAtlasGenerator 输出中解析出编码载荷: {line}");
        }

        var payload = line.Substring(first + 1, last - first - 1);

        // 载荷里不应再出现裸引号或反斜杠, 否则无法安全写入 TeaScript 字面量。
        if (payload.Contains('"') || payload.Contains('\\'))
        {
            throw new ExportException(
                $"FontAtlasGenerator 返回的载荷含有无法嵌入字面量的字符: {payload}");
        }

        return payload;
    }

    /// <summary>转义出现在 smt 字面量里的字符。</summary>
    private static string EscapeForSmt(string text) =>
        text.Replace("\r", string.Empty).Replace("\n", "\\n");

    /// <summary>子进程执行结果。</summary>
    private readonly record struct ProcResult(int ExitCode, string StdOut, string StdErr);

    /// <summary>
    /// 实际拉起子进程。
    /// <para>
    /// 注意: FontAtlasGenerator 在结尾会调用 <c>Console.ReadKey()</c>，
    /// 而 stdin 被重定向时该调用必定抛 InvalidOperationException，
    /// 导致进程以 0xE0434352 退出——即便转码本身已经完全成功。
    /// 因此这里不把退出码当作成败依据，只将其作为诊断信息返回，
    /// 真正的判定标准是「产物文件是否生成」。
    /// </para>
    /// </summary>
    private ProcResult RunProcess(string configPath, string workDir, string inputPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        psi.ArgumentList.Add(configPath);
        psi.ArgumentList.Add(inputPath);

        using var proc = Process.Start(psi)
            ?? throw new ExportException($"无法启动 FontAtlasGenerator: {_exePath}");

        var stdout = proc.StandardOutput.ReadToEndAsync();
        var stderr = proc.StandardError.ReadToEndAsync();

        // FontAtlasGenerator 结尾可能等待按键，直接关闭其 stdin 让读取立即返回。
        try
        {
            proc.StandardInput.Write(Environment.NewLine);
            proc.StandardInput.Close();
        }
        catch (IOException)
        {
            // 进程可能已退出，忽略。
        }

        if (!proc.WaitForExit(120_000))
        {
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // 忽略清理失败。
            }

            throw new ExportException("FontAtlasGenerator 执行超时 (120s)");
        }

        var outText = stdout.GetAwaiter().GetResult();
        var errText = stderr.GetAwaiter().GetResult();

        return new ProcResult(proc.ExitCode, outText, errText);
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
        catch (Exception)
        {
            // 临时目录清理失败不影响主流程。
        }
    }
}
