using System.Globalization;

namespace Admin.Helpers;

public static class MetricHelper
{
    public static bool TryGetDouble(Dictionary<string, object> metrics, string key, out double value)
    {
        value = 0;
        if (!metrics.TryGetValue(key, out var obj) || obj is null) return false;

        switch (obj)
        {
            case double d:  value = d;       return true;
            case float f:   value = f;       return true;
            case int i:     value = i;       return true;
            case long l:    value = l;       return true;
            case decimal m: value = (double)m; return true;
            case string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed):
                value = parsed; return true;
            default:
                if (obj is System.Text.Json.JsonElement je)
                {
                    if (je.ValueKind == System.Text.Json.JsonValueKind.Number && je.TryGetDouble(out var jd))
                    { value = jd; return true; }

                    if (je.ValueKind == System.Text.Json.JsonValueKind.String &&
                        double.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var js))
                    { value = js; return true; }
                }
                return false;
        }
    }
}
