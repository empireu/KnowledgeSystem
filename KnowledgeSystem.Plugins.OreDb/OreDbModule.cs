using System.Globalization;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

// ReSharper disable UnusedMember.Global

namespace KnowledgeSystem.Plugins.OreDb;

[SlashCommand("oredb", "Ore database for Space Engineers asteroid scans")]
public class OreDbModule(OreDbStores stores) : ApplicationCommandModule<ApplicationCommandContext>
{
    [SubSlashCommand("query", "Shows how many ore deposits of a type exist in an instance and the largest deposit volume")]
    public async Task QueryAsync(
        [SlashCommandParameter(Name = "instance", Description = "The server instance, e.g. Greeks")] string instance,
        [SlashCommandParameter(Name = "ore", Description = "The ore type, e.g. uraninite_01")] string ore)
    {
        instance = instance.ToLowerInvariant();
        ore = ore.ToLowerInvariant();

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        var result = await stores.QueryAsync(instance, ore, CancellationToken.None);

        if (result == null)
        {
            await Context.Interaction.ModifyResponseAsync(m => m.Content = $"Instance **\"{instance}\"** not found.");
            return;
        }

        var content = result.DepositCount == 0
            ? $"No `{ore}` deposits found in instance **\"{instance}\"**."
            : $"`{ore}` in **\"{instance}\"**: __{result.DepositCount}__ deposits, largest deposit __{FormatVolume(result.LargestDeposit)} m³__, richest asteroid __{FormatVolume(result.LargestSum)} m³__.";

        await Context.Interaction.ModifyResponseAsync(m => m.Content = content);
    }

    [SubSlashCommand("pop", "Closest asteroid to a GPS position with more than the minimum volume of an ore")]
    public async Task PopAsync(
        [SlashCommandParameter(Name = "instance", Description = "The server instance, e.g. Greeks")] string instance,
        [SlashCommandParameter(Name = "gps", Description = "GPS string")] string gps,
        [SlashCommandParameter(Name = "ore", Description = "The ore type, e.g. Uranium")] string ore,
        [SlashCommandParameter(Name = "minimum_volume", Description = "Minimum ore volume in cubic meters")] double minimumVolume,
        [SlashCommandParameter(Name = "mode", Description = "Measure: sum of all deposits on the asteroid (default) or largest single deposit")] PopMode mode = PopMode.Sum)
    {
        instance = instance.ToLowerInvariant();
        ore = ore.ToLowerInvariant();

        await Context.Interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        if (!GpsParser.TryParse(gps, out double x, out double y, out double z))
        {
            await Context.Interaction.ModifyResponseAsync(m => m.Content = "Invalid GPS. Use a Space Engineers GPS string (GPS:name:x:y:z:color:) or plain x:y:z.");
            return;
        }

        var result = await stores.PopAsync(instance, x, y, z, ore, minimumVolume, mode, CancellationToken.None);

        if (result == null)
        {
            await Context.Interaction.ModifyResponseAsync(m => m.Content = $"No asteroid in instance **\"{instance}\"** has more than __{FormatVolume(minimumVolume)} m³__ of `{ore}`.");
            return;
        }

        var measureLabel = mode == PopMode.Sum ? "total" : "largest deposit";
        var content = $"Closest: \"{result.AsteroidName}\" - {FormatVolume(result.Volume)} m³ {measureLabel} of `{ore}`, {FormatVolume(result.Distance)} m away. Marked as mined.\n```\n{CreateGps(instance, ore, result)}\n```";

        await Context.Interaction.ModifyResponseAsync(m => m.Content = content);
    }

    private static string CreateGps(string instance, string ore, OrePopResult result)
    {
        var name = $"{instance} {ore} {result.Volume:0}";
        return $"GPS:{name}:{FormatCoordinate(result.X)}:{FormatCoordinate(result.Y)}:{FormatCoordinate(result.Z)}:#00FF00:";
    }

    private static string FormatVolume(double value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string FormatCoordinate(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
