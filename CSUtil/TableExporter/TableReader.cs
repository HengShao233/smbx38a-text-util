using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace TableExporter;

/// <summary>
/// 读取配置表源文件。支持 <c>.xlsx</c> 与 <c>.csv</c>。
/// <para>
/// 表头约定（第一行）：<c>字段名:类型</c> 或 <c>字段名:类型[]</c>，
/// 数组可用 <c>字段名:类型[分隔符]</c> 指定自定义分隔符。
/// 以 <c>#</c> 开头的列会被忽略（注释列）。
/// </para>
/// </summary>
public static class TableReader
{
    /// <summary>按扩展名分派读取。</summary>
    public static TableData Read(string path, Diagnostics diag)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();

        var grid = ext switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(path),
            ".csv" => ReadCsv(path),
            _ => throw new ExportException($"不支持的表格式: {ext} ({path})"),
        };

        return Parse(name, grid, diag);
    }

    /// <summary>把二维文本网格解析成结构化表。</summary>
    private static TableData Parse(string name, List<List<string>> grid, Diagnostics diag)
    {
        if (grid.Count == 0)
        {
            throw new ExportException($"<{name}> 表内容为空");
        }

        // 检测格式：文档三行头 vs 旧单行头
        // 文档格式：第1行备注(可含<sheetName>)，第2行字段名，第3行类型
        // 旧格式：第1行 字段名:类型
        int headerRowIdx;
        int typeRowIdx;
        int dataStartRow;
        string sheetName = name;

        bool isThreeRowHeader = DetectThreeRowHeader(grid);

        var fields = new List<FieldDef>();
        if (isThreeRowHeader)
        {
            // 第 1 行：备注/表名，尝试提取 <sheetName>
            if (grid.Count > 0 && grid[0].Count > 0)
            {
                var remark = grid[0][0];
                var bracketStart = remark.IndexOf('<');
                var bracketEnd = remark.IndexOf('>');
                if (bracketStart >= 0 && bracketEnd > bracketStart)
                {
                    sheetName = remark.Substring(bracketStart + 1, bracketEnd - bracketStart - 1).Trim();
                }
            }

            headerRowIdx = 1; // 第 2 行
            typeRowIdx = 2;   // 第 3 行
            dataStartRow = 3;
        }
        else
        {
            headerRowIdx = 0;
            typeRowIdx = -1;  // 旧格式类型在表头行内
            dataStartRow = 1;
        }

        if (grid.Count <= headerRowIdx)
        {
            throw new ExportException($"<{name}> 缺少字段名行");
        }

        var headerRow = grid[headerRowIdx];
        List<string>? typeRow = typeRowIdx >= 0 && grid.Count > typeRowIdx ? grid[typeRowIdx] : null;

        for (var col = 0; col < headerRow.Count; col++)
        {
            var fieldName = headerRow[col].Trim();
            if (fieldName.Length == 0 || fieldName.StartsWith('#'))
            {
                continue;
            }

            string typePart;
            if (typeRow != null)
            {
                // 文档三行头格式：类型在单独行
                typePart = col < typeRow.Count ? typeRow[col].Trim() : "str";
            }
            else
            {
                // 旧格式：字段名:类型 在同一单元格
                var colonIndex = fieldName.IndexOf(':');
                if (colonIndex >= 0)
                {
                    typePart = fieldName[(colonIndex + 1)..].Trim();
                    fieldName = fieldName[..colonIndex].Trim();
                }
                else
                {
                    typePart = "str";
                    diag.Warn($"<{name}> 列 '{fieldName}' 未标注类型，按 str 处理");
                }
            }

            var def = ParseType(name, fieldName, typePart, col, diag);

            // 跳过列：保留源表列内容用于备注，但不参与导出（不生成字段/访问器）。
            if (def.Type == FieldType.Skip)
            {
                diag.Warn($"<{name}> 列 '{fieldName}' 标记为 skip，已跳过导出（保留为备注列）");
                continue;
            }

            fields.Add(def);
        }

        if (fields.Count == 0)
        {
            throw new ExportException($"<{name}> 未解析到任何有效列");
        }

        if (fields.All(f => !f.IsId))
        {
            throw new ExportException($"<{name}> 缺少必需的主键列 'id'");
        }

        var idField = fields.First(f => f.IsId);
        if (idField.IsArray)
        {
            throw new ExportException($"<{name}> 主键列 'id' 不能是数组");
        }

        var rows = new List<RowData>();
        var seenIds = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var r = dataStartRow; r < grid.Count; r++)
        {
            var line = r + 1;
            var rowCells = grid[r];

            if (rowCells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fields)
            {
                var value = f.ColumnIndex < rowCells.Count ? rowCells[f.ColumnIndex] : string.Empty;
                cells[f.Name] = value ?? string.Empty;
            }

            var id = cells[idField.Name].Trim();

            if (id.StartsWith('#'))
            {
                continue;
            }

            if (id.Length == 0)
            {
                diag.Error($"<{name}> 第 {line} 行: id 为空");
                continue;
            }

            if (seenIds.TryGetValue(id, out var prev))
            {
                diag.Error($"<{name}> 第 {line} 行: id '{id}' 与第 {prev} 行重复");
                continue;
            }

            seenIds[id] = line;
            rows.Add(new RowData { Id = id, SourceLine = line, Cells = cells });
        }

        return new TableData { Name = sheetName, Fields = fields, Rows = rows };
    }

    /// <summary>
    /// 检测是否为文档三行头格式：
    /// 第 1 行所有非空单元格都不含 ':'（纯字段名），且行数 >= 3。
    /// </summary>
    private static bool DetectThreeRowHeader(List<List<string>> grid)
    {
        if (grid.Count < 3) return false;
        var row1 = grid[1]; // 第 2 行（字段名行）
        if (row1 == null || row1.Count == 0) return false;

        // 第 2 行至少有一个非空单元格且不含 ':'
        foreach (var cell in row1)
        {
            var trimmed = cell.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Contains(':')) return false; // 旧格式
        }

        // 第 3 行（类型行）至少有一个非空单元格
        var row2 = grid[2];
        if (row2 == null) return false;
        return row2.Any(c => !string.IsNullOrWhiteSpace(c));
    }

    /// <summary>解析类型声明，支持文档规范的 int/str/text 及别名。</summary>
    private static FieldDef ParseType(string table, string fieldName, string typePart, int col, Diagnostics diag)
    {
        if (fieldName.Length == 0)
        {
            throw new ExportException($"[{table}] 第 {col + 1} 列缺少字段名");
        }

        var isArray = false;
        var separator = ",";

        var bracket = typePart.IndexOf('[');
        if (bracket >= 0)
        {
            if (!typePart.EndsWith(']'))
            {
                throw new ExportException($"[{table}] 列 '{fieldName}' 的数组标记未闭合");
            }

            isArray = true;
            var inner = typePart[(bracket + 1)..^1];
            if (inner.Length > 0)
            {
                separator = inner;
            }

            typePart = typePart[..bracket].Trim();
        }

        // 文档规范类型：int(i/i32/integer) / str(s/string) / text(t/txt)
        // 兼容旧类型：float/double→按 int*10000 处理(不支持)，bool→int(0/1)，ref→int
        var type = typePart.ToLowerInvariant() switch
        {
            "int" or "i" or "i32" or "integer" => FieldType.Int,
            "str" or "s" or "string" => FieldType.String,
            "text" or "t" or "txt" => FieldType.Text,
            "bool" or "boolean" => FieldType.Int, // bool 归约为 int (0/1)
            "ref" or "uuid" => FieldType.Int,     // ref 归约为 int
            "skip" or "none" or "x" or "comment" or "remark" or "memo" or "注释" => FieldType.Skip, // 跳过列：不导出，仅作备注
            _ => throw new ExportException($"[{table}] 列 '{fieldName}' 的类型 '{typePart}' 不受支持 (仅支持 int/str/text/skip)"),
        };

        return new FieldDef
        {
            Name = fieldName,
            Type = type,
            IsArray = isArray,
            ArraySeparator = separator,
            ColumnIndex = col,
        };
    }

    /// <summary>读取 xlsx 的首个工作表为文本网格。</summary>
    private static List<List<string>> ReadXlsx(string path)
    {
        using var doc = SpreadsheetDocument.Open(path, false);
        var workbookPart = doc.WorkbookPart
            ?? throw new ExportException($"{path} 不是有效的 xlsx");

        var sheet = workbookPart.Workbook.Descendants<Sheet>().FirstOrDefault()
            ?? throw new ExportException($"{path} 中没有工作表");

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!.Value!);
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;

        var grid = new List<List<string>>();

        foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
        {
            var rowIndex = (int)(row.RowIndex?.Value ?? 0);

            // 补齐中间被完全跳过的空行，保证行号与 Excel 一致。
            while (grid.Count < rowIndex - 1)
            {
                grid.Add([]);
            }

            var cells = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                var colIndex = ColumnIndexOf(cell.CellReference?.Value);
                while (cells.Count < colIndex)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(ReadCellText(cell, sharedStrings));
            }

            grid.Add(cells);
        }

        return grid;
    }

    /// <summary>把 <c>B7</c> 这样的引用换算成 0-based 列号。</summary>
    private static int ColumnIndexOf(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return 0;
        }

        var index = 0;
        foreach (var c in cellReference)
        {
            if (!char.IsLetter(c))
            {
                break;
            }

            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index - 1;
    }

    /// <summary>取单元格文本，处理共享字符串与内联字符串。</summary>
    private static string ReadCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var raw = cell.CellValue?.InnerText ?? string.Empty;

        if (cell.DataType?.Value == CellValues.SharedString)
        {
            if (sharedStrings is null || !int.TryParse(raw, out var ssIndex))
            {
                return string.Empty;
            }

            return sharedStrings.ElementAt(ssIndex).InnerText;
        }

        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.Text ?? string.Empty;
        }

        if (cell.DataType?.Value == CellValues.Boolean)
        {
            return raw == "1" ? "true" : "false";
        }

        return raw;
    }

    /// <summary>读取 csv 为文本网格，支持双引号包裹与转义。</summary>
    private static List<List<string>> ReadCsv(string path)
    {
        var text = File.ReadAllText(path, DetectEncoding(path));
        var grid = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    grid.Add(row);
                    row = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            grid.Add(row);
        }

        return grid;
    }

    /// <summary>简单的 BOM 探测，默认 UTF-8。</summary>
    private static Encoding DetectEncoding(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> bom = stackalloc byte[3];
        var read = fs.Read(bom);
        if (read == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        return new UTF8Encoding(false);
    }
}
