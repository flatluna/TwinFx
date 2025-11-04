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

        // ✅ FIXED: Add Application Insights and Functions configuration FIRST
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

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

        // Configuración de logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // ✅ FIXED: Moved Semantic Kernel registration after basic services
        services.AddSingleton<Kernel>(serviceProvider =>
        {
            try
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Warning: Failed to initialize Semantic Kernel: {ex.Message}");
                // Return a minimal kernel if configuration fails
                return Kernel.CreateBuilder().Build();
            }
        });

        // Registrar servicios TwinFx con configuraciones fuertemente tipadas
        services.AddSingleton<AzureSearchService>();
        services.AddSingleton<DataLakeClientFactory>();
        services.AddSingleton<CosmosDbService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<CosmosDbService>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            var storageOptions = serviceProvider.GetRequiredService<IOptions<AzureStorageSettings>>();
            return new CosmosDbService(logger, cosmosOptions, storageOptions);
        });
        
        // ✅ Registrar el nuevo servicio de Homes
        services.AddSingleton<HomesCosmosDbService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<HomesCosmosDbService>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new HomesCosmosDbService(logger, cosmosOptions, configuration);
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

        // ✅ Registrar el nuevo AgenteHomes para gestión inteligente de casas/viviendas
        services.AddSingleton<AgenteHomes>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<AgenteHomes>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new AgenteHomes(logger, configuration);
        });

        // ✅ Registrar el nuevo servicio de Cursos
        services.AddSingleton<CursosCosmosDbService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<CursosCosmosDbService>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            return new CursosCosmosDbService(logger, cosmosOptions);
        });

        // ✅ Registrar el nuevo servicio de Skills
        services.AddSingleton<SkillsCosmosDB>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SkillsCosmosDB>>();
            var cosmosOptions = serviceProvider.GetRequiredService<IOptions<CosmosDbSettings>>();
            return new SkillsCosmosDB(logger, cosmosOptions);
        });

        // ✅ Registrar el nuevo SkillsAgent para búsqueda de recursos de aprendizaje con AI
        services.AddSingleton<SkillsAgent>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SkillsAgent>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new SkillsAgent(logger, configuration);
        });

        // ✅ Registrar el nuevo AiWebSearchAgent para búsquedas web inteligentes con Bing Grounding
        services.AddSingleton<AiWebSearchAgent>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<AiWebSearchAgent>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new AiWebSearchAgent(logger, configuration);
        });

        // ✅ Registrar el nuevo PicturesFamilySearchIndex para indexación de fotos familiares en Azure AI Search
        services.AddSingleton<PicturesFamilySearchIndex>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<PicturesFamilySearchIndex>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new PicturesFamilySearchIndex(logger, configuration);
        });

        // ✅ Registrar el nuevo SportsActivityService para gestión de actividades deportivas
        services.AddSingleton<SportsActivityService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<SportsActivityService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return new SportsActivityService(logger, configuration);
        });

        // ✅ FIXED: Add routing configuration at the end
        services.AddRouting(options => options.LowercaseUrls = true);
    })
    .ConfigureLogging(logging =>
    {
        // Configuración de logging adicional si es necesario
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

Console.WriteLine("✅ TwinFx host configured successfully. Starting application...");
await host.RunAsync();