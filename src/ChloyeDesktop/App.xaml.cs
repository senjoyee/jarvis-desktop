using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ChloyeDesktop.Services;

namespace ChloyeDesktop;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Chloye Desktop starting...");

        // Auto-start MCP servers
        var mcpManager = Services.GetRequiredService<McpManager>();
        _ = mcpManager.InitializeAsync(); // Fire and forget
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        services.AddSingleton<DatabaseService>();
        services.AddSingleton<SecretsService>();
        services.AddSingleton<ConversationService>();
        services.AddSingleton<ChatService>();
        services.AddSingleton<McpManager>();
        services.AddSingleton<SkillService>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        var logger = Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("Chloye Desktop shutting down...");
        base.OnExit(e);
    }
}
