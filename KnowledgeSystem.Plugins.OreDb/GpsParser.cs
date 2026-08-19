using System.Globalization;

namespace KnowledgeSystem.Plugins.OreDb;

public static class GpsParser
{
    public static bool TryParse(string text, out double x, out double y, out double z)
    {
        x = 0;
        y = 0;
        z = 0;

        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var parts = text.Split(':');

        int offset;
        if (parts.Length == 3)
        {
            offset = 0;
        }
        else if (parts.Length >= 5 && parts[0].Equals("GPS", StringComparison.OrdinalIgnoreCase))
        {
            offset = 2;
        }
        else
        {
            return false;
        }

        return double.TryParse(parts[offset], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[offset + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            && double.TryParse(parts[offset + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }
}
