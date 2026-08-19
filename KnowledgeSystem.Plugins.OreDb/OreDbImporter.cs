using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ReSharper disable PropertyCanBeMadeInitOnly.Local
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable UnusedAutoPropertyAccessor.Local
// ReSharper disable ForCanBeConvertedToForeach
// ReSharper disable LoopCanBeConvertedToQuery

namespace KnowledgeSystem.Plugins.OreDb;

public sealed class OreDbImporter(ILogger<OreDbImporter> logger, OreDbStores stores) : IHostedService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var count = await ImportFromWorkingDirectoryAsync(cancellationToken);

        if (count == 0)
        {
            logger.LogInformation("No ore database import files found in working directory");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public async Task<int> ImportFromWorkingDirectoryAsync(CancellationToken cancellationToken)
    {
        var imported = 0;

        foreach (var path in Directory.EnumerateFiles(Environment.CurrentDirectory, "IMPORT_*.json"))
        {
            var fileName = Path.GetFileName(path);
            var instanceName = fileName
                .Substring("IMPORT_".Length, fileName.Length - "IMPORT_".Length - ".json".Length)
                .ToLowerInvariant();

            Channel<ImportedAsteroid>? channel = null;
            Task? parserTask = null;

            try
            {
                int count;

                await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan))
                {
                    channel = Channel.CreateBounded<ImportedAsteroid>(new BoundedChannelOptions(1024)
                    {
                        SingleWriter = true,
                        SingleReader = true,
                        FullMode = BoundedChannelFullMode.Wait
                    });

                    parserTask = ReadAsteroids(stream, channel.Writer, cancellationToken);

                    count = await stores.ImportInstanceAsync(instanceName, channel.Reader, cancellationToken);
                    await parserTask;
                }

                File.Delete(path);

                logger.LogInformation("Imported {count} asteroids for instance {instance} from {file}", count, instanceName, fileName);
                imported++;
            }
            catch (Exception ex)
            {
                channel?.Writer.TryComplete();

                if (parserTask != null)
                {
                    try
                    {
                        await parserTask;
                    }
                    catch
                    {
                        // Ignored
                    }
                }

                var failedPath = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "FAILED_" + fileName.Substring("IMPORT_".Length));

                try
                {
                    File.Move(path, failedPath, true);
                }
                catch
                {
                    // Keeps the original file if it cannot be renamed
                }

                logger.LogError(ex, "Import failed for {file}, kept as {failed}", fileName, failedPath);
            }
        }

        return imported;
    }

    private static async Task ReadAsteroids(Stream stream, ChannelWriter<ImportedAsteroid> writer, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var jsonAsteroid in JsonSerializer.DeserializeAsyncEnumerable<JsonAsteroid>(stream, JsonOptions, cancellationToken))
            {
                if (jsonAsteroid == null)
                {
                    continue;
                }

                var ores = new List<ImportedOreDeposit>();

                var jsonOres = jsonAsteroid.Ores ?? [];
                for (var oreIndex = 0; oreIndex < jsonOres.Count; oreIndex++)
                {
                    var jsonOre = jsonOres[oreIndex];
                    ores.Add(new ImportedOreDeposit(
                        (jsonOre.Id ?? string.Empty).ToLowerInvariant(),
                        jsonOre.Volume,
                        jsonOre.Position?.X ?? 0.0,
                        jsonOre.Position?.Y ?? 0.0,
                        jsonOre.Position?.Z ?? 0.0,
                        jsonOre.Estimated)
                    );
                }

                var result = new ImportedAsteroid(
                    jsonAsteroid.Name ?? string.Empty,
                    jsonAsteroid.Position?.X ?? 0.0,
                    jsonAsteroid.Position?.Y ?? 0.0,
                    jsonAsteroid.Position?.Z ?? 0.0,
                    jsonAsteroid.Size,
                    ores
                );

                await writer.WriteAsync(result, cancellationToken);
            }

            writer.Complete();
        }
        catch (Exception ex)
        {
            writer.Complete(ex);
            throw;
        }
    }

    private sealed class JsonAsteroid
    {
        public string? Name { get; set; }

        public JsonPosition? Position { get; set; }

        public float Size { get; set; }

        public List<JsonOre>? Ores { get; set; }
    }

    private sealed class JsonOre
    {
        public string? Id { get; set; }

        public double Volume { get; set; }

        public JsonPosition? Position { get; set; }

        public bool Estimated { get; set; }
    }

    private sealed class JsonPosition
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Z { get; set; }
    }
}
