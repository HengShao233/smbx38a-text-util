using System.Drawing.Text;
using System.Text;
using Utf8Json;

namespace FontAtlasGenerator;

internal static partial class Program
{
    private static int CheckJson(Stream json, out Context ctx)
    {
        var score = 0;
        ctx = new Context();

        try
        {
            var obj = JsonSerializer.Deserialize<dynamic>(json);
            var objDict = obj as IReadOnlyDictionary<string, dynamic> ?? new Dictionary<string, dynamic>();

            var fontPath = objDict.TryGetValue("font-path", out var v) ? v as string ?? "" : "";
            if (File.Exists(fontPath)) score += 100;
            ctx.FontPath = fontPath;

            var outputPath = objDict.TryGetValue("output-path", out var v2) ? v2 as string ?? "" : "";
            if (Directory.Exists(outputPath)) score += 10;
            ctx.OutputPath = outputPath;

            var emSize = objDict.TryGetValue("em-size", out var v3) ? v3 is double emSizeDouble ? emSizeDouble : -1 : -1;
            if (emSize > 0) score++;
            else emSize = 9;
            ctx.EmSize = (float)emSize;

            var offsetArr = objDict.TryGetValue("grid-offset", out var v4) ? v4 as IReadOnlyList<dynamic> ?? Array.Empty<dynamic>() : Array.Empty<dynamic>();
            var offsetX = int.MinValue;
            var offsetY = int.MinValue;
            if (offsetArr.Count >= 1)
            {
                offsetX = offsetArr[0] is double ? (int)offsetArr[0] : 0;
                if (offsetArr.Count > 1)
                    offsetY = offsetArr[1] is double ? (int)offsetArr[1] : 0;
            }

            if (offsetX > int.MinValue || offsetY > int.MinValue) score++;
            else offsetX = offsetY = 0;
            ctx.OffsetData = (offsetX, offsetY);

            var charSize = objDict.TryGetValue("grid-size", out var v5) ? v5 is double charSizeDouble ? charSizeDouble : -1 : -1;
            if (charSize > 0) score++;
            else charSize = 12;
            ctx.CharSize = (int)charSize;

            var canvasSize = objDict.TryGetValue("canvas-size", out var v6) ? v6 is double canvasSizeDouble ? canvasSizeDouble : -1 : -1;
            if (canvasSize > 0) score++;
            else canvasSize = 2048;
            ctx.CanvasSize = (int)canvasSize;

            var charSet = objDict.TryGetValue("char-set", out var v7) ? v7 as string : null;
            if (charSet != null) score++;
            ctx.Content = charSet == null ? Array.Empty<char>() : new AtlasGen.StringListAgent(charSet);

            var isAdditionCharSet = !objDict.TryGetValue("addition-char-set", out var v8) || v8 is not bool b || b;
            ctx.IsAdditionalCharSet = isAdditionCharSet;

            switch (objDict.TryGetValue("script", out var v9) ? v9 : null)
            {
                case string script when File.Exists(script):
                    ctx.Scripts = new[] { script };
                    break;
                case IReadOnlyList<dynamic> scripts:
                {
                    List<string>? pathList = null;
                    foreach (var s in scripts)
                    {
                        if (s is not string sPath || !File.Exists(sPath)) continue;
                        pathList ??= new List<string>();
                        pathList.Add(sPath);
                    }
                    ctx.Scripts = pathList;
                    break;
                }
            }

            var isOutputUtil = objDict.TryGetValue("output-util", out var v10) && v10 is true;
            ctx.IsOutputUtil = isOutputUtil;

            var isScan = objDict.TryGetValue("scan-folder-script", out var v11) && v11 is true;
            ctx.IsScanFolder = isScan;

            List<AtlasGen.OffsetData>? spcOffList = null;
            var spcOffset = objDict.TryGetValue("spc-grid-offset", out var v12) ? v12 as IReadOnlyList<dynamic> ?? Array.Empty<dynamic>() : Array.Empty<dynamic>();
            foreach (var off in spcOffset)
            {
                if (off is not IReadOnlyList<dynamic> offData) continue;
                if (offData.Count < 1) continue;
                var c = offData[0] is string cStr && !string.IsNullOrEmpty(cStr)
                    ? cStr[0]
                    : offData[0] is double cDouble ? (char)(int)cDouble : '\0';
                var x = 0;
                var y = 0;
                if (offData.Count > 1) x = offData[1] is double xDouble ? (int)xDouble : 0;
                if (offData.Count > 2) y = offData[2] is double yDouble ? (int)yDouble : 0;
                spcOffList ??= new List<AtlasGen.OffsetData>();
                spcOffList.Add(new AtlasGen.OffsetData { C = c, X = x, Y = y });
            }
            ctx.SpcOffice = spcOffList == null ? Array.Empty<AtlasGen.OffsetData>() : spcOffList;

            List<Offset>? offsets = null;
            var offset = objDict.TryGetValue("char-offset-y", out var v13) ? v13 as IReadOnlyList<dynamic> ?? Array.Empty<dynamic>() : Array.Empty<dynamic>();
            foreach (var off in offset)
            {
                if (off is not IReadOnlyList<dynamic> offData) continue;
                if (offData.Count < 1) continue;
                var c = offData[0] is string cStr && !string.IsNullOrEmpty(cStr)
                    ? cStr[0]
                    : offData[0] is double cDouble ? (char)(int)cDouble : '\0';
                if (c == '\0') continue;
                var x = offData[1] is double xDouble ? (int)xDouble : 0;
                offsets ??= new List<Offset>();
                offsets.Add(new Offset { C = c, Y = x });
            }
            ctx.Offsets = offsets == null ? Array.Empty<Offset>() : offsets;

            List<Size>? sizes = null;
            var sizeList = objDict.TryGetValue("char-size", out var v14) ? v14 as IReadOnlyList<dynamic> ?? Array.Empty<dynamic>() : Array.Empty<dynamic>();
            foreach (var size in sizeList)
            {
                if (size is not IReadOnlyList<dynamic> offData) continue;
                if (offData.Count < 1) continue;
                var c = offData[0] is string cStr && !string.IsNullOrEmpty(cStr)
                    ? cStr[0]
                    : offData[0] is double cDouble ? (char)(int)cDouble : '\0';
                if (c == '\0') continue;
                var x = offData[1] is double xDouble ? (int)xDouble : 0;
                sizes ??= new List<Size>();
                sizes.Add(new Size { C = c, X = x });
            }
            ctx.Sizes = sizes == null ? Array.Empty<Size>() : sizes;

            ctx.FontRenderType = objDict.TryGetValue("font-render-type", out var v15) ? v15 is double dType ? (sbyte)dType : (sbyte)-1 : (sbyte)-1;

            return score;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }

    private static string BuildJson(in Context curr)
    {
        static string BuildGridOffset(IReadOnlyList<AtlasGen.OffsetData> offsets)
        {
            if (offsets.Count < 1) return "[\n    // [\"?\", 0, 0]\n    // [here are 0 items...]\n  ]";

            var sb = new StringBuilder();
            sb.Append("[\n");
            for (var i = 0; i < offsets.Count; i++)
            {
                var d = offsets[i];
                if (i != 0) sb.Append(",\n");
                sb.Append("    [\"");
                sb.Append(d.C).Append('"').Append($", {d.X}, {d.Y}]");
            }

            sb.Append("\n  ]");
            return sb.ToString();
        }

        static string BuildCharOffset(IReadOnlyList<Offset> offsets)
        {
            if (offsets.Count < 1) return "[\n    // [\"?\", 0]\n    // [here are 0 items...]\n  ]";

            var sb = new StringBuilder();
            sb.Append("[\n");
            for (var i = 0; i < offsets.Count; i++)
            {
                var d = offsets[i];
                if (i != 0) sb.Append(",\n");
                sb.Append("    [\"");
                sb.Append(d.C).Append('"').Append($", {d.Y}]");
            }
            sb.Append("\n  ]");
            return sb.ToString();
        }

        static string BuildCharSize(IReadOnlyList<Size> offsets)
        {
            if (offsets.Count < 1) return "[\n    // [\"?\", 0]\n    // [here are 0 items...]\n  ]";

            var sb = new StringBuilder();
            sb.Append("[\n");
            for (var i = 0; i < offsets.Count; i++)
            {
                var d = offsets[i];
                if (i != 0) sb.Append(",\n");
                sb.Append("    [\"");
                sb.Append(d.C).Append('"').Append($", {d.X}]");
            }
            sb.Append("\n  ]");
            return sb.ToString();
        }

        var jsonSb = new StringBuilder();
        jsonSb.Append("{\n  // font file").Append('\n');
        jsonSb.Append($"  \"font-path\": \"{curr.FontPath}\",").Append('\n');
        jsonSb.Append($"  // atlas output path\n  \"output-path\": \"{curr.OutputPath}\",").Append('\n');
        jsonSb.Append($"  // render conf\n  \"em-size\": {curr.EmSize},").Append('\n');
        jsonSb.Append($"  // render conf\n  \"grid-size\": {curr.CharSize},").Append('\n');
        jsonSb.Append($"  // render conf\n  \"grid-offset\": [{curr.OffsetData.x}, {curr.OffsetData.y}],").Append('\n');
        jsonSb.Append($"  // render conf\n  \"spc-grid-offset\": {BuildGridOffset(curr.SpcOffice)},").Append('\n');
        jsonSb.Append($"  // render conf\n  \"char-offset-y\": {BuildCharOffset(curr.Offsets)},").Append('\n');
        jsonSb.Append($"  // render conf\n  \"char-size\": {BuildCharSize(curr.Sizes)},").Append('\n');
        jsonSb.Append($"  // render conf\n  \"canvas-size\": {curr.CanvasSize},").Append('\n');
        jsonSb.Append($"  // render conf\n  \"font-render-type\": {curr.FontRenderType}, // {(TextRenderingHint)curr.FontRenderType}").Append('\n');
        jsonSb.Append($"  // ex char set\n  \"char-set\": \"\",").Append('\n');
        jsonSb.Append($"  // is char set additional\n  \"addition-char-set\": {(curr.IsAdditionalCharSet ? "true" : "false")},").Append('\n');
        jsonSb.Append($"  // scripts need to transcode\n  \"script\": [],").Append('\n');
        jsonSb.Append($"  // is scan folder\n  \"scan-folder-script\": {(curr.IsScanFolder ? "true" : "false")},").Append('\n');
        jsonSb.Append($"  // need to gen util\n  \"output-util\": {(curr.IsOutputUtil ? "true" : "false")}").Append('\n');
        jsonSb.Append('}').Append('\n');
        return jsonSb.ToString();
    }
}