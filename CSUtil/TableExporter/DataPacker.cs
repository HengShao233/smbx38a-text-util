using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TableExporter;

/// <summary>
/// 打包后的表数据，严格遵循文档三层紧凑结构：
/// <list type="bullet">
///   <item><c>__row_map</c>：按 uuid 升序排列，每行 <c>[uuid 定宽][data_map:id 1位][data_map:offset 2位]</c>。</item>
///   <item><c>__data_map_XX</c>：紧凑排列的字段定位 <c>[chunkId 2位][offset 3位][len 3位]</c>（每字段 8 位），最多 92 个，每个最大 8463 字符。</item>
///   <item><c>__data_chunk_XXXX</c>：紧凑排列的数据单元，最多 8463 个，每个最大 16383 字符。</item>
/// </list>
/// 数据单元紧凑堆叠：多个行的数据堆在同一个 chunk 中直到接近 16383 才新建。
/// data_map 同理：多个行的字段定位堆在同一个 data_map 中直到接近 8463 才新建。
/// </summary>
public sealed class PackedTable
{
    /// <summary>排序后的 (uuid, rowIndex) 列表，uuid 升序。</summary>
    public List<(int Uuid, int RowIndex)> RowOrder { get; init; } = new();

    /// <summary>整个 __row_map 串。</summary>
    public string RowMap { get; init; } = string.Empty;

    /// <summary>uuid 在 row_map 中的定宽位数（base92）。</summary>
    public int UuidWidth { get; init; }

    /// <summary>每个 data_map 串（紧凑排列，下标 = data_map 序号）。</summary>
    public List<string> DataMaps { get; init; } = new();

    /// <summary>每个 data_chunk 串（紧凑排列，下标 = chunk 序号）。</summary>
    public List<string> DataChunks { get; init; } = new();

    /// <summary>每个字段是否需要 TXT(D()) 包装（text 或非 ascii string）。</summary>
    public List<bool> FieldNeedsTxt { get; init; } = new();

    /// <summary>标量数组的定宽位数（按字段，-1 表示非标量数组或不是数组）。</summary>
    public List<int> FieldArrayElemWidth { get; init; } = new();

    public TableData Table { get; init; } = null!;
    public PerfectHash Hash { get; init; } = null!;
}

public static class DataPacker
{
    private const int MaxChunkLen = 16383;
    private const int MaxDataMapLen = 8463;

    /// <summary>
    /// 打包整张表。
    /// <paramref name="textResolver"/> 把 string/text 原文转成 FontAtlas payload。
    /// <paramref name="refResolver"/> 把 ref 原文（uuid）解析为目标行 1-based 行号。
    /// </summary>
    public static PackedTable Pack(
        TableData table,
        PerfectHash hash,
        Func<string, string> textResolver,
        Func<string, int> refResolver)
    {
        int nRows = table.Rows.Count;
        int nFields = table.Fields.Count;

        // 导出接口名碰撞检测：Get<field> 和 Get<field>Len
        // 若同时存在 XXX 和 XXXLen 两个字段，GetXXXLen 会与 GetXXX+Len 碰撞
        var pascalNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in table.Fields)
        {
            var pascal = TeaScriptEmitter.ToPascal(f.Name);
            if (f.IsArray && pascalNames.Contains(pascal + "Len"))
            {
                throw new ExportException(
                    $"[{table.Name}] 字段名碰撞: '{f.Name}' 与已存在的 '{pascal + "Len"}' 字段会导致导出接口名冲突 (Get{pascal}Len)。请重命名其中一个字段。");
            }
            if (!f.IsArray && pascalNames.Contains(pascal))
            {
                // 同名字段（Pascal 后相同）在非数组标量间也会碰撞 Get<field>
                throw new ExportException(
                    $"[{table.Name}] 字段名碰撞: '{f.Name}' 与已存在的字段生成相同的接口名 Get{pascal}。请重命名。");
            }
            // 如果当前是数组字段，检查是否存在 XXXLen 标量字段
            if (f.IsArray && pascalNames.Contains(pascal + "Len"))
            {
                throw new ExportException(
                    $"[{table.Name}] 字段名碰撞: 数组字段 '{f.Name}' 的 Get{pascal}Len 接口与已存在字段冲突。");
            }
            // 如果当前是标量字段名为 XXXLen，检查是否存在数组字段 XXX
            if (!f.IsArray && pascal.EndsWith("Len", StringComparison.Ordinal))
            {
                var baseName = pascal.Substring(0, pascal.Length - 3);
                if (pascalNames.Contains(baseName))
                {
                    throw new ExportException(
                        $"[{table.Name}] 字段名碰撞: '{f.Name}' (Get{pascal}) 与数组字段 Get{baseName}Len 接口冲突。请重命名其中一个。");
                }
            }
            pascalNames.Add(pascal);
        }

        // 计算每个字段的 TXT 包装需求与数组定宽。
        var fieldNeedsTxt = new List<bool>(nFields);
        var fieldArrayElemWidth = new List<int>(nFields);
        for (int f = 0; f < nFields; f++)
        {
            var def = table.Fields[f];
            bool needsTxt = def.Type is FieldType.Text;
            if (def.Type is FieldType.String)
            {
                bool anyNonAscii = table.Rows.Any(r =>
                {
                    if (!r.Cells.TryGetValue(def.Name, out var v)) return false;
                    foreach (var c in v) if (c > 127) return true;
                    return false;
                });
                if (anyNonAscii) needsTxt = true;
            }

            fieldNeedsTxt.Add(needsTxt);

            int elemWidth = -1;
            if (def.IsArray && def.Type is not (FieldType.String or FieldType.Text))
            {
                int maxLen = 1;
                foreach (var row in table.Rows)
                {
                    if (!row.Cells.TryGetValue(def.Name, out var raw)) continue;
                    var items = SplitArray(raw, def.ArraySeparator);
                    foreach (var it in items)
                    {
                        var enc = ValueEncoder.EncodeScalar(def.Type, it, textResolver);
                        if (enc.Text.Length > maxLen) maxLen = enc.Text.Length;
                    }
                }

                elemWidth = maxLen;
            }

            fieldArrayElemWidth.Add(elemWidth);
        }

        // 计算 uuid 并排序。
        var rowOrder = new List<(int Uuid, int RowIndex)>(nRows);
        for (int r = 0; r < nRows; r++)
        {
            rowOrder.Add((hash.Compute(table.Rows[r].Id), r));
        }

        rowOrder.Sort((a, b) => a.Uuid.CompareTo(b.Uuid));

        int uuidWidth = 1;
        if (nRows > 0)
        {
            int maxUuid = rowOrder[^1].Uuid;
            while (Base92.EncodeUInt((ulong)maxUuid).Length > uuidWidth) uuidWidth++;
            if (uuidWidth > 4) uuidWidth = 4;
        }

        // ---- 紧凑排列打包 ----
        // chunk: 所有行的数据单元紧凑堆叠，超过 MaxChunkLen 才新建。
        // data_map: 所有行的字段定位（每字段 8 位）紧凑堆叠，超过 MaxDataMapLen 才新建。
        var chunkList = new List<StringBuilder> { new StringBuilder() };
        var mapList = new List<StringBuilder> { new StringBuilder() };

        // 每行的 (dataMapId, dataMapOffset) —— row_map 中存储
        var rowMapEntries = new List<(int dataMapId, int dataMapOffset)>(nRows);

        // 按排序后的顺序（rowOrder）依次处理每行
        foreach (var (_, rowIndex) in rowOrder)
        {
            var row = table.Rows[rowIndex];
            var currentChunk = chunkList[^1];
            var currentMap = mapList[^1];

            // 记录该行第一个字段定位在 data_map 中的 1-based 偏移和 data_map 序号
            int rowMapDataMapIdx = mapList.Count - 1; // 0-based data_map 序号
            int rowMapOffset1Based = currentMap.Length + 1; // 在该 data_map 中的 1-based 偏移

            for (int f = 0; f < nFields; f++)
            {
                var def = table.Fields[f];
                row.Cells.TryGetValue(def.Name, out var raw);
                raw ??= string.Empty;

                // 先编码数据单元，确定其长度
                string dataUnit;
                int chunkId = 0;
                int chunkOffset1Based = 0;
                int dataLen = 0;
                if (def.IsArray)
                {
                    var items = SplitArray(raw, def.ArraySeparator);
                    var (elemTexts, _) = ValueEncoder.EncodeArray(def.Type, items, textResolver);

                    // 先确定数据单元放在哪个 chunk（可能新建 chunk）
                    int headerLen = elemTexts.Count * 8;
                    int totalUnitLen = headerLen + elemTexts.Sum(e => e.Length);
                    if (currentChunk.Length + totalUnitLen > MaxChunkLen && currentChunk.Length > 0)
                    {
                        chunkList.Add(new StringBuilder());
                        currentChunk = chunkList[^1];
                    }

                    int chunkIdArr = chunkList.Count - 1;
                    int headerStart0 = currentChunk.Length;
                    int headerStart1 = headerStart0 + 1;

                    // 生成数组头 + 元素
                    var headerSb = new StringBuilder(headerLen);
                    var elemSb = new StringBuilder();
                    int elemOffset = headerLen; // 元素相对于数组头起始的偏移
                    for (int ei = 0; ei < elemTexts.Count; ei++)
                    {
                        string elemText = elemTexts[ei];
                        int absOffset1Based = headerStart1 + elemOffset;
                        headerSb.Append(Encode2(chunkIdArr));
                        headerSb.Append(Encode3(absOffset1Based));
                        headerSb.Append(Encode3(elemText.Length));
                        elemSb.Append(elemText);
                        elemOffset += elemText.Length;
                    }

                    currentChunk.Append(headerSb.ToString());
                    currentChunk.Append(elemSb.ToString());

                    dataUnit = headerSb.ToString() + elemSb.ToString();
                    dataLen = headerLen;
                    chunkId = chunkIdArr;
                    chunkOffset1Based = headerStart1;
                }
                else
                {
                    string cellRaw = raw;
                    if (def.Type == FieldType.Ref)
                    {
                        int targetRow = refResolver(raw);
                        cellRaw = targetRow.ToString(CultureInfo.InvariantCulture);
                    }

                    var enc = ValueEncoder.EncodeScalar(def.Type, cellRaw, textResolver);
                    dataUnit = enc.Text;

                    // 确定数据单元放在哪个 chunk
                    if (currentChunk.Length + dataUnit.Length > MaxChunkLen && currentChunk.Length > 0)
                    {
                        chunkList.Add(new StringBuilder());
                        currentChunk = chunkList[^1];
                    }

                    chunkId = chunkList.Count - 1;
                    chunkOffset1Based = currentChunk.Length + 1;
                    currentChunk.Append(dataUnit);
                    dataLen = dataUnit.Length;
                }

                // 生成字段定位段 [chunkId 2位][offset 3位][len 3位]
                string fieldLoc = Encode2(chunkId) + Encode3(chunkOffset1Based) + Encode3(dataLen);

                // 确定字段定位放在哪个 data_map
                // 如果当前 data_map 放不下这 8 字符，新建 data_map
                if (currentMap.Length + 8 > MaxDataMapLen && currentMap.Length > 0)
                {
                    mapList.Add(new StringBuilder());
                    currentMap = mapList[^1];
                    // 该行的字段定位跨了 data_map，更新 rowMapOffset
                    // 但文档说"一组字段位禁止跨 data_map"，所以单段不会跨
                    // 只是该行后续字段在新 data_map 中
                    // rowMapOffset 仍指向该行第一个字段的位置
                }

                currentMap.Append(fieldLoc);
            }

            // 该行所有字段定位已写入，记录 row_map entry
            // rowMapDataMapIdx 和 rowMapOffset1Based 是处理该行之前记录的
            // 注意：如果该行第一个字段触发了新建 data_map，需要修正
            // 但 DataPacker 在写字段定位前检查是否新建 data_map，所以第一个字段
            // 不会触发新建（因为它是在记录 rowMapOffset 之后才写的）
            // 只有后续字段可能触发新建
            int dataMapIdForRow = rowMapDataMapIdx;
            int offsetInMap = rowMapOffset1Based;

            rowMapEntries.Add((dataMapIdForRow + 1, offsetInMap)); // dataMapId 存 1-based（0 为空槽）
        }

        // 生成 row_map（按 uuid 排序）：[uuid 定宽][data_map:id 1位][data_map:offset 2位]
        var rowMapSb = new StringBuilder();
        for (int i = 0; i < rowOrder.Count; i++)
        {
            var (uuid, _) = rowOrder[i];
            var (dmapId, dmapOff) = rowMapEntries[i];
            rowMapSb.Append(Base92.EncodeUInt((ulong)uuid).PadLeft(uuidWidth, Base92.ZeroChar));
            rowMapSb.Append(Encode1(dmapId));
            rowMapSb.Append(Encode2(dmapOff));
        }

        return new PackedTable
        {
            RowOrder = rowOrder,
            RowMap = rowMapSb.ToString(),
            UuidWidth = uuidWidth,
            DataMaps = mapList.Select(sb => sb.ToString()).ToList(),
            DataChunks = chunkList.Select(sb => sb.ToString()).ToList(),
            FieldNeedsTxt = fieldNeedsTxt,
            FieldArrayElemWidth = fieldArrayElemWidth,
            Table = table,
            Hash = hash,
        };
    }

    internal static List<string> SplitArray(string raw, string sep)
    {
        if (string.IsNullOrEmpty(raw)) return new List<string>();
        return raw.Split(new[] { sep }, StringSplitOptions.None).ToList();
    }

    // base92 定宽编码辅助：数位 0 = 空格。
    private static string Encode1(int v) => Base92.EncodeUInt((ulong)v).PadLeft(1, Base92.ZeroChar);
    private static string Encode2(int v) => Base92.EncodeUInt((ulong)v).PadLeft(2, Base92.ZeroChar);
    private static string Encode3(int v) => Base92.EncodeUInt((ulong)v).PadLeft(3, Base92.ZeroChar);
}
