using System;
using System.Collections.Generic;
using System.Text;

namespace TableExporter;

/// <summary>
/// 把任意 uuid 字符串映射为「在本表内无碰撞」的整数槽位。
/// <para>
/// 运行时（TeaScript）只需用同样算法把 uuid 算成槽位（0-based），再加 1 即得到
/// <c>__row_map</c> 中的行号。哈希函数：<c>h = (h * P + code) mod M</c>，
/// 其中 <c>M</c> 为哈希表大小（质数），<c>P</c> 与盐从候选表中搜索到无碰撞为止。
/// 由于 <c>h &lt; M</c> 且 <c>P</c> 不大，<c>h * P + code</c> 的中间值被限制在 2^53 以内，
/// TeaScript 浮点运算完全精确。
/// </para>
/// </summary>
public sealed class PerfectHash
{
    /// <summary>模数上限（仅用于文档说明），实际模数为 TableSize。</summary>
    public const int MaxModulus = 2147483647;

    /// <summary>
    /// 乘子上限。需保证 h*P + code &lt; 2^53，即 P &lt; 2^53 / M。
    /// 当 M 为数百量级时，P 取几千以内即可。
    /// </summary>
    private static readonly int[] MultiplierCandidates =
    [
        31, 33, 37, 41, 43, 47, 53, 59, 61, 67, 71, 73, 79, 83, 89, 97,
        101, 103, 107, 109, 113, 127, 131, 137, 139, 149, 151, 157, 163, 167,
        173, 179, 181, 191, 193, 197, 199, 211, 223, 227, 229, 233, 239, 241,
        251, 257, 263, 269, 271, 277, 281, 283, 293, 307, 311, 313, 317, 331,
        337, 347, 349, 353, 359, 367, 373, 379, 383, 389, 397, 401, 409, 419,
        421, 431, 433, 439, 443, 449, 457, 461, 463, 467, 479, 487, 491, 499,
        503, 509, 521, 523, 541, 547, 557, 563, 569, 571, 577, 587, 593, 599,
        601, 607, 613, 617, 619, 631, 641, 643, 647, 653, 659, 661, 673, 677,
        683, 691, 701, 709, 719, 727, 733, 739, 743, 751, 757, 761, 769, 773,
        787, 797, 809, 811, 821, 823, 827, 829, 839, 853, 857, 859, 863, 877,
        881, 883, 887, 907, 911, 919, 929, 937, 941, 947, 953, 967, 971, 977,
        983, 991, 997, 1009, 1013, 1019, 1021, 1031, 1033, 1039, 1049, 1051,
        1061, 1063, 1069, 1087, 1091, 1093, 1097, 1103, 1109, 1117, 1123, 1129,
        1151, 1153, 1163, 1171, 1181, 1187, 1193, 1201, 1213, 1217, 1223, 2053,
        3079, 4093,
    ];

    /// <summary>哈希表大小（质数）。</summary>
    public int TableSize { get; private init; }

    /// <summary>选定的乘子。</summary>
    public int Multiplier { get; private init; }

    /// <summary>选定的初始盐。</summary>
    public int Seed { get; private init; }

    private PerfectHash(int tableSize, int multiplier, int seed)
    {
        TableSize = tableSize;
        Multiplier = multiplier;
        Seed = seed;
    }

    /// <summary>计算哈希槽位（0-based）。与生成的 TeaScript 运行时实现逐位一致。</summary>
    public int Compute(string key) => Compute(key, Multiplier, Seed, TableSize);

    /// <summary>纯函数版本，便于搜索参数时复用。</summary>
    public static int Compute(string key, int multiplier, int seed, int modulus)
    {
        long h = seed;
        foreach (var c in key)
        {
            h = (h * multiplier + c) % modulus;
        }

        return (int)h;
    }

    public static int NextPrime(int n)
    {
        if (n < 2) return 2;
        int candidate = n;
        while (!IsPrime(candidate)) candidate++;
        return candidate;
    }

    private static bool IsPrime(int n)
    {
        if (n < 2) return false;
        if (n % 2 == 0) return n == 2;
        for (int i = 3; i * i <= n; i += 2)
            if (n % i == 0) return false;
        return true;
    }

    /// <summary>搜索一组能让所有 key 互不碰撞的参数（mod tableSize）。</summary>
    /// <exception cref="ExportException">在候选空间内找不到无碰撞参数时抛出。</exception>
    public static PerfectHash Build(IReadOnlyList<string> keys, string tableName, int tableSize)
    {
        if (keys.Count == 0)
        {
            return new PerfectHash(tableSize, 31, 0);
        }

        var seen = new HashSet<int>(keys.Count);

            foreach (var multiplier in MultiplierCandidates)
            {
                // 乘数必须小于表大小，否则 (multiplier mod tableSize) 退化成弱哈希。
                if (multiplier >= tableSize)
                {
                    continue;
                }

                // 盐的搜索范围适当扩大以在较大表上仍能命中无碰撞参数。
                for (var seed = 0; seed < 4096; seed++)
            {
                seen.Clear();
                var ok = true;
                foreach (var key in keys)
                {
                    if (!seen.Add(Compute(key, multiplier, seed, tableSize)))
                    {
                        ok = false;
                        break;
                    }
                }

                if (ok)
                {
                    return new PerfectHash(tableSize, multiplier, seed);
                }
            }
        }

        throw new ExportException(
            $"[{tableName}] 无法为 {keys.Count} 条 uuid 找到无碰撞哈希参数，" +
            "请检查是否存在完全重复的 id，或扩大候选参数表。");
    }

    /// <summary>把参数信息渲染成注释，便于排查。</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.Append("multiplier=").Append(Multiplier)
          .Append(", seed=").Append(Seed)
          .Append(", tableSize=").Append(TableSize);
        return sb.ToString();
    }
}
