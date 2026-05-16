using System.ClientModel.Primitives;
using System.Text.Json;
using OpenAI.Chat;

namespace KnowledgeSystem.Agents.Helper;

#pragma warning disable OPENAI001
public sealed class ExtendedChatCompletionOptions : ChatCompletionOptions
{
    public string? ProviderOnly { get; set; }
        
    protected override void JsonModelWriteCore(Utf8JsonWriter writer, ModelReaderWriterOptions options)
    {
        base.JsonModelWriteCore(writer, options);
            
        if (!string.IsNullOrEmpty(ProviderOnly))
        {
            writer.WritePropertyName("provider"u8);
            writer.WriteStartObject();
            writer.WritePropertyName("only"u8);
            writer.WriteStartArray();
            writer.WriteStringValue(ProviderOnly!);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
#pragma warning restore OPENAI001
