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

        // 文本转码模式标志：不再单独走 stdin 流式子命令，而是进入下方主流程，
        // 先解析 cfg json（CheckJson/BuildJson），再对入参中的文字逐条转码
        // （StringEncoder.Encoding），并把转码载荷逐行输出到 stdout。
        // 此模式下所有信息性输出改走 stderr，保证 stdout 只含转码结果。
        var transcodeMode = args is { Length: > 0 } && args.Any(v => v == "--transcode" || v == "-t");

        try
        {
            var smtList = new List<string>();
            var jsonList = new List<string>();
            var textList = new List<string>();

            const string jsonName = "cfg";

            // 转码模式下所有信息性输出改走 stderr，保证 stdout 只含转码载荷。
            var infoOut = transcodeMode ? Console.Error : Console.Out;

            foreach (var arg in args)
            {
                if (arg == "--transcode" || arg == "-t") continue;
                if (!File.Exists(arg))
                {
                    // 非文件入参视为待转码文字。
                    textList.Add(arg);
                    continue;
                }

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
                infoOut.WriteLine($"found json: {targetJson}");
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
                    infoOut.WriteLine("read default cfg json");
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
            infoOut.WriteLine(" ");
            infoOut.WriteLine("-------------------------------");
            infoOut.WriteLine(" ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            infoOut.WriteLine($"curr environment data: \n{j}");
            infoOut.WriteLine(" ");
            Console.ForegroundColor = ConsoleColor.White;
            infoOut.WriteLine("-------------------------------");
            infoOut.WriteLine(" ");

            foreach (var d in curr.SpcOffice)
                AtlasGen.SingleCharOffset[d.C] = d;

            // 空配置（无 canvas-size/char-size）下避免除零；xCnt 仅用于 TxtDecoder 生成。
            var xCnt = curr.CharSize > 0 ? curr.CanvasSize / curr.CharSize : 0;
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
                infoOut.WriteLine("-------------------------------");
                infoOut.WriteLine(" ");
                Console.ForegroundColor = ConsoleColor.Green;
                infoOut.WriteLine("gen atlas success");
                Console.ForegroundColor = ConsoleColor.White;
                infoOut.WriteLine(" ");
                infoOut.WriteLine("-------------------------------");
                infoOut.WriteLine(" ");
            }

            // 运行时根据 ctx.Content 动态构建字符映射，保证与 atlas 生成 (151-157 行) 的字符集一致。
            var charIdMap = BuildCharIdMap(curr.Content, curr.IsAdditionalCharSet);

            // 对入参中的文字逐条转码，并把转码载荷逐行输出到 stdout。
            // 与文件模式 StringEncoder.ReplaceALine 共用同一套字符映射与转码方法。
            if (textList.Count > 0)
            {
                // 显式使用 UTF-8，避免 stdout 被重定向时退回到控制台默认 OEM 编码（如 GBK）导致乱码。
                using var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
                foreach (var text in textList)
                {
                    try
                    {
                        stdout.WriteLine(EncodeToPayload(text, charIdMap));
                    }
                    catch (Exception e)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine($"[transcode] failed on text: {e.Message}");
                        Console.ForegroundColor = ConsoleColor.White;
                        stdout.WriteLine(string.Empty);
                    }
                }
            }

            // 仅转码模式：传入了 .smt 但当前没有可用字体图集时，跳过图集生成，
            // 直接对 $"..." 字面量做字模转码。注意：此模式下 text 列运行时仍需要
            // 一份与图集匹配的 TxtDecoder.smt 才能真正还原字形。
            if (smtList.Count > 0 && !File.Exists(curr.FontPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                infoOut.WriteLine("[transcode-only] 未提供字体图集，仅对文本做转码（不生成图集）");
                Console.ForegroundColor = ConsoleColor.White;
            }

            if (curr.IsScanFolder)
                EnumModifySmt(Directory.EnumerateFiles(".", "*.smt", SearchOption.TopDirectoryOnly));
            else EnumModifySmt(curr.Scripts == null ? smtList : smtList.Concat(curr.Scripts));

            void EnumModifySmt(IEnumerable<string> l)
            {
                Console.ForegroundColor = ConsoleColor.White;
                infoOut.WriteLine("-------------------------------");
                infoOut.WriteLine(" ");
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
                    infoOut.WriteLine($"transcoding str from: {file}");
                    Console.ForegroundColor = ConsoleColor.Green;
                    infoOut.WriteLine($"------------ save to: {f2.Name}");
                }
                Console.ForegroundColor = ConsoleColor.White;
                infoOut.WriteLine(" ");
                infoOut.WriteLine("-------------------------------");
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
            // 转码模式下信息性输出走 stderr，保证 stdout 只有转码载荷。
            var errOut = transcodeMode ? Console.Error : Console.Out;
            Console.ForegroundColor = ConsoleColor.Red;
            errOut.WriteLine(e);
        }

        // 注意：不再调用 Console.ReadKey()，以避免在 stdio 被重定向（如被子进程调用）
        // 时抛 InvalidOperationException 导致进程崩溃。转码/图集生成完成后直接退出。
        Console.ForegroundColor = ConsoleColor.White;
    }

    /// <summary>
    /// 对单条文本做字模转码，返回可直接回传给调用方的载荷
    /// （等价于剥离 TXD 的 TXT(D(" ... ")) 外层包裹）。
    /// </summary>
    private static string EncodeToPayload(string text, IReadOnlyDictionary<long, int> charIdMap)
    {
        // 与文件模式 StringEncoder.ReplaceALine 保持一致：先对反斜杠做预处理，再转码。
        var processed = text.Replace("\\", "\\\\");
        var encoded = StringEncoder.Encoding(processed.AsSpan(), charIdMap);

        // 与文件模式 ExtractEncodedPayload 等价：剥离外层 TXT(D(" ... ")) 包裹，
        // 只回传载荷本体，使调用方无需再做二次解析。
        var first = encoded.IndexOf('"');
        var last = encoded.LastIndexOf('"');
        return first >= 0 && last > first
            ? encoded.Substring(first + 1, last - first - 1)
            : encoded;
    }
}