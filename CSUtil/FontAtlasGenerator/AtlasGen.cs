using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace FontAtlasGenerator;

public static class AtlasGen
{
    public struct OffsetData
    {
        public long C;
        public int X;
        public int Y;
    }
    public static readonly Dictionary<long, OffsetData> SingleCharOffset = new();

    public class MultipleListAgent<T> : IReadOnlyList<T>
    {
        public int Count { get; }

        public T this[int index]
        {
            get
            {
                foreach (var list in _lists)
                {
                    if (list.Count > index) return list[index];
                    index -= list.Count;
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public MultipleListAgent(IReadOnlyList<T>[] lists)
        {
            Count = lists.Sum(v => v.Count);
            _lists = lists;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public IEnumerator<T> GetEnumerator()
        {
            // ReSharper disable once LoopCanBeConvertedToQuery
            foreach (var list in _lists)
                foreach (var item in list)
                    yield return item;
        }

        private readonly IReadOnlyList<T>[] _lists;
    }

    public class StringListAgent : IReadOnlyList<long>
    {
        public int Count => _cps.Count;
        public long this[int index] => _cps[index];

        public StringListAgent(string s) => _cps = CommonStandardHanzi.EnumerateCodePoints(s);

        public IEnumerator<long> GetEnumerator() => _cps.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private readonly List<long> _cps;
    }

    public static int Gen(
        string fontPath,
        float emSize,
        string opPath,
        (float x, float y) offset,
        int canvasSize,
        int charSize,
        IReadOnlyList<long> s,
        sbyte fontRenderType)
    {
        var collection = new PrivateFontCollection();
        collection.AddFontFile(fontPath);
        var font = new Font(collection.Families[0], emSize);

        var bmp = new Bitmap(2048, 2048, PixelFormat.Format32bppArgb);
        var brush = new SolidBrush(Color.White);
        var gfx = Graphics.FromImage(bmp);
        gfx.TextRenderingHint = fontRenderType is < 0 or > (sbyte)TextRenderingHint.ClearTypeGridFit
            ? TextRenderingHint.SingleBitPerPixelGridFit
            : (TextRenderingHint)fontRenderType;
        gfx.Clear(Color.Transparent);

        var xCnt = canvasSize / charSize;

        for (var idx = 0; idx <= 127; idx++)
        {
            // ReSharper disable once PossibleLossOfFraction
            var y = idx / xCnt * charSize + offset.y;
            var x = idx % xCnt * charSize + offset.x;

            if (SingleCharOffset.TryGetValue(idx, out var v))
            {
                x += v.X;
                y += v.Y;
            }
            gfx.DrawString(((char)idx).ToString(), font, brush, new PointF(x, y));
        }

        for (var idx = 128; idx - 128 < s.Count; idx++)
        {
            // ReSharper disable once PossibleLossOfFraction
            var y = idx / xCnt * charSize + offset.y;
            var x = idx % xCnt * charSize + offset.x;
            var cp = s[idx - 128];
            if (SingleCharOffset.TryGetValue(cp, out var v))
            {
                x += v.X;
                y += v.Y;
            }
            gfx.DrawString(char.ConvertFromUtf32((int)cp), font, brush, new PointF(x, y));
        }

        gfx.Dispose();
        bmp.Save($"{opPath}/atlas-gen-{Guid.NewGuid()}.png", ImageFormat.Png);
        bmp.Dispose();

        return xCnt;
    }
}
