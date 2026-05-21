using System.Text.Json;

// ReSharper disable UnusedMember.Global
// ReSharper disable MemberCanBePrivate.Global

namespace KnowledgeSystem.Agents.Tools;

public sealed class ToolBuilder(string toolId)
{
    private string _description = string.Empty;
    private readonly List<ToolArgument> _arguments = [];
    private readonly List<ToolArgument> _requiredArguments = [];
    
    #region API
    
    public ToolBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ToolBuilder WithArgument(ToolArgument argument)
    {
        if (_arguments.Any(x => x.ArgumentName == argument.ArgumentName))
        {
            throw new InvalidOperationException($"Duplicate tool argument \"{argument.ArgumentName}\"");
        }
        
        _arguments.Add(argument);
        
        return this;
    }

    public ToolBuilder WithStringArgument(string name, string description, out StringArgument argument)
    {
        argument = new StringArgument(name, description);
        return WithArgument(argument);
    }

    public ToolBuilder WithIntegerArgument(string name, string description, out IntegerArgument argument)
    {
        argument = new IntegerArgument(name, description);
        return WithArgument(argument);
    }

    public ToolBuilder WithNumberArgument(string name, string description, out NumberArgument argument)
    {
        argument = new NumberArgument(name, description);
        return WithArgument(argument);
    }

    public ToolBuilder WithBooleanArgument(string name, string description, out BooleanArgument argument)
    {
        argument = new BooleanArgument(name, description);
        return WithArgument(argument);
    }

    public ToolBuilder WithEnumArgument(string name, string description, string[] allowedValues, out EnumArgument argument)
    {
        argument = new EnumArgument(name, description, allowedValues);
        return WithArgument(argument);
    }

    public ToolBuilder WithArrayArgument(string name, string description, out ArrayArgument argument)
    {
        argument = new ArrayArgument(name, description);
        return WithArgument(argument);
    }

    public ToolBuilder WithRequiredArgument(ToolArgument argument)
    {
        WithArgument(argument);
        _requiredArguments.Add(argument);
        return this;
    }

    public ToolBuilder WithRequiredStringArgument(string name, string description, out StringArgument argument)
    {
        argument = new StringArgument(name, description); 
        return WithRequiredArgument(argument);
    }

    public ToolBuilder WithRequiredIntegerArgument(string name, string description, out IntegerArgument argument)
    {
        argument = new IntegerArgument(name, description);
        return WithRequiredArgument(argument);
    }

    public ToolBuilder WithRequiredNumberArgument(string name, string description, out NumberArgument argument)
    {
        argument = new NumberArgument(name, description);
        return WithRequiredArgument(argument);
    }

    public ToolBuilder WithRequiredBooleanArgument(string name, string description, out BooleanArgument argument)
    {
        argument = new BooleanArgument(name, description);
        return WithRequiredArgument(argument);
    }

    public ToolBuilder WithRequiredEnumArgument(string name, string description, string[] allowedValues, out EnumArgument argument)
    {
        argument = new EnumArgument(name, description, allowedValues); 
        return WithRequiredArgument(argument);
    }

    public ToolBuilder WithRequiredArrayArgument(string name, string description, out ArrayArgument argument)
    {
        argument = new ArrayArgument(name, description);
        return WithRequiredArgument(argument);
    }
    
    #endregion

    /// <summary>
    ///     Builds the final tool definition.
    /// </summary>
    /// <param name="strict"></param>
    /// <returns></returns>
    public AgentTool Build(bool? strict = null)
    {
        JsonElement parametersSchema;

        if (_arguments.Count > 0)
        {
            var properties = new Dictionary<string, Dictionary<string, object>>();
            foreach (var arg in _arguments)
            {
                var propSchema = new Dictionary<string, object>
                {
                    ["type"] = arg.JsonTypeName,
                    ["description"] = arg.ArgumentDescription
                };
                arg.AddSchemaProperties(propSchema);
                properties[arg.ArgumentName] = propSchema;
            }

            var schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties,
            };

            var requiredNames = strict == true
                ? _arguments.Select(a => a.ArgumentName).ToArray()
                : _requiredArguments.Select(a => a.ArgumentName).ToArray();

            if (requiredNames.Length > 0)
            {
                schema["required"] = requiredNames;
            }

            if (strict == true)
            {
                schema["additionalProperties"] = false;
            }

            var json = JsonSerializer.Serialize(schema);
            parametersSchema = JsonDocument.Parse(json).RootElement.Clone();
        }
        else
        {
            parametersSchema = JsonDocument.Parse("{}").RootElement.Clone();
        }

        return new AgentTool
        {
            ToolId = toolId,
            Description = _description,
            Arguments = _arguments.ToArray(),
            RequiredArguments = _requiredArguments.ToArray(),
            ParametersSchema = parametersSchema
        };
    }
}
