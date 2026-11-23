using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DS1Companion.Core.Presentation;

namespace DS1Companion.Tests;

/// <summary>
/// Статическая проверка XAML без запуска WPF (работает и в Linux CI):
/// каждый {Binding Path} должен существовать во ViewModel текущего DataContext,
/// каждый {StaticResource Key} — быть объявлен в одном из словарей.
/// </summary>
public class XamlBindingTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Design = "http://schemas.microsoft.com/expression/blend/2008";
    private static readonly Assembly CoreAssembly = typeof(MainViewModel).Assembly;

    private static string UiDir => Path.Combine(TestData.SolutionDir, "src", "DS1Companion.UI");

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(UiDir, "*.xaml", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                          && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void AllBindings_ResolveAgainstViewModels()
    {
        var validator = new BindingValidator();
        foreach (var file in XamlFiles()) validator.Validate(XDocument.Load(file), Path.GetFileName(file));
        Assert.True(validator.Errors.Count == 0, string.Join(Environment.NewLine, validator.Errors));
        Assert.True(validator.Checked > 100, $"Проверено всего {validator.Checked} биндингов — валидатор, похоже, их не видит");
    }

    [Fact]
    public void Validator_CatchesTypos()
    {
        const string xaml = """
            <UserControl xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
                         xmlns:vm="clr-namespace:DS1Companion.Core.Presentation;assembly=DS1Companion.Core"
                         d:DataContext="{d:DesignInstance Type=vm:MainViewModel}">
              <StackPanel>
                <TextBlock Text="{Binding LocationNameRuu}" />
                <Border DataContext="{Binding Weapon}"><TextBlock Text="{Binding Titel}" /></Border>
                <ItemsControl ItemsSource="{Binding RecentLoot}">
                  <ItemsControl.ItemTemplate><DataTemplate><TextBlock Text="{Binding Nme}" /></DataTemplate></ItemsControl.ItemTemplate>
                </ItemsControl>
                <TextBlock Text="{Binding Weapon.Title}" />
              </StackPanel>
            </UserControl>
            """;
        var v = new BindingValidator();
        v.Validate(XDocument.Parse(xaml), "test.xaml");
        Assert.Equal(3, v.Errors.Count);
        Assert.Contains(v.Errors, e => e.Contains("LocationNameRuu"));
        Assert.Contains(v.Errors, e => e.Contains("Titel") && e.Contains("WeaponPanelViewModel"));
        Assert.Contains(v.Errors, e => e.Contains("Nme") && e.Contains("LootRowViewModel"));
    }

    [Fact]
    public void AllStaticResources_AreDefined()
    {
        var docs = XamlFiles().Select(f => (File: Path.GetFileName(f), Text: File.ReadAllText(f))).ToList();
        var keys = docs.SelectMany(d => Regex.Matches(d.Text, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value)).ToHashSet();
        var missing = docs.SelectMany(d => Regex.Matches(d.Text, @"\{(?:Static|Dynamic)Resource\s+([^\s}]+)\}")
                              .Select(m => (d.File, Key: m.Groups[1].Value)))
                          .Where(r => !keys.Contains(r.Key))
                          .Select(r => $"{r.File}: нет ресурса '{r.Key}'")
                          .Distinct().ToList();
        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EventHandlersInXaml_ExistInCodeBehind()
    {
        foreach (var file in XamlFiles())
        {
            var codeBehind = file + ".cs";
            if (!File.Exists(codeBehind)) continue;
            var code = File.ReadAllText(codeBehind);
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "(?:Click|MouseEnter|MouseLeave|MouseLeftButtonDown)=\"([A-Za-z_]+)\""))
                Assert.Contains($"void {m.Groups[1].Value}(", code);
        }
    }

    private sealed class BindingValidator
    {
        public List<string> Errors { get; } = new();
        public int Checked { get; private set; }
        private string _file = "";

        public void Validate(XDocument doc, string file)
        {
            _file = file;
            Walk(doc.Root!, null);
        }

        private void Walk(XElement el, Type? ctx)
        {
            if (el.Attribute(Design + "DataContext") is { } design && DesignType(design.Value) is { } dt) ctx = dt;
            if (el.Name.LocalName == "DataTemplate" && el.Attribute("DataType") is { } dataType && XType(dataType.Value) is { } tt) ctx = tt;
            if (el.Attribute("DataContext") is { } dc && ParseBinding(dc.Value) is { } dcPath)
                ctx = ctx is null ? null : Resolve(ctx, dcPath, el, "DataContext");

            Type? itemType = null, contentType = null;
            foreach (var attr in el.Attributes())
            {
                if (attr.Name.LocalName == "DataContext" || attr.Name.Namespace == Design) continue;
                if (ParseBinding(attr.Value) is not { } path || ctx is null) continue;
                var resolved = Resolve(ctx, path, el, attr.Name.LocalName);
                if (attr.Name.LocalName == "ItemsSource") itemType = ElementType(resolved);
                if (attr.Name.LocalName == "Content") contentType = resolved;
            }

            foreach (var child in el.Elements())
            {
                var name = child.Name.LocalName;
                if (!name.Contains('.')) { Walk(child, ctx); continue; }
                var prop = name[(name.IndexOf('.') + 1)..];
                var childCtx = prop switch
                {
                    "ItemTemplate" => itemType,
                    "ContentTemplate" => contentType,
                    "Resources" or "ItemsPanel" => null,
                    _ => ctx,
                };
                foreach (var grand in child.Elements()) Walk(grand, childCtx);
            }
        }

        private Type? Resolve(Type ctx, string path, XElement el, string attribute)
        {
            Checked++;
            if (path is "" or ".") return ctx;
            var type = ctx;
            foreach (var segment in path.Split('.'))
            {
                if (segment.Contains('[')) return null; // индексаторы не проверяем
                var prop = AllProperties(type).FirstOrDefault(p => p.Name == segment);
                if (prop is null)
                {
                    Errors.Add($"{_file}: <{el.Name.LocalName} {attribute}=\"{{Binding {path}}}\"> — у {type.Name} нет свойства '{segment}'");
                    return null;
                }
                type = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
            }
            return type;
        }

        private static IEnumerable<PropertyInfo> AllProperties(Type t) =>
            t.IsInterface
                ? t.GetProperties().Concat(t.GetInterfaces().SelectMany(i => i.GetProperties()))
                : t.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        private static Type? ElementType(Type? t)
        {
            if (t is null) return null;
            var enumerable = (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>) ? t : null)
                             ?? t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0];
        }

        /// <summary>Путь простого биндинга; null — не биндинг или биндинг к чужому источнику (ElementName/RelativeSource/Source).</summary>
        private static string? ParseBinding(string value)
        {
            var v = value.Trim();
            if (!v.StartsWith("{Binding", StringComparison.Ordinal) || !v.EndsWith('}')) return null;
            var inner = v["{Binding".Length..^1].Trim();
            var parts = SplitTopLevel(inner);
            if (parts.Any(p => p.StartsWith("ElementName=") || p.StartsWith("RelativeSource=") || p.StartsWith("Source="))) return null;
            var path = parts.FirstOrDefault(p => !p.Contains('=')) ?? parts.FirstOrDefault(p => p.StartsWith("Path="))?["Path=".Length..];
            return path?.Trim() ?? "";
        }

        private static List<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') depth--;
                else if (s[i] == ',' && depth == 0) { parts.Add(s[start..i].Trim()); start = i + 1; }
            }
            if (start < s.Length) parts.Add(s[start..].Trim());
            return parts.Where(p => p.Length > 0).ToList();
        }

        private static Type? DesignType(string value)
        {
            var m = Regex.Match(value, @"Type=(?:\w+:)?(\w+)");
            return m.Success ? FindType(m.Groups[1].Value) : null;
        }

        private static Type? XType(string value)
        {
            var m = Regex.Match(value, @"\{x:Type\s+(?:\w+:)?(\w+)\}");
            return m.Success ? FindType(m.Groups[1].Value) : null;
        }

        private static Type? FindType(string name) => CoreAssembly.GetTypes().FirstOrDefault(t => t.Name == name);
    }
}
