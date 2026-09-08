using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.OreDb.AgentTools;

public sealed class LargestOreToolHandler(
    AgentTool tool,
    StringArgument oreArgument,
    StringArgument gpsArgument,
    StringArgument instanceArgument,
    NumberArgument maxDistanceArgument,
    IntegerArgument countArgument,
    BooleanArgument markMinedArgument,
    OreDbStores stores,
    ILogger<LargestOreToolHandler> logger
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var largestTool = new ToolBuilder("oredb_largest_ore")
            .WithDescription("Finds the unmined asteroid with the largest total volume of one ore type, optionally within max_distance_km of a GPS position. Omit gps or pass 0:0:0 to search the whole instance (no distance reported). Set count to more than 1 to list the N biggest by total instead; that list is read-only. mark_mined=true claims the single result (marks the asteroid mined, returns a GPS); otherwise stats only, no GPS.")
            .WithRequiredStringArgument("ore", "Ore type exactly as stored in the database, e.g. uraninite_01. Use the exact name from your ore list.", out var oreArg)
            .WithStringArgument("gps", "Player position as x:y:z or a full SE GPS string. Omit or pass 0:0:0 for an instance-wide search.", out var gpsArg)
            .WithRequiredStringArgument("instance", "Instance name exactly as stored, e.g. greeks. Use the exact name from your instance list.", out var instanceArg)
            .WithNumberArgument("max_distance_km", "Max distance from gps in km. Default: unlimited. Requires gps.", out var maxDistanceArg)
            .WithIntegerArgument("count", "Number of biggest asteroids by total volume to return, 1 to 25. Default 1 (the single richest, claimable with mark_mined). More than 1 returns a read-only ordered list and mark_mined must be false.", out var countArg)
            .WithBooleanArgument("mark_mined", "True: mark the found asteroid mined and return a GPS. False (default): stats only, nothing changes.", out var markMinedArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<LargestOreToolHandler>(
            serviceProvider,
            largestTool,
            oreArg,
            gpsArg,
            instanceArg,
            maxDistanceArg,
            countArg,
            markMinedArg
        );

        registry.RegisterTool(largestTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<BasicContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var ore = oreArgument.GetValue(args).ToLowerInvariant();
        var instance = instanceArgument.GetValue(args).ToLowerInvariant();
        var gps = gpsArgument.GetValueOrNull(args);

        var hasMaxDistance = maxDistanceArgument.TryGetValue(args, out var maxDistanceKm);
        var hasCount = countArgument.TryGetValue(args, out var requestedCount);
        var countValue = Math.Clamp(hasCount ? requestedCount : 1, 1, 25);
        var markMined = markMinedArgument.TryGetValue(args, out var marked) && marked;

        if (markMined && countValue > 1)
        {
            return Error("mark_mined can only claim a single asteroid. Call without count (or with count 1) to claim.");
        }

        var hasOrigin = false;
        var originX = 0.0;
        var originY = 0.0;
        var originZ = 0.0;

        if (gps != null)
        {
            if (!GpsParser.TryParse(gps, out originX, out originY, out originZ))
            {
                return Error("The gps argument is invalid. Provide plain coordinates x:y:z or a full Space Engineers GPS string like GPS:me:12.3:45.6:78.9:#FF0000:.");
            }

            hasOrigin = originX != 0.0 || originY != 0.0 || originZ != 0.0;
        }

        if (hasMaxDistance && !hasOrigin)
        {
            return Error("The max_distance_km argument requires a gps origin argument. Omit both to search the whole instance.");
        }

        try
        {
            return countValue == 1
                ? await ExecuteSingleAsync(instance, ore, hasOrigin, originX, originY, originZ, hasMaxDistance, maxDistanceKm, markMined, cancellationToken)
                : await ExecuteListAsync(instance, ore, hasOrigin, originX, originY, originZ, hasMaxDistance, maxDistanceKm, countValue, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "oredb_largest_ore failed for instance {instance} and ore {ore}", instance, ore);
            return Error("The ore database could not be queried right now.");
        }
    }

    private async Task<ToolExecutionResult> ExecuteSingleAsync(string instance, string ore, bool hasOrigin, double originX, double originY, double originZ, bool hasMaxDistance, double maxDistanceKm, bool markMined, CancellationToken cancellationToken)
    {
        var result = await stores.LargestOreAsync(
            instance,
            ore,
            hasOrigin ? originX : null,
            hasOrigin ? originY : null,
            hasOrigin ? originZ : null,
            hasMaxDistance ? maxDistanceKm : null,
            markMined,
            cancellationToken
        );

        if (result == null)
        {
            return Error(hasOrigin && hasMaxDistance
                ? $"No unmined asteroid with `{ore}` was found within {OreFormatting.FormatCoordinate(maxDistanceKm)} km of the given position in \"{instance}\"."
                : $"No unmined asteroid with `{ore}` was found in \"{instance}\".");
        }

        var sb = new StringBuilder();
        sb.Append($"Richest: \"{result.AsteroidName}\" - {OreFormatting.FormatVolume(result.Volume)} m3 of `{ore}`");

        if (result.Distance != null)
        {
            sb.Append($", {OreFormatting.FormatDistance(result.Distance.Value)} away");
        }

        sb.Append('.');
        sb.AppendLine();
        sb.Append($"Contents: {OreFormatting.FormatOreBreakdown(result.Ores)}");

        if (markMined)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Marked as mined:");
            sb.AppendLine("```");
            sb.AppendLine(OreFormatting.CreateGps(instance, result.Ores, result.X, result.Y, result.Z));
            sb.Append("```");
        }

        return Success(sb.ToString());
    }

    private async Task<ToolExecutionResult> ExecuteListAsync(string instance, string ore, bool hasOrigin, double originX, double originY, double originZ, bool hasMaxDistance, double maxDistanceKm, int count, CancellationToken cancellationToken)
    {
        var results = await stores.LargestOresAsync(
            instance,
            ore,
            hasOrigin ? originX : null,
            hasOrigin ? originY : null,
            hasOrigin ? originZ : null,
            hasMaxDistance ? maxDistanceKm : null,
            count,
            cancellationToken
        );

        if (results == null)
        {
            return Error($"Instance \"{instance}\" was not found in the ore database.");
        }

        if (results.Count == 0)
        {
            return Error(hasOrigin && hasMaxDistance
                ? $"No unmined asteroid with `{ore}` was found within {OreFormatting.FormatCoordinate(maxDistanceKm)} km of the given position in \"{instance}\"."
                : $"No unmined asteroid with `{ore}` was found in \"{instance}\".");
        }

        var sb = new StringBuilder();

        for (var index = 0; index < results.Count; index++)
        {
            var row = results[index];
            sb.Append($"{index + 1}. \"{row.AsteroidName}\" - {OreFormatting.FormatVolume(row.Volume)} m3 of `{ore}`");

            if (row.Distance != null)
            {
                sb.Append($", {OreFormatting.FormatDistance(row.Distance.Value)} away");
            }

            sb.Append('.');
            sb.AppendLine($" Contents: {OreFormatting.FormatOreBreakdown(row.Ores)}");
        }

        sb.Append("Read-only list; no asteroid was marked as mined and no GPS was returned.");

        return Success(sb.ToString());
    }
}
