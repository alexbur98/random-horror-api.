using DS1Companion.Core.Data;
using DS1Companion.Core.Models;

namespace DS1Companion.Core.Logic;

public sealed record WeaponAdvice(
    string DisplayName,
    int Level,
    string ScalingLetters,
    string ScalingHint,
    string NextStep,
    bool IsMaxed);

/// <summary>Формирует понятные подсказки «что нужно для следующего уровня заточки».</summary>
public sealed class UpgradeAdvisor
{
    private readonly KnowledgeBase _kb;

    public UpgradeAdvisor(KnowledgeBase kb) => _kb = kb;

    public WeaponAdvice? Advise(EquippedWeapon? weapon)
    {
        if (weapon is null || !_kb.Game.HasWeaponUpgrades) return null;
        var item = (weapon.ItemId is { } id ? _kb.FindItem(id) : null) ?? _kb.FindWeaponItem(weapon.RawId);
        var level = weapon.UpgradeLevel;
        var infusion = WeaponIdCodec.InfusionNameRu(weapon.Infusion);
        var baseName = item?.NameRu ?? $"Оружие #{weapon.BaseId}";
        var name = (string.IsNullOrEmpty(infusion) ? baseName : $"{infusion} {LowerFirst(baseName)}")
                   + (level > 0 ? $" +{level}" : "");

        var info = item is null ? null : _kb.FindWeaponUpgrade(item.Id);
        if (info is null)
            return new WeaponAdvice(name, level, "", "Нет данных о скейлинге — добавьте оружие в weapons_upgrade.json.", "", false);

        var letters = $"СИЛ {info.ScalingStr} · ЛВК {info.ScalingDex} · ИНТ {info.ScalingInt} · ВЕР {info.ScalingFth}";
        var path = _kb.FindPath(info.PathId);
        if (weapon.Infusion != 0)
            return new WeaponAdvice(name, level, letters, info.ScalingHintRu,
                "Ветка заточки изменена — уточните путь у соответствующего кузнеца.", false);
        if (path is null)
            return new WeaponAdvice(name, level, letters, info.ScalingHintRu, "", false);

        var step = path.Steps.FirstOrDefault(s => s.ToLevel == level + 1);
        if (step is null)
            return new WeaponAdvice(name, level, letters, info.ScalingHintRu,
                $"Максимальная заточка по пути «{path.NameRu}» достигнута.", true);

        return new WeaponAdvice(name, level, letters, info.ScalingHintRu, DescribeStep(step, path), false);
    }

    public string DescribeStep(UpgradeStep step, UpgradePath path)
    {
        var material = _kb.FindItem(step.MaterialItemId)?.NameRu ?? step.MaterialItemId;
        var qty = step.Quantity > 1 ? $"{step.Quantity} × {material}" : material;
        var ember = step.EmberItemId is null ? "" : $" и {_kb.FindItem(step.EmberItemId)?.NameRu ?? step.EmberItemId}";
        var souls = step.Souls > 0 ? $" ({DisplayText.FormatSouls(step.Souls)} душ)" : "";
        return $"Для +{step.ToLevel} нужен {qty}{ember} — {path.SmithRu}{souls}.";
    }

    private static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
