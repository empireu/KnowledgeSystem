namespace KnowledgeSystem.Retrieval.Api.Graph;

public enum RawRelationshipType
{
    /// <summary>
    ///     The LHS entities performed an action on the entities on the RHS (directed).
    /// </summary>
    Application,
    /// <summary>
    ///     The LHS entities are related with the entities on the RHS (bidirectional).
    /// </summary>
    Relation
}