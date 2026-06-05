using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Surveillance.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Surveillance.Agent;

public sealed class DbStatusToolHandler(AgentTool tool, CanonicalDbContext canonicalDb) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var statusTool = new ToolBuilder("db_status")
            .WithDescription("Lists all guilds and channels in the database with per-channel statistics: message count, claim count, distinct entity count, and the date range of claims. Use this to see what data is available before searching.")
            .Build();

        var handler = ActivatorUtilities.CreateInstance<DbStatusToolHandler>(
            serviceProvider,
            statusTool
        );

        registry.RegisterTool(statusTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();

        // Per-channel stats:
        await using var cmd = canonicalDb.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT b.GuildName, b.ChannelName,
                   COUNT(DISTINCT m.Id) AS message_count,
                   COUNT(c.id) AS claim_count,
                   COUNT(DISTINCT c.subject_entity_id) AS entity_count,
                   MIN(c.timestamp) AS first_ts,
                   MAX(c.timestamp) AS last_ts
            FROM Batches b
            JOIN Messages m ON m.BatchId = b.Id
            LEFT JOIN canonical_claims c ON c.source_message_id = m.Id
            GROUP BY b.Id
            ORDER BY b.GuildName, b.ChannelName;
            """;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        var totalMessages = 0;
        var totalClaims = 0;
        string? currentGuild = null;
        var hasRows = false;

        while (await reader.ReadAsync(cancellationToken))
        {
            hasRows = true;
            var guildName = reader.GetString(0);
            var channelName = reader.GetString(1);
            var msgCount = reader.GetInt32(2);
            var claimCount = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            var entityCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
            var firstTs = reader.IsDBNull(5) ? (long?)null : reader.GetInt64(5);
            var lastTs = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6);

            totalMessages += msgCount;
            totalClaims += claimCount;

            if (guildName != currentGuild)
            {
                if (currentGuild != null) sb.AppendLine();
                sb.AppendLine($"@ {guildName}");
                currentGuild = guildName;
            }

            var dateRange = firstTs != null
                ? $"  {DateTimeOffset.FromUnixTimeMilliseconds(firstTs.Value):yyyy-MM-dd} — {DateTimeOffset.FromUnixTimeMilliseconds(lastTs!.Value):yyyy-MM-dd}"
                : "";

            sb.AppendLine($"  {channelName}  messages:{msgCount}  claims:{claimCount}  entities:{entityCount}{dateRange}");
        }

        if (!hasRows)
        {
            return Success("Database is empty — no guilds, channels, or messages ingested yet.");
        }

        // Totals:
        sb.AppendLine();
        sb.AppendLine("── Totals ──");

        await using var totalCmd = canonicalDb.Connection.CreateCommand();
        totalCmd.CommandText = "SELECT COUNT(*) FROM canonical_entities;";
        var totalEntities = (long)(await totalCmd.ExecuteScalarAsync(cancellationToken))!;

        sb.AppendLine($"  messages: {totalMessages}  claims: {totalClaims}  canonical entities: {totalEntities}");

        return Success(sb.ToString());
    }
}
