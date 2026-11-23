using System.Globalization;

namespace DS1Companion.Core.Memory;

/// <summary>Шаблон байтов вида "48 8B 05 ?? ?? ?? ?? 48 85 C0" ('?' или '??' — любой байт).</summary>
public sealed class AobPattern
{
    public byte[] Bytes { get; }
    public bool[] Mask { get; } // true — байт должен совпасть
    public int Length => Bytes.Length;

    private AobPattern(byte[] bytes, bool[] mask)
    {
        Bytes = bytes;
        Mask = mask;
    }

    public static AobPattern Parse(string pattern)
    {
        var tokens = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) throw new FormatException("Пустой AOB-шаблон");
        var bytes = new byte[tokens.Length];
        var mask = new bool[tokens.Length];
        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t is "?" or "??") continue;
            if (t.Length != 2 || !byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                throw new FormatException($"Неверный байт '{t}' в AOB-шаблоне");
            mask[i] = true;
        }
        if (!mask.Any(m => m)) throw new FormatException("AOB-шаблон из одних масок");
        return new AobPattern(bytes, mask);
    }

    public bool MatchesAt(ReadOnlySpan<byte> data, int index)
    {
        if (index < 0 || index + Length > data.Length) return false;
        for (int j = 0; j < Length; j++)
            if (Mask[j] && data[index + j] != Bytes[j]) return false;
        return true;
    }
}

public static class AobScanner
{
    /// <summary>Индекс первого совпадения в буфере или -1.</summary>
    public static int Find(ReadOnlySpan<byte> data, AobPattern pattern)
    {
        // Якорь — первый немаскированный байт, ищем его быстрым IndexOf.
        int anchor = Array.IndexOf(pattern.Mask, true);
        byte anchorByte = pattern.Bytes[anchor];
        int pos = 0;
        while (pos <= data.Length - pattern.Length)
        {
            int rel = data[(pos + anchor)..].IndexOf(anchorByte);
            if (rel < 0) return -1;
            int candidate = pos + rel;
            if (candidate + pattern.Length > data.Length) return -1;
            if (pattern.MatchesAt(data, candidate)) return candidate;
            pos = candidate + 1;
        }
        return -1;
    }

    /// <summary>Сканирует модуль процесса кусками с перекрытием. Возвращает абсолютный адрес или null.</summary>
    public static long? ScanModule(IProcessMemory memory, AobPattern pattern, int chunkSize = 4 * 1024 * 1024)
    {
        var buffer = new byte[chunkSize + pattern.Length];
        long start = memory.ModuleBase, end = memory.ModuleBase + memory.ModuleSize;
        for (long addr = start; addr < end; addr += chunkSize)
        {
            int len = (int)Math.Min(buffer.Length, end - addr);
            if (len < pattern.Length) break;
            var span = buffer.AsSpan(0, len);
            if (!memory.TryRead(addr, span))
            {
                // Невыделенная страница внутри модуля — читаем постранично, пропуская дыры.
                if (ScanPaged(memory, pattern, addr, len) is { } paged) return paged;
                continue;
            }
            int idx = Find(span, pattern);
            if (idx >= 0) return addr + idx;
        }
        return null;
    }

    private static long? ScanPaged(IProcessMemory memory, AobPattern pattern, long addr, int len)
    {
        const int page = 4096;
        var buf = new byte[page + pattern.Length];
        for (long p = addr; p < addr + len; p += page)
        {
            int l = (int)Math.Min(buf.Length, addr + len - p);
            if (l < pattern.Length || !memory.TryRead(p, buf.AsSpan(0, l))) continue;
            int idx = Find(buf.AsSpan(0, l), pattern);
            if (idx >= 0) return p + idx;
        }
        return null;
    }

    /// <summary>Разрешает RIP-относительную адресацию: инструкция по адресу address, int32-смещение на ripOffset.</summary>
    public static long? ResolveRipRelative(IProcessMemory memory, long instructionAddress, int ripOffset, int instructionLength)
    {
        var rel = memory.ReadInt32(instructionAddress + ripOffset);
        return rel is null ? null : instructionAddress + instructionLength + rel.Value;
    }
}

public static class PointerChain
{
    /// <summary>
    /// Разыменовывает цепочку: addr = [staticPtr]; для всех смещений, кроме последнего, addr = [addr + off];
    /// результат — addr + последнее смещение. Пустая цепочка — сам staticPtr.
    /// </summary>
    public static long? Resolve(IProcessMemory memory, long staticPointer, IReadOnlyList<long> offsets)
    {
        if (offsets.Count == 0) return staticPointer;
        var addr = memory.ReadInt64(staticPointer);
        if (addr is null or 0) return null;
        for (int i = 0; i < offsets.Count - 1; i++)
        {
            addr = memory.ReadInt64(addr.Value + offsets[i]);
            if (addr is null or 0) return null;
        }
        return addr.Value + offsets[^1];
    }
}
