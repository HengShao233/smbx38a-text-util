using System.Text.RegularExpressions;
using AsciiBinary;

// ReSharper disable MemberCanBePrivate.Global

namespace FontAtlasGenerator;

public static partial class StringEncoder
{
    // 编码规则:
    //     ascii 字符: [space][ascii-code]
    //     码表字符: [35~126][35~126]

    public const int MaxCode = 8192 + 129;

    public static string Encoding(ReadOnlySpan<char> src, IReadOnlyDictionary<char, int> charMap)
    {
        if (src.IsEmpty) return "";
        var sb = new List<ulong>();
        var sbSpText = new List<ulong>();
        var spDict = new Dictionary<string, int>();

        if (src.Length >= MaxCode) src = src[..(MaxCode - 1)];
        var strLength = 0;

        Append(129, sb, false, true);
        strLength = 0;

        var escaping = false;
        for (var i = 0; i < src.Length; i++)
        {
            var c = src[i];
            if (c == '\\' && !escaping)
            {
                escaping = true;
                continue;
            }

            if (escaping)
            {
                if (c == '{')
                {
                    for (var j = i + 1; j < src.Length; j++)
                    {
                        var cc = src[j];
                        if (char.IsNumber(cc))
                            continue;
                        if (char.IsAsciiLetter(cc) ||
                            cc is '+' or '-' or '/' or '*' or '.' or '~' or '#' or '@' or '%' or '$' or '[' or ']' or '|' or ':' or ',' or '_' or '=' or ';' or '!' or '?' or '&' or '<' or '>' or '(' or ')' or ' ')
                            continue;

                        if (cc == '}')
                        {
                            if (j == i + 1) break;
                            var s = src.Slice(i + 1, j - i - 1);
                            var id = double.TryParse(s, out var v) ? (long)v : -1;
                            switch (id)
                            {
                                case < 0:
                                    sb.Add(0);
                                    strLength++;
                                    var sss = s.ToString();
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

        void Append(int c, List<ulong> sbInner, bool appendO = false, bool isUseId = false)
        {
            var cId = c;

            if (c is >= 0 and <= 128 && !isUseId) sbInner.Add((uint)c);
            else if (isUseId || charMap.TryGetValue((char)c, out cId))
            {
                cId += 128;
                if (cId is > MaxCode or < 0) cId = MaxCode;

                if (!appendO) sbInner.Add((ulong)cId);
                else sbInner[0] = (ulong)cId;
            }
            else sbInner.Add(0);
            if (sbInner == sb) strLength++;
        }
    }

    public static readonly Regex StrMatch = StrMatchGen();
    public static string? ReplaceALine(string? line, IReadOnlyDictionary<char, int> charMap, ref uint count)
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

    [GeneratedRegex("(TXT\\s*\\(\\s*D\\s*\\(\\s*)?\\$\"([\\s\\S]+)\"")]
    private static partial Regex StrMatchGen();
}