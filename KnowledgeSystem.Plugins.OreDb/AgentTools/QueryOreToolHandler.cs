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
    OreDbStores stores,
    ILogger<QueryOreToolHandler> logger
) : ToolHandler<BasicContext>.Plain(tool)
{
    public static void Register(AgentToolRegistry<BasicContext> registry, IServiceProvider serviceProvider)
    {
        var queryTool = new ToolBuilder("oredb_query_ore")
            .WithDescription("Reports remaining deposits of one ore type in an instance: deposit count, largest single deposit and richest asteroid. Read-only; never claims and never returns a GPS.")
            .WithRequiredStringArgument("ore", "Ore type exactly as stored in the database, e.g. uraninite_01. Use the exact name from your ore list.", out var oreArg)
            .WithRequiredStringArgument("instance", "Instance name exactly as stored, e.g. greeks. Use the exact name from your instance list.", out var instanceArg)
            .Build();

        var handler = ActivatorUtilities.CreateInstance<QueryOreToolHandler>(
            serviceProvider,
            queryTool,
            oreArg,
            instanceArg
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
}
