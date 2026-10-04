using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TableExporter;

/// <summary>
/// 极简 SMBX 38A .lvl/.wld/.wls 读写：仅用于安全地注入 / 替换嵌入脚本（SU/GSU）。
/// <para>
/// 设计目标：不触碰其它 marker 的字段，最大程度保留原文件结构（行尾 / 空行 / 字段顺序）。
/// 参考 smbx-38a skill 的 lvlfile.py 与 lvl-format.md 的踩坑项 17 / 18 / 22：
/// <list type="bullet">
///   <item>脚本 marker 必须用大写 <c>SU</c>（关卡脚本）/ <c>GSU</c>（全局脚本）。</item>
///   <item>脚本 body 必须用系统 ANSI / GBK 字节做 base64，否则引擎按 GBK 解码会乱码。</item>
///   <item>脚本名（SU/GSU 第一字段）在 38A 中是"强制百分号编码"：<b>每个字符</b>（含字母数字）都编成
///   <c>%XX</c>，如 <c>bmp_utils</c> -> <c>%62%6D%70%5F%75%74%69%6C%73</c>（与编辑器自身写出的一致）。
///   读取时解码后比较、写入时按此规则编码，确保同名可正确识别与去重（F3）。</item>
/// </list>
/// </para>
/// </summary>
public sealed class LvlFile
{
    private readonly List<Entry> _entries = new();
    private string _newline = "\r\n";
    private int _version = 65;

    private static readonly Regex HeaderRe = new(@"^SMBXFile(\d+)\s*$", RegexOptions.Compiled);

    public static LvlFile Load(string path)
    {
        var raw = File.ReadAllBytes(path);

        Encoding enc;
        int bomOffset = 0;
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            enc = Encoding.UTF8;
            bomOffset = 3;
        }
        else
        {
            enc = Encoding.ASCII; // 38A 文件默认 ASCII（非 ASCII 字段走百分号编码）
        }

        string text;
        if (bomOffset > 0)
        {
            text = enc.GetString(raw, bomOffset, raw.Length - bomOffset);
        }
        else
        {
            text = enc.GetString(raw);
        }

        var nl = text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
        var lines = text.Split(new[] { nl }, StringSplitOptions.None);

        var lvl = new LvlFile { _newline = nl };
        if (lines.Length == 0)
        {
            throw new ExportException($"{path}: 空文件");
        }

        var m = HeaderRe.Match(lines[0]);
        if (!m.Success)
        {
            throw new ExportException($"{path}: 缺少 SMBXFile?? 头 (首行: {lines[0]})");
        }

        lvl._version = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);

        foreach (var ln in lines.Skip(1))
        {
            if (ln.Length == 0)
            {
                lvl._entries.Add(new Entry { Marker = string.Empty, Fields = Array.Empty<string>() });
                continue;
            }

            var idx = ln.IndexOf('|');
            if (idx < 0)
            {
                lvl._entries.Add(new Entry { Marker = ln, Fields = Array.Empty<string>() });
                continue;
            }

            var marker = ln.Substring(0, idx);
            var rest = ln.Substring(idx + 1);
            var fields = rest.Length == 0 ? Array.Empty<string>() : rest.Split('|');
            lvl._entries.Add(new Entry { Marker = marker, Fields = fields });
        }

        return lvl;
    }

    /// <summary>创建仅含 <c>SMBXFile65</c> 头的最小 lvl（用户应在编辑器中补全）。</summary>
    public static LvlFile CreateMinimal(int version = 65)
    {
        return new LvlFile { _version = version, _newline = "\r\n" };
    }

    /// <summary>
    /// 写入 / 替换一个嵌入脚本。若已存在同名脚本（大小写不敏感）则替换其内容与 marker；
    /// 否则按 marker 分组追加到同类脚本块末尾，否则文件末尾（避开尾部空行）。
    /// </summary>
    public void SetScript(string name, string body, string marker)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ExportException("脚本名不能为空");
        }

        int existing = -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (NameEquals(_entries[i], name))
            {
                existing = i;
                break;
            }
        }

        var b64 = EncodeScriptBody(body);
        if (existing >= 0)
        {
            _entries[existing].Marker = marker;
            _entries[existing].Fields = new[] { EncodeName(name), b64 };
            return;
        }

        var entry = new Entry { Marker = marker, Fields = new[] { EncodeName(name), b64 } };
        int insertAt = _entries.Count;
        int lastSame = -1;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Marker == marker) lastSame = i;
        }

        if (lastSame >= 0)
        {
            insertAt = lastSame + 1;
        }
        else
        {
            while (insertAt > 0
                   && _entries[insertAt - 1].Marker == string.Empty
                   && _entries[insertAt - 1].Fields.Length == 0)
            {
                insertAt--;
            }
        }

        _entries.Insert(insertAt, entry);
    }

    /// <summary>判断关卡是否已存在指定名称的脚本（大小写不敏感）。</summary>
    public bool HasScript(string name) =>
        _entries.Any(e => NameEquals(e, name));

    /// <summary>
    /// 删除所有与 <paramref name="name"/> 同名的脚本（大小写不敏感；name 按 38A 百分号编码规则解码后比较）。
    /// 循环删除直到不再存在同名脚本，确保彻底清干净（F3）。
    /// </summary>
    public void RemoveScript(string name)
    {
        int removed;
        do
        {
            removed = _entries.RemoveAll(e => NameEquals(e, name));
        } while (removed > 0);
    }

    /// <summary>
    /// 同名脚本存在多条时，仅保留第一条（关卡中原本的脚本），移除其余重复条目。
    /// 用于 <c>replaceAlways=false</c> 的依赖脚本：检测到已存在即保留原脚本、不补充，并清理历史遗留重复。
    /// 返回被移除的重复条数。
    /// </summary>
    public int KeepOnlyFirst(string name)
    {
        int firstIdx = -1;
        int removed = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            if (!NameEquals(_entries[i], name)) continue;
            if (firstIdx < 0)
            {
                firstIdx = i;
                continue;
            }

            _entries.RemoveAt(i);
            removed++;
            i--; // 删除后回退索引
        }

        return removed;
    }

    /// <summary>
    /// 把已存在、但存储形式不规范（历史"半编码"，如 <c>bmp%5Futils</c>）的脚本名改写为 38A 规范的
    /// 强制百分号编码形式（<c>%62%6D%70%5F%75%74%69%6C%73</c>），脚本正文与其它字段保持不变。
    /// 用于修复旧版本写入的错误脚本名，返回被改写的条目数。
    /// </summary>
    public int NormalizeScriptName(string name)
    {
        var canonical = EncodeName(name);
        int fixedCount = 0;
        foreach (var e in _entries)
        {
            if (!NameEquals(e, name)) continue;
            if (e.Fields.Length > 0 && !string.Equals(e.Fields[0], canonical, StringComparison.Ordinal))
            {
                e.Fields[0] = canonical;
                fixedCount++;
            }
        }

        return fixedCount;
    }

    /// <summary>
    /// 重排脚本顺序：lib/utils 类（以 utils/util/lib/libs 结尾或以 lib 开头）置于最前，
    /// 其后是 TxtDecoder 脚本，再是表整合脚本，最后是其它游戏逻辑脚本；
    /// 同类脚本保持原相对顺序。非脚本条目位置不变。
    /// </summary>
    public void ReorderScripts(string tableScriptName, string txtDecoderScriptName)
    {
        var slots = new List<int>();
        var scripts = new List<Entry>();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (IsScriptMarker(_entries[i].Marker))
            {
                slots.Add(i);
                scripts.Add(_entries[i]);
            }
        }

        if (slots.Count == 0) return;

        var ordered = scripts
            .Select((e, idx) => (e, idx))
            .OrderBy(x => PriorityOf(x.e, tableScriptName, txtDecoderScriptName))
            .ThenBy(x => x.idx)
            .Select(x => x.e)
            .ToList();

        for (int k = 0; k < slots.Count; k++)
        {
            _entries[slots[k]] = ordered[k];
        }
    }

    private static int PriorityOf(Entry e, string tableScriptName, string txtDecoderScriptName)
    {
        var name = ScriptName(e);
        if (IsLibUtilsName(name)) return 0;
        if (string.Equals(name, DecodeName(txtDecoderScriptName), StringComparison.OrdinalIgnoreCase)) return 1;
        if (string.Equals(name, DecodeName(tableScriptName), StringComparison.OrdinalIgnoreCase)) return 2;
        return 3;
    }

    private static bool IsLibUtilsName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.StartsWith("lib", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("utils", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("util", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("lib", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("libs", StringComparison.OrdinalIgnoreCase);
    }

    public void Save(string path)
    {
        var sb = new StringBuilder();
        sb.Append("SMBXFile").Append(_version);
        foreach (var e in _entries)
        {
            sb.Append(_newline);
            if (e.Fields.Length == 0)
            {
                sb.Append(e.Marker);
            }
            else
            {
                sb.Append(e.Marker).Append('|').Append(string.Join("|", e.Fields));
            }
        }

        File.WriteAllBytes(path, Encoding.ASCII.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// 读取脚本名时解码百分号转义（38A 强制把每个字符编成 <c>%XX</c>）。
    /// 同时兼容历史产物中"未编码 / 半编码"的名字（如 <c>bmp%5Futils</c>、裸 <c>MyTables</c>），
    /// 保证改名前后都能被正确识别与去重。
    /// </summary>
    private static string DecodeName(string raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.IndexOf('%') < 0) return raw;

        var bytes = new List<byte>(raw.Length);
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] == '%' && i + 2 < raw.Length && IsHex(raw[i + 1]) && IsHex(raw[i + 2]))
            {
                bytes.Add((byte)(HexVal(raw[i + 1]) * 16 + HexVal(raw[i + 2])));
                i += 3;
            }
            else
            {
                // 未编码字符：按其 UTF-8 字节并入，避免非 ASCII 名字被拆坏。
                bytes.AddRange(Encoding.UTF8.GetBytes(raw[i].ToString()));
                i++;
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    /// <summary>
    /// 写入时按 38A 约定对脚本名做百分号编码：<b>每个字符</b>（含字母数字）都编成 <c>%XX</c>。
    /// <para>
    /// 注意：不能只转义下划线（得到 <c>bmp%5Futils</c> 这种"半编码"名字）——它不是 38A 的合法
    /// 编码形式，引擎/编辑器侧不会把它当编码串解码，其中的 <c>%</c> 还会被当成非法标识符字符
    /// 替换成 <c>_</c>，最终脚本名被显示为 <c>bmp_5Futils</c> 这类错误名字。
    /// </para>
    /// </summary>
    private static string EncodeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length * 3);
        foreach (byte b in Encoding.UTF8.GetBytes(name))
        {
            sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    private static string ScriptName(Entry e) =>
        e.Fields.Length > 0 ? DecodeName(e.Fields[0]) : string.Empty;

    private static bool NameEquals(Entry e, string name) =>
        IsScriptMarker(e.Marker) && e.Fields.Length > 0 &&
        string.Equals(ScriptName(e), DecodeName(name), StringComparison.OrdinalIgnoreCase);

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    private static int HexVal(char c) =>
        c >= '0' && c <= '9' ? c - '0'
            : c >= 'A' && c <= 'F' ? c - 'A' + 10
            : c - 'a' + 10;

    private static bool IsScriptMarker(string marker) =>
        marker is "S" or "Su" or "SU" or "GS" or "GSu" or "GSU";

    /// <summary>把脚本源码编成 base64：默认用系统 ANSI / GBK 字节（38A 引擎期望的）。</summary>
    private static string EncodeScriptBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;

        Encoding gbk;
        try
        {
            gbk = Encoding.GetEncoding("GBK");
        }
        catch (Exception)
        {
            // 极少数环境缺少 GBK codepage，退化为 gb2312 / ASCII。
            try
            {
                gbk = Encoding.GetEncoding("gb2312");
            }
            catch (Exception)
            {
                gbk = Encoding.ASCII;
            }
        }

        // 克隆并设置替换回退，避免极少数无法被 GBK 表示的字符抛出异常（用 '?' 代替）。
        gbk = (Encoding)gbk.Clone();
        gbk.EncoderFallback = EncoderFallback.ReplacementFallback;

        var bytes = gbk.GetBytes(body);
        return Convert.ToBase64String(bytes);
    }

    private sealed class Entry
    {
        public string Marker = string.Empty;
        public string[] Fields = Array.Empty<string>();
    }
}
