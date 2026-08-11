using System.Text;

namespace TableExporter;

/// <summary>表中一列所支持的类型。</summary>
public enum FieldType
{
    /// <summary>跳过列：不参与导出，仅保留源表内容用于备注。类型可写 skip / none / x 等。</summary>
    Skip,

    /// <summary>32 位整数。</summary>
    Int,

    /// <summary>浮点数（导出时按定点/字符串保留精度）。</summary>
    Float,

    /// <summary>布尔值，导出为 0/1。</summary>
    Bool,

    /// <summary>原样字符串（不做字模转码，仅做 base92 安全转义）。</summary>
    String,

    /// <summary>需要交给 FontAtlasGenerator 做字模转码的富文本。</summary>
    Text,

    /// <summary>指向本表或它表某行的 uuid 引用，导出为完美哈希后的 int。</summary>
    Ref,
}

/// <summary>一列的定义。</summary>
public sealed class FieldDef
{
    public required string Name { get; init; }
    public required FieldType Type { get; init; }

    /// <summary>是否为数组列（表头以 <c>[]</c> 结尾）。</summary>
    public bool IsArray { get; init; }

    /// <summary>数组元素分隔符，默认 <c>|</c>。</summary>
    public string ArraySeparator { get; init; } = "|";

    /// <summary>列在原始 sheet 中的下标，用于错误定位。</summary>
    public int ColumnIndex { get; init; }

    /// <summary>该列是否为主键 uuid 列。</summary>
    public bool IsId => string.Equals(Name, "id", StringComparison.OrdinalIgnoreCase);

    public override string ToString() =>
        $"{Name}:{Type}{(IsArray ? "[]" : string.Empty)}";
}

/// <summary>一行数据，字段值均以字符串形态保存，序列化阶段再按类型编码。</summary>
public sealed class RowData
{
    /// <summary>该行的 uuid（id 列原文）。</summary>
    public required string Id { get; init; }

    /// <summary>在 sheet 中的行号（1-based，用于报错）。</summary>
    public required int SourceLine { get; init; }

    /// <summary>字段名 -> 原始单元格文本。</summary>
    public required Dictionary<string, string> Cells { get; init; }
}

/// <summary>一张解析完成的表。</summary>
public sealed class TableData
{
    public required string Name { get; init; }
    public required List<FieldDef> Fields { get; init; }
    public required List<RowData> Rows { get; init; }

    public FieldDef? FindField(string name) =>
        Fields.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
}

/// <summary>导表过程中的可定位异常。</summary>
public sealed class ExportException : Exception
{
    public ExportException(string message) : base(message) { }

    public static ExportException At(string table, int line, string message) =>
        new($"[{table}] 第 {line} 行: {message}");
}

/// <summary>把诊断信息收集起来，一次性汇报，避免逐条中断。</summary>
public sealed class Diagnostics
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];

    public bool HasErrors => _errors.Count > 0;

    public void Error(string message) => _errors.Add(message);

    public void Warn(string message) => _warnings.Add(message);

    public void FlushWarnings(TextWriter output)
    {
        foreach (var w in _warnings)
        {
            output.WriteLine($"  [warn] {w}");
        }

        _warnings.Clear();
    }

    /// <summary>若存在错误则抛出聚合异常。</summary>
    public void ThrowIfError()
    {
        if (!HasErrors)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"导表失败，共 {_errors.Count} 个错误:");
        foreach (var e in _errors)
        {
            sb.AppendLine($"  - {e}");
        }

        throw new ExportException(sb.ToString().TrimEnd());
    }
}
