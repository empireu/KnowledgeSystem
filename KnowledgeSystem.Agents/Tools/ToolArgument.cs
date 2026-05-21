using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace KnowledgeSystem.Agents.Tools;

public abstract class ToolArgument(string argumentName, string argumentDescription)
{
    /// <summary>
    ///     The name of the argument, that will be written in the payload. Must be JSON-Compatible.
    /// </summary>
    public string ArgumentName { get; } = argumentName;

    /// <summary>
    ///     The argument description string.
    /// </summary>
    public string ArgumentDescription { get; } = argumentDescription;

    /// <summary>
    ///     The JSON Schema type name for this argument.
    /// </summary>
    public abstract string JsonTypeName { get; }

    /// <summary>
    ///     Adds type-specific schema properties (e.g. "enum" for EnumArgument, "items" for ArrayArgument).
    /// </summary>
    public virtual void AddSchemaProperties(Dictionary<string, object> propertySchema) { }

    protected bool TryGetRawValue(ArgumentExtractionResult result, [NotNullWhen(true)] out object? value)
    {
        value = null;
        if (!result.Arguments.TryGetValue(this, out var raw) || raw == null)
        {
            return false;
        }
        
        value = raw;
        return true;
    }

    protected static string? ConvertToString(object? raw) => raw switch
    {
        null => null,
        string s => s,
        JsonElement jsonElement => jsonElement.ValueKind == JsonValueKind.String
            ? jsonElement.GetString()
            : jsonElement.GetRawText(),
        _ => raw.ToString()
    };
}

public sealed class StringArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "string";

    public bool TryGetValue(ArgumentExtractionResult result, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return TryGetRawValue(result, out var raw) && (value = ConvertToString(raw)) != null;
    }

    public string GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided.");
        }
        
        return value;
    }
    
    public string? GetValueOrNull(ArgumentExtractionResult result) => TryGetValue(result, out var value) ? value : null;
}

public sealed class IntegerArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "integer";

    public bool TryGetValue(ArgumentExtractionResult result, out int value)
    {
        value = 0;
        if (!TryGetRawValue(result, out var raw))
        {
            return false;
        }

        switch (raw)
        {
            case int intValue:
                value = intValue;
                return true;
            case long longValue when longValue is >= int.MinValue and <= int.MaxValue:
                value = (int)longValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } jsonElement when jsonElement.TryGetInt32(out value):
                return true;
        }

        return int.TryParse(ConvertToString(raw), out value);
    }

    public int GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided or could not be parsed as integer.");
        }
        
        return value;
    }
}

public sealed class NumberArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "number";

    public bool TryGetValue(ArgumentExtractionResult result, out double value)
    {
        value = 0;
        if (!TryGetRawValue(result, out var raw))
        {
            return false;
        }

        switch (raw)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case decimal decimalValue:
                value = (double)decimalValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.Number } jsonElement when jsonElement.TryGetDouble(out value):
                return true;
        }

        return double.TryParse(ConvertToString(raw), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public double GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided or could not be parsed as number.");
        }
        
        return value;
    }
}

public sealed class BooleanArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "boolean";

    public bool TryGetValue(ArgumentExtractionResult result, out bool value)
    {
        value = false;
        if (!TryGetRawValue(result, out var raw))
        {
            return false;
        }

        switch (raw)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } jsonElement:
                value = jsonElement.GetBoolean();
                return true;
        }

        return bool.TryParse(ConvertToString(raw), out value);
    }

    public bool GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided or could not be parsed as boolean.");
        }
        
        return value;
    }
}

public sealed class EnumArgument(string argumentName, string argumentDescription, string[] allowedValues) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "string";
    public string[] AllowedValues { get; } = allowedValues;

    public override void AddSchemaProperties(Dictionary<string, object> propertySchema)
    {
        propertySchema["enum"] = AllowedValues;
    }

    public bool TryGetValue(ArgumentExtractionResult result, [NotNullWhen(true)] out string? value)
    {
        var rawValue = TryGetRawValue(result, out var raw) ? ConvertToString(raw) : null;
        if (rawValue != null && AllowedValues.Contains(rawValue))
        {
            value = rawValue;
            return true;
        }
        
        value = null;
        return false;
    }

    public string GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided or value is not one of the allowed values: [{string.Join(", ", AllowedValues)}].");
        }
        
        return value;
    }
}

public sealed class ArrayArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "array";

    public override void AddSchemaProperties(Dictionary<string, object> propertySchema)
    {
        propertySchema["items"] = new Dictionary<string, string> { ["type"] = "string" };
    }

    public bool TryGetValue(ArgumentExtractionResult result, [NotNullWhen(true)] out string[]? value)
    {
        value = null;
        if (!TryGetRawValue(result, out var raw))
        {
            return false;
        }

        switch (raw)
        {
            case string[] values:
                value = values;
                return true;
            case JsonElement jsonElement when jsonElement.ValueKind == JsonValueKind.Array:
                value = jsonElement.EnumerateArray().Select(e => ConvertToString(e)!).ToArray();
                return true;
            case IEnumerable<object?> enumerable:
            {
                value = enumerable.Select(ConvertToString).Where(x => x != null).Cast<string>().ToArray();
                return true;
            }
        }

        var rawText = ConvertToString(raw);
        if (rawText == null)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawText);
            
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            value = document.RootElement
                .EnumerateArray()
                .Select(e => ConvertToString(e)!)
                .ToArray();
            
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public string[] GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided or could not be parsed as string array.");
        }
        
        return value!;
    }
}
