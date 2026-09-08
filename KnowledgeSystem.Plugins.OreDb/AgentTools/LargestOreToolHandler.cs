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
    BooleanArgument markMinedArgument,
    OreDbStores stores,
    ILogger<LargestOreToolHandler> logger
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var largestTool = new ToolBuilder("oredb_largest_ore")
            .WithDescription("Finds the unmined asteroid with the largest total volume of one ore type, optionally within max_distance_km of a GPS position. Omit gps or pass 0:0:0 to search the whole instance (no distance reported). mark_mined=true claims it (marks the asteroid mined, returns a GPS); otherwise stats only, no GPS.")
            .WithRequiredStringArgument("ore", "Ore type exactly as stored in the database, e.g. uraninite_01. Use the exact name from your ore list.", out var oreArg)
            .WithStringArgument("gps", "Player position as x:y:z or a full SE GPS string. Omit or pass 0:0:0 for an instance-wide search.", out var gpsArg)
            .WithRequiredStringArgument("instance", "Instance name exactly as stored, e.g. greeks. Use the exact name from your instance list.", out var instanceArg)
            .WithNumberArgument("max_distance_km", "Max distance from gps in km. Default: unlimited. Requires gps.", out var maxDistanceArg)
            .WithBooleanArgument("mark_mined", "True: mark the found asteroid mined and return a GPS. False (default): stats only, nothing changes.", out var markMinedArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<LargestOreToolHandler>(
            serviceProvider,
            largestTool,
            oreArg,
            gpsArg,
            instanceArg,
            maxDistanceArg,
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
        var markMined = markMinedArgument.TryGetValue(args, out var marked) && marked;

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
}
