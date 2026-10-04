using System.Text;
using System.Text.RegularExpressions;

namespace TableExporter;

/// <summary>
/// 把一个 lvl 下多张表的脚本合并为一个「整合脚本」。
/// <para>
/// 关键点：生成器对每张表都会声明一批全局变量（<c>__row_map</c>、<c>__data_map_XX</c>、
/// <c>__data_chunk_XXXX</c> 以及一堆 <c>__</c> 前缀的临时变量）。如果直接把多张表的脚本
/// 拼到一起，这些全局 <c>Dim</c> 会重名冲突。因此整合时给每张表的「变量名」加上表名前缀；
/// 而每张表导出的函数名本就带表名（Pascal 化的脚本名，如 <c>Char_SetId</c>），已经天然不冲突。
/// </para>
/// </summary>
public static class ScriptIntegrator
{
    /// <summary>
    /// 需要按表加前缀的全局变量清单。
    /// <list type="bullet">
    ///   <item>以 <c>_</c> 结尾的条目为「前缀型」：会匹配后续带序号/字段的变量
    ///         （如 <c>__data_map_XX</c>、<c>__data_chunk_XXXX</c>）。</item>
    ///   <item>其余为整词匹配（带单词边界），只命中该变量本身。</item>
    /// </list>
    /// 顺序很重要：更长的 / 可能成为其它变量前缀的条目必须排在前面，
    /// 例如 <c>__tmp_long2</c> 必须先于 <c>__tmp_long</c>，<c>__curr_data_map_offset_real</c>
    /// 必须先于 <c>__curr_data_map_offset</c>。
    /// </summary>
    private static readonly string[] GlobalVars =
    [
        "__tmp_long2", "__tmp_long",
        "__data_chunk_", "__data_chunk",
        "__data_map_", "__data_map",
        "__curr_data_map_offset_real", "__curr_data_map_offset", "__curr_data_map_id",
        "__field_index_real", "__field_index", "__field_array_index",
        "__row_map",
        "__data_offset", "__data_length", "__target_data", "__temp_str",
        "__last_error", "__curr_row_index", "__orig_data_map_id", "__orig_data_map_offset",
        "__tmp_int", "__tmp_str",
    ];

    /// <summary>
    /// 把脚本名清洗成合法的 TeaScript 标识符前缀：仅保留字母数字，且必须以字母开头。
    /// </summary>
    public static string SanitizePrefix(string scriptName)
    {
        var sb = new StringBuilder();
        foreach (var c in scriptName)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }

        var s = sb.ToString();
        if (s.Length == 0) s = "T";
        if (!char.IsLetter(s[0])) s = "T" + s;
        return s;
    }

    /// <summary>
    /// 给一张表生成的脚本里的全局变量加上 <paramref name="prefix"/> 前缀，避免合并后重名。
    /// 仅改全局变量名，不动已经带表名的导出函数名与 <c>__TableExport_&lt;Sheet&gt;_</c> 辅助脚本名。
    /// </summary>
    public static string PrefixTable(string script, string prefix)
    {
        foreach (var v in GlobalVars)
        {
            bool isPrefixVar = v.EndsWith("_", StringComparison.Ordinal);
            var pattern = isPrefixVar
                ? @"\b" + Regex.Escape(v)
                : @"\b" + Regex.Escape(v) + @"\b";
            script = Regex.Replace(script, pattern, prefix + v);
        }

        return script;
    }

    /// <summary>
    /// 把多张表的（已加前缀的）脚本合并为一个整合脚本。所有注释（含每张表的头注释与
    /// 脚本内的 ' 注释行）被单独拆出并集中置于脚本最前面，其后才是全部代码。
    /// </summary>
    public static string Integrate(
        IReadOnlyList<(string ScriptName, string LvlName, string SourcePath, string PrefixedScript)> tables,
        string targetScriptName)
    {
        var commentLines = new List<string>();
        var codeLines = new List<string>();

        // 顶部整合说明（作为注释，置于最前）
        commentLines.Add("' ============================================================");
        commentLines.Add("' 整合配置表脚本 (由 TableExporter 批处理生成, 禁止手动修改)");
        commentLines.Add($"' 目标脚本名: {targetScriptName}");
        commentLines.Add($"' 包含表数量: {tables.Count}");
        commentLines.Add("' ============================================================");
        commentLines.Add(string.Empty);

        foreach (var t in tables)
        {
            var prefix = SanitizePrefix(t.ScriptName);

            // 本表头注释（注释，前移）
            commentLines.Add("' ------------------------------------------------------------");
            commentLines.Add($"' 表(脚本): {t.ScriptName}    所属关卡: {t.LvlName}");
            commentLines.Add($"' 源文件: {t.SourcePath}");
            commentLines.Add(
                $"' 函数前缀: {TeaScriptEmitter.ToPascal(t.ScriptName)}_    " +
                $"变量前缀: {prefix}__");
            commentLines.Add("' ------------------------------------------------------------");
            commentLines.Add(string.Empty);

            // 拆分本表脚本自身的注释与代码：注释前移、代码后放
            var (comments, code) = SplitComments(t.PrefixedScript);
            commentLines.AddRange(comments);
            if (comments.Count > 0) commentLines.Add(string.Empty);
            codeLines.AddRange(code);
            codeLines.Add(string.Empty);
        }

        var sb = new StringBuilder();
        foreach (var c in commentLines)
        {
            sb.Append(c).Append('\n');
        }

        foreach (var c in codeLines)
        {
            sb.Append(c).Append('\n');
        }

        // 统一为 \n 行尾，与生成器其余部分保持一致。
        return sb.ToString().Replace("\r\n", "\n");
    }

    /// <summary>
    /// 把一段脚本拆分为注释行与代码行：以 ' 开头的行视为注释，其余（含空行）视为代码。
    /// </summary>
    private static (List<string> Comments, List<string> Code) SplitComments(string script)
    {
        var comments = new List<string>();
        var code = new List<string>();
        foreach (var raw in script.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("'", StringComparison.Ordinal))
            {
                comments.Add(raw);
            }
            else
            {
                code.Add(raw);
            }
        }

        return (comments, code);
    }
}
