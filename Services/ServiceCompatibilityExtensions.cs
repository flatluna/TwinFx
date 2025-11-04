using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Métodos de extensión temporales para mantener compatibilidad durante la migración
/// </summary>
public static class ServiceCompatibilityExtensions
{
    /// <summary>
    /// Crear CosmosDbTwinProfileService desde IConfiguration (método de compatibilidad temporal)
    /// </summary>
    public static CosmosDbService CreateCosmosService(this IConfiguration configuration, ILogger<CosmosDbService> logger)
    {
        var cosmosSettings = new CosmosDbSettings
        {
            Endpoint = configuration["COSMOS_ENDPOINT"] ?? configuration["Values:COSMOS_ENDPOINT"] ?? "",
            Key = configuration["COSMOS_KEY"] ?? configuration["Values:COSMOS_KEY"] ?? "",
            DatabaseName = configuration["COSMOS_DATABASE_NAME"] ?? configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        };

        var storageSettings = new AzureStorageSettings
        {
            AccountName = configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_NAME"] ?? "",
            AccountKey = configuration["AZURE_STORAGE_ACCOUNT_KEY"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_KEY"] ?? ""
        };

        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(cosmosSettings);
        var storageOptions = Microsoft.Extensions.Options.Options.Create(storageSettings);
        return new CosmosDbService(logger, cosmosOptions, storageOptions);
    }

    public static ProfileCosmosDB CreateProfileCosmosService(this IConfiguration configuration, ILogger<ProfileCosmosDB> logger)
    {
        var cosmosSettings = new CosmosDbSettings
        {
            Endpoint = configuration["COSMOS_ENDPOINT"] ?? configuration["Values:COSMOS_ENDPOINT"] ?? "",
            Key = configuration["COSMOS_KEY"] ?? configuration["Values:COSMOS_KEY"] ?? "",
            DatabaseName = configuration["COSMOS_DATABASE_NAME"] ?? configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
        };

        var storageSettings = new AzureStorageSettings
        {
            AccountName = configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_NAME"] ?? "",
            AccountKey = configuration["AZURE_STORAGE_ACCOUNT_KEY"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_KEY"] ?? ""
        };

        var cosmosOptions = Microsoft.Extensions.Options.Options.Create(cosmosSettings);
        var storageOptions = Microsoft.Extensions.Options.Options.Create(storageSettings);
        return new ProfileCosmosDB(logger, cosmosOptions, storageOptions);
    }

    /// <summary>
    /// Crear DataLakeClientFactory desde IConfiguration (método de compatibilidad temporal)
    /// </summary>
    public static DataLakeClientFactory CreateDataLakeFactory(this IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        var storageSettings = new AzureStorageSettings
        {
            AccountName = configuration["AZURE_STORAGE_ACCOUNT_NAME"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_NAME"] ?? "",
            AccountKey = configuration["AZURE_STORAGE_ACCOUNT_KEY"] ?? configuration["Values:AZURE_STORAGE_ACCOUNT_KEY"] ?? ""
        };

        var options = Microsoft.Extensions.Options.Options.Create(storageSettings);
        return new DataLakeClientFactory(loggerFactory, options);
    }

    /// <summary>
    /// Crear WorkDocumentsCosmosService desde IConfiguration (método de compatibilidad temporal)
    /// </summary>
    public static WorkDocumentsCosmosService CreateWorkDocumentsService(this IConfiguration configuration, ILogger<WorkDocumentsCosmosService> logger)
    {
        // Para WorkDocumentsCosmosService, mantener compatibilidad con constructor legacy por ahora
        return new WorkDocumentsCosmosService(logger, configuration);
    }
}