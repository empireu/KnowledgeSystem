using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.OreDb.AgentTools;

public sealed class NearbyOresToolHandler(
    AgentTool tool,
    StringArgument oreArgument,
    StringArgument gpsArgument,
    StringArgument instanceArgument,
    IntegerArgument countArgument,
    NumberArgument minimumVolumeArgument,
    OreDbStores stores,
    ILogger<NearbyOresToolHandler> logger
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var nearbyTool = new ToolBuilder("oredb_nearby_ores")
            .WithDescription("Surveys the closest unmined asteroids to a GPS position that contain more than a minimum total volume of one ore type, ordered by distance. Each row: name, volume, distance, full ore contents. Read-only; never claims and never returns a GPS.")
            .WithRequiredStringArgument("ore", "Ore type exactly as stored in the database, e.g. uraninite_01. Use the exact name from your ore list.", out var oreArg)
            .WithRequiredStringArgument("gps", "Player position as x:y:z or a full SE GPS string.", out var gpsArg)
            .WithRequiredStringArgument("instance", "Instance name exactly as stored, e.g. greeks. Use the exact name from your instance list.", out var instanceArg)
            .WithIntegerArgument("count", "Max rows, 1 to 25. Default: 5.", out var countArg)
            .WithNumberArgument("minimum_volume", "Minimum total volume of the ore on the asteroid in m3. Default: any amount.", out var minimumVolumeArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<NearbyOresToolHandler>(
            serviceProvider,
            nearbyTool,
            oreArg,
            gpsArg,
            instanceArg,
            countArg,
            minimumVolumeArg
        );

        registry.RegisterTool(nearbyTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<BasicContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var ore = oreArgument.GetValue(args).ToLowerInvariant();
        var instance = instanceArgument.GetValue(args).ToLowerInvariant();
        var gps = gpsArgument.GetValue(args);

        if (!GpsParser.TryParse(gps, out var x, out var y, out var z))
        {
            return Error("The gps argument is invalid. Provide plain coordinates x:y:z or a full Space Engineers GPS string like GPS:me:12.3:45.6:78.9:#FF0000:.");
        }

        var hasCount = countArgument.TryGetValue(args, out var count);
        var countValue = Math.Clamp(hasCount ? count : 5, 1, 25);

        var hasMinimumVolume = minimumVolumeArgument.TryGetValue(args, out var minimumVolume);

        try
        {
            var results = await stores.NearbyOresAsync(
                instance,
                x,
                y,
                z,
                ore,
                hasMinimumVolume ? minimumVolume : 0.0,
                countValue,
                cancellationToken
            );

            if (results == null)
            {
                return Error($"Instance \"{instance}\" was not found in the ore database.");
            }

            if (results.Count == 0)
            {
                return Success(hasMinimumVolume
                    ? $"No unmined asteroid with more than {OreFormatting.FormatVolume(minimumVolume)} m3 of `{ore}` was found near the given position in \"{instance}\"."
                    : $"No unmined asteroid with `{ore}` was found near the given position in \"{instance}\".");
            }

            var sb = new StringBuilder();

            for (var index = 0; index < results.Count; index++)
            {
                var row = results[index];
                sb.Append($"{index + 1}. \"{row.AsteroidName}\" - {OreFormatting.FormatVolume(row.Volume)} m3 of `{ore}`, {OreFormatting.FormatDistance(row.Distance!.Value)} away.");
                sb.AppendLine($" Contents: {OreFormatting.FormatOreBreakdown(row.Ores)}");
            }

            sb.Append("This is a read-only survey: nothing was marked as mined and no GPS was produced.");

            return Success(sb.ToString());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "oredb_nearby_ores failed for instance {instance} and ore {ore}", instance, ore);
            return Error("The ore database could not be queried right now.");
        }
    }
}
