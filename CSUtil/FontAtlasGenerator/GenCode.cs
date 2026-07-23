using System.Text;

namespace FontAtlasGenerator;

internal static partial class Program
{
    private static string GenCode(in Context curr, int xCnt, IReadOnlyDictionary<char, int> charIdMap)
    {
        var code = CodeTemplate.S.Replace("{xCnt}", $"{xCnt}").Replace("{sizeX}", $"{curr.CharSize}");

        var sb = new StringBuilder();
        sb.Clear();
        AppendLines(sb, curr.Offsets, charIdMap, o => o.C, o => o.Y);
        sb.Append("    ");
        code = code.Replace("{offsetExtends}", sb.ToString());

        sb.Clear();
        AppendLines(sb, curr.Sizes, charIdMap, o => o.C, o => o.X);
        sb.Append("    ");
        code = code.Replace("{sizeExtends}", sb.ToString());
        return code;
    }

    // 将 (id, ret) 序列中「连续相邻(id 连续递增)且 ret 相等」、长度大于 3 的段合并为区间判断。
    private static void AppendLines<T>(
        StringBuilder sb,
        IReadOnlyList<T> offsets,
        IReadOnlyDictionary<char, int> charIdMap,
        Func<T, char> getC,
        Func<T, int> getRet)
    {
        var seen = new HashSet<char>();
        var entries = new List<(int id, int ret)>();
        foreach (var offset in offsets)
        {
            var c = getC(offset);
            if (!seen.Add(c)) continue;
            int id;
            if (c <= 128)
                id = c;
            else if (!charIdMap.TryGetValue(c, out id)) continue;
            entries.Add((id, getRet(offset)));
        }

        entries.Sort((a, b) => a.id.CompareTo(b.id));

        var i = 0;
        while (i < entries.Count)
        {
            var start = i;
            var ret = entries[i].ret;
            while (i + 1 < entries.Count &&
                   entries[i + 1].ret == ret &&
                   entries[i + 1].id == entries[i].id + 1) { i++; }

            if (i - start + 1 > 3)
            {
                sb.Append($"    If id >= {entries[start].id} And id <= {entries[i].id} Then Return {ret}\n");
            }
            else
            {
                for (var j = start; j <= i; j++)
                    sb.Append($"    If id = {entries[j].id} Then Return {entries[j].ret}\n");
            }
            i++;
        }
    }
}