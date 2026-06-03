using Microsoft.Extensions.AI;

namespace KnowledgeSystem.Retrieval.Graph;

public class BasicOneShotFeatureExtractionPipelineDescription
{
    /// <summary>
    ///     The client used for the extraction.
    /// </summary>
    public required IChatClient ChatClient { get; init; }

    /// <summary>
    ///     The system prompt guiding entity and relationship extraction.
    ///     Describes what to extract and conveys the expected JSON shape inline.
    /// </summary>
    public string SystemPrompt { get; init; } = ExtractSystemPrompt;

    /// <summary>
    ///     Default system prompt for entity extraction.
    /// </summary>
    public const string ExtractSystemPrompt =
        """
        You are an information extraction system. Extract all named entities and the explicit relationships between them from the provided text.

        Respond with a JSON object in exactly this structure:
        {
          "entities": [
            {
              "name": "primary entity name",
              "names": ["alias1", "alias2"],
              "type": "free-form type, e.g. person, organization, concept",
              "description": "short description specific to this text",
              "evidence": "exact quoted sentence(s) from the text"
            }
          ],
          "relationships": [
            {
              "source": "name of the source entity",
              "target": "name of the target entity",
              "description": "the action or connection between them",
              "relationship_type": "application" or "relation",
              "evidence": "exact quoted sentence(s) from the text"
            }
          ]
        }

        Extraction rules:
        - Provide the primary name for each entity. If the entity is referred to by multiple names or aliases in the text, include them in the "names" array.
        - The "type" field is free-form (e.g. "military commander", "corporation", "philosophical concept").
        - The "evidence" field must be the exact sentence(s) from the text, character-for-character.
        - For relationships, classify as "application" (directed action: X does something to Y) or "relation" (mutual connection: X is connected to Y).
        - Only extract what is explicitly stated or can be directly inferred from the provided text.
        - Do not fabricate entities or relationships.
        """; // (make no mistakes)
    
    /// <summary>
    ///     Factory for the chat completion options.
    ///     <bold>The <see cref="ChatOptions.ResponseFormat"/> must be left null; it will be set for structured output.</bold>
    /// </summary>
    public Func<ChatOptions>? StructuredCompletionFactory { get; init; }

    /// <summary>
    ///     Fuzzy-matching threshold for parsing.
    /// </summary>
    public double MatchThreshold { get; init; } = 0.7;
}