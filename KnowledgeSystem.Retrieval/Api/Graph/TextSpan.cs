namespace KnowledgeSystem.Retrieval.Api.Graph;

public readonly struct TextSpan(int start, int length)
{
    public int Start { get; } = start;
    public int Length { get; } = length;
    public int EndExclusive => Start + Length;

    public bool Contains(int offset) => offset >= Start && offset < EndExclusive;
}