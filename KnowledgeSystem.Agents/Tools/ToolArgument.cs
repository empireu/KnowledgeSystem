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

    protected bool TryGetRawValue(ArgumentExtractionResult result, [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (!result.Arguments.TryGetValue(this, out var raw) || raw == null)
        {
            return false;
        }
        
        value = raw;
        return true;
    }
}

public sealed class StringArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "string";

    public bool TryGetValue(ArgumentExtractionResult result, [NotNullWhen(true)] out string? value) => TryGetRawValue(result, out value);

    public string GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
        {
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided.");
        }
        
        return value!;
    }
}

public sealed class IntegerArgument(string argumentName, string argumentDescription) : ToolArgument(argumentName, argumentDescription)
{
    public override string JsonTypeName => "integer";

    public bool TryGetValue(ArgumentExtractionResult result, out int value)
    {
        value = 0;
        return TryGetRawValue(result, out var raw) && int.TryParse(raw, out value);
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
        return TryGetRawValue(result, out var raw) && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
        return TryGetRawValue(result, out var raw) && bool.TryParse(raw, out value);
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
        if (TryGetRawValue(result, out var raw) && AllowedValues.Contains(raw))
        {
            value = raw;
            return true;
        }
        
        value = null;
        return false;
    }

    public string GetValue(ArgumentExtractionResult result)
    {
        if (!TryGetValue(result, out var value))
            throw new InvalidOperationException($"Argument \"{ArgumentName}\" was not provided or value is not one of the allowed values: [{string.Join(", ", AllowedValues)}].");
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
        if (!TryGetRawValue(result, out var raw)) return false;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                value = doc.RootElement.EnumerateArray().Select(e => e.GetString()!).ToArray();
                return true;
            }
        }
        catch(Exception e)
        {
            // Breakpoint:
            // ReSharper disable once EmptyStatement
            ;
        }
        return false;
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