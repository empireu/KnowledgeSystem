using KnowledgeSystem.PluginLoader;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetCord;
using NetCord.Rest;

namespace KnowledgeSystem.Plugins.RestApi;

[Plugin("RestApi", "mqr.rest.api", "0.0.0", "empireu")]
public sealed class RestApiPlugin(
    ILogger<RestApiPlugin> logger,
    IServiceProvider serviceProvider,
    IOptions<RestApiOptions> config,
    ILoggerFactory loggerFactory
) : IPlugin
{
    private const int MaxContentLength = 2000;
    private const int MaxEmbedTitleLength = 256;
    private const int MaxEmbedDescriptionLength = 4096;
    private const int MaxEmbedFields = 25;
    private const int MaxFieldNameLength = 256;
    private const int MaxFieldValueLength = 1024;

    private WebApplication? _app;

    public async Task Start(CancellationToken cancellationToken)
    {
        var restClient = serviceProvider.GetService<RestClient>();

        if (restClient == null)
        {
            throw new InvalidOperationException("RestClient is not registered. Enable Discord integration with core.ProvideDiscordIntegration to use the RestApi plugin.");
        }

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = "RestApi"
        });

        builder.WebHost.UseUrls(config.Value.ServerUrl);

        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(loggerFactory);
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

        _app = builder.Build();

        var apiKey = config.Value.ApiKey;

        if (!string.IsNullOrEmpty(apiKey))
        {
            _app.Use(async (HttpContext context, Func<Task> next) =>
            {
                string? token = null;

                var auth = context.Request.Headers.Authorization.FirstOrDefault();

                if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    token = auth["Bearer ".Length..];
                }

                token ??= context.Request.Query["token"].FirstOrDefault();

                if (token == apiKey)
                {
                    await next();
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized", cancellationToken: context.RequestAborted);
            });
        }

        var channelId = config.Value.ChannelId!.Value;

        _app.MapPost("/api/notifications", async (NotificationRequest? request, CancellationToken ct) =>
        {
            var validationError = Validate(request);

            if (validationError != null)
            {
                return Results.BadRequest(validationError);
            }

            var properties = new MessageProperties { Content = request!.Content };

            if (request.Embed != null)
            {
                properties.Embeds = [BuildEmbed(request.Embed)];
            }

            try
            {
                var message = await restClient.SendMessageAsync(channelId, properties, cancellationToken: ct);

                return Results.Accepted(null, message.Id);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to send notification to channel {channel}", channelId);

                return Results.Problem("Failed to send notification to Discord.", statusCode: StatusCodes.Status502BadGateway);
            }
        });

        await _app.StartAsync(cancellationToken);

        logger.LogInformation("RestApi notification server listening on {url}", config.Value.ServerUrl);
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        if (_app != null)
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
            _app = null;
            logger.LogInformation("RestApi notification server stopped");
        }
    }

    private static string? Validate(NotificationRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Content))
        {
            return "Content is required.";
        }

        if (request.Content.Length > MaxContentLength)
        {
            return $"Content exceeds the {MaxContentLength} character limit.";
        }

        if (request.Embed == null)
        {
            return null;
        }

        if (request.Embed.Title != null && request.Embed.Title.Length > MaxEmbedTitleLength)
        {
            return $"Embed title exceeds the {MaxEmbedTitleLength} character limit.";
        }

        if (request.Embed.Description != null && request.Embed.Description.Length > MaxEmbedDescriptionLength)
        {
            return $"Embed description exceeds the {MaxEmbedDescriptionLength} character limit.";
        }

        var fields = request.Embed.Fields;
        if (request.Embed.Color != null && request.Embed.Color.Value > 0xFFFFFF)
        {
            return "Embed color must be a 24-bit RGB value.";
        }


        if (fields == null || fields.Count == 0)
        {
            return request.Embed.Title == null && request.Embed.Description == null
                ? "Embed must have a title, a description, or at least one field."
                : null;
        }

        if (fields.Count > MaxEmbedFields)
        {
            return $"Embed exceeds the {MaxEmbedFields} field limit.";
        }

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];

            if (string.IsNullOrWhiteSpace(field.Name))
            {
                return $"Field {i + 1} has no name.";
            }

            if (field.Name.Length > MaxFieldNameLength)
            {
                return $"Field {i + 1} name exceeds the {MaxFieldNameLength} character limit.";
            }

            if (string.IsNullOrWhiteSpace(field.Value))
            {
                return $"Field {i + 1} has no value.";
            }

            if (field.Value.Length > MaxFieldValueLength)
            {
                return $"Field {i + 1} value exceeds the {MaxFieldValueLength} character limit.";
            }
        }

        return null;
    }

    private static EmbedProperties BuildEmbed(NotificationEmbed notificationEmbed)
    {
        var embed = new EmbedProperties();

        if (notificationEmbed.Title != null)
        {
            embed = embed.WithTitle(notificationEmbed.Title);
        }

        if (notificationEmbed.Description != null)
        {
            embed = embed.WithDescription(notificationEmbed.Description);
        }

        if (notificationEmbed.Color != null)
        {
            embed = embed.WithColor(new Color((int)notificationEmbed.Color.Value));
        }

        if (notificationEmbed.Fields != null)
        {
            var fields = new List<EmbedFieldProperties>(notificationEmbed.Fields.Count);

            foreach (var field in notificationEmbed.Fields)
            {
                var fieldProperties = new EmbedFieldProperties()
                    .WithName(field.Name!)
                    .WithValue(field.Value!);

                if (field.Inline == true)
                {
                    fieldProperties = fieldProperties.WithInline();
                }

                fields.Add(fieldProperties);
            }

            embed = embed.WithFields(fields);
        }

        return embed;
    }
}
