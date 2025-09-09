namespace TwinFx.Models;

/// <summary>
/// Configuración fuertemente tipada para Azure OpenAI
/// </summary>
public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string AlternativeDeploymentName { get; set; } = string.Empty;
}

/// <summary>
/// Configuración fuertemente tipada para Azure Cosmos DB
/// </summary>
public class CosmosDbSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
}

/// <summary>
/// Configuración fuertemente tipada para Azure Storage Data Lake
/// </summary>
public class AzureStorageSettings
{
    public string AccountName { get; set; } = string.Empty;
    public string AccountKey { get; set; } = string.Empty;
}

/// <summary>
/// Configuración fuertemente tipada para Azure Document Intelligence
/// </summary>
public class DocumentIntelligenceSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

/// <summary>
/// Configuración fuertemente tipada para Azure AI Search
/// </summary>
public class AzureSearchSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
}