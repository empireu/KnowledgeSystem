using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Embedding;
using KnowledgeSystem.Plugins.Library;
using KnowledgeSystem.Plugins.Surveillance.Database;
using Microsoft.Extensions.DependencyInjection;

namespace KnowledgeSystem.Plugins.Surveillance.Agent;

public sealed class SearchClaimsToolHandler(
    AgentTool tool,
    StringArgument queryArgument,
    StringArgument subjectArgument,
    StringArgument objectArgument,
    StringArgument modalityArgument,
    StringArgument dateFromArgument,
    StringArgument dateToArgument,
    EnumArgument sortByArgument,
    IntegerArgument limitArgument,
    BooleanArgument semanticArgument,
    StringArgument channelArgument,
    StringArgument guildArgument,
    CanonicalDbContext canonicalDb,
    IEmbeddingService embeddingService
) : ToolHandler<BasicContext>.Plain(tool)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;
    
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var searchTool = new ToolBuilder("search_claims")
            .WithDescription("Searches claims with structured filters. All filters are optional and combine with AND. Use semantic=true for natural-language queries (e.g. 'torpedo balance complaints').")
            .WithStringArgument("query", "FTS5 text search on predicate and object_literal. When semantic=true, this becomes a natural-language vector search query.", out var queryArg)
            .WithStringArgument("subject", "Entity name or ID — resolves to canonical entity. Filter claims where this entity is the subject.", out var subjectArg)
            .WithStringArgument("object", "Entity name or ID — resolves to canonical entity. Filter claims where this entity is the object.", out var objectArg)
            .WithStringArgument("modality", "Exact modality filter (fact, opinion, speculation, joke, emotion, desire, intention, etc.).", out var modalityArg)
            .WithStringArgument("date_from", "ISO date lower bound (e.g. 2025-01-15).", out var dateFromArg)
            .WithStringArgument("date_to", "ISO date upper bound.", out var dateToArg)
            .WithEnumArgument("sort_by", "Sort order.", ["date_asc", "date_desc"], out var sortByArg)
            .WithIntegerArgument("limit", $"Max results. Default {DefaultLimit}, hard cap {MaxLimit}.", out var limitArg)
            .WithBooleanArgument("semantic", "When true, uses semantic vector search on claim sentences instead of FTS5.", out var semanticArg)
            .WithStringArgument("channel", "Filter by Discord channel name (exact match).", out var channelArg)
            .WithStringArgument("guild", "Filter by Discord guild/server name (exact match).", out var guildArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<SearchClaimsToolHandler>(
            serviceProvider,
            searchTool,
            queryArg,
            subjectArg,
            objectArg,
            modalityArg,
            dateFromArg,
            dateToArg,
            sortByArg,
            limitArg,
            semanticArg,
            channelArg,
            guildArg
        );

        registry.RegisterTool(searchTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(AgentRunner<BasicContext> runner, ArgumentExtractionResult args, CancellationToken cancellationToken)
    {
        var query = queryArgument.GetValueOrNull(args);
        var subject = subjectArgument.GetValueOrNull(args);
        var obj = objectArgument.GetValueOrNull(args);
        var modality = modalityArgument.GetValueOrNull(args);
        var dateFrom = dateFromArgument.GetValueOrNull(args);
        var dateTo = dateToArgument.GetValueOrNull(args);
        var channel = channelArgument.GetValueOrNull(args);
        var guild = guildArgument.GetValueOrNull(args);
        var semantic = semanticArgument.TryGetValue(args, out var sem) && sem;
        var sortAsc = !sortByArgument.TryGetValue(args, out var sortVal) || sortVal == "date_asc";

        // Treat empty/whitespace as if it wasn't provided:
        
        if (string.IsNullOrWhiteSpace(query))
        {
            query = null;
            semantic = false;
        }
        if (string.IsNullOrWhiteSpace(channel))
        {
            channel = null;
        }
        else if (channel.StartsWith('#'))
        {
            channel = channel[1..];
        }
        
        if (string.IsNullOrWhiteSpace(guild))
        {
            guild = null;
        }
        
        var limit = limitArgument.TryGetValue(args, out var lim) 
            ? Math.Clamp(lim, 1, MaxLimit)
            : DefaultLimit;

        long? subjectId = null;
        long? objectId = null;

        if (subject != null)
        {
            var resolved = canonicalDb.GetCanonicalEntityByAlias(subject);
            if (resolved == null)
            {
                return Error($"Subject entity '{subject}' not found.");
            }

            subjectId = resolved.Value.Id;
        }

        if (obj != null)
        {
            var resolved = canonicalDb.GetCanonicalEntityByAlias(obj);
            if (resolved == null)
            {
                return Error($"Object entity '{obj}' not found.");
            }

            objectId = resolved.Value.Id;
        }

        var results = semantic && query != null
            ? await SemanticSearchAsync(query, subjectId, objectId, modality, dateFrom, dateTo, channel, guild, sortAsc, limit, cancellationToken)
            : Fts5Search(query, subjectId, objectId, modality, dateFrom, dateTo, channel, guild, sortAsc, limit);

        if (results.Count == 0)
        {
            return Success("No claims found.");
        }

        var sb = new StringBuilder();

        foreach (var (id, subjName, predicate, objLabel, claimModality, timestamp, evidence, channelName, guildName) in results)
        {
            var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).ToString("yyyy-MM-dd");
            sb.AppendLine($"#{id} [{subjName}] {predicate} {objLabel} ({claimModality}) — {date} — #{channelName} @{guildName}");
            sb.AppendLine($"  \"{Truncate(evidence, 120)}\"");
        }

        return Success(sb.ToString());
    }

    private async Task<List<ClaimResult>> SemanticSearchAsync(
        string query,
        long? subjectId,
        long? objectId,
        string? modality,
        string? dateFrom,
        string? dateTo,
        string? channel,
        string? guild,
        bool sortAsc,
        int limit,
        CancellationToken cancellationToken)
    {
        var queryEmbedding = await embeddingService.EmbedAsync(query, cancellationToken);
        var hits = canonicalDb.SearchClaimSentenceEmbeddings(queryEmbedding.Span, limit);

        if (hits.Count == 0)
        {
            return [];
        }

        var hitIds = hits
            .Select(h => h.ClaimId)
            .ToList();
        
        return BatchGetClaims(
            hitIds,
            subjectId,
            objectId,
            modality,
            dateFrom,
            dateTo,
            channel,
            guild,
            sortAsc,
            limit
        );
    }

    private List<ClaimResult> Fts5Search(
        string? query,
        long? subjectId,
        long? objectId,
        string? modality,
        string? dateFrom,
        string? dateTo,
        string? channel,
        string? guild,
        bool sortAsc,
        int limit)
    {
        var baseWhere = new List<string>();
        var baseParams = new List<(string Name, object Value)>();

        if (query != null)
        {
            baseWhere.Add("c.id IN (SELECT rowid FROM canonical_claims_fts WHERE canonical_claims_fts MATCH @query)");
            baseParams.Add(("@query", SanitizeFts5Query(query)));
        }

        return ExecuteFilteredClaimQuery(baseWhere, baseParams, subjectId, objectId, modality, dateFrom, dateTo, channel, guild, sortAsc, limit);
    }

    private List<ClaimResult> BatchGetClaims(
        List<long> claimIds,
        long? subjectId,
        long? objectId,
        string? modality,
        string? dateFrom,
        string? dateTo,
        string? channel,
        string? guild,
        bool sortAsc,
        int limit)
    {
        var baseWhere = new List<string> { "c.id IN (" + string.Join(", ", claimIds) + ")" };
        var baseParams = new List<(string Name, object Value)>();
        return ExecuteFilteredClaimQuery(baseWhere, baseParams, subjectId, objectId, modality, dateFrom, dateTo, channel, guild, sortAsc, limit);
    }

    private List<ClaimResult> ExecuteFilteredClaimQuery(
        List<string> baseWhere,
        List<(string Name, object Value)> baseParams,
        long? subjectId,
        long? objectId,
        string? modality,
        string? dateFrom,
        string? dateTo,
        string? channel,
        string? guild,
        bool sortAsc,
        int limit)
    {
        var results = new List<ClaimResult>();
        var where = new List<string>(baseWhere);
        var parameters = new List<(string Name, object Value)>(baseParams);

        if (subjectId != null)
        {
            where.Add("c.subject_entity_id = @subj");
            parameters.Add(("@subj", subjectId.Value));
        }

        if (objectId != null)
        {
            where.Add("c.object_entity_id = @obj");
            parameters.Add(("@obj", objectId.Value));
        }

        if (modality != null)
        {
            where.Add("c.modality = @mod");
            parameters.Add(("@mod", modality));
        }

        if (dateFrom != null && DateTime.TryParse(dateFrom, out var df))
        {
            var ms = new DateTimeOffset(df, TimeSpan.Zero).ToUnixTimeMilliseconds();
            where.Add("c.timestamp >= @from");
            parameters.Add(("@from", ms));
        }

        if (dateTo != null && DateTime.TryParse(dateTo, out var dt))
        {
            var ms = new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeMilliseconds();
            where.Add("c.timestamp <= @to");
            parameters.Add(("@to", ms));
        }

        if (channel != null)
        {
            where.Add("b.ChannelName = @channel");
            parameters.Add(("@channel", channel));
        }

        if (guild != null)
        {
            where.Add("b.GuildName = @guild");
            parameters.Add(("@guild", guild));
        }

        var whereClause = "WHERE " + string.Join(" AND ", where);
        var order = sortAsc ? "ASC" : "DESC";

        using var cmd = canonicalDb.Connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT c.id, s.primary_name, c.predicate,
                   COALESCE(o.primary_name, c.object_literal, '?') AS obj_label,
                   c.modality, c.timestamp, c.evidence_text,
                   b.ChannelName, b.GuildName
            FROM canonical_claims c
            JOIN canonical_entities s ON c.subject_entity_id = s.id
            LEFT JOIN canonical_entities o ON c.object_entity_id = o.id
            JOIN Messages m ON c.source_message_id = m.Id
            JOIN Batches b ON m.BatchId = b.Id
            {whereClause}
            ORDER BY c.timestamp {order}
            LIMIT @limit;
            """;

        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new ClaimResult(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)
            ));
        }

        return results;
    }

    private static string SanitizeFts5Query(string query)
    {
        // Wrap in double quotes to prevent FTS5 operator parsing.
        // Double any existing quotes per SQLite escaping rules.
        return "\"" + query.Replace("\"", "\"\"") + "\"";
    }

    private static string Truncate(string text, int maxLen)
    {
        return text.Length <= maxLen ? text : text[..(maxLen - 3)] + "...";
    }

    private record struct ClaimResult(
        long Id,
        string SubjectName,
        string Predicate,
        string ObjectLabel,
        string Modality,
        long Timestamp,
        string Evidence,
        string ChannelName,
        string GuildName
    );
}
