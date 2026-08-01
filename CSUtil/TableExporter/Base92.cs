using System;
using System.Text;

namespace TableExporter
{
    // Base92 编解码。
    // 字母表为 92 个可打印 ASCII 字符：包含空格(0x20)，按 ASCII 码升序排列，
    // 排除 '"'(0x22) '\\'(0x5C) '`'(0x60) 三个字符。
    // 数位 0 对应空格。这与导出的 TeaScript 脚本里 _D/_D_Dict 必须保持一致。
    public static class Base92
    {
        // 字母表动态生成，保证恰好 92 个字符：包含空格(0x20)，再加上 0x21..0x7E 中
        // 排除 '"'(0x22) '\\'(0x5C) '`'(0x60) 三个字符后的所有可打印 ASCII。
        // 数位 0 对应空格。
        public static readonly string Alphabet = BuildAlphabet();

        // 数位 0 是空格（字母表第 0 位）。
        public const char ZeroChar = ' ';

        public static int AlphabetLength => Alphabet.Length; // 应为 92

        static Base92()
        {
            if (Alphabet.Length != 92)
                throw new InvalidOperationException("Base92 字母表长度必须为 92，当前为 " + Alphabet.Length);
        }

        private static string BuildAlphabet()
        {
            var sb = new StringBuilder(92);
            for (var code = 0x20; code <= 0x7E; code++)
            {
                if (code is 0x22 or 0x5C or 0x60) continue; // 排除引号/反斜杠/反引号
                sb.Append((char)code);
            }

            return sb.ToString();
        }

        // 非负整数 -> Base92 字符串（最高位在前；0 编码为单个空格）。
        public static string EncodeUInt(ulong n)
        {
            if (n == 0)
                return ZeroChar.ToString();

            var digits = new char[12]; // 92^11 > 2^64
            int len = 0;
            ulong v = n;
            while (v > 0)
            {
                int d = (int)(v % 92);
                digits[len++] = Alphabet[d];
                v /= 92;
            }
            // digits 现在是低位在前，反转得到高位在前。
            var sb = new StringBuilder(len);
            for (int i = len - 1; i >= 0; i--)
                sb.Append(digits[i]);
            return sb.ToString();
        }

        // Base92 字符串 -> 无符号整数。遇到字母表外字符视为 0（空格等价）。
        public static ulong DecodeUInt(string s)
        {
            ulong result = 0;
            if (string.IsNullOrEmpty(s)) return 0;
            foreach (char c in s)
            {
                int idx = Alphabet.IndexOf(c);
                int d = idx < 0 ? 0 : idx;
                result = result * 92 + (ulong)d;
            }
            return result;
        }

        // --- 有符号整数：用 zigzag 映射到非负后再 Base92 编码 ---
        // zigzag: 0->0, -1->1, 1->2, -2->3, 2->4 ...
        private static ulong ZigZagEncode(long v) => (ulong)((v << 1) ^ (v >> 63));

        private static long ZigZagDecode(ulong u)
        {
            long v = (long)u;
            return (-(v & 1)) ^ (v >> 1);
        }

        public static string EncodeInt(int v) => EncodeUInt(ZigZagEncode(v));

        public static int DecodeInt(string s) => (int)ZigZagDecode(DecodeUInt(s));

        // 定点小数：保留 4 位小数，乘 10000 后按整数编码。
        public static string EncodeFloat(double v)
        {
            long scaled = (long)Math.Round(v * 10000.0, MidpointRounding.AwayFromZero);
            return EncodeInt((int)scaled);
        }

        public static double DecodeFloat(string s)
        {
            int scaled = DecodeInt(s);
            return scaled / 10000.0;
        }
    }
}
