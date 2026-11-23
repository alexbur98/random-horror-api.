using System.Globalization;
using System.Text.Json;
using DS1Companion.Core.Data;

namespace DS1Companion.Core.Memory;

public sealed class BaseDefinition
{
    public string Aob { get; set; } = "";
    public int RipOffset { get; set; } = 3;
    public int InstructionLength { get; set; } = 7;
    public bool Verified { get; set; }
    public string? Note { get; set; }
}

public enum FieldType { Int32, Byte, Int64, Utf16 }

public sealed class FieldDefinition
{
    public string Base { get; set; } = "";
    /// <summary>Смещения строками в hex ("0x10") или десятичном виде.</summary>
    public List<string> Offsets { get; set; } = new();
    public FieldType Type { get; set; } = FieldType.Int32;
    public int Length { get; set; } = 16;
    public bool Verified { get; set; }

    public IReadOnlyList<long> ParsedOffsets() => Offsets.Select(OffsetsConfig.ParseOffset).ToList();
}

public sealed class InventoryDefinition
{
    public string Base { get; set; } = "";
    /// <summary>Цепочка до начала массива записей.</summary>
    public List<string> ListOffsets { get; set; } = new();
    /// <summary>Цепочка до int32 с количеством записей (если нет — читаем capacity и пропускаем пустые).</summary>
    public List<string>? CountOffsets { get; set; }
    public int Capacity { get; set; } = 2048;
    public int EntrySize { get; set; } = 0x1C;
    public int CategoryOffset { get; set; }
    public int IdOffset { get; set; } = 4;
    public int QuantityOffset { get; set; } = 8;
    public bool Verified { get; set; }
}

public sealed class OffsetsConfig
{
    public string ProcessName { get; set; } = "DarkSoulsRemastered";
    public string GameVersion { get; set; } = "";
    public int PollIntervalMs { get; set; } = 250;
    public Dictionary<string, BaseDefinition> Bases { get; set; } = new();
    public Dictionary<string, FieldDefinition> Fields { get; set; } = new();
    public InventoryDefinition? Inventory { get; set; }

    public static OffsetsConfig Load(string path)
    {
        using var s = File.OpenRead(path);
        return JsonSerializer.Deserialize<OffsetsConfig>(s, KnowledgeBase.JsonOptions)
               ?? throw new InvalidDataException("offsets.json пуст");
    }

    public static long ParseOffset(string value)
    {
        var v = value.Trim();
        bool negative = v.StartsWith('-');
        if (negative) v = v[1..];
        long result = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(v, CultureInfo.InvariantCulture);
        return negative ? -result : result;
    }

    /// <summary>Проверка целостности: все поля ссылаются на существующие базы, AOB парсятся, смещения валидны.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var (name, b) in Bases)
        {
            try { AobPattern.Parse(b.Aob); }
            catch (FormatException ex) { errors.Add($"base '{name}': {ex.Message}"); }
        }
        foreach (var (name, f) in Fields)
        {
            if (!Bases.ContainsKey(f.Base)) errors.Add($"field '{name}': неизвестная база '{f.Base}'");
            try { f.ParsedOffsets(); }
            catch (FormatException) { errors.Add($"field '{name}': неверное смещение"); }
        }
        if (Inventory is { } inv && !Bases.ContainsKey(inv.Base))
            errors.Add($"inventory: неизвестная база '{inv.Base}'");
        return errors;
    }
}
