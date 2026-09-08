using System.Globalization;

namespace KnowledgeSystem.Plugins.OreDb;

internal static class OreFormatting
{
    private static readonly IReadOnlyDictionary<string, string> OreSymbols = new Dictionary<string, string>
    {
        ["silicon"] = "Si",
        ["nickel"] = "Ni",
        ["cobalt"] = "Co",
        ["lead"] = "Pb",
        ["copper"] = "Cu",
        ["iron"] = "Fe",
        ["tungsten"] = "W",
        ["magnesium"] = "Mg",
        ["gold"] = "Au",
        ["silver"] = "Ag",
        ["uraninite"] = "U",
        ["titanium"] = "Ti",
        ["platinum"] = "Pt"
    };

    public static string CreateGps(string instanceName, IReadOnlyList<OreVolume> ores, double x, double y, double z)
    {
        var oreNames = string.Join(" ", ores.Select(o => $"{FormatOreName(o.OreType)} {o.Volume / 1000.0:0}K"));
        var name = $"{instanceName} {oreNames}";
        return $"GPS:{name}:{FormatCoordinate(x)}:{FormatCoordinate(y)}:{FormatCoordinate(z)}:#00FF00:";
    }

    public static string FormatOreName(string oreType)
    {
        var separator = oreType.IndexOf('_');
        var baseName = separator < 0 ? oreType : oreType[..separator];
        var suffix = separator < 0 ? string.Empty : oreType[(separator + 1)..];
        var symbol = OreSymbols.TryGetValue(baseName.ToLowerInvariant(), out var element) ? element : baseName;
        return suffix.Length == 0 ? symbol : symbol + suffix.TrimStart('0');
    }

    public static string FormatVolume(double value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string FormatCoordinate(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    public static string FormatDistance(double meters)
    {
        return meters >= 1000.0
            ? $"{meters / 1000.0:0.##} km"
            : $"{meters:0} m";
    }

    public static string FormatCompactVolume(double value)
    {
        if (value >= 1000000.0)
        {
            return $"{value / 1000000.0:0.#}M";
        }

        if (value >= 1000.0)
        {
            return $"{value / 1000.0:0.#}K";
        }

        return $"{value:0}";
    }

    public static string FormatOreBreakdown(IReadOnlyList<OreVolume> ores)
    {
        return string.Join(", ", ores.Select(o => $"{FormatOreName(o.OreType)} {FormatCompactVolume(o.Volume)}"));
    }
}
