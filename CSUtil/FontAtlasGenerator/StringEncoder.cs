using System.Text;
using System.Text.RegularExpressions;
using AsciiBinary;

// ReSharper disable MemberCanBePrivate.Global

namespace FontAtlasGenerator;

public static partial class StringEncoder
{
    // 编码规则:
    //     ascii 字符: [space][ascii-code]
    //     码表字符: [35~126][35~126]

    public const int MaxCode = 9870 + 129;

    public static string Encoding(ReadOnlySpan<char> src, IReadOnlyDictionary<long, int> charMap)
    {
        if (src.IsEmpty) return "";
        // 先按 Unicode 码点 (utf64) 切分, 规避 UTF-16 代理对 (1 个逻辑字符 = 2 个 char) 被拆成两项.
        var cps = new List<long>(src.Length);
        {
            var i = 0;
            while (i < src.Length)
            {
                if (Rune.DecodeFromUtf16(src[i..], out var rune, out var consumed) == System.Buffers.OperationStatus.Done)
                {
                    cps.Add(rune.Value);
                    i += consumed;
                }
                else
                {
                    cps.Add(src[i]);
                    i += 1;
                }
            }
        }

        if (cps.Count >= MaxCode) cps = cps.GetRange(0, MaxCode - 1);
        var strLength = 0;

        var sb = new List<ulong>();
        var sbSpText = new List<ulong>();
        var spDict = new Dictionary<string, int>();

        Append(129, sb, false, true);
        strLength = 0;

        var escaping = false;
        for (var i = 0; i < cps.Count; i++)
        {
            var c = cps[i];
            if (c == '\\' && !escaping)
            {
                escaping = true;
                continue;
            }

            if (escaping)
            {
                if (c == '{')
                {
                    for (var j = i + 1; j < cps.Count; j++)
                    {
                        var cc = cps[j];
                        if (IsNumberCp(cc))
                            continue;
                        if (IsAsciiLetterCp(cc) || IsFlagLiteral(cc))
                            continue;

                        if (cc == '}')
                        {
                            if (j == i + 1) break;
                            var sbText = new StringBuilder();
                            foreach (var cp in cps.GetRange(i + 1, j - i - 1))
                                sbText.Append(char.ConvertFromUtf32((int)cp));
                            var s = sbText.ToString();
                            var id = double.TryParse(s, out var v) ? (long)v : -1;
                            switch (id)
                            {
                                case < 0:
                                    sb.Add(0);
                                    strLength++;
                                    var sss = s;
                                    if (sss.Length > 50)
                                    {
                                        var color = Console.ForegroundColor;
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"warn: flag lenght is more than 50: {sss}");
                                        sss = sss[..50];
                                        Console.ForegroundColor = color;
                                    }

                                    if (spDict.TryGetValue(sss, out var value)) Append(value, sb, false, true);
                                    else
                                    {
                                        Append(spDict[sss] = sbSpText.Count, sb, false, true);
                                        Append(sss.Length + 40, sbSpText);
                                        sbSpText.AddRange(sss.Select(sc => (ulong)sc));
                                    }
                                    break;
                                case >= MaxCode:
                                    Append(MaxCode, sb, false, true);
                                    break;
                                default:
                                    {
                                        if (id is 39 or 34)
                                        {
                                            id -= 128;
                                            Append((int)id, sb, false, true);
                                        } else Append((int)id, sb);
                                    }
                                    break;
                            }
                            i += j - i;
                            goto BBreak;
                        }
                        break;
                    }

                    Append(c, sb);
                    BBreak:
                    escaping = false;
                    continue;
                }

                Append(c switch
                {
                    // '0' => '\0',
                    // 'a' => '\a',
                    // 'b' => '\b',
                    // 'f' => '\f',
                    // 'n' => '\n',
                    // 'r' => '\r',
                    // 't' => '\t',
                    // 'v' => '\v',
                    _ => c
                }, sb);

                escaping = false;
                continue;
            }

            Append(c, sb);
        }

        if (strLength > 0) Append(strLength + 1, sb, true, true);
        sb.AddRange(sbSpText);
        return new string(AscBin.EncodeReadable(sb.ToArray()).Select(v => (char)v).ToArray());

        void Append(long c, List<ulong> sbInner, bool appendO = false, bool isUseId = false)
        {
            if (c is >= 0 and <= 128 && !isUseId)
            {
                sbInner.Add((uint)c);
            }
            else
            {
                long cId;
                if (isUseId) cId = c;
                else if (charMap.TryGetValue(c, out var id)) cId = id;
                else
                {
                    sbInner.Add(0);
                    if (sbInner == sb) strLength++;
                    return;
                }

                cId += 128;
                if (cId is > MaxCode or < 0) cId = MaxCode;

                if (!appendO) sbInner.Add((ulong)cId);
                else sbInner[0] = (ulong)cId;
            }
            if (sbInner == sb) strLength++;
        }
    }

    public static readonly Regex StrMatch = StrMatchGen();
    public static string? ReplaceALine(string? line, IReadOnlyDictionary<long, int> charMap, ref uint count)
    {
        if (string.IsNullOrEmpty(line)) return line;
        var res = StrMatch.Match(line);
        if (!res.Success) return line;

        var isSuccess = false;
        var s = StrMatch.Replace(line, match =>
        {
            var prefix = match.Groups[1].Value;
            var content = match.Groups[2];
            if (content.Value.Length <= 0) return match.Value;
            if (prefix.Length != 0) return match.Value;
            isSuccess = true;
            return $"TXT(D(\"{Encoding(content.Value, charMap)}\"))";
        });

        if (isSuccess) count++;
        return s;
    }

    private static bool IsNumberCp(long cp) => cp <= 0xFFFF && char.IsNumber((char)cp);

    private static bool IsAsciiLetterCp(long cp)
        => cp is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsFlagLiteral(long cp)
        => cp is '+' or '-' or '/' or '*' or '.' or '~' or '#' or '@' or '%' or '$'
            or '[' or ']' or '|' or ':' or ',' or '_' or '=' or ';' or '!' or '?'
            or '&' or '<' or '>' or '(' or ')' or ' ';

    [GeneratedRegex("(TXT\\s*\\(\\s*D\\s*\\(\\s*)?\\$\"([\\s\\S]+)\"")]
    private static partial Regex StrMatchGen();
}
