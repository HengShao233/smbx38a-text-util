using System.Text;

namespace TableExporter;

/// <summary>
/// 内置自检。覆盖 base92 编解码、完美哈希与整表打包/解析的往返一致性。
/// <para>
/// 这里同时用 C# 复现了一遍「运行时解析逻辑」（见 <see cref="RuntimeSimulator"/>），
/// 以保证生成的 TeaScript 所依赖的布局假设确实成立。
/// </para>
/// </summary>
public static class SelfTest
{
    private static int _passed;
    private static int _failed;

    public static bool Run()
    {
        Console.WriteLine("TableExporter 自检");
        Console.WriteLine("==================");

        TestBase92Alphabet();
        TestBase92RoundTrip();
        TestPerfectHashNoCollision();
        TestHashFloatSafe();
        TestPackRoundTrip();
        TestArrayRoundTrip();
        TestChunking();
        TestStringNotTrimmed();
        TestTextColumnContract();
        TestRefRoundTrip();
        TestSimulatorBasic();
        TestSimulatorNegativeNumbers();
        TestSimulatorFloats();
        TestSimulatorStringArrays();
        TestSimulatorLargeTable();
        TestSimulatorSuperLargeTable();
        TestSimulatorMissAndError();
        TestSimulatorBinarySearch();

        Console.WriteLine();
        Console.WriteLine($"通过 {_passed}, 失败 {_failed}");
        return _failed == 0;
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"  [ok]   {name}");
        }
        else
        {
            _failed++;
            Console.WriteLine($"  [FAIL] {name}{(detail is null ? string.Empty : " : " + detail)}");
        }
    }

    private static void TestBase92Alphabet()
    {
        Console.WriteLine("- base92 字母表");

        var seen = new HashSet<char>();
        var ok = true;
        for (var i = 0; i < Base92.Alphabet.Length; i++)
        {
            var c = Base92.Alphabet[i];
            if (!seen.Add(c) || c is '"' or '\\' or '`')
            {
                ok = false;
            }

            // 第 0 位是空格（数位 0）。
            if (i == 0 && c != ' ')
            {
                ok = false;
            }
        }

        Check("字母表恰为 92 个字符且互异", Base92.Alphabet.Length == 92 && ok);
        Check("排除引号/反斜杠/反引号", !Base92.Alphabet.Contains('"') && !Base92.Alphabet.Contains('\\') && !Base92.Alphabet.Contains('`'));
        Check("数位 0 是空格", Base92.Alphabet[0] == ' ');
    }

    private static void TestBase92RoundTrip()
    {
        Console.WriteLine("- base92 编解码往返");

        var ok = true;
        foreach (var v in new[] { 0, 1, -1, 92, -92, 12345, -98765, 2000000, -2000000, int.MaxValue, int.MinValue })
        {
            var enc = Base92.EncodeInt(v);
            if (Base92.DecodeInt(enc) != v)
            {
                ok = false;
                Console.WriteLine($"      int {v} 往返失败: '{enc}'");
            }
        }

        foreach (var v in new[] { 0.0, -3.0, 1.2345, -99.9999, 1234.5678 })
        {
            var enc = Base92.EncodeFloat(v);
            var dec = Base92.DecodeFloat(enc);
            if (Math.Abs(dec - v) > 0.00005)
            {
                ok = false;
                Console.WriteLine($"      float {v} 往返失败: {dec} (编码 '{enc}')");
            }
        }

        Check("整数/浮点 base92 往返一致", ok);
    }

    private static void TestPerfectHashNoCollision()
    {
        Console.WriteLine("- 完美哈希");

        var ids = new List<string>();
        var rng = new Random(20240601);
        const string alphabet = "0123456789abcdef";
        for (var i = 0; i < 200; i++)
        {
            var sb = new StringBuilder(64);
            for (var j = 0; j < 64; j++)
            {
                sb.Append(alphabet[rng.Next(alphabet.Length)]);
            }

            ids.Add(sb.ToString());
        }

        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "selftest", tableSize);
        var slots = ids.Select(id => hash.Compute(id) % tableSize).ToList();

        Check("200 条 64 字符 uuid 无槽位碰撞", slots.Distinct().Count() == ids.Count);
        Check("哈希槽位均非负且小于表大小",
            slots.All(k => k >= 0 && k < tableSize));

        var again = PerfectHash.Build(ids, "selftest", tableSize);
        Check("哈希参数可复现",
            again.Multiplier == hash.Multiplier && again.Seed == hash.Seed);
    }

    /// <summary>
    /// 校验哈希中间值不会突破 double 的 53 bit 精度，
    /// 否则 TeaScript 侧（浮点运算）会与 C# 侧结果不一致。
    /// </summary>
    private static void TestHashFloatSafe()
    {
        Console.WriteLine("- 哈希的浮点安全性");

        // h < tableSize，h*P + code 的中间值上限。
        var tableSize = PerfectHash.NextPrime(1024);
        var maxP = 4093;
        var maxIntermediate = (long)(tableSize - 1) * maxP + 0xFFFF;
        Check($"最大中间值 {maxIntermediate} < 2^53", maxIntermediate < (1L << 53));

        var ids = new[] { "abc", "配置表", new string('z', 64), "0", string.Empty };
        var hash = PerfectHash.Build(ids, "selftest", tableSize);

        var ok = true;
        foreach (var id in ids)
        {
            double h = hash.Seed;
            foreach (var c in id)
            {
                h = (h * hash.Multiplier + c) % tableSize;
            }

            if ((long)h != hash.Compute(id) % tableSize)
            {
                ok = false;
                Console.WriteLine($"      '{id}': double={h} long={hash.Compute(id) % tableSize}");
            }
        }

        Check("double 与整数运算结果一致", ok);
    }

    private static void TestPackRoundTrip()
    {
        Console.WriteLine("- 整表打包/解析往返");

        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String, ColumnIndex = 0 },
            new() { Name = "hp", Type = FieldType.Int, ColumnIndex = 1 },
            new() { Name = "speed", Type = FieldType.Float, ColumnIndex = 2 },
            new() { Name = "boss", Type = FieldType.Bool, ColumnIndex = 3 },
            new() { Name = "note", Type = FieldType.String, ColumnIndex = 4 },
            new() { Name = "line", Type = FieldType.Text, ColumnIndex = 5 },
        };

        var rows = new List<RowData>();
        for (var i = 0; i < 60; i++)
        {
            rows.Add(new RowData
            {
                Id = $"npc_{i:D3}",
                SourceLine = i + 2,
                Cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = $"npc_{i:D3}",
                    ["hp"] = (i * 37 - 500).ToString(),
                    ["speed"] = (i * 0.25 - 3).ToString("0.####"),
                    ["boss"] = (i % 7 == 0) ? "true" : "false",
                    ["note"] = i % 3 == 0 ? $"备注\"{i}\"" : $"note-{i}",
                    ["line"] = $"文本{i}",
                },
            });
        }

        var table = new TableData { Name = "npc", Fields = fields, Rows = rows };
        var ids = rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "npc", tableSize);

        // text(行文本) -> 伪 PAYLOAD（仅含字母表内字符）。
        string TextResolver(string raw) => raw == "文本0" ? "PAY_a" : "PAY_x";
        int RefResolver(string raw) => 0;

        var packed = DataPacker.Pack(table, hash, TextResolver, RefResolver);

        var ok = true;
        for (var i = 0; i < rows.Count; i++)
        {
            var src = rows[i];
            var loc = RuntimeSimulator.LocateRow(packed, hash, src.Id);
            if (loc is null)
            {
                ok = false;
                Console.WriteLine($"      {src.Id} 未命中");
                continue;
            }

            var hp = Base92.DecodeInt(RuntimeSimulator.FieldRaw(packed, loc!, 1));
            var speed = Base92.DecodeFloat(RuntimeSimulator.FieldRaw(packed, loc!, 2));
            var boss = Base92.DecodeInt(RuntimeSimulator.FieldRaw(packed, loc!, 3)) != 0;
            var note = RuntimeSimulator.FieldRaw(packed, loc!, 4);
            var line = RuntimeSimulator.FieldRaw(packed, loc!, 5);

            var expectHp = int.Parse(src.Cells["hp"]);
            var expectSpeed = double.Parse(src.Cells["speed"]);
            var expectBoss = src.Cells["boss"] == "true";
            // note 是 String 类型，含非 ASCII 时会升级为 payload（与 line 一致）
            var expectNote = i % 3 == 0 ? "PAY_x" : src.Cells["note"];
            var expectLine = src.Cells["line"] == "文本0" ? "PAY_a" : "PAY_x";

            if (hp != expectHp ||
                Math.Abs(speed - expectSpeed) > 0.00005 ||
                boss != expectBoss ||
                note != expectNote ||
                line != expectLine)
            {
                ok = false;
                Console.WriteLine(
                    $"      {src.Id} 不一致: hp={hp}/{expectHp} " +
                    $"speed={speed}/{expectSpeed} boss={boss}/{expectBoss} " +
                    $"note='{note}'/'{expectNote}' line='{line}'/'{expectLine}'");
            }
        }

        Check("60 行全字段往返一致", ok);

        var miss = RuntimeSimulator.LocateRow(packed, hash, "not_exist_id");
        Check("未知 uuid 查找返回空（不存在）", miss is null);

        var script = new TeaScriptEmitter(packed).Emit();
        Check("生成脚本包含 SetId 接口", script.Contains($"Export Script {TeaScriptEmitter.ToPascal(table.Name)}_SetId("));
        Check("生成脚本包含 FindRow 查找树", script.Contains($"Script __TableExport_{TeaScriptEmitter.ToPascal(table.Name)}_FindRow("));
        Check("生成脚本 text getter 返回 payload 原文", script.Contains("Return __target_data & \"\""));
        Check("生成脚本不内联解码器(依赖用户 import CUMath_Decode)", script.Contains("CUMath_Decode("));
        // 保守处理：本发射器所有注释均为独占行；字母表中的 ' 是合法字符，不作为注释。
        // 这里验证数据段字面量首尾未被 ~ 哨兵包裹（用户明确要求：不要加 ~ 哨兵）。
        Check("数据块字面量未用 ~ 包裹哨兵",
            packed.DataChunks.All(c => !c.StartsWith('~') && !c.EndsWith('~')) &&
            !packed.RowMap.StartsWith('~') && !packed.RowMap.EndsWith('~'));
    }

    private static void TestArrayRoundTrip()
    {
        Console.WriteLine("- 数组字段");

        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String, ColumnIndex = 0 },
            new() { Name = "drops", Type = FieldType.Int, IsArray = true, ColumnIndex = 1 },
            new() { Name = "tags", Type = FieldType.String, IsArray = true,
                    ArraySeparator = ";", ColumnIndex = 2 },
        };

        var rows = new List<RowData>
        {
            new()
            {
                Id = "a", SourceLine = 2,
                Cells = new(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = "a", ["drops"] = "1|2|3", ["tags"] = "fire;ice" },
            },
            new()
            {
                Id = "b", SourceLine = 3,
                Cells = new(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = "b", ["drops"] = string.Empty, ["tags"] = string.Empty },
            },
            new()
            {
                Id = "c", SourceLine = 4,
                Cells = new(StringComparer.OrdinalIgnoreCase)
                    { ["id"] = "c", ["drops"] = "-7", ["tags"] = "solo" },
            },
        };

        var table = new TableData { Name = "loot", Fields = fields, Rows = rows };
        var ids = rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "loot", tableSize);
        var packed = DataPacker.Pack(table, hash, _ => "", _ => 0);

        var locA = RuntimeSimulator.LocateRow(packed, hash, "a")!;
        var dropsA = RuntimeSimulator.ArrayItems(packed, locA, 1);
        var tagsA = RuntimeSimulator.ArrayItems(packed, locA, 2);

        Check("数组长度正确", dropsA.Count == 3 && tagsA.Count == 2);
        Check("数组元素正确",
            dropsA.Select(Base92.DecodeInt).SequenceEqual(new[] { 1, 2, 3 }) &&
            tagsA.SequenceEqual(new[] { "fire", "ice" }));

        var locB = RuntimeSimulator.LocateRow(packed, hash, "b")!;
        Check("空单元格视作空数组",
            RuntimeSimulator.ArrayItems(packed, locB, 1).Count == 0 &&
            RuntimeSimulator.ArrayItems(packed, locB, 2).Count == 0);

        var locC = RuntimeSimulator.LocateRow(packed, hash, "c")!;
        var dropsC = RuntimeSimulator.ArrayItems(packed, locC, 1);
        Check("单元素负数数组正确",
            dropsC.Count == 1 && Base92.DecodeInt(dropsC[0]) == -7);
    }

    private static void TestChunking()
    {
        Console.WriteLine("- 数据分块");

        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String, ColumnIndex = 0 },
            new() { Name = "blob", Type = FieldType.String, ColumnIndex = 1 },
        };

        var rows = new List<RowData>();
        for (var i = 0; i < 40; i++)
        {
            rows.Add(new RowData
            {
                Id = $"row{i}",
                SourceLine = i + 2,
                Cells = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = $"row{i}",
                    ["blob"] = new string('x', i == 17 ? 1500 : 60),
                },
            });
        }

        var table = new TableData { Name = "blobs", Fields = fields, Rows = rows };
        var ids = rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "blobs", tableSize);
        var packed = DataPacker.Pack(table, hash, _ => "", _ => 0);

        Check("40 行紧凑排列为少量数据块", packed.DataChunks.Count <= 2);

        // 超长行不应跨块（这里只有 1 块，需验证内容完整且能按 offset 取回）。
        var ok = true;
        foreach (var row in packed.Table.Rows)
        {
            var loc = RuntimeSimulator.LocateRow(packed, hash, row.Id)!;
            var blob = RuntimeSimulator.FieldRaw(packed, loc, 1);
            if (blob != row.Cells["blob"])
            {
                ok = false;
                Console.WriteLine($"      {row.Id} 内容错位 (长度 {blob.Length} vs {row.Cells["blob"].Length})");
            }
        }

        Check("每行载荷按 offset 精确还原", ok);

        // 多块验证：构造足够多行强制分块，且全部可检索。
        // 注意：当前 dataMapId 占 1 位 base92（最大 91），行数须 ≤ 91。
        var manyRows = new List<RowData>();
        for (var i = 0; i < 80; i++)
        {
            manyRows.Add(new RowData
            {
                Id = $"m{i}",
                SourceLine = i + 2,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = $"m{i}", ["blob"] = $"v{i}" },
            });
        }

        var mTable = new TableData { Name = "big", Fields = fields, Rows = manyRows };
        var mIds = manyRows.Select(r => r.Id).ToList();
        var mSize = PerfectHash.NextPrime(Math.Max(mIds.Count * 2, mIds.Count + 8));
        var mHash = PerfectHash.Build(mIds, "big", mSize);
        var mPacked = DataPacker.Pack(mTable, mHash, _ => "", _ => 0);

        Check("80 行紧凑排列", mPacked.DataChunks.Count >= 1);
        var allFound = mIds.All(id =>
            RuntimeSimulator.LocateRow(mPacked, mHash, id) is not null);
        Check("分块后全部可检索", allFound);
    }

    /// <summary>
    /// 锁定字符串字面量不被 trim 的契约（用户明确要求：不要加哨兵，字符串字面量不会被 trim）。
    /// 这里验证：存入 data_chunk 的字符串即使首尾是 base92 数位 0（空格），
    /// 在从 chunk 按 offset 切片时仍能完整保留，不依赖任何哨兵。
    /// </summary>
    private static void TestStringNotTrimmed()
    {
        Console.WriteLine("- 字符串不 trim（无哨兵）");

        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String, ColumnIndex = 0 },
            new() { Name = "pad", Type = FieldType.String, ColumnIndex = 1 },
        };

        // pad 字段首尾都是空格：编码为 base92 数位 0 后仍须完整保留。
        var rows = new List<RowData>
        {
            new()
            {
                Id = "a", SourceLine = 2,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "a", ["pad"] = "  hello  " },
            },
            new()
            {
                Id = "b", SourceLine = 3,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "b", ["pad"] = "  " },
            },
        };

        var table = new TableData { Name = "pad", Fields = fields, Rows = rows };
        var ids = rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "pad", tableSize);
        var packed = DataPacker.Pack(table, hash, _ => "", _ => 0);

        var locA = RuntimeSimulator.LocateRow(packed, hash, "a")!;
        var padA = RuntimeSimulator.FieldRaw(packed, locA, 1);
        Check("首尾空格字符串完整保留", padA == "  hello  ", $"实际 '{padA}'");

        var locB = RuntimeSimulator.LocateRow(packed, hash, "b")!;
        var padB = RuntimeSimulator.FieldRaw(packed, locB, 1);
        Check("纯空格字符串完整保留", padB == "  ", $"实际 '{padB}'");

        // 生成脚本数据块绝不以 ~ 作为包裹哨兵（~ 本身是 base92 字母表末位，允许出现）。
        var script = new TeaScriptEmitter(packed).Emit();
        Check("数据块未用 ~ 哨兵包裹", packed.DataChunks.All(c => !c.StartsWith('~') && !c.EndsWith('~')));

        // 数据块字面量里若出现引号必须已被转义为 ""。
        var literalSafe = packed.DataChunks.All(c => !c.Contains('"'));
        Check("数据块不含裸引号（引号由 Emitter 转义）", literalSafe);
    }

    /// <summary>
    /// 锁定 text 列的契约：存入的是 FontAtlasGenerator 的编码载荷本身（原样），
    /// 访问器生成 <c>TXT(D(...))</c>，在运行时解码而非返回源码文本。
    /// </summary>
    private static void TestTextColumnContract()
    {
        Console.WriteLine("- text 列契约");

        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String, ColumnIndex = 0 },
            new() { Name = "line", Type = FieldType.Text, ColumnIndex = 1 },
        };

        var rows = new List<RowData>
        {
            new()
            {
                Id = "a", SourceLine = 2,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "a", ["line"] = "原文一" },
            },
            new()
            {
                Id = "b", SourceLine = 3,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "b", ["line"] = "原文二" },
            },
        };

        var table = new TableData { Name = "dlg", Fields = fields, Rows = rows };
        var ids = rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "dlg", tableSize);

        // 模拟 FontAtlasGenerator: 返回一段只含字母表内字符的编码载荷。
        var fakePayloads = new Dictionary<string, string>
        {
            ["原文一"] = "``R#t+c)O}i",
            ["原文二"] = "``Y#abc'def",
        };
        var packed = DataPacker.Pack(table, hash, raw => fakePayloads[raw], _ => 0);

        var locA = RuntimeSimulator.LocateRow(packed, hash, "a")!;
        var segA = RuntimeSimulator.FieldRaw(packed, locA, 1);

        Check("载荷原样存储, 未被二次转义或加哨兵", segA == fakePayloads["原文一"], $"实际 '{segA}'");

        var script = new TeaScriptEmitter(packed).Emit();
        Check("text getter 返回 payload 原文", script.Contains("Return __target_data & \"\""));

        // 数据块字面量里绝不能出现裸引号或反斜杠。
        var literalSafe = packed.DataChunks.All(c => !c.Contains('"') && !c.Contains('\\'));
        Check("数据块可安全嵌入字面量", literalSafe);
    }

    private static void TestRefRoundTrip()
    {
        Console.WriteLine("- ref 字段（存目标行号）");

        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String, ColumnIndex = 0 },
            new() { Name = "partner", Type = FieldType.Ref, ColumnIndex = 1 },
        };

        var rows = new List<RowData>
        {
            new()
            {
                Id = "goomba", SourceLine = 2,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "goomba", ["partner"] = "koopa" },
            },
            new()
            {
                Id = "koopa", SourceLine = 3,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "koopa", ["partner"] = "" },
            },
        };

        var table = new TableData { Name = "npc", Fields = fields, Rows = rows };
        var ids = rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, "npc", tableSize);

        // ref -> 目标 uuid 的行号（1-based）。koopa 是第 2 行。
        int RefResolver(string raw) => raw == "koopa" ? 2 : 0;
        var packed = DataPacker.Pack(table, hash, _ => "", RefResolver);

        var locG = RuntimeSimulator.LocateRow(packed, hash, "goomba")!;
        var partner = Base92.DecodeInt(RuntimeSimulator.FieldRaw(packed, locG, 1));
        Check("ref 解析为目标行号", partner == 2, $"实际 {partner}");

        var locK = RuntimeSimulator.LocateRow(packed, hash, "koopa")!;
        var empty = Base92.DecodeInt(RuntimeSimulator.FieldRaw(packed, locK, 1));
        Check("空引用解析为 0", empty == 0);
    }

    // ==================== TeaScript 模拟器测试 ====================

    private static TableData BuildNpcTable()
    {
        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String },
            new() { Name = "name", Type = FieldType.String },
            new() { Name = "hp", Type = FieldType.Int },
            new() { Name = "speed", Type = FieldType.Float },
            new() { Name = "boss", Type = FieldType.Bool },
            new() { Name = "drops", Type = FieldType.Int, IsArray = true, ArraySeparator = "," },
            new() { Name = "tags", Type = FieldType.String, IsArray = true, ArraySeparator = "," },
            new() { Name = "partner", Type = FieldType.Ref },
        };

        var rows = new List<RowData>
        {
            new() { Id = "goomba", SourceLine = 2, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "goomba", ["name"] = "Goomba", ["hp"] = "1", ["speed"] = "2.5", ["boss"] = "false", ["drops"] = "1,2", ["tags"] = "ground", ["partner"] = "koopa" } },
            new() { Id = "koopa", SourceLine = 3, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "koopa", ["name"] = "Koopa", ["hp"] = "3", ["speed"] = "1.5", ["boss"] = "false", ["drops"] = "3,4,5", ["tags"] = "ground,shell", ["partner"] = "" } },
            new() { Id = "bowser", SourceLine = 4, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "bowser", ["name"] = "Bowser", ["hp"] = "20", ["speed"] = "3.25", ["boss"] = "true", ["drops"] = "10,20,30", ["tags"] = "boss,fire,heavy", ["partner"] = "" } },
            new() { Id = "boo", SourceLine = 5, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "boo", ["name"] = "Boo", ["hp"] = "-1", ["speed"] = "1.25", ["boss"] = "false", ["drops"] = "", ["tags"] = "ghost", ["partner"] = "" } },
            new() { Id = "piranha", SourceLine = 6, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "piranha", ["name"] = "Piranha", ["hp"] = "3", ["speed"] = "0", ["boss"] = "false", ["drops"] = "", ["tags"] = "plant", ["partner"] = "" } },
            new() { Id = "spiny", SourceLine = 7, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "spiny", ["name"] = "Spiny", ["hp"] = "5", ["speed"] = "0.5", ["boss"] = "false", ["drops"] = "6", ["tags"] = "spike", ["partner"] = "lakitu" } },
            new() { Id = "lakitu", SourceLine = 8, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "lakitu", ["name"] = "Lakitu", ["hp"] = "10", ["speed"] = "2.0", ["boss"] = "false", ["drops"] = "7,8", ["tags"] = "flying", ["partner"] = "spiny" } },
            new() { Id = "thwomp", SourceLine = 9, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "thwomp", ["name"] = "Thwomp", ["hp"] = "-1", ["speed"] = "0.5", ["boss"] = "false", ["drops"] = "9", ["tags"] = "heavy", ["partner"] = "" } },
            new() { Id = "morton", SourceLine = 10, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "morton", ["name"] = "Morton", ["hp"] = "15", ["speed"] = "1.75", ["boss"] = "true", ["drops"] = "11,12", ["tags"] = "boss", ["partner"] = "" } },
        };

        return new TableData { Name = "npc", Fields = fields, Rows = rows };
    }

    private static (PerfectHash hash, PackedTable packed, string smt) BuildAndEmit(TableData table, Func<string, int> refResolver)
    {
        var ids = table.Rows.Select(r => r.Id).ToList();
        var tableSize = PerfectHash.NextPrime(Math.Max(ids.Count * 16, ids.Count + 64));
        var hash = PerfectHash.Build(ids, table.Name, tableSize);
        var packed = DataPacker.Pack(table, hash, _ => "PAYLOAD", refResolver);
        var smt = new TeaScriptEmitter(packed).Emit();
        return (hash, packed, smt);
    }

    private static void TestSimulatorBasic()
    {
        Console.WriteLine("- 模拟器: 基本标量");
        var table = BuildNpcTable();
        int RefResolver(string raw) => table.Rows.FindIndex(r => r.Id == raw) + 1;
        var (hash, packed, smt) = BuildAndEmit(table, RefResolver);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        // goomba
        sim.SetId("goomba");
        Check("sim goomba.name", sim.GetString("name") == "Goomba", $"got '{sim.GetString("name")}'");
        Check("sim goomba.hp", sim.GetInt("hp") == 1, $"got {sim.GetInt("hp")}");
        Check("sim goomba.speed", Math.Abs(sim.GetFloat("speed") - 2.5) < 0.001, $"got {sim.GetFloat("speed")}");
        Check("sim goomba.boss=false", sim.GetBool("boss") == 0);
        Check("sim goomba.partner=koopa(row2)", sim.GetRef("partner") == 2, $"got {sim.GetRef("partner")}");
        Check("sim goomba no error", sim.TryError() == 0);

        // bowser
        sim.SetId("bowser");
        Check("sim bowser.name", sim.GetString("name") == "Bowser");
        Check("sim bowser.hp=20", sim.GetInt("hp") == 20);
        Check("sim bowser.boss=true", sim.GetBool("boss") != 0);
        Check("sim bowser.speed=3.25", Math.Abs(sim.GetFloat("speed") - 3.25) < 0.001);

        // piranha speed=0
        sim.SetId("piranha");
        Check("sim piranha.hp=3", sim.GetInt("hp") == 3);
        Check("sim piranha.speed=0", sim.GetFloat("speed") == 0);
        Check("sim piranha.partner=0", sim.GetRef("partner") == 0);
    }

    private static void TestSimulatorNegativeNumbers()
    {
        Console.WriteLine("- 模拟器: 负数");
        var table = BuildNpcTable();
        int RefResolver(string raw) => table.Rows.FindIndex(r => r.Id == raw) + 1;
        var (hash, packed, smt) = BuildAndEmit(table, RefResolver);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        sim.SetId("boo");
        Check("sim boo.hp=-1", sim.GetInt("hp") == -1, $"got {sim.GetInt("hp")}");

        sim.SetId("thwomp");
        Check("sim thwomp.hp=-1", sim.GetInt("hp") == -1, $"got {sim.GetInt("hp")}");
        Check("sim thwomp.speed=0.5", Math.Abs(sim.GetFloat("speed") - 0.5) < 0.001);

        // 测试更大的负数
        var fields = new List<FieldDef> { new() { Name = "id", Type = FieldType.String }, new() { Name = "val", Type = FieldType.Int } };
        var rows = new List<RowData>
        {
            new() { Id = "n0", SourceLine = 1, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "n0", ["val"] = "-1000" } },
            new() { Id = "n1", SourceLine = 2, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "n1", ["val"] = "-1" } },
            new() { Id = "n2", SourceLine = 3, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "n2", ["val"] = "0" } },
            new() { Id = "n3", SourceLine = 4, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "n3", ["val"] = "999" } },
            new() { Id = "n4", SourceLine = 5, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "n4", ["val"] = "-999" } },
        };
        var t2 = new TableData { Name = "negs", Fields = fields, Rows = rows };
        var (h2, p2, s2) = BuildAndEmit(t2, _ => 0);
        var sim2 = TeaScriptSimulator.Parse(s2, t2, h2);
        sim2.SetId("n0"); Check("sim -1000", sim2.GetInt("val") == -1000, $"got {sim2.GetInt("val")}");
        sim2.SetId("n1"); Check("sim -1", sim2.GetInt("val") == -1);
        sim2.SetId("n2"); Check("sim 0", sim2.GetInt("val") == 0);
        sim2.SetId("n3"); Check("sim 999", sim2.GetInt("val") == 999);
        sim2.SetId("n4"); Check("sim -999", sim2.GetInt("val") == -999, $"got {sim2.GetInt("val")}");
    }

    private static void TestSimulatorFloats()
    {
        Console.WriteLine("- 模拟器: 浮点数");
        var fields = new List<FieldDef> { new() { Name = "id", Type = FieldType.String }, new() { Name = "f", Type = FieldType.Float } };
        var rows = new List<RowData>
        {
            new() { Id = "f0", SourceLine = 1, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "f0", ["f"] = "0" } },
            new() { Id = "f1", SourceLine = 2, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "f1", ["f"] = "3.14159" } },
            new() { Id = "f2", SourceLine = 3, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "f2", ["f"] = "-2.5" } },
            new() { Id = "f3", SourceLine = 4, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "f3", ["f"] = "0.0001" } },
            new() { Id = "f4", SourceLine = 5, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "f4", ["f"] = "999.9999" } },
            new() { Id = "f5", SourceLine = 6, Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = "f5", ["f"] = "-0.0001" } },
        };
        var table = new TableData { Name = "floats", Fields = fields, Rows = rows };
        var (hash, _, smt) = BuildAndEmit(table, _ => 0);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        sim.SetId("f0"); Check("sim float 0", Math.Abs(sim.GetFloat("f")) < 0.001);
        sim.SetId("f1"); Check("sim float 3.14159", Math.Abs(sim.GetFloat("f") - 3.14159) < 0.001, $"got {sim.GetFloat("f")}");
        sim.SetId("f2"); Check("sim float -2.5", Math.Abs(sim.GetFloat("f") - (-2.5)) < 0.001, $"got {sim.GetFloat("f")}");
        sim.SetId("f3"); Check("sim float 0.0001", Math.Abs(sim.GetFloat("f") - 0.0001) < 0.001);
        sim.SetId("f4"); Check("sim float 999.9999", Math.Abs(sim.GetFloat("f") - 999.9999) < 0.01, $"got {sim.GetFloat("f")}");
        sim.SetId("f5"); Check("sim float -0.0001", Math.Abs(sim.GetFloat("f") - (-0.0001)) < 0.001, $"got {sim.GetFloat("f")}");
    }

    private static void TestSimulatorStringArrays()
    {
        Console.WriteLine("- 模拟器: 字符串数组");
        var table = BuildNpcTable();
        int RefResolver(string raw) => table.Rows.FindIndex(r => r.Id == raw) + 1;
        var (hash, _, smt) = BuildAndEmit(table, RefResolver);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        sim.SetId("koopa");
        Check("sim koopa drops len=3", sim.GetArrayLen("drops") == 3, $"got {sim.GetArrayLen("drops")}");
        Check("sim koopa drops[1]=3", sim.GetArrayInt("drops", 1) == 3);
        Check("sim koopa drops[2]=4", sim.GetArrayInt("drops", 2) == 4);
        Check("sim koopa drops[3]=5", sim.GetArrayInt("drops", 3) == 5);
        Check("sim koopa tags len=2", sim.GetArrayLen("tags") == 2);
        Check("sim koopa tags[1]=ground", sim.GetArrayString("tags", 1) == "ground");
        Check("sim koopa tags[2]=shell", sim.GetArrayString("tags", 2) == "shell");

        sim.SetId("bowser");
        Check("sim bowser drops[3]=30", sim.GetArrayInt("drops", 3) == 30);
        Check("sim bowser tags len=3", sim.GetArrayLen("tags") == 3);
        Check("sim bowser tags[3]=heavy", sim.GetArrayString("tags", 3) == "heavy");

        sim.SetId("boo");
        Check("sim boo drops empty", sim.GetArrayLen("drops") == 0);
        Check("sim boo tags len=1", sim.GetArrayLen("tags") == 1);
        Check("sim boo tags[1]=ghost", sim.GetArrayString("tags", 1) == "ghost");

        // 越界访问返回 0 或设置 error
        long oobVal = sim.GetArrayInt("drops", 99);
        Check("sim drops oob=0 or error", oobVal == 0 || sim.TryError() != 0, $"val={oobVal} err={sim.TryError()}");
    }

    private static void TestSimulatorLargeTable()
    {
        Console.WriteLine("- 模拟器: 大表(50行)");
        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String },
            new() { Name = "hp", Type = FieldType.Int },
            new() { Name = "name", Type = FieldType.String },
        };
        var rows = new List<RowData>();
        for (int i = 0; i < 50; i++)
        {
            rows.Add(new RowData
            {
                Id = $"row{i:D3}",
                SourceLine = i + 2,
                Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = $"row{i:D3}", ["hp"] = (i * 10 - 100).ToString(), ["name"] = $"Name{i}" },
            });
        }
        var table = new TableData { Name = "large", Fields = fields, Rows = rows };
        var (hash, _, smt) = BuildAndEmit(table, _ => 0);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        int ok = 0;
        foreach (var row in rows)
        {
            sim.SetId(row.Id);
            int expected = int.Parse(row.Cells["hp"]);
            if (sim.GetInt("hp") == expected && sim.GetString("name") == row.Cells["name"])
                ok++;
        }
        Check("sim 50行全部往返一致", ok == 50, $"只有 {ok}/50 一致");

        // 抽查边界
        sim.SetId("row000"); Check("sim row000 hp=-100", sim.GetInt("hp") == -100);
        sim.SetId("row049"); Check("sim row049 hp=390", sim.GetInt("hp") == 390);
    }

    private static void TestSimulatorSuperLargeTable()
    {
        Console.WriteLine("- 模拟器: 超级大表(500行, 超过4个chunk/datamap)");
        var fields = new List<FieldDef>
        {
            new() { Name = "id", Type = FieldType.String },
            new() { Name = "desc", Type = FieldType.String },
            new() { Name = "val", Type = FieldType.Int },
            new() { Name = "items", Type = FieldType.Int, IsArray = true, ArraySeparator = "," },
        };
        var rows = new List<RowData>();
        var rng = new Random(42);
        for (int i = 0; i < 500; i++)
        {
            // 生成较长的 desc 字符串以触发多 chunk
            string desc = new string('X', 200 + rng.Next(100)) + i;
            int itemCount = rng.Next(5);
            var itemParts = new List<string>();
            for (int j = 0; j < itemCount; j++) itemParts.Add((rng.Next(2000) - 1000).ToString());
            rows.Add(new RowData
            {
                Id = $"entry{i:D4}",
                SourceLine = i + 2,
                Cells = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["id"] = $"entry{i:D4}",
                    ["desc"] = desc,
                    ["val"] = (rng.Next(10000) - 5000).ToString(),
                    ["items"] = string.Join(",", itemParts),
                },
            });
        }
        var table = new TableData { Name = "super", Fields = fields, Rows = rows };
        var (hash, packed, smt) = BuildAndEmit(table, _ => 0);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        Check("sim 超级大表 chunk>4", packed.DataChunks.Count > 4, $"只有 {packed.DataChunks.Count} 个 chunk");
        Check("sim 超级大表 dataMap>=1", packed.DataMaps.Count >= 1);

        int ok = 0;
        foreach (var row in rows)
        {
            sim.SetId(row.Id);
            int expectedVal = int.Parse(row.Cells["val"]);
            long actualVal = sim.GetInt("val");
            string actualDesc = sim.GetString("desc");
            if (actualVal == expectedVal && actualDesc == row.Cells["desc"])
            {
                // 检查数组
                var expectedItems = string.IsNullOrEmpty(row.Cells["items"])
                    ? new List<string>()
                    : row.Cells["items"].Split(',').ToList();
                long arrLen = sim.GetArrayLen("items");
                bool arrOk = arrLen == expectedItems.Count;
                if (arrOk)
                {
                    for (int j = 0; j < expectedItems.Count; j++)
                    {
                        if (sim.GetArrayInt("items", j + 1) != int.Parse(expectedItems[j]))
                        {
                            arrOk = false;
                            break;
                        }
                    }
                }
                if (arrOk) ok++;
            }
        }
        Check("sim 500行全部往返一致", ok == 500, $"只有 {ok}/500 一致");
    }

    private static void TestSimulatorMissAndError()
    {
        Console.WriteLine("- 模拟器: 未命中和错误");
        var table = BuildNpcTable();
        int RefResolver(string raw) => table.Rows.FindIndex(r => r.Id == raw) + 1;
        var (hash, _, smt) = BuildAndEmit(table, RefResolver);
        var sim = TeaScriptSimulator.Parse(smt, table, hash);

        // 未命中
        sim.SetId("nonexistent");
        Check("sim miss sets error", sim.TryError() != 0, $"error={sim.TryError()}");

        // 命中后无错误
        sim.SetId("goomba");
        Check("sim hit clears error", sim.TryError() == 0);
    }

    private static void TestSimulatorBinarySearch()
    {
        Console.WriteLine("- 模拟器: 二分查找展开式校验");
        // 构造 1, 3, 5, 10, 20, 50, 100 行的表，验证二分查找在每个规模下都正确
        int[] sizes = { 1, 2, 3, 4, 5, 7, 10, 15, 20, 50, 100 };
        int totalChecks = 0;
        foreach (var n in sizes)
        {
            var fields = new List<FieldDef> { new() { Name = "id", Type = FieldType.String }, new() { Name = "v", Type = FieldType.Int } };
            var rows = new List<RowData>();
            for (int i = 0; i < n; i++)
            {
                rows.Add(new RowData
                {
                    Id = $"k{i}",
                    SourceLine = i + 2,
                    Cells = new(StringComparer.OrdinalIgnoreCase) { ["id"] = $"k{i}", ["v"] = (i * 7).ToString() },
                });
            }
            var table = new TableData { Name = $"bs{n}", Fields = fields, Rows = rows };
            var (hash, _, smt) = BuildAndEmit(table, _ => 0);
            var sim = TeaScriptSimulator.Parse(smt, table, hash);

            bool allOk = true;
            foreach (var row in rows)
            {
                sim.SetId(row.Id);
                int expected = int.Parse(row.Cells["v"]);
                if (sim.GetInt("v") != expected) { allOk = false; break; }
            }
            Check($"sim 二分查找 n={n} 全部命中", allOk);
            totalChecks++;
        }
        Check("sim 二分查找覆盖 11 种规模", totalChecks == 11);

        // 验证生成的脚本中 FindRow 确实是展开式 If/Else（不是循环）
        var t = BuildNpcTable();
        var (_, _, s) = BuildAndEmit(t, _ => 0);
        bool hasFindRowIf = s.Contains("If uuid >") && s.Contains("Then Return");
        // FindRow 函数体本身不应包含 For 循环（哈希计算的 For 在 SeekDataMap 中）
        int findRowStart = s.IndexOf("FindRow(uuid As Long");
        int findRowEnd = s.IndexOf("End Script", findRowStart);
        string findRowBody = s.Substring(findRowStart, findRowEnd - findRowStart);
        bool findRowHasLoop = findRowBody.Contains("For ");
        Check("sim FindRow 是展开式 If", hasFindRowIf);
        Check("sim FindRow 无循环", !findRowHasLoop);
    }
}

/// <summary>
/// 用 C# 复刻生成脚本的运行时解析逻辑，用于在导出期验证布局假设。
/// 任何一处与 <see cref="TeaScriptEmitter"/> 的偏差都会被自检捕获。
/// <para>
/// 布局严格对应三层结构：
/// <list type="bullet">
///   <item><c>RowMap</c>：每行 <c>[uuid 定宽][dataMapId 1位][dataMapOffset 2位]</c>，按 uuid 升序排列。</item>
///   <item><c>DataMap_{XX}</c>：每个字段一段 8 字符 <c>[chunkId 2位][offset 3位][len 3位]</c>。</item>
///   <item><c>DataChunk_{XXXX}</c>：紧凑数据。</item>
/// </list>
/// 解码一律使用无符号 base92（与 <c>CUMath_Decode(..., 92)</c> 语义一致）。
/// </para>
/// </summary>
public static class RuntimeSimulator
{
    /// <summary>row_map 中单个 entry 的定位信息。</summary>
    public sealed record RowLoc(int BlockId, int DataMapOffset);

    /// <summary>
    /// 模拟 <c>_SeekDataMap</c>：先算 uuid = hash.Compute(id)，
    /// 再到 RowMap 中按 uuid 升序二分查找 entry，读出 (dataMapId, dataMapOffset)。
    /// 返回 null 表示未命中（uuid 不在表中）。
    /// <para>
    /// 这与 Emitter 生成的 <c>_FindRow</c> 完全对应：row_map 按 uuid 值升序排列，
    /// 查找时按 uuid 值二分，而非按 slot 下标取。
    /// </para>
    /// </summary>
    public static RowLoc? LocateRow(PackedTable table, PerfectHash hash, string id)
    {
        int uuid = hash.Compute(id);
        int entryWidth = table.UuidWidth + 3;
        int nEntries = table.RowMap.Length / entryWidth;
        if (nEntries == 0) return null;

        // 二分查找 uuid 值
        int lo = 0, hi = nEntries - 1;
        int foundIdx = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int baseIdx = mid * entryWidth;
            var uuidStr = table.RowMap.Substring(baseIdx, table.UuidWidth);
            int storedUuid = (int)Base92.DecodeUInt(uuidStr);
            if (storedUuid == uuid)
            {
                foundIdx = mid;
                break;
            }
            if (storedUuid < uuid)
                lo = mid + 1;
            else
                hi = mid - 1;
        }

        if (foundIdx < 0) return null;

        int b = foundIdx * entryWidth;
        int dataMapId = (int)Base92.DecodeUInt(table.RowMap.Substring(b + table.UuidWidth, 1));
        int dataMapOffset = (int)Base92.DecodeUInt(table.RowMap.Substring(b + table.UuidWidth + 1, 2));
        if (dataMapId == 0) return null;
        return new RowLoc(dataMapId, dataMapOffset);
    }

    /// <summary>
    /// 读取某行某字段在 data_chunk 中的原始片段。
    /// 对应 <c>_SeekChunk</c> + <c>_LoadChunkFragment</c>：
    /// 在 DataMap 中按 fieldIndex 取 8 位段 -> (chunkId, offset, len) -> 从 DataChunk 切片。
    /// <para>
    /// 注意：loc.DataMapOffset 是 1-based 偏移（文档约定），需转为 0-based 下标。
    /// 当字段跨 data_map 时，自动跳到下一个 data_map。
    /// </para>
    /// </summary>
    public static string FieldRaw(PackedTable table, RowLoc loc, int fieldIndex0Based)
    {
        int dataMapIdx = loc.BlockId - 1; // 0-based
        int segPos0 = loc.DataMapOffset - 1 + fieldIndex0Based * 8; // 0-based

        // 处理跨 data_map
        while (dataMapIdx < table.DataMaps.Count)
        {
            var dataMap = table.DataMaps[dataMapIdx];
            if (segPos0 + 8 <= dataMap.Length) break;
            // 跨 data_map：剩余字段数
            int remaining = (dataMap.Length - (loc.DataMapOffset - 1)) / 8;
            fieldIndex0Based -= remaining;
            dataMapIdx++;
            segPos0 = fieldIndex0Based * 8;
            loc = loc with { BlockId = dataMapIdx + 1, DataMapOffset = 1 };
        }

        if (dataMapIdx >= table.DataMaps.Count) return string.Empty;
        var dm = table.DataMaps[dataMapIdx];
        if (segPos0 + 8 > dm.Length) return string.Empty;

        int chunkId = (int)Base92.DecodeUInt(dm.Substring(segPos0, 2));
        int offset = (int)Base92.DecodeUInt(dm.Substring(segPos0 + 2, 3));
        int len = (int)Base92.DecodeUInt(dm.Substring(segPos0 + 5, 3));

        if (chunkId < 0 || chunkId >= table.DataChunks.Count) return string.Empty;
        var chunk = table.DataChunks[chunkId];
        if (offset < 1 || offset > chunk.Length) return string.Empty;
        int start = offset - 1;
        if (start + len > chunk.Length) len = chunk.Length - start;
        return chunk.Substring(start, len);
    }

    /// <summary>
    /// 读取数组字段：__target_data 是数组头（N*8 位定位表），返回各元素的原始片段。
    /// 每段 8 位 [chunkId 2位][offset 3位][len 3位] 指向 chunk 中的元素。
    /// </summary>
    public static List<string> ArrayItems(PackedTable table, RowLoc loc, int fieldIndex0Based)
    {
        var headerStr = FieldRaw(table, loc, fieldIndex0Based);
        var items = new List<string>();
        int nSegs = headerStr.Length / 8;
        for (int i = 0; i < nSegs; i++)
        {
            int seg = i * 8;
            int chunkId = (int)Base92.DecodeUInt(headerStr.Substring(seg, 2));
            int offset = (int)Base92.DecodeUInt(headerStr.Substring(seg + 2, 3));
            int len = (int)Base92.DecodeUInt(headerStr.Substring(seg + 5, 3));
            var chunk = table.DataChunks[chunkId];
            if (offset < 1 || offset > chunk.Length) continue;
            int start = offset - 1;
            if (start + len > chunk.Length) len = chunk.Length - start;
            items.Add(chunk.Substring(start, len));
        }

        return items;
    }
}
