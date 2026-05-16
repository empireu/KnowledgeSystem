namespace KnowledgeSystem.Agent.FastContext;

public sealed partial class FastContextToolHandler
{
    private static readonly HashSet<string> DefaultBlacklistedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "are", "but", "not", "you", "all", "can", "had",
        "her", "was", "one", "our", "out", "day", "get", "has", "him", "his",
        "how", "its", "may", "new", "now", "old", "see", "two", "who", "boy",
        "did", "she", "use", "way", "many", "sit", "set", "run", "ago", "off",
        "too", "any", "say", "try", "ask", "end", "why", "let", "put", "own",
        "tell", "very", "when", "much", "would", "there", "their", "what", "said",
        "each", "which", "will", "about", "could", "other", "after", "first",
        "never", "these", "think", "where", "being", "every", "great", "might",
        "shall", "still", "those", "while", "this", "that", "with", "have", "from",
        "they", "know", "want", "been", "good", "some", "time", "than", "them", "well", "were"
    };
}