using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using TwinFx.Agents;
using TwinFx.Services;
using TwinFx.Models;
using System;
using TwinFx.Functions;

Console.WriteLine("🚀 Starting TwinFx Azure Functions Application...");

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(workerApplication =>
    {
        Console.WriteLine("🔧 Registering CORS middleware...");
        // Registrar middleware CORS primero en el pipeline
        workerApplication.UseMiddleware<TwinFx.CorsMiddleware>();
    })
    .ConfigureAppConfiguration((context, config) =>
    {
        // Establecer el directorio base
        config.SetBasePath(AppContext.BaseDirectory);

        // Agregar settings.json a la configuración
        config.AddJsonFile("settings.json", optional: true, reloadOnChange: true);

        // Agregar local.settings.json configuración
        config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);

        // Agregar variables de entorno
        config.AddEnvironmentVariables();

        Console.WriteLine("📋 Configuration sources added: settings.json, local.settings.json y environment variables");
    })
    .ConfigureServices((context, services) =>
    {
        // Acceder a la configuración
        IConfiguration configuration = context.Configuration;

        // Registrar las secciones de configuración como clases fuertemente tipadas
        services.Configure<AzureOpenAISettings>(configuration.GetSection("Values:AzureOpenAI"));
        services.Configure<CosmosDbSettings>(options =>
        {
            options.Endpoint = configuration["Values:COSMOS_ENDPOINT"] ?? "";
            options.Key = configuration["Values:COSMOS_KEY"] ?? "";
            options.DatabaseName = configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB";
        });
        services.Configure<AzureStorageSettings>(options =>
        {
            options.AccountName = configuration["Values:AZURE_STORAGE_ACCOUNT_NAME"] ?? "";
            options.AccountKey = configuration["Values:AZURE_STORAGE_ACCOUNT_KEY"] ?? "";
        });
        services.Configure<DocumentIntelligenceSettings>(configuration.GetSection("Values:DocumentIntelligence"));
        services.Configure<AzureSearchSettings>(options =>
        {
            options.Endpoint = configuration["Values:AZURE_SEARCH_ENDPOINT"] ?? "";
            options.ApiKey = configuration["Values:AZURE_SEARCH_API_KEY"] ?? "";
            options.IndexName = configuration["Values:AZURE_SEARCH_INDEX_NAME"] ?? "";
        });

        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Configuración de logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Agregar servicios Semantic Kernel
        services.AddSingleton<Kernel>(serviceProvider =>
        {
            var azureOpenAISettings = serviceProvider.GetRequiredService<IOptions<AzureOpenAISettings>>().Value;

            var kernelBuilder = Kernel.CreateBuilder();

            // Usar los valores de configuración fuertemente tipados
            var deploymentName = azureOpenAISettings.DeploymentName ?? "gpt4mini";
            var endpoint = azureOpenAISettings.Endpoint ?? "https://flatbitai.openai.azure.com/";
            var apiKey = azureOpenAISettings.ApiKey ?? "";

            Console.WriteLine($"🤖 Semantic Kernel - Using deployment: {deploymentName} at {endpoint}");

            // Configurar Azure OpenAI
            kernelBuilder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);

            // Registrar plugins
            kernelBuilder.Plugins.AddFromType<TwinFx.Plugins.SearchDocumentsPlugin>();
            kernelBuilder.Plugins.AddFromType<TwinFx.Plugins.ManagePicturesPlugin>();

            return kernelBuilder.Build();
        });

        // Registrar servicios TwinFx con configuraciones fuertemente tipadas
        services.AddSingleton<AzureSearchService>();
        services.AddSingleton<DataLakeClientFactory>();
        services.AddSingleton<CosmosDbTwinProfileService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<CosmosDbTwinProfileService>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            var storageOptions = serviceProvider.GetRequiredService<IOptions<AzureStorageSettings>>();
            return new CosmosDbTwinProfileService(logger, cosmosOptions, storageOptions);
        });
        
        // ✅ Registrar el nuevo servicio de Homes
        services.AddSingleton<HomesCosmosDbService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<HomesCosmosDbService>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            return new HomesCosmosDbService(logger, cosmosOptions);
        });
        
        // ✅ Registrar el nuevo servicio de Diary
        services.AddSingleton<DiaryCosmosDbService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DiaryCosmosDbService>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            return new DiaryCosmosDbService(logger, cosmosOptions);
        });
        
        // ✅ Registrar el nuevo DiaryAgent para extracción de recibos con AI Vision
        services.AddSingleton<DiaryAgent>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DiaryAgent>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new DiaryAgent(logger, configuration, serviceProvider);
        });

        // ✅ Registrar DiarySearchIndex para indexación con vectores en Azure AI Search
        services.AddSingleton<DiarySearchIndex>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<DiarySearchIndex>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new DiarySearchIndex(logger, configuration);
        });

        services.AddRouting(options => options.LowercaseUrls = true);
    })
    .ConfigureLogging(logging =>
    {
        // Configuración de logging adicional si es necesario
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

await host.RunAsync();