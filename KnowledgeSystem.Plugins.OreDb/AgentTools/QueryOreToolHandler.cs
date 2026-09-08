using KnowledgeSystem.Agents.Orchestration;
using KnowledgeSystem.Agents.Orchestration.Tools;
using KnowledgeSystem.Agents.Tools;
using KnowledgeSystem.Plugins.Library;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KnowledgeSystem.Plugins.OreDb.AgentTools;

public sealed class QueryOreToolHandler(
    AgentTool tool,
    StringArgument oreArgument,
    StringArgument instanceArgument,
    NumberArgument minimumTotalVolumeArgument,
    OreDbStores stores,
    ILogger<QueryOreToolHandler> logger
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var queryTool = new ToolBuilder("oredb_query_ore")
            .WithDescription("Reports ore stats for one ore type in an instance: remaining deposit count, largest single deposit and richest asteroid. With minimum_total_volume set, reports how many unmined asteroids hold more than that total of the ore. Read-only; never claims and never returns a GPS.")
            .WithRequiredStringArgument("ore", "Ore type exactly as stored in the database, e.g. uraninite_01. Use the exact name from your ore list.", out var oreArg)
            .WithRequiredStringArgument("instance", "Instance name exactly as stored, e.g. greeks. Use the exact name from your instance list.", out var instanceArg)
            .WithNumberArgument("minimum_total_volume", "Count only unmined asteroids whose total volume of the ore is greater than this, in m3 (600K = 600000). Use for 'how many asteroids have over X of ore'. When omitted, the standard instance stats are returned.", out var minimumTotalVolumeArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<QueryOreToolHandler>(
            serviceProvider,
            queryTool,
            oreArg,
            instanceArg,
            minimumTotalVolumeArg
        );

        registry.RegisterTool(queryTool, handler);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(
        AgentRunner<BasicContext> runner,
        ArgumentExtractionResult args,
        CancellationToken cancellationToken)
    {
        var ore = oreArgument.GetValue(args).ToLowerInvariant();
        var instance = instanceArgument.GetValue(args).ToLowerInvariant();

        try
        {
            if (minimumTotalVolumeArgument.TryGetValue(args, out var minimumTotalVolume))
            {
                return await ExecuteTotalsAsync(instance, ore, minimumTotalVolume, cancellationToken);
            }

            var result = await stores.QueryAsync(instance, ore, cancellationToken);

            if (result == null)
            {
                return Error($"Instance \"{instance}\" was not found in the ore database.");
            }

            if (result.DepositCount == 0)
            {
                return Success($"No unmined `{ore}` deposits were found in instance \"{instance}\".");
            }

            return Success(
                $"`{ore}` in \"{instance}\": {result.DepositCount} remaining deposits, largest single deposit {OreFormatting.FormatVolume(result.LargestDeposit)} m3, richest asteroid \"{result.RichestAsteroidName}\" with {OreFormatting.FormatVolume(result.LargestSum)} m3 in total."
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogError(e, "oredb_query_ore failed for instance {instance} and ore {ore}", instance, ore);
            return Error("The ore database could not be queried right now.");
        }
    }

    private async Task<ToolExecutionResult> ExecuteTotalsAsync(string instance, string ore, double minimumTotalVolume, CancellationToken cancellationToken)
    {
        var result = await stores.AsteroidTotalsAsync(instance, ore, minimumTotalVolume, cancellationToken);

        if (result == null)
        {
            return Error($"Instance \"{instance}\" was not found in the ore database.");
        }

        if (result.AsteroidCount == 0)
        {
            return Success($"No unmined asteroid in \"{instance}\" has more than {OreFormatting.FormatVolume(minimumTotalVolume)} m3 of `{ore}`.");
        }

        if (result.AsteroidCount == 1)
        {
            return Success(
                $"1 unmined asteroid in \"{instance}\" holds more than {OreFormatting.FormatVolume(minimumTotalVolume)} m3 of `{ore}`: \"{result.RichestAsteroidName}\" with {OreFormatting.FormatVolume(result.RichestVolume)} m3."
            );
        }

        return Success(
            $"{result.AsteroidCount} unmined asteroids in \"{instance}\" hold more than {OreFormatting.FormatVolume(minimumTotalVolume)} m3 of `{ore}`: {OreFormatting.FormatVolume(result.CombinedVolume)} m3 in total across them, richest \"{result.RichestAsteroidName}\" with {OreFormatting.FormatVolume(result.RichestVolume)} m3."
        );
    }
}
