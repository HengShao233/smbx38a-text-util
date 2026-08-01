using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TableExporter;

/// <summary>
/// 模拟 SMBX 38A TeaScript 运行时执行生成的 .smt 表脚本。
/// <para>
/// 这是一个针对性模拟器（非通用解释器）：它解析生成的 .smt 中的全局变量数据，
/// 然后按文档定义的调用链 (SetId → SeekDataMap → FindRow → SeekChunk → LoadDataMap → LoadChunkFragment)
/// 逐步执行，验证打包/发射的正确性。
/// </para>
/// <para>
/// CUMath_Decode 的行为严格复刻 cumath_utils.smt 中的实现（无符号 base92）。
/// </para>
/// </summary>
public sealed class TeaScriptSimulator
{
    private readonly string _sheet;
    private readonly string _rowMap;
    private readonly int _uuidWidth;
    private readonly List<string> _dataMaps = new();
    private readonly List<string> _dataChunks = new();
    private readonly PerfectHash _hash;
    private readonly List<FieldDef> _fields;
    private readonly List<bool> _fieldNeedsTxt;

    // 运行时状态
    private string _currId = "";
    private int _currDataMapId = 0;
    private int _currDataMapOffset = 0;
    private long _fieldIndex = 0;
    private int _fieldArrayIndex = 0;
    private long _currDataMapOffsetReal = 0;
    private long _fieldIndexReal = 0;
    private long _dataOffset = 0;
    private long _dataLength = 0;
    private int _dataChunk = 0;
    private string _targetData = "";
    private string _tempStr = "";
    private long _lastError = 0;
    // SetId 时的原始值，每次 LoadField 前恢复
    private int _origDataMapId = 0;
    private int _origDataMapOffset = 0;

    private TeaScriptSimulator(string sheet, string rowMap, int uuidWidth,
        List<string> dataMaps, List<string> dataChunks,
        PerfectHash hash, List<FieldDef> fields, List<bool> fieldNeedsTxt)
    {
        _sheet = sheet;
        _rowMap = rowMap;
        _uuidWidth = uuidWidth;
        _dataMaps = dataMaps;
        _dataChunks = dataChunks;
        _hash = hash;
        _fields = fields;
        _fieldNeedsTxt = fieldNeedsTxt;
    }

    /// <summary>
    /// 从生成的 .smt 脚本文本解析出模拟器实例。
    /// </summary>
    public static TeaScriptSimulator Parse(string smt, TableData table, PerfectHash hash)
    {
        string sheet = TeaScriptEmitter.ToPascal(table.Name);
        string? rowMapRaw = ExtractStringVar(smt, "__row_map");
        if (rowMapRaw == null) throw new FormatException("Cannot find __row_map in smt");
        string rowMap = rowMapRaw;
        int uuidWidth = GuessUuidWidth(table, hash);

        var dataMaps = new List<string>();
        for (int i = 0; ; i++)
        {
            var v = ExtractStringVar(smt, $"__data_map_{i:D2}");
            if (v == null) break;
            dataMaps.Add(v);
        }

        var dataChunks = new List<string>();
        for (int i = 0; ; i++)
        {
            var v = ExtractStringVar(smt, $"__data_chunk_{i:D4}");
            if (v == null) break;
            dataChunks.Add(v);
        }

        var fieldNeedsTxt = new List<bool>();
        foreach (var f in table.Fields)
        {
            bool needsTxt = f.Type == FieldType.Text;
            if (f.Type == FieldType.String)
            {
                needsTxt = table.Rows.Any(r =>
                {
                    if (!r.Cells.TryGetValue(f.Name, out var v)) return false;
                    foreach (var c in v) if (c > 127) return true;
                    return false;
                });
            }
            fieldNeedsTxt.Add(needsTxt);
        }

        return new TeaScriptSimulator(sheet, rowMap, uuidWidth, dataMaps, dataChunks,
            hash, table.Fields, fieldNeedsTxt);
    }

    private static int GuessUuidWidth(TableData table, PerfectHash hash)
    {
        int nRows = table.Rows.Count;
        if (nRows == 0) return 1;
        int maxUuid = 0;
        foreach (var row in table.Rows)
            maxUuid = Math.Max(maxUuid, hash.Compute(row.Id));
        int w = 1;
        while (Base92.EncodeUInt((ulong)maxUuid).Length > w) w++;
        return Math.Min(w, 4);
    }

    private static string? ExtractStringVar(string smt, string varName)
    {
        // 匹配 Dim __row_map As String = "..."
        // 字符串中的双引号转义为 ""
        var pattern = $@"Dim\s+{Regex.Escape(varName)}\s+As\s+String\s*=\s*""((?:[^""]|"""")*)""";
        var m = Regex.Match(smt, pattern);
        if (!m.Success) return null;
        return m.Groups[1].Value.Replace("\"\"", "\"");
    }

    // -------- 公开接口（模拟 Export Script） --------

    public void SetId(string id)
    {
        _currId = id;
        _lastError = 0;
        if (SeekDataMap() != 0)
        {
            _currDataMapId = -1;
            _lastError = -1;
        }
        _origDataMapId = _currDataMapId;
        _origDataMapOffset = _currDataMapOffset;
    }

    public long TryError() => _lastError;

    public long GetInt(string fieldName) => GetLongZigZag(fieldName);
    public double GetFloat(string fieldName) => GetLongZigZag(fieldName) / 10000.0;
    public long GetBool(string fieldName) => GetLongZigZag(fieldName);
    public long GetRef(string fieldName) => GetLongZigZag(fieldName);
    public string GetString(string fieldName)
    {
        LoadField(fieldName);
        return _targetData;
    }

    // 诊断用：返回最后一次 LoadField 后的内部状态
    public string DumpState() =>
        $"mapId={_currDataMapId} mapOff={_currDataMapOffset} chunk={_dataChunk} off={_dataOffset} len={_dataLength} targetLen={_targetData.Length}";

    public long GetArrayLen(string fieldName)
    {
        int fi = FieldIndex(fieldName);
        _currDataMapId = _origDataMapId;
        _currDataMapOffset = _origDataMapOffset;
        _fieldIndex = fi;
        _fieldArrayIndex = -1;
        int sc = SeekChunk();
        if (sc != 0) { _diagMsg = $"GetArrayLen SeekChunk failed: {DumpState()}"; return 0; }
        int lc = LoadChunkFragment(0);
        if (lc != 0) { _diagMsg = $"GetArrayLen LoadChunkFragment failed: {DumpState()}"; return 0; }
        return _targetData.Length / 8;
    }

    public string DiagMsg => _diagMsg;
    private string _diagMsg = "";

    public long GetArrayInt(string fieldName, int i)
    {
        LoadArrayElement(fieldName, i);
        if (string.IsNullOrEmpty(_targetData) || _targetData.Length < 1) return 0;
        return ZigZagDecode(_targetData);
    }

    public string GetArrayString(string fieldName, int i)
    {
        LoadArrayElement(fieldName, i);
        return _targetData;
    }

    // -------- 内部执行逻辑 --------

    private long GetLongZigZag(string fieldName)
    {
        LoadField(fieldName);
        return ZigZagDecode(_targetData);
    }

    private long GetLong(string fieldName)
    {
        LoadField(fieldName);
        var def = _fields[FieldIndex(fieldName)];
        if (def.Type == FieldType.Int || def.Type == FieldType.Float || def.Type == FieldType.Ref || def.Type == FieldType.Bool)
            return ZigZagDecode(_targetData);
        // String/text: 不应调此方法
        return CumathDecode(_targetData, 1, _targetData.Length, 92);
    }

    private void LoadField(string fieldName)
    {
        _currDataMapId = _origDataMapId;
        _currDataMapOffset = _origDataMapOffset;
        int fi = FieldIndex(fieldName);
        _fieldIndex = fi;
        _fieldArrayIndex = -1;
        SeekChunk();
        LoadChunkFragment(0);
    }

    private void LoadArrayElement(string fieldName, int i)
    {
        _currDataMapId = _origDataMapId;
        _currDataMapOffset = _origDataMapOffset;
        int fi = FieldIndex(fieldName);
        _fieldIndex = fi;
        _fieldArrayIndex = i;
        SeekChunk();
        LoadChunkFragment(1);
    }

    private int FieldIndex(string fieldName)
    {
        for (int i = 0; i < _fields.Count; i++)
            if (string.Equals(_fields[i].Name, fieldName, StringComparison.OrdinalIgnoreCase))
                return i;
        throw new ArgumentException($"Unknown field: {fieldName}");
    }

    /// <summary>模拟 SeekDataMap: 计算 uuid, 二分查找 row_map, 解析 data_map_id 和 offset。</summary>
    private int SeekDataMap()
    {
        long uuid = _hash.Seed;
        for (int i = 0; i < _currId.Length; i++)
        {
            uuid = (uuid * _hash.Multiplier + (int)_currId[i]) % _hash.TableSize;
        }

        int entry = FindRow((int)uuid);
        _lastEntry = entry;
        _lastUuid = (int)uuid;
        if (entry <= 0) return -1;

        int entryWidth = _uuidWidth + 3;
        int base1 = (entry - 1) * entryWidth + 1; // 1-based
        _currDataMapId = (int)CumathDecode(_rowMap, base1 + _uuidWidth, 1, 92);
        _currDataMapOffset = (int)CumathDecode(_rowMap, base1 + _uuidWidth + 1, 2, 92);
        return 0;
    }

    public int LastEntry => _lastEntry;
    public int LastUuid => _lastUuid;
    private int _lastEntry = 0;
    private int _lastUuid = 0;

    /// <summary>模拟 FindRow: 在有序 uuid 列表中二分查找。</summary>
    private int FindRow(int uuid)
    {
        int entryWidth = _uuidWidth + 3;
        int nEntries = _rowMap.Length / entryWidth;
        int lo = 0, hi = nEntries - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int baseIdx = mid * entryWidth;
            string uuidStr = _rowMap.Substring(baseIdx, _uuidWidth);
            int storedUuid = (int)Base92.DecodeUInt(uuidStr);
            if (storedUuid == uuid) return mid + 1; // 1-based
            if (storedUuid < uuid) lo = mid + 1;
            else hi = mid - 1;
        }
        return 0;
    }

    /// <summary>模拟 SeekChunk: 处理跨 data_map, 调用 LoadDataMap。</summary>
    private int SeekChunk()
    {
        _currDataMapOffsetReal = _currDataMapOffset;
        _fieldIndexReal = _fieldIndex;
        long mapLen = GetDataMapLen(_currDataMapId);
        if (mapLen < 0) return -1;

        // 跨 data_map 检查
        if (_currDataMapOffsetReal + _fieldIndexReal * 8 + 7 > mapLen)
        {
            // remaining = 该行在旧 data_map 中的字段数
            // 不能超过该行从 field 0 到当前 field 的数量
            long availSlots = (mapLen - _currDataMapOffsetReal + 1) / 8;
            long remaining = Math.Min(_fieldIndexReal, availSlots);
            _fieldIndexReal -= remaining;
            _currDataMapId += 1;
            _currDataMapOffsetReal = 1;
            mapLen = GetDataMapLen(_currDataMapId);
            if (mapLen < 0) return -1;
        }

        long seg = _currDataMapOffsetReal + _fieldIndexReal * 8;
        return LoadDataMap(_currDataMapId, (int)seg);
    }

    private long GetDataMapLen(int idx)
    {
        // idx 是 1-based
        int i = idx - 1;
        if (i < 0 || i >= _dataMaps.Count) return -1;
        return _dataMaps[i].Length;
    }

    /// <summary>模拟 LoadDataMap: 截取 8 字符片段, 解码 chunkId/offset/length。</summary>
    private int LoadDataMap(int idx, int offset)
    {
        _tempStr = "";
        int i = idx - 1; // 1-based to 0-based
        if (i < 0 || i >= _dataMaps.Count) return -1;
        var dm = _dataMaps[i];
        if (offset < 1 || offset + 7 > dm.Length) return -1;
        _tempStr = dm.Substring(offset - 1, 8);
        if (_tempStr == "") return -1;
        _dataChunk = (int)CumathDecode(_tempStr, 1, 2, 92);
        _dataOffset = CumathDecode(_tempStr, 3, 3, 92);
        _dataLength = CumathDecode(_tempStr, 6, 3, 92);
        return 0;
    }

    /// <summary>模拟 LoadChunkFragment: 从 chunk 截取片段, 支持数组头递归。</summary>
    private int LoadChunkFragment(int checkArray)
    {
        _targetData = "";
        if (_dataChunk < 0 || _dataChunk >= _dataChunks.Count) return -1;
        var chunk = _dataChunks[_dataChunk];
        if (_dataOffset < 1 || _dataOffset > chunk.Length) return -1;
        int start = (int)_dataOffset - 1;
        int len = (int)_dataLength;
        if (start + len > chunk.Length) len = chunk.Length - start;
        _targetData = chunk.Substring(start, len);
        if (_targetData == "") return -1;

        if (checkArray != 0 && _fieldArrayIndex >= 0)
        {
            string ah = _targetData;
            int arrSeg = 1 + (_fieldArrayIndex - 1) * 8; // 1-based, _fieldArrayIndex is 1-based
            if (arrSeg + 7 > ah.Length) return -1;
            _dataChunk = (int)CumathDecode(ah, arrSeg, 2, 92);
            _dataOffset = CumathDecode(ah, arrSeg + 2, 3, 92);
            _dataLength = CumathDecode(ah, arrSeg + 5, 3, 92);
            return LoadChunkFragment(0);
        }
        return 0;
    }

    /// <summary>
    /// 模拟 CUMath_Decode(s, start, length, base)。
    /// start 是 1-based, base92 无符号。
    /// </summary>
    private static long CumathDecode(string s, int start, int length, int baseVal)
    {
        if (start < 1) start = 1;
        if (start + length - 1 > s.Length) length = s.Length - start + 1;
        if (length <= 0) return -1;
        if (baseVal < 2 || baseVal > 92) return -1;

        start--; // 转 0-based
        long result = 0;
        for (int h = 1; h <= length; h++)
        {
            int charCode = (int)s[start + h - 1];
            int digitVal = Char92Code(charCode);
            if (digitVal < 0) return -1;
            result += digitVal * SimplePow(baseVal, length - h);
        }
        return result;
    }

    private static int Char92Code(int asc)
    {
        // cumath_utils.smt 的 Char92Code: ASCII 0x20-0x7E 去掉 " \ `
        // 数位 0 = 空格 (0x20)
        if (asc < 0x20 || asc > 0x7E) return -1;
        if (asc == '"' || asc == '\\' || asc == '`') return -1;
        int code = asc - 0x20;
        // 跳过被排除的字符
        if (asc > '"') code--;
        if (asc > '\\') code--;
        if (asc > '`') code--;
        return code;
    }

    private static long SimplePow(int baseVal, int exp)
    {
        long result = 1;
        for (int i = 0; i < exp; i++) result *= baseVal;
        return result;
    }

    private static long ZigZagDecode(string data)
    {
        if (string.IsNullOrEmpty(data)) return 0;
        long zz = CumathDecode(data, 1, data.Length, 92);
        if (zz < 0) return 0; // CUMath_Decode 返回 -1 表示错误
        long sg = zz & 1;
        long ab = zz / 2; // 整除
        return sg == 1 ? -(ab + 1) : ab;
    }
}
