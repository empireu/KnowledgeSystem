namespace KnowledgeSystem.Retrieval.Lexical;

public static class Tokenizer
{
    private static readonly HashSet<string> StopList = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "i",

        "am", "an", "as", "at", "be", "by", "do", "he", "hi", "if", "in", "is", 
        "me", "my", "no", "of", "ok", "on", "or", "so", "to", "up", "us", "we",

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
    
    /// <summary>
    ///     Tokenizes the query into specific words, excluding stop words.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="includeFullQuery">If true, includes the exact literal string as a token with a frequency of 1. The dictionary will have <see cref="StringComparer.OrdinalIgnoreCase"/>.</param>
    public static Dictionary<string, int> TokenizeWithFrequency(string query, bool includeFullQuery)
    {
        var tokens = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    
        if (string.IsNullOrWhiteSpace(query))
        {
            return tokens;
        }

        query = query.Trim();

        if (includeFullQuery)
        {
            // Add the exact string:
            tokens.Add(query, 1);
        }

        var wordStart = -1;
        var stopList = StopList;

        for (var i = 0; i <= query.Length; i++)
        {
            var isLetterOrDigit = i < query.Length && char.IsLetterOrDigit(query[i]);

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (isLetterOrDigit && wordStart < 0)
            {
                // Start of a new word:
                wordStart = i;
            }
            else if (!isLetterOrDigit && wordStart >= 0)
            {
                // End of the current word:
                var length = i - wordStart;

                if (length >= 2)
                {
                    var word = query[wordStart..i];

                    var isIdenticalToFullQuery = includeFullQuery && word.Equals(query, StringComparison.OrdinalIgnoreCase);

                    if (!isIdenticalToFullQuery && !stopList.Contains(word))
                    {
                        if (!tokens.TryAdd(word, 1))
                        {
                            tokens[word]++;
                        }
                    }
                }

                wordStart = -1;
            }
        }

        return tokens;
    }

    /// <summary>
    ///     Helper that gets a set of unique tokens. 
    /// </summary>
    public static string[] TokenizeQuery(string query, bool includeFullQuery)
    {
        var frequencies = TokenizeWithFrequency(query, includeFullQuery);
        
        return frequencies.Keys.ToArray();
    }
}