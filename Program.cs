using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using TwinFx.Agents;
using TwinFx.Services;

Console.WriteLine("🚀 Starting TwinFx Azure Functions Application...");

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(workerApplication =>
    {
        Console.WriteLine("🔧 Registering CORS middleware...");
        // Register CORS middleware first in the pipeline
        workerApplication.UseMiddleware<TwinFx.CorsMiddleware>();
    })
    .ConfigureServices(services =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Add logging configuration
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Add Semantic Kernel services
        services.AddSingleton<Kernel>(serviceProvider =>
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var kernelBuilder = Kernel.CreateBuilder();
            
            // Configure Azure OpenAI using configuration values
            kernelBuilder.AddAzureOpenAIChatCompletion(
                deploymentName: configuration?.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                              configuration?.GetValue<string>("AzureOpenAI:DeploymentName") ?? "gpt4mini",
                endpoint: configuration?.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                         configuration?.GetValue<string>("AzureOpenAI:Endpoint") ?? "https://flatbitai.openai.azure.com/",
                apiKey: configuration?.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                       configuration?.GetValue<string>("AzureOpenAI:ApiKey") ?? "");
            
            // Register plugins
            kernelBuilder.Plugins.AddFromType<TwinFx.Plugins.SearchDocumentsPlugin>();
            kernelBuilder.Plugins.AddFromType<TwinFx.Plugins.ManagePicturesPlugin>();
            
            return kernelBuilder.Build();
        });

        // Register TwinFx services
        services.AddSingleton<AzureSearchService>();
        services.AddSingleton<DataLakeClientFactory>();

        services.AddRouting(options => options.LowercaseUrls = true);
    })
    .Build();
 

await host.RunAsync();await host.RunAsync();