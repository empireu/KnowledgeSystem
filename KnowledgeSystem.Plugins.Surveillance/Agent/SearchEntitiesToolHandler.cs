using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Surveillance.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Surveillance.Agent;

public sealed class SearchEntitiesToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    StringArgument typeArgument,
    CanonicalDbContext canonicalDb,
    IEmbeddingService embeddingService
) : ToolHandler<BasicContext>.Plain(tool)
{
    private const int ResultCount = 8;
    private const int OverFetch = 3;
    
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var searchTool = new ToolBuilder("search_entities")
            .WithDescription("Finds canonical entities by name, description, or type. Tries exact alias match first, then full-text search, then semantic vector search as a fallback.")
            .WithRequiredStringArgument("query", "Entity name or description to search for.", out var queryArg)
            .WithStringArgument("type", "Filter by entity type (e.g. person, organization, ship).", out var typeArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<SearchEntitiesToolHandler>(
            serviceProvider,
            searchTool,
            queryArg,
            typeArg
        );

        registry.RegisterTool(searchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValue(args);
        var type = typeArgument.GetValueOrNull(args);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Error("Query must not be empty.");
        }

        var visitedEntities = new HashSet<long>();
        
        var sb = new StringBuilder();
        
        // Exact match by name:
        AppendNameMatch(query, type, sb, visitedEntities);

        // Full-text search:
        AppendFtsResults(query, type, sb, visitedEntities);
        
        // Vector search:
        await VectorSearch(query, type, sb, visitedEntities, cancellationToken);

        return visitedEntities.Count == 0 
            ? Success("No results found.")
            : Success(sb.ToString());
    }

    private void AppendNameMatch(string query, string? type, StringBuilder sb, HashSet<long> visited)
    {
        var exactByName = canonicalDb.GetCanonicalEntityByAlias(query);

        if (exactByName == null)
        {
            return;
        }
        
        if (type == null || string.Equals(exactByName.Value.Type, type, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("Perfect match: ");
        }
        else
        {
            sb.Append("Partial, name-only exact match: ");
        }

        visited.Add(exactByName.Value.Id);
        
        sb.AppendLine(FormatEntity(
            exactByName.Value.Id,
            exactByName.Value.PrimaryName,
            exactByName.Value.Type
        ));
    }

    private void AppendFtsResults(string query, string? type, StringBuilder sb, HashSet<long> visited)
    {
        var ftsResults = DbFts5Search(query, type)
            .Where(x => !visited.Contains(x.Id))
            .ToList();

        if (ftsResults.Count == 0)
        {
            return;
        }

        sb.AppendLine($"Full-text search results:");
        
        foreach (var (id, name, entityType) in ftsResults)
        {
            sb.AppendLine(FormatEntity(id, name, entityType));
            visited.Add(id);
        }
    }

    private async Task VectorSearch(string query, string? type, StringBuilder sb, HashSet<long> visited, CancellationToken cancellationToken)
    {
        var queryEmbedding = await embeddingService.EmbedAsync(query, cancellationToken);
        var hits = canonicalDb
            .SearchEntityEmbeddings(queryEmbedding.Span, ResultCount * OverFetch)
            .Where(h => !visited.Contains(h.EntityId))
            .ToList();

        if (hits.Count == 0)
        {
            return;
        }

        sb.AppendLine("Semantic search results:");

        var added = 0;
        foreach (var (entityId, _) in hits)
        {
            await using var cmd = canonicalDb.Connection.CreateCommand();
            cmd.CommandText = "SELECT primary_name, type FROM canonical_entities WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", entityId);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (!await reader.ReadAsync(cancellationToken))
            {
                continue;
            }
            
            var name = reader.GetString(0);
            var entityType = reader.IsDBNull(1) ? null : reader.GetString(1);

            if (type != null && !string.Equals(entityType, type, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine(FormatEntity(
                entityId, 
                name, 
                entityType
            ));

            visited.Add(entityId);
            added++;
            if (added >= ResultCount)
            {
                break;
            }
        }
    }
    
    private List<(long Id, string Name, string? Type)> DbFts5Search(string query, string? type)
    {
        var results = new List<(long, string, string?)>();

        using var cmd = canonicalDb.Connection.CreateCommand();

        var typeFilter = type != null ? "AND e.type = @type" : "";
        cmd.CommandText = $"""
            SELECT e.id, e.primary_name, e.type
            FROM canonical_entities_fts f
            JOIN canonical_entities e ON e.id = f.rowid
            WHERE canonical_entities_fts MATCH @query
            {typeFilter}
            ORDER BY rank
            LIMIT @limit;
            """;

        cmd.Parameters.AddWithValue("@query", SanitizeFts5Query(query));
        cmd.Parameters.AddWithValue("@limit", ResultCount);

        if (type != null)
        {
            cmd.Parameters.AddWithValue("@type", type);
        }

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            results.Add((
                reader.GetInt64(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)
            ));
        }

        return results;
    }

    private static string FormatEntity(long id, string name, string? type)
    {
        var typeStr = type ?? "unknown";
        return $"#{id} \"{name}\" ({typeStr})";
    }

    private static string SanitizeFts5Query(string query)
    {
        return "\"" + query.Replace("\"", "\"\"") + "\"";
    }
}
