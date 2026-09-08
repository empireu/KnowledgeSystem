using System.Text;
using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.OreDb.AgentTools;

public sealed class ClosestOreToolHandler(
    AgentTool tool,
    StringArgument oreArgument,
    StringArgument gpsArgument,
    StringArgument instanceArgument,
    NumberArgument minimumVolumeArgument,
    BooleanArgument markMinedArgument,
    OreDbStores stores,
    ILogger<ClosestOreToolHandler> logger
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var closestTool = new ToolBuilder("oredb_closest_ore")
            .WithDescription("Finds the unmined asteroid closest to a GPS position that contains more than a minimum total volume of one ore type. Reports name, volume and distance. mark_mined=true claims it (marks the asteroid mined, returns a GPS); otherwise stats only, no GPS.")
            .WithRequiredStringArgument("ore", "Ore type exactly as stored in the database, e.g. uraninite_01. Use the exact name from your ore list.", out var oreArg)
            .WithRequiredStringArgument("gps", "Player position as x:y:z or a full SE GPS string.", out var gpsArg)
            .WithRequiredStringArgument("instance", "Instance name exactly as stored, e.g. greeks. Use the exact name from your instance list.", out var instanceArg)
            .WithNumberArgument("minimum_volume", "Minimum total volume of the ore on the asteroid in m3. Default: any amount.", out var minimumVolumeArg)
            .WithBooleanArgument("mark_mined", "True: mark the found asteroid mined and return a GPS. False (default): stats only, nothing changes.", out var markMinedArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<ClosestOreToolHandler>(
            serviceProvider,
            closestTool,
            oreArg,
            gpsArg,
            instanceArg,
            minimumVolumeArg,
            markMinedArg
        );

        registry.RegisterTool(closestTool, handler);
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

        var hasMinimumVolume = minimumVolumeArgument.TryGetValue(args, out var minimumVolume);
        var markMined = markMinedArgument.TryGetValue(args, out var marked) && marked;

        try
        {
            var result = await stores.ClosestOreAsync(
                instance,
                x,
                y,
                z,
                ore,
                hasMinimumVolume ? minimumVolume : 0.0,
                markMined,
                cancellationToken
            );

            if (result == null)
            {
                return Error(hasMinimumVolume
                    ? $"No unmined asteroid in \"{instance}\" has more than {OreFormatting.FormatVolume(minimumVolume)} m3 of `{ore}`."
                    : $"No unmined asteroid with `{ore}` was found in \"{instance}\".");
            }

            var sb = new StringBuilder();
            sb.Append($"Closest: \"{result.AsteroidName}\" - {OreFormatting.FormatVolume(result.Volume)} m3 of `{ore}`, {OreFormatting.FormatDistance(result.Distance!.Value)} away.");
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
            logger.LogError(e, "oredb_closest_ore failed for instance {instance} and ore {ore}", instance, ore);
            return Error("The ore database could not be queried right now.");
        }
    }
}
