using System.Text.Json;
using Microsoft.ML.Tokenizers;
using OpenAI.Chat;
using HfTokenizer = Tokenizers.HuggingFace.Tokenizer.Tokenizer;

namespace KnowledgeSystem.Agents.Context.TokenEstimation;

public enum TokenizerKind
{
    Tiktoken,
    HuggingFace,
}

public enum ChatTemplateFormat
{
    ChatMl,
    Gemma,
    Glm,
}

public sealed class TokenizerInfo
{
    public required string ModelName { get; init; }
    
    public required TokenizerKind Kind { get; init; }
    
    public required ChatTemplateFormat ChatFormat { get; init; }
    
    public string? TokenizerDir { get; init; }
    
    public static TokenizerInfo Gemma(string tokenizerDir) => new()
    {
        ModelName = "gemma",
        Kind = TokenizerKind.HuggingFace,
        ChatFormat = ChatTemplateFormat.Gemma,
        TokenizerDir = tokenizerDir
    };

    public static TokenizerInfo Glm(string tokenizerDir) => new()
    {
        ModelName = "glm",
        Kind = TokenizerKind.HuggingFace,
        ChatFormat = ChatTemplateFormat.Glm,
        TokenizerDir = tokenizerDir
    };
}

public sealed class TokenizerHelper : ITokenEstimator
{
    private readonly ChatTemplateFormatter _formatter;
    private readonly ITokenizerBackend _backend;

    private TokenizerHelper(ChatTemplateFormatter formatter, ITokenizerBackend backend)
    {
        _formatter = formatter;
        _backend = backend;
    }

    public int CountTokens(IEnumerable<ChatMessage> messages)
    {
        var formatted = _formatter.Format(messages);
        return _backend.CountTokens(formatted);
    }
    
    public static TokenizerHelper Create(TokenizerInfo info)
    {
        ChatTemplateFormatter formatter;
        ITokenizerBackend backend;

        switch (info.Kind)
        {
            case TokenizerKind.Tiktoken:
                formatter = ChatTemplateFormatter.ChatMl();
                backend = new TiktokenBackend(info.ModelName);
                break;
            case TokenizerKind.HuggingFace:
            {
                var dir = info.TokenizerDir ?? throw new ArgumentException("TokenizerDir is required", nameof(info));
                var tokens = SpecialTokens.FromDirectory(dir);
                formatter = ChatTemplateFormatter.Create(info.ChatFormat, tokens);
                backend = new HuggingFaceBackend(Path.Combine(dir, "tokenizer.json"));
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(info), info.Kind, null);
        }

        return new TokenizerHelper(formatter, backend);
    }

    public void Dispose() => _backend.Dispose();
}

internal interface ITokenizerBackend : IDisposable
{
    int CountTokens(string text);
}

internal sealed class TiktokenBackend(string modelName) : ITokenizerBackend
{
    private readonly Tokenizer _tokenizer = TiktokenTokenizer.CreateForModel(modelName);

    public int CountTokens(string text) => _tokenizer.CountTokens(text);

    public void Dispose() { }
}

internal sealed class HuggingFaceBackend(string tokenizerJsonPath) : ITokenizerBackend
{
    private readonly HfTokenizer _tokenizer = HfTokenizer.FromFile(tokenizerJsonPath);

    public int CountTokens(string text) => _tokenizer.Encode(text, true).First().Ids.Count;

    public void Dispose() => _tokenizer.Dispose();
}

internal sealed class SpecialTokens
{
    public static readonly SpecialTokens Empty = new()
    {
        BosToken = "",
        EosToken = "",
        StartTurnToken = "",
        EndTurnToken = ""
    };
    
    public required string BosToken { get; init; }
    public required string EosToken { get; init; }
    public required string StartTurnToken { get; init; }
    public required string EndTurnToken { get; init; }

    public static SpecialTokens FromDirectory(string directory)
    {
        var configPath = Path.Combine(directory, "tokenizer_config.json");
       
        if (!File.Exists(configPath))
        {
            return Empty;
        }

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var bos = root.TryGetProperty("bos_token", out var b) ? b.GetString() ?? "" : "";
        var eos = root.TryGetProperty("eos_token", out var e) ? e.GetString() ?? "" : "";

        // Gemma-4 uses sot_token/eot_token for turn markers:
        var startTurn = root.TryGetProperty("sot_token", out var sot) ? sot.GetString() ?? "" : "";
        var endTurn = root.TryGetProperty("eot_token", out var eot) ? eot.GetString() ?? "" : "";

        // Fallback for older Gemma models:
        if (string.IsNullOrEmpty(startTurn))
        {
            startTurn = "<start_of_turn>";
        }
        if (string.IsNullOrEmpty(endTurn))
        {
            endTurn = "<end_of_turn>";
        }

        return new SpecialTokens
        {
            BosToken = bos,
            EosToken = eos,
            StartTurnToken = startTurn,
            EndTurnToken = endTurn
        };
    }
}