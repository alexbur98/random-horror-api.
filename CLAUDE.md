# CLAUDE.md

Компаньон для Dark Souls: Remastered: C# / .NET 8, WPF, MVVM (CommunityToolkit.Mvvm). Тексты UI и данных — на русском.

## Команды
- `dotnet build` — всё решение (WPF собирается и на Linux благодаря `EnableWindowsTargeting` в `Directory.Build.props`;
  нужен Microsoft SDK, а не source-built из дистрибутива).
- `dotnet test` — xUnit, только `DS1Companion.Core` (кроссплатформенно).
- `dotnet run --project src/DS1Companion.UI` — запуск (только Windows).

## Архитектура
- `src/DS1Companion.Core` (net8.0, без ссылок на WPF): модели, база знаний, трекер, чтение памяти **и ViewModel'и**
  (`Presentation/`). ViewModel'и лежат в Core, чтобы тестировать их и проверять XAML-биндинги рефлексией.
- `src/DS1Companion.UI` (net8.0-windows): только представление и платформенные сервисы — XAML, `EmberField`, `ItemIcon`,
  `WpfSoundService`, `GamePollingService`, `WindowPlacement` (Win32). Composition root — `App.xaml.cs`.
- Поток данных: `IGameStateReader.Read()` (фон) → `MainViewModel.OnSnapshot` (UI) → `GameSessionTracker.Process` → события
  → свойства VM. Время — только через `MainViewModel.Tick(delta)` и `IClock`; никаких `DateTime.Now`/таймеров в логике.
- ViewModel вызываются только из UI-потока. Снимки от устаревшего ридера (после переключения демо) игнорируются.

## Правила
- Чтение памяти строго read-only: `OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION)` + `ReadProcessMemory`.
  Не добавлять `WriteProcessMemory`, `VirtualProtectEx` и т. п. — тест `MemoryAccess_IsStrictlyReadOnly` это проверяет.
- Новый биндинг в XAML: у корня view должен быть `d:DataContext="{d:DesignInstance Type=vm:...}"`, у шаблонов —
  `DataType="{x:Type vm:...}"` или `ItemsSource` с типизированной коллекцией, иначе `XamlBindingTests` не проверит путь.
- Все `StaticResource` объявляются в `Themes/Theme.xaml`, `Themes/Glyphs.xaml` или `App.xaml`.
- JSON: snake_case, перечисления строками (`KnowledgeBase.JsonOptions`).

## Data/offsets.json
```jsonc
{
  "process_name": "DarkSoulsRemastered",       // имя процесса без .exe
  "poll_interval_ms": 250,
  "bases": {                                    // AOB → RIP-relative → адрес статического указателя
    "game_data_man": { "aob": "48 8B 05 ?? ?? ?? ?? ...", "rip_offset": 3, "instruction_length": 7, "verified": false }
  },
  "fields": {                                   // addr = [base]; addr = [addr+off] для всех, кроме последнего; итог = addr+last
    "souls": { "base": "game_data_man", "offsets": ["0x10", "0x94"], "type": "Int32" }   // Int32 | Byte | Int64 | Utf16 (+length)
  },
  "inventory": { "base": "...", "list_offsets": [...], "count_offsets": [...], "entry_size": 28,
                 "category_offset": 0, "id_offset": 4, "quantity_offset": 8 }   // категория >> 28: 0 оружие, 1 броня, 2 кольцо, 4 goods
}
```
Известные поля: `character_name`, `deaths`, `souls`, `level`, `vitality`…`faith`, `resistance`, `weapon_id`,
`weapon_durability`, `weapon_max_durability`, `area`, `block`, `bloodstain_souls`, `boss_fight_active`.
Значения не проверены на живой игре; неправдоподобные отбрасываются в `DsrMemoryReader.Sane`.

## Иконки
Положить PNG в `src/DS1Companion.UI/Assets/Icons/{icon_key}.png` или `{id}.png` (копируются в output при сборке;
в git не коммитятся — см. `.gitignore`). Помощник: `python Scripts/fetch_icons.py --from-dir <папка> [--map map.csv]`.
Без файла используется векторный глиф `Glyph.{IconGlyph}` из `Themes/Glyphs.xaml`.
