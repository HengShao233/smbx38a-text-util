using System.Text;

namespace FontAtlasGenerator;


internal static partial class Program
{
    private const string Version = "2.2.2";

    private struct Offset
    {
        public long C;
        public int Y;
    }

    private struct Size
    {
        public long C;
        public int X;
    }

    private struct Context
    {
        public string FontPath = "";
        public float EmSize = 0;
        public string OutputPath = "";
        public (float x, float y) OffsetData = (0, 0);
        public int CanvasSize = 0;
        public int CharSize = 0;
        public bool IsAdditionalCharSet = true;
        public IReadOnlyList<long> Content = Array.Empty<long>();
        public IReadOnlyList<string>? Scripts = null;
        public bool IsOutputUtil = false;
        public bool IsScanFolder = false;
        public IReadOnlyList<AtlasGen.OffsetData> SpcOffice = Array.Empty<AtlasGen.OffsetData>();
        public sbyte FontRenderType = -1;
        public IReadOnlyList<Offset> Offsets = Array.Empty<Offset>();
        public IReadOnlyList<Size> Sizes = Array.Empty<Size>();

        public string? FormattedSourceChatSet = null;

        public Context() {}
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

            const string jsonName = "cfg";

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
            var isFound = false;
            foreach (var json in jsonList)
            {
                using var f = File.Open(json, FileMode.Open, FileAccess.Read);
                var sc = CheckJson(f, out var context);
                if (sc <= score) continue;
                curr = context;
                score = sc;
                targetJson = json;
                isFound = true;
            }

            if (!string.IsNullOrEmpty(targetJson))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"found json: {targetJson}");
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                if (File.Exists($"./.{jsonName}.json"))
                {
                    using var f = File.Open($"./.{jsonName}.json", FileMode.Open, FileAccess.Read);
                    CheckJson(f, out var context);
                    isFound = true;
                    curr = context;

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("read default cfg json");
                    Console.ForegroundColor = ConsoleColor.White;
                }
            }

            var j = BuildJson(curr);
            if (!isFound)
            {
                using var ff = File.Open($"./.{jsonName}.json", FileMode.Create, FileAccess.Write);
                using var sw = new StreamWriter(ff);
                sw.Write(j);
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(" ");
            Console.WriteLine("-------------------------------");
            Console.WriteLine(" ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"curr environment data: \n{j}");
            Console.WriteLine(" ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("-------------------------------");
            Console.WriteLine(" ");

            foreach (var d in curr.SpcOffice)
                AtlasGen.SingleCharOffset[d.C] = d;

            var xCnt = curr.CanvasSize / curr.CharSize;
            if (smtList.Count <= 0 && !string.IsNullOrWhiteSpace(curr.OutputPath) && File.Exists(curr.FontPath) && Directory.Exists(curr.OutputPath))
            {
                // 生成字符贴图集
                xCnt = AtlasGen.Gen(
                    curr.FontPath,
                    curr.EmSize,
                    curr.OutputPath,
                    curr.OffsetData,
                    curr.CanvasSize,
                    curr.CharSize,
                    curr.IsAdditionalCharSet
                        ? new AtlasGen.MultipleListAgent<long>(new[]
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

            // 运行时根据 ctx.Content 动态构建字符映射，保证与 atlas 生成 (151-157 行) 的字符集一致。
            var charIdMap = BuildCharIdMap(curr.Content, curr.IsAdditionalCharSet);

            if (curr.IsScanFolder)
                EnumModifySmt(Directory.EnumerateFiles(".", "*.smt", SearchOption.TopDirectoryOnly));
            else EnumModifySmt(curr.Scripts == null ? smtList : smtList.Concat(curr.Scripts));

            void EnumModifySmt(IEnumerable<string> l)
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
                        sb.Append(StringEncoder.ReplaceALine(s.ReadLine(), charIdMap, ref successCnt)).Append('\n');
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

            IReadOnlyDictionary<long, int> BuildCharIdMap(IReadOnlyList<long> content, bool isAdditional)
            {
                var map = new Dictionary<long, int>();
                var offset = 0;
                if (isAdditional)
                {
                    var sCps = CommonStandardHanzi.SCodepoints;
                    for (var idx = 0; idx < sCps.Count; idx++)
                        map[sCps[idx]] = idx;
                    offset = sCps.Count;
                }
                for (var i = 0; i < content.Count; i++)
                    map[content[i]] = offset + i;
                return map;
            }

            if (curr.IsOutputUtil)
            {
                using var ff = File.Open("./TxtDecoder.smt", FileMode.Create, FileAccess.Write);
                using var sw = new StreamWriter(ff);
                sw.Write(GenCode(curr, xCnt, charIdMap));
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