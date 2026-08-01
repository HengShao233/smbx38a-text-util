using System;
using System.Collections.Generic;
using System.Text;

namespace TableExporter;

/// <summary>
/// 把 <see cref="PackedTable"/> 渲染为符合文档约定的 TeaScript 脚本。
/// <para>
/// 严格遵循文档 teas table util.md 的三层结构：
/// <list type="bullet">
///   <item><c>__row_map</c>：[uuid 定宽][__data_map:id 1位][__data_map:offset 2位]，uuid 有序。</item>
///   <item><c>__data_map_XX</c>：字段定位 [__data_chunk:id 2位][__data_chunk:offset 3位][length 3位] = 8 位。</item>
///   <item><c>__data_chunk_XXXX</c>：紧凑数据。</item>
/// </list>
/// 全局变量一律 <c>__</c> 前缀；工具 Script 名为 <c>__TableExport_&lt;Sheet&gt;_*</c>。
/// 所有查找（FindRow/GetDataMapLen/LoadDataMap/LoadChunkFragment）展开为写死 If 分支，无循环。
/// 不使用局部 Dim，所有临时变量用全局 temp 区。
/// 解码直接调用 CUMath_Decode（用户自行 import）。
/// </para>
/// </summary>
public sealed class TeaScriptEmitter
{
    private readonly PackedTable _packed;
    private readonly string _sheet;
    private readonly int _uuidWidth;
    private readonly int _nFields;
    private readonly int _nRows;
    private readonly List<int> _sortedUuids;
    private readonly string _prefix;

    public TeaScriptEmitter(PackedTable packed, string? prefix = null)
    {
        _packed = packed;
        _sheet = prefix ?? ToPascal(packed.Table.Name);
        _prefix = "__TableExport_" + _sheet + "_";
        _uuidWidth = packed.UuidWidth;
        _nFields = packed.Table.Fields.Count;
        _nRows = packed.Table.Rows.Count;
        _sortedUuids = new List<int>(packed.RowOrder.Count);
        foreach (var (u, _) in packed.RowOrder) _sortedUuids.Add(u);
    }

    public static string ToPascal(string s)
    {
        if (string.IsNullOrEmpty(s)) return "Table";
        var sb = new StringBuilder(s.Length);
        bool cap = true;
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(cap ? char.ToUpperInvariant(c) : c);
                cap = false;
            }
            else
            {
                cap = true;
            }
        }

        return sb.Length == 0 ? "Table" : sb.ToString();
    }

    public string Emit()
    {
        var sb = new StringBuilder();
        sb.AppendLine("' 该脚本内容由配置表导出, 禁止修改");
        sb.AppendLine("' -------- 接口列表");
        sb.AppendLine("' 真实定义放在最底下");
        AppendInterfaceList(sb);
        sb.AppendLine();
        sb.AppendLine("' 依赖(由调用方自行 import, 本脚本不内联/不 include):");
        sb.AppendLine("'   CUMath_Decode(str, start, length, 92)  <- cumath_utils.smt");
        sb.AppendLine("'   TXT(D(payload))                        <- TxtDecoder.smt");
        sb.AppendLine();

        EmitStaticData(sb);
        EmitDynamicData(sb);
        EmitGetDataMapLen(sb);
        EmitLoadDataMap(sb);
        EmitLoadChunkFragment(sb);
        EmitFindRow(sb);
        EmitSeekDataMap(sb);
        EmitSeekChunk(sb);
        EmitInterfaces(sb);

        return sb.ToString();
    }

    // -------- 接口列表注释 --------

    private void AppendInterfaceList(StringBuilder sb)
    {
        sb.Append("' Export Script ").Append(_sheet).AppendLine("_SetId(id As String)");
        for (int fi = 0; fi < _packed.Table.Fields.Count; fi++)
        {
            var f = _packed.Table.Fields[fi];
            string retType = FieldReturnType(f, fi);
            if (f.IsArray)
                sb.Append("' Export Script ").Append(_sheet).Append("_Get").Append(ToPascal(f.Name))
                  .Append("(i As Long, Return ").Append(retType).AppendLine(")");
            else
                sb.Append("' Export Script ").Append(_sheet).Append("_Get").Append(ToPascal(f.Name))
                  .Append("(Return ").Append(retType).AppendLine(")");
        }
        sb.Append("' Export Script ").Append(_sheet).AppendLine("_TryError(Return Long)");
    }

    private string FieldReturnType(FieldDef f, int fi)
    {
        if (f.Type == FieldType.Text) return "String";
        if (f.Type == FieldType.String && _packed.FieldNeedsTxt[fi]) return "String";
        if (f.Type == FieldType.String) return "String";
        if (f.Type == FieldType.Float) return "Double";
        return "Long";
    }

    // -------- 静态数据块 --------

    private void EmitStaticData(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("' -------- 静态数据块");
        sb.Append("Dim __row_map As String = \"").Append(Escape(_packed.RowMap)).AppendLine("\"");
        for (int i = 0; i < _packed.DataMaps.Count; i++)
        {
            sb.Append("Dim __data_map_").Append(i.ToString("D2")).Append(" As String = \"")
              .Append(Escape(_packed.DataMaps[i])).AppendLine("\"");
        }

        for (int i = 0; i < _packed.DataChunks.Count; i++)
        {
            sb.Append("Dim __data_chunk_").Append(i.ToString("D4")).Append(" As String = \"")
              .Append(Escape(_packed.DataChunks[i])).AppendLine("\"");
        }
    }

    // -------- 动态数据块（全局 temp 变量区，不用局部 Dim） --------

    private void EmitDynamicData(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("' -------- 动态数据块");
        sb.AppendLine("Dim __curr_id As String = \"\"");
        sb.AppendLine("Dim __curr_data_map_id As Integer = 0");
        sb.AppendLine("Dim __curr_data_map_offset As Integer = 0");
        sb.AppendLine("Dim __field_index As Long = 0");
        sb.AppendLine("Dim __field_array_index As Integer = 0");
        sb.AppendLine("Dim __curr_data_map_offset_real As Long = 0");
        sb.AppendLine("Dim __field_index_real As Long = 0");
        sb.AppendLine("Dim __data_offset As Long = 0");
        sb.AppendLine("Dim __data_length As Long = 0");
        sb.AppendLine("Dim __data_chunk As Integer = 0");
        sb.AppendLine("Dim __target_data As String = \"\"");
        sb.AppendLine("Dim __temp_str As String = \"\"");
        sb.AppendLine("Dim __last_error As Long = 0");
        sb.AppendLine("Dim __orig_data_map_id As Integer = 0");
        sb.AppendLine("Dim __orig_data_map_offset As Integer = 0");
        // temp 变量区（替代局部 Dim）
        sb.AppendLine("Dim __tmp_long As Long = 0");
        sb.AppendLine("Dim __tmp_long2 As Long = 0");
        sb.AppendLine("Dim __tmp_int As Integer = 0");
        sb.AppendLine("Dim __tmp_str As String = \"\"");
    }

    // -------- GetDataMapLen（展开式二分，无循环） --------

    private void EmitGetDataMapLen(StringBuilder sb)
    {
        sb.AppendLine();
        sb.Append("Script ").Append(_prefix).AppendLine("GetDataMapLen(idx As Integer, Return Long)");
        if (_packed.DataMaps.Count == 0)
        {
            sb.AppendLine("    Return -1");
        }
        else
        {
            EmitBinarySelectLen(sb, 0, _packed.DataMaps.Count - 1, "    ");
            sb.AppendLine("    Return -1");
        }
        sb.AppendLine("End Script");
    }

    // 展开：返回对应 data_map 的长度 (idx 是 1-based)
    private void EmitBinarySelectLen(StringBuilder sb, int lo, int hi, string indent)
    {
        int m = hi - lo + 1;
        if (m <= 0) return;
        if (m <= 3)
        {
            for (int k = lo; k <= hi; k++)
            {
                sb.Append(indent).Append("If idx = ").Append(k + 1).Append(" Then Return ")
                  .Append(_packed.DataMaps[k].Length).AppendLine();
            }
            return;
        }

        int mid = lo + m / 2;
        sb.Append(indent).Append("If idx > ").Append(mid + 1).AppendLine(" Then");
        EmitBinarySelectLen(sb, mid + 1, hi, indent + "    ");
        sb.Append(indent).AppendLine("Else");
        EmitBinarySelectLen(sb, lo, mid, indent + "    ");
        sb.Append(indent).AppendLine("End If");
    }

    // -------- LoadDataMap（展开式二分，无循环） --------

    private void EmitLoadDataMap(StringBuilder sb)
    {
        sb.AppendLine();
        sb.Append("Script ").Append(_prefix).AppendLine("LoadDataMap(idx As Integer, offset As Integer, Return Integer)");
        sb.AppendLine("    __temp_str = \"\"");
        EmitBinarySelectMap(sb, 0, _packed.DataMaps.Count - 1, "    ");
        sb.AppendLine("    If \"\" = __temp_str Then Return -1");
        // __temp_str 是从 data_map 中 offset 位置截取的 8 字符片段
        // CUMath_Decode 的 start 是 1-based，相对于 __temp_str
        sb.AppendLine("    __data_chunk = CUMath_Decode(__temp_str, 1, 2, 92)");
        sb.AppendLine("    __data_offset = CUMath_Decode(__temp_str, 3, 3, 92)");
        sb.AppendLine("    __data_length = CUMath_Decode(__temp_str, 6, 3, 92)");
        sb.AppendLine("    Return 0");
        sb.AppendLine("End Script");
    }

    // 展开：选 DataMap 串并截取 8 字符片段 (idx 是 1-based)
    private void EmitBinarySelectMap(StringBuilder sb, int lo, int hi, string indent)
    {
        int m = hi - lo + 1;
        if (m <= 0) return;
        if (m <= 3)
        {
            for (int k = lo; k <= hi; k++)
            {
                sb.Append(indent).Append("If idx = ").Append(k + 1).Append(" Then __temp_str = Mid(__data_map_")
                  .Append(k.ToString("D2")).Append(", offset, 8)").AppendLine();
            }
            return;
        }

        int mid = lo + m / 2;
        sb.Append(indent).Append("If idx > ").Append(mid + 1).AppendLine(" Then");
        EmitBinarySelectMap(sb, mid + 1, hi, indent + "    ");
        sb.Append(indent).AppendLine("Else");
        EmitBinarySelectMap(sb, lo, mid, indent + "    ");
        sb.Append(indent).AppendLine("End If");
    }

    // -------- LoadChunkFragment（展开式二分 + 数组头递归，无循环） --------

    private void EmitLoadChunkFragment(StringBuilder sb)
    {
        sb.AppendLine();
        sb.Append("Script ").Append(_prefix).AppendLine("LoadChunkFragment(checkArray As Integer, Return Integer)");
        sb.AppendLine("    __target_data = \"\"");
        EmitBinarySelectChunk(sb, 0, _packed.DataChunks.Count - 1, "    ");
        sb.AppendLine("    If \"\" = __target_data Then Return -1");
        // 数组头递归：checkArray<>0 且 __field_array_index>=0 时
        // __target_data 是数组头，根据 __field_array_index 定位元素
        sb.AppendLine("    If checkArray <> 0 Then");
        sb.AppendLine("        If __field_array_index >= 0 Then");
        // arrSeg = 1 + (__field_array_index - 1) * 8 （1-based，__field_array_index 是 1-based）
        sb.AppendLine("            __tmp_int = 1 + (__field_array_index - 1) * 8");
        sb.AppendLine("            If __tmp_int + 7 > Len(__target_data) Then Return -1");
        sb.AppendLine("            __data_chunk = CUMath_Decode(__target_data, __tmp_int, 2, 92)");
        sb.AppendLine("            __data_offset = CUMath_Decode(__target_data, __tmp_int + 2, 3, 92)");
        sb.AppendLine("            __data_length = CUMath_Decode(__target_data, __tmp_int + 5, 3, 92)");
        // 递归调用，checkArray=0
        sb.Append("            Return ").Append(_prefix).AppendLine("LoadChunkFragment(0)");
        sb.AppendLine("        End If");
        sb.AppendLine("    End If");
        sb.AppendLine("    Return 0");
        sb.AppendLine("End Script");
    }

    // 展开：选 DataChunk 串并截取片段
    private void EmitBinarySelectChunk(StringBuilder sb, int lo, int hi, string indent)
    {
        int m = hi - lo + 1;
        if (m <= 0) return;
        if (m <= 3)
        {
            for (int k = lo; k <= hi; k++)
            {
                sb.Append(indent).Append("If __data_chunk = ").Append(k).Append(" Then __target_data = Mid(__data_chunk_")
                  .Append(k.ToString("D4")).Append(", __data_offset, __data_length)").AppendLine();
            }
            return;
        }

        int mid = lo + m / 2;
        sb.Append(indent).Append("If __data_chunk > ").Append(mid).AppendLine(" Then");
        EmitBinarySelectChunk(sb, mid + 1, hi, indent + "    ");
        sb.Append(indent).AppendLine("Else");
        EmitBinarySelectChunk(sb, lo, mid, indent + "    ");
        sb.Append(indent).AppendLine("End If");
    }

    // -------- FindRow（展开式二分查找，无循环） --------

    private void EmitFindRow(StringBuilder sb)
    {
        sb.AppendLine();
        sb.Append("Script ").Append(_prefix).AppendLine("FindRow(uuid As Long, Return Integer)");
        EmitFindRowTree(sb, 1, _sortedUuids.Count, "    ");
        sb.AppendLine("    Return 0");
        sb.AppendLine("End Script");
    }

    private void EmitFindRowTree(StringBuilder sb, int lo, int hi, string indent)
    {
        int m = hi - lo + 1;
        if (m <= 0) return;
        if (m == 1)
        {
            sb.Append(indent).Append("If uuid = ").Append(_sortedUuids[lo - 1]).Append(" Then Return ").Append(lo).AppendLine();
            return;
        }

        if (m <= 3)
        {
            for (int k = lo; k <= hi; k++)
            {
                sb.Append(indent).Append("If uuid = ").Append(_sortedUuids[k - 1]).Append(" Then Return ").Append(k).AppendLine();
            }
            return;
        }

        int mid = lo + m / 2;
        sb.Append(indent).Append("If uuid > ").Append(_sortedUuids[mid - 1]).AppendLine(" Then");
        EmitFindRowTree(sb, mid + 1, hi, indent + "    ");
        sb.Append(indent).AppendLine("Else");
        EmitFindRowTree(sb, lo, mid, indent + "    ");
        sb.Append(indent).AppendLine("End If");
    }

    // -------- SeekDataMap --------

    private void EmitSeekDataMap(StringBuilder sb)
    {
        var h = _packed.Hash;
        int entryWidth = _uuidWidth + 3;
        sb.AppendLine();
        sb.Append("Script ").Append(_prefix).AppendLine("SeekDataMap(Return Integer)");
        // uuid = seed; for each char: uuid = (uuid * multiplier + Asc(char)) Mod tableSize
        // 用全局 temp 变量，不用局部 Dim
        sb.AppendLine("    __tmp_long = " + h.Seed);
        sb.AppendLine("    For __tmp_int = 1 To Len(__curr_id) Step 1");
        sb.AppendLine("        __tmp_long = (__tmp_long * " + h.Multiplier + " + Asc(Mid(__curr_id, __tmp_int, 1))) Mod " + h.TableSize);
        sb.AppendLine("    Next");
        sb.Append("    __tmp_int = ").Append(_prefix).AppendLine("FindRow(__tmp_long)");
        sb.AppendLine("    If __tmp_int <= 0 Then Return -1");
        // base = (entry - 1) * entryWidth + 1 （1-based）
        sb.AppendLine("    __tmp_long = (__tmp_int - 1) * " + entryWidth + " + 1");
        sb.AppendLine("    __curr_data_map_id = CUMath_Decode(__row_map, __tmp_long + " + _uuidWidth + ", 1, 92)");
        sb.AppendLine("    __curr_data_map_offset = CUMath_Decode(__row_map, __tmp_long + " + (_uuidWidth + 1) + ", 2, 92)");
        sb.AppendLine("    Return 0");
        sb.AppendLine("End Script");
    }

    // -------- SeekChunk（跨 data_map 处理） --------

    private void EmitSeekChunk(StringBuilder sb)
    {
        sb.AppendLine();
        sb.Append("Script ").Append(_prefix).AppendLine("SeekChunk(Return Integer)");
        sb.AppendLine("    __curr_data_map_offset_real = __curr_data_map_offset");
        sb.AppendLine("    __field_index_real = __field_index");
        sb.Append("    __tmp_long = ").Append(_prefix).AppendLine("GetDataMapLen(__curr_data_map_id)");
        sb.AppendLine("    If __tmp_long < 0 Then Return -1");
        // 跨 data_map 检查: offset_real + field_index_real * 8 + 7 > mapLen
        sb.AppendLine("    If __curr_data_map_offset_real + __field_index_real * 8 + 7 > __tmp_long Then");
        // remaining = min(field_index_real, (mapLen - offset_real + 1) / 8)
        sb.AppendLine("        __tmp_long2 = (__tmp_long - __curr_data_map_offset_real + 1) \\ 8");
        sb.AppendLine("        If __tmp_long2 > __field_index_real Then __tmp_long2 = __field_index_real");
        sb.AppendLine("        __field_index_real = __field_index_real - __tmp_long2");
        sb.AppendLine("        __curr_data_map_id = __curr_data_map_id + 1");
        sb.AppendLine("        __curr_data_map_offset_real = 1");
        sb.Append("        __tmp_long = ").Append(_prefix).AppendLine("GetDataMapLen(__curr_data_map_id)");
        sb.AppendLine("        If __tmp_long < 0 Then Return -1");
        sb.AppendLine("    End If");
        // seg = offset_real + field_index_real * 8
        sb.AppendLine("    __tmp_long = __curr_data_map_offset_real + __field_index_real * 8");
        sb.Append("    Return ").Append(_prefix).AppendLine("LoadDataMap(__curr_data_map_id, __tmp_long)");
        sb.AppendLine("End Script");
    }

    // -------- 字段接口 --------

    private void EmitInterfaces(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("' -------- 字段接口实现");

        // SetId
        sb.Append("Export Script ").Append(_sheet).AppendLine("_SetId(id As String)");
        sb.AppendLine("    __curr_id = id");
        sb.AppendLine("    __last_error = 0");
        sb.Append("    If ").Append(_prefix).AppendLine("SeekDataMap() <> 0 Then");
        sb.AppendLine("        __curr_data_map_id = -1");
        sb.AppendLine("        __last_error = -1");
        sb.AppendLine("    End If");
        sb.AppendLine("    __orig_data_map_id = __curr_data_map_id");
        sb.AppendLine("    __orig_data_map_offset = __curr_data_map_offset");
        sb.AppendLine("End Script");
        sb.AppendLine();

        for (int f = 0; f < _nFields; f++)
        {
            var def = _packed.Table.Fields[f];
            EmitFieldGetter(sb, def, f);
        }

        // TryError
        sb.Append("Export Script ").Append(_sheet).AppendLine("_TryError(Return Long)");
        sb.AppendLine("    Return __last_error");
        sb.AppendLine("End Script");
    }

    private void EmitFieldGetter(StringBuilder sb, FieldDef def, int f)
    {
        string getterName = _sheet + "_Get" + ToPascal(def.Name);
        bool isStringType = def.Type == FieldType.String || def.Type == FieldType.Text;

        if (def.IsArray)
        {
            EmitArrayGetter(sb, def, f, getterName, isStringType);
            return;
        }

        // 标量字段
        string retType = FieldReturnType(def, f);
        sb.Append("Export Script ").Append(getterName).Append("(Return ").Append(retType).AppendLine(")");
        // 恢复原始 dataMapId/offset（避免被前一个字段污染）
        sb.AppendLine("    __curr_data_map_id = __orig_data_map_id");
        sb.AppendLine("    __curr_data_map_offset = __orig_data_map_offset");
        sb.Append("    __field_index = ").Append(f).AppendLine();
        sb.AppendLine("    __field_array_index = -1");
        sb.Append("    If ").Append(_prefix).AppendLine("SeekChunk() <> 0 Then");
        sb.AppendLine("        __last_error = -2");
        sb.Append("        Return ").Append(isStringType ? "\"\"" : "0").AppendLine();
        sb.AppendLine("    End If");
        sb.Append("    If ").Append(_prefix).AppendLine("LoadChunkFragment(0) <> 0 Then");
        sb.AppendLine("        __last_error = -3");
        sb.Append("        Return ").Append(isStringType ? "\"\"" : "0").AppendLine();
        sb.AppendLine("    End If");

        if (isStringType)
        {
            // String/text: 返回 payload 原文，拼接空串避免崩溃
            sb.AppendLine("    Return __target_data & \"\"");
        }
        else if (def.Type == FieldType.Int || def.Type == FieldType.Bool || def.Type == FieldType.Ref)
        {
            // zigzag 反变换（Bool/Ref 值非负，zigzag 不变）
            EmitZigZagDecode(sb, "    ");
            sb.AppendLine("    Return __tmp_long");
        }
        else if (def.Type == FieldType.Float)
        {
            EmitZigZagDecode(sb, "    ");
            sb.AppendLine("    Return __tmp_long / 10000.0");
        }
        else
        {
            sb.AppendLine("    Return __tmp_long");
        }

        sb.AppendLine("End Script");
        sb.AppendLine();
    }

    private void EmitZigZagDecode(StringBuilder sb, string indent)
    {
        // zz = CUMath_Decode(target, 1, len, 92)
        // sg = zz And 1
        // ab = zz \ 2
        // result = ab; if sg=1 then result = -(ab+1)
        sb.Append(indent).AppendLine("__tmp_long = CUMath_Decode(__target_data, 1, Len(__target_data), 92)");
        sb.Append(indent).AppendLine("__tmp_long2 = __tmp_long And 1");
        sb.Append(indent).AppendLine("__tmp_long = __tmp_long \\ 2");
        sb.Append(indent).AppendLine("If __tmp_long2 = 1 Then __tmp_long = -(__tmp_long + 1)");
    }

    private void EmitArrayGetter(StringBuilder sb, FieldDef def, int f, string getterName, bool isStringType)
    {
        string retType = isStringType ? "String" : "Long";

        // Get<field>(i)
        sb.Append("Export Script ").Append(getterName).Append("(i As Long, Return ").Append(retType).AppendLine(")");
        sb.AppendLine("    __curr_data_map_id = __orig_data_map_id");
        sb.AppendLine("    __curr_data_map_offset = __orig_data_map_offset");
        sb.Append("    __field_index = ").Append(f).AppendLine();
        sb.AppendLine("    __field_array_index = i");
        sb.Append("    If ").Append(_prefix).AppendLine("SeekChunk() <> 0 Then");
        sb.AppendLine("        __last_error = -2");
        sb.Append("        Return ").Append(isStringType ? "\"\"" : "0").AppendLine();
        sb.AppendLine("    End If");
        // checkArray=1 触发数组头递归
        sb.Append("    If ").Append(_prefix).AppendLine("LoadChunkFragment(1) <> 0 Then");
        sb.AppendLine("        __last_error = -3");
        sb.Append("        Return ").Append(isStringType ? "\"\"" : "0").AppendLine();
        sb.AppendLine("    End If");

        if (isStringType)
        {
            sb.AppendLine("    Return __target_data & \"\"");
        }
        else
        {
            EmitZigZagDecode(sb, "    ");
            sb.AppendLine("    Return __tmp_long");
        }

        sb.AppendLine("End Script");
        sb.AppendLine();

        // Get<field>Len
        string lenName = getterName + "Len";
        sb.Append("Export Script ").Append(lenName).AppendLine("(Return Long)");
        sb.AppendLine("    __curr_data_map_id = __orig_data_map_id");
        sb.AppendLine("    __curr_data_map_offset = __orig_data_map_offset");
        sb.Append("    __field_index = ").Append(f).AppendLine();
        sb.AppendLine("    __field_array_index = -1");
        sb.Append("    If ").Append(_prefix).AppendLine("SeekChunk() <> 0 Then Return 0");
        sb.Append("    If ").Append(_prefix).AppendLine("LoadChunkFragment(0) <> 0 Then Return 0");
        // 数组头是 N*8 字符的定位表，元素个数 = Len / 8
        sb.AppendLine("    Return Len(__target_data) \\ 8");
        sb.AppendLine("End Script");
        sb.AppendLine();
    }

    private string Escape(string s) => s.Replace("\"", "\"\"");
}
