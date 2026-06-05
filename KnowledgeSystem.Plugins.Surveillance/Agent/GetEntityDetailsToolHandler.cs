using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Surveillance.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Surveillance.Agent;

public sealed class GetEntityDetailsToolHandler(
    AgentTool tool,
    StringArgument entityArgument,
    CanonicalDbContext canonicalDb
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var detailsTool = new ToolBuilder("get_entity_details")
            .WithDescription("Returns statistics and context about a canonical entity: claim counts by modality, top related entities, and the date range of its claims. Use this to understand an entity's footprint before querying claims.")
            .WithRequiredStringArgument("entity", "Entity name or numeric ID.", out var entityArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<GetEntityDetailsToolHandler>(
            serviceProvider,
            detailsTool,
            entityArg
        );

        registry.RegisterTool(detailsTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var entity = entityArgument.GetValue(args);

        // Try as numeric ID first:
        long? entityId = null;
        string? entityName = null;
        string? entityType = null;
        string? entityDesc = null;

        if (long.TryParse(entity, out var parsedId))
        {
            using var idCmd = canonicalDb.Connection.CreateCommand();
            idCmd.CommandText = "SELECT primary_name, type, description FROM canonical_entities WHERE id = @id";
            idCmd.Parameters.AddWithValue("@id", parsedId);

            await using var reader = await idCmd.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken))
            {
                entityId = parsedId;
                entityName = reader.GetString(0);
                entityType = reader.IsDBNull(1) ? null : reader.GetString(1);
                entityDesc = reader.IsDBNull(2) ? null : reader.GetString(2);
            }
        }

        if (entityId == null)
        {
            var resolved = canonicalDb.GetCanonicalEntityByAlias(entity);

            if (resolved == null)
            {
                return Error($"Entity '{entity}' not found.");
            }

            entityId = resolved.Value.Id;
            entityName = resolved.Value.PrimaryName;
            entityType = resolved.Value.Type;
            entityDesc = resolved.Value.Description;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"#{entityId} \"{entityName}\" ({entityType ?? "unknown"})");

        if (entityDesc != null)
        {
            sb.AppendLine($"  Description: {entityDesc}");
        }

        // Claim counts by modality (as subject):
        using (var cmd = canonicalDb.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT modality, COUNT(*) FROM canonical_claims
                WHERE subject_entity_id = @id
                GROUP BY modality ORDER BY COUNT(*) DESC;
                """;

            cmd.Parameters.AddWithValue("@id", entityId.Value);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            sb.AppendLine("  Claims as subject:");

            var total = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                var mod = reader.GetString(0);
                var count = reader.GetInt32(1);
                sb.AppendLine($"    {mod,-14} {count,4}");
                total += count;
            }

            sb.AppendLine($"    {"─ TOTAL",-14} {total,4}");
        }

        // Claim counts by modality (as object):
        using (var cmd = canonicalDb.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT modality, COUNT(*) FROM canonical_claims
                WHERE object_entity_id = @id
                GROUP BY modality ORDER BY COUNT(*) DESC;
                """;

            cmd.Parameters.AddWithValue("@id", entityId.Value);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            sb.AppendLine("  Claims as object:");

            var total = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                var mod = reader.GetString(0);
                var count = reader.GetInt32(1);
                sb.AppendLine($"    {mod,-14} {count,4}");
                total += count;
            }

            if (total == 0)
            {
                sb.AppendLine("    (none)");
            }
            else
            {
                sb.AppendLine($"    {"─ TOTAL",-14} {total,4}");
            }
        }

        // Top related entities (co-occurring as object):
        using (var cmd = canonicalDb.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT o.primary_name, o.type, COUNT(*) AS n
                FROM canonical_claims c
                JOIN canonical_entities o ON c.object_entity_id = o.id
                WHERE c.subject_entity_id = @id
                GROUP BY o.id
                ORDER BY n DESC
                LIMIT 10;
                """;

            cmd.Parameters.AddWithValue("@id", entityId.Value);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (reader.HasRows)
            {
                sb.AppendLine("  Top related entities (as object):");

                while (await reader.ReadAsync(cancellationToken))
                {
                    var name = reader.GetString(0);
                    var type = reader.IsDBNull(1) ? "?" : reader.GetString(1);
                    var count = reader.GetInt32(2);
                    sb.AppendLine($"    {name} ({type}) ×{count}");
                }
            }
        }

        // Date range:
        using (var cmd = canonicalDb.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT MIN(timestamp), MAX(timestamp) FROM canonical_claims
                WHERE subject_entity_id = @id OR object_entity_id = @id;
                """;

            cmd.Parameters.AddWithValue("@id", entityId.Value);

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            if (await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0))
            {
                var min = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0));
                var max = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
                sb.AppendLine($"  Date range: {min:yyyy-MM-dd} — {max:yyyy-MM-dd}");
            }
        }

        return Success(sb.ToString());
    }
}
