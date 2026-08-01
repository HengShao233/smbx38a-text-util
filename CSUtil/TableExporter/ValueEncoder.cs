using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TableExporter;

/// <summary>
/// 把单元格原始文本按字段类型编码为写入 <c>__data_chunk</c> 的紧凑串。
/// <para>
/// 设计约束（与导出的 TeaScript 运行时完全对应）：
/// 1. 整数 / 浮点 / 布尔 / 引用 一律用无符号 base92 编码（<see cref="Base92"/>），
///    运行时通过 <c>CUMath_Decode(..., 92)</c> 还原。文档案例均为非负数，故不引入 zigzag。
/// 2. 字符串 / 富文本 存入 FontAtlasGenerator 转码后的 payload（一串 base92 安全字符）。
///    富文本访问器包 <c>TXT(D("payload"))</c>；纯 ascii 字符串原样返回，非 ascii 字符串
///    自动升级为 payload + <c>TXT(D())</c> 以避免 TeaScript 非 ascii 字面量崩溃。
/// 3. 数组：标量数组每个元素定宽（取该列最大值位数）以支撑 O(1) 定位；字符串数组
///    使用 [4位count][每元素: 4位len + content] 的长度前缀方案。
/// </para>
/// </summary>
public static class ValueEncoder
{
    /// <summary>单个值编码结果。</summary>
    public sealed record Encoded(string Text, bool NeedsTxtWrap);

    private static bool IsAllAscii(string s)
    {
        foreach (var c in s)
        {
            if (c > 127) return false;
        }

        return true;
    }

    /// <summary>
    /// 编码单个标量值。
    /// <paramref name="raw"/> 为单元格原文；对 Ref 类型 <paramref name="raw"/> 已是目标行 1-based 行号字符串。
    /// <paramref name="textResolver"/> 把原文（string/text）转成 FontAtlas payload。
    /// </summary>
    public static Encoded EncodeScalar(FieldType type, string raw, Func<string, string> textResolver)
    {
        switch (type)
        {
            case FieldType.Int:
            {
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                {
                    v = 0;
                }

                // 用 zigzag 编码到无符号再 base92，运行时 CUMath_Decode 得到 zigzag 值后需反 zigzag。
                return new Encoded(Base92.EncodeInt((int)v), false);
            }

            case FieldType.Float:
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    d = 0;
                }

                long scaled = (long)Math.Round(d * 10000.0, MidpointRounding.AwayFromZero);
                return new Encoded(Base92.EncodeInt((int)scaled), false);
            }

            case FieldType.Bool:
            {
                bool b = raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                return new Encoded(Base92.EncodeInt(b ? 1 : 0), false);
            }

            case FieldType.Ref:
            {
                if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
                {
                    row = 0;
                }

                return new Encoded(Base92.EncodeInt((int)row), false);
            }

            case FieldType.Text:
            {
                var payload = textResolver(raw);
                return new Encoded(payload, true);
            }

            case FieldType.String:
            default:
            {
                if (IsAllAscii(raw))
                {
                    return new Encoded(raw, false);
                }

                // 含非 ascii：升级为 payload + TXT(D())，避免字面量崩溃。
                var payload = textResolver(raw);
                return new Encoded(payload, true);
            }
        }
    }

    /// <summary>
    /// 编码数组。返回 (elemTexts, elemWidth)。
    /// <para>
    /// 按文档要求，数组在 chunk 中的存储为：[数组头][元素1][元素2]...。
    /// 数组头是 N 个 8 位段 [chunkId 2位][offset 3位][len 3位] 的定位表，每段指向一个元素。
    /// 本方法只负责编码各元素文本（标量定宽、字符串变长），数组头由 DataPacker 在排入 chunk 时生成。
    /// </para>
    /// </summary>
    public static (List<string> ElemTexts, int ElemWidth) EncodeArray(
        FieldType elemType,
        IReadOnlyList<string> items,
        Func<string, string> textResolver)
    {
        if (elemType is FieldType.String or FieldType.Text)
        {
            var texts = new List<string>(items.Count);
            foreach (var it in items)
            {
                var enc = EncodeScalar(elemType, it, textResolver);
                texts.Add(enc.Text);
            }

            return (texts, -1);
        }

        // 标量数组：定宽。
        var parts = new string[items.Count];
        int maxLen = 1;
        for (int i = 0; i < items.Count; i++)
        {
            var enc = EncodeScalar(elemType, items[i], textResolver);
            parts[i] = enc.Text;
            if (enc.Text.Length > maxLen) maxLen = enc.Text.Length;
        }

        var texts2 = new List<string>(items.Count);
        foreach (var p in parts)
        {
            texts2.Add(p.Length >= maxLen ? p : p.PadLeft(maxLen, Base92.ZeroChar));
        }

        return (texts2, maxLen);
    }

    /// <summary>把串补齐到 4 位（高位补 base92 的 0 字符 = 空格）。</summary>
    public static string Pad4(string s)
    {
        if (s.Length >= 4) return s;
        return s.PadLeft(4, Base92.ZeroChar);
    }
}
