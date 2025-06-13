using System.Text;

namespace FontAtlasGenerator;

internal static partial class Program
{
    private static string GenCode(in Context curr, int xCnt, IReadOnlyDictionary<char, int> charIdMap)
    {
        var code = CodeTemplate.S.Replace("{xCnt}", $"{xCnt}").Replace("{sizeX}", $"{curr.CharSize}");

        var sb = new StringBuilder();
        sb.Clear();
        var set = new HashSet<char>();

        if (curr.Offsets.Count > 0)
        {
            foreach (var offset in curr.Offsets)
            {
                if (!set.Add(offset.C)) continue;
                if (offset.C <= 128)
                {
                    sb.Append($"    If id = {(int)offset.C} Then Return {offset.Y}\n");
                    continue;
                }

                if (!charIdMap.TryGetValue(offset.C, out var value)) continue;
                sb.Append($"    If id = {value} Then Return {offset.Y}\n");
            }
        }
        sb.Append("    ");
        code = code.Replace("{offsetExtends}", sb.ToString());

        sb.Clear();
        set.Clear();

        if (curr.Sizes.Count > 0)
        {
            foreach (var offset in curr.Sizes)
            {
                if (!set.Add(offset.C)) continue;
                if (offset.C <= 128)
                {
                    sb.Append($"    If id = {(int)offset.C} Then Return {offset.X}\n");
                    continue;
                }

                if (!charIdMap.TryGetValue(offset.C, out var value)) continue;
                sb.Append($"    If id = {value} Then Return {offset.X}\n");
            }
        }
        sb.Append("    ");
        code = code.Replace("{sizeExtends}", sb.ToString());
        return code;
    }
}