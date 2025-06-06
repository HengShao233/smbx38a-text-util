using System.Drawing.Text;
using Utf8Json;
using System.Text;

namespace FontAtlasGenerator;

internal static class Program
{
    private const string Version = "2.0.0";

    private struct Context
    {
        public string FontPath = "";
        public float EmSize = 0;
        public string OutputPath = "";
        public (float x, float y) Offset = (0, 0);
        public int CanvasSize = 0;
        public int CharSize = 0;
        public bool IsAdditionalCharSet = true;
        public IReadOnlyList<char> Content = Array.Empty<char>();
        public IReadOnlyList<string>? Scripts = null;
        public bool IsOutputUtil = false;
        public bool IsScanFolder = false;
        public IReadOnlyList<AtlasGen.OffsetData> SpcOffice = Array.Empty<AtlasGen.OffsetData>();
        public sbyte FontRenderType = -1;

        public Context() {}
    }

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
            ctx.Offset = (offsetX, offsetY);

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

            var spcOffList = (List<AtlasGen.OffsetData>?)null;
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
            ctx.FontRenderType = objDict.TryGetValue("font-render-type", out var v13) ? v13 is double dType ? (sbyte)dType : (sbyte)-1 : (sbyte)-1;

            return score;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }
    }

    private static void Main(string[] args)
    {
        if (args is { Length: > 0 } &&
            args.Any(v => v.ToLower() is
                "--version" or
                "-v" or
                "version" or
                "\\v" or
                // ReSharper disable once StringLiteralTypo
                "\\version"))
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"Font Atlas Generator VERSION {Version}");
            return;
        }

        try
        {
            var smtList = new List<string>();
            var jsonList = new List<string>();

            foreach (var arg in args)
            {
                if (!File.Exists(arg)) continue;
                var ext = Path.GetExtension(arg);
                switch (ext)
                {
                    case ".json":
                        jsonList.Add(arg);
                        break;
                    case ".smt":
                        smtList.Add(arg);
                        break;
                }
            }

            var score = 0;
            var curr = new Context();
            var targetJson = "";
            foreach (var json in jsonList)
            {
                using var f = File.Open(json, FileMode.Open, FileAccess.Read);
                var sc = CheckJson(f, out var context);
                if (sc <= score) continue;
                curr = context;
                score = sc;
                targetJson = json;
            }

            if (!string.IsNullOrEmpty(targetJson))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"found json: {targetJson}");
                Console.ForegroundColor = ConsoleColor.White;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" ");
            Console.WriteLine("-------------------------------");
            Console.WriteLine(" ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("curr environment data: \n{\n  // font file");
            Console.WriteLine($"  \"font-path\": \"{curr.FontPath}\",");
            Console.WriteLine($"  // atlas output path\n  \"output-path\": \"{curr.OutputPath}\",");
            Console.WriteLine($"  // render conf\n  \"em-size\": {curr.EmSize},");
            Console.WriteLine($"  // render conf\n  \"grid-size\": {curr.CharSize},");
            Console.WriteLine($"  // render conf\n  \"grid-offset\": [{curr.Offset.x}, {curr.Offset.y}],");
            Console.WriteLine($"  // render conf\n  \"spc-grid-offset\": [\n    // [\"?\", 0, 0]\n    // [here are {curr.SpcOffice.Count} items...]\n  ],");
            Console.WriteLine($"  // render conf\n  \"canvas-size\": {curr.CanvasSize},");
            Console.WriteLine($"  // render conf\n  \"font-render-type\": {curr.FontRenderType}, // {(TextRenderingHint)curr.FontRenderType}");
            Console.WriteLine($"  // ex char set\n  \"char-set\": \"a char set with {curr.Content.Count} chars\",");
            Console.WriteLine($"  // is char set additional\n  \"addition-char-set\": {(curr.IsAdditionalCharSet ? "true" : "false")},");
            Console.WriteLine($"  // scripts need to transcode\n  \"script\": [\"*here are {curr.Scripts?.Count ?? 0} script files...*\"],");
            Console.WriteLine($"  // is scan folder\n  \"scan-folder-script\": {(curr.IsScanFolder ? "true" : "false")},");
            Console.WriteLine($"  // need to gen util\n  \"output-util\": {(curr.IsOutputUtil ? "true" : "false")},");
            Console.WriteLine("}");
            Console.WriteLine(" ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("-------------------------------");
            Console.WriteLine(" ");

            foreach (var d in curr.SpcOffice)
                AtlasGen.SingleCharOffset[d.C] = d;

            var xCnt = 170;
            if (File.Exists(curr.FontPath) && Directory.Exists(curr.OutputPath))
            {
                // 生成字符贴图集
                xCnt = AtlasGen.Gen(
                    curr.FontPath,
                    curr.EmSize,
                    curr.OutputPath,
                    curr.Offset,
                    curr.CanvasSize,
                    curr.CharSize,
                    curr.IsAdditionalCharSet
                        ? new AtlasGen.MultipleListAgent<char>(new[]
                        {
                            new AtlasGen.StringListAgent(CommonStandardHanzi.S),
                            curr.Content
                        })
                        : curr.Content,
                    curr.FontRenderType
                );

                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("-------------------------------");
                Console.WriteLine(" ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("gen atlas success");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(" ");
                Console.WriteLine("-------------------------------");
                Console.WriteLine(" ");
            }

            for (var i = 0; i < curr.Content.Count; i++)
            {
                var addChar = curr.Content[i];
                CommonStandardHanzi.LoadAdditionalChar(addChar,
                    curr.IsAdditionalCharSet ? CommonStandardHanzi.S.Length + i : i, curr.IsAdditionalCharSet);
            }

            if (curr.IsScanFolder || (jsonList.Count <= 0 && smtList.Count <= 0))
                EnumModifySmt(Directory.EnumerateFiles(".", "*.smt", SearchOption.TopDirectoryOnly));
            else EnumModifySmt(curr.Scripts == null ? smtList : smtList.Concat(curr.Scripts));

            static void EnumModifySmt(IEnumerable<string> l)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("-------------------------------");
                Console.WriteLine(" ");
                foreach (var file in l)
                {
                    if (Path.GetFileName(file).TrimStart().StartsWith("encoded")) continue;
                    using var f = File.Open(file, FileMode.Open, FileAccess.ReadWrite);
                    using var s = new StreamReader(f);
                    var sb = new StringBuilder();

                    var successCnt = (uint)0;
                    while (!s.EndOfStream)
                        sb.Append(StringEncoder.ReplaceALine(s.ReadLine(), CommonStandardHanzi.CharIdMap, ref successCnt)).Append('\n');
                    if (sb.Length > 0) sb.Length--;

                    f.Close();
                    if (successCnt <= 0) continue;

                    var fName = Path.GetFileNameWithoutExtension(file);
                    using var f2 = File.Open($"encoded-{fName}.smt", FileMode.Create,
                        FileAccess.Write);
                    using var sw = new StreamWriter(f2);
                    sw.WriteLine(sb);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine($"transcoding str from: {file}");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"------------ save to: {f2.Name}");
                }
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine(" ");
                Console.WriteLine("-------------------------------");
            }

            if (curr.IsOutputUtil)
            {
                using var ff = File.Open("./TxtDecoder.smt", FileMode.Create, FileAccess.Write);
                using var sw = new StreamWriter(ff);
                sw.Write(CodeTemplate.S.Replace("{xCnt}", $"{xCnt}").Replace("{sizeX}", $"{curr.CharSize}"));
            }
        }
        catch (Exception e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(e);
        }

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine("please press any ket to exit...");
        Console.ReadKey();
    }
}