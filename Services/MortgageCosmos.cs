using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TwinFx.Agents;
using TwinFx.Models;

namespace TwinFx.Services;

// ========================================
// CLASES DE MORTGAGE STATEMENT - Copiadas exactamente de AgentMortgage.cs
// ========================================
 

// ========================================
// CLASE PARA COSMOS DB
// ========================================

public class MortgageDocumentData
{
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("twinId")]
    public string TwinID { get; set; } = string.Empty;
    
    [JsonProperty("homeId")]
    public string HomeId { get; set; } = string.Empty;
    
    [JsonProperty("fileName")]
    public string FileName { get; set; } = string.Empty;
    
    [JsonProperty("filePath")]
    public string FilePath { get; set; } = string.Empty;
    
    [JsonProperty("containerName")]
    public string ContainerName { get; set; } = string.Empty;
    
    [JsonProperty("documentUrl")]
    public string DocumentUrl { get; set; } = string.Empty;
    
    [JsonProperty("mortgageStatementReport")]
    public MortgageStatementReport MortgageStatementReport { get; set; } = new();
    
    [JsonProperty("aiAnalysisResultJson")]
    public string AiAnalysisResultJson { get; set; } = string.Empty;
    
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "mortgage";

    public string HtmlReport { get; set; } = "mortgage";

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["TwinID"] = TwinID,
            ["homeId"] = HomeId,
            ["fileName"] = FileName,
            ["filePath"] = FilePath,
            ["containerName"] = ContainerName,
            ["documentUrl"] = DocumentUrl,
            ["mortgageStatementReport"] = MortgageStatementReport,
            ["aiAnalysisResultJson"] = AiAnalysisResultJson,
            ["fechaCreacion"] = FechaCreacion.ToString("O"),
            ["fechaActualizacion"] = FechaActualizacion.ToString("O"),
            ["type"] = Type
        };
    }
}

// ========================================
// SERVICIO COSMOS DB
// ========================================

public class MortgageCosmosDbService
{
    private readonly ILogger<MortgageCosmosDbService> _logger;
    private readonly Container _mortgageContainer;
    private readonly IConfiguration _configuration;

    public MortgageCosmosDbService(ILogger<MortgageCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration = null)
    {
        _logger = logger;
        _configuration = configuration;
        var cosmosSettings = cosmosOptions.Value;
        
        _logger.LogInformation("💰 Initializing Mortgage Cosmos DB Service");
        _logger.LogInformation($"   • Endpoint: {cosmosSettings.Endpoint}");
        _logger.LogInformation($"   • Database: {cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   • Container: TwinMortgage");
        
        var client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
        var database = client.GetDatabase(cosmosSettings.DatabaseName);
        _mortgageContainer = database.GetContainer("TwinMortgage");
        
        _logger.LogInformation("✅ Mortgage Cosmos DB Service initialized successfully");
    }

    /// <summary>
    /// Guardar análisis de hipoteca con la estructura completa de MortgageStatementReport
    /// </summary>
    /// <param name="mortgageStatementReport">El objeto completo deserializado con todos los datos</param>
    /// <param name="aiAnalysisResultJson">El JSON original de respuesta del AI</param>
    /// <param name="twinId">ID del Twin (partition key)</param>
    /// <param name="homeId">ID de la casa relacionada</param>
    /// <param name="fileName">Nombre del archivo</param>
    /// <param name="filePath">Ruta del archivo</param>
    /// <param name="containerName">Nombre del contenedor DataLake</param>
    /// <param name="documentUrl">URL del documento</param>
    /// <returns>ID del documento guardado o null si falló</returns>
    public async Task<string?> SaveMortgageAnalysisAsync(
        TwinFx.Agents.MortgageStatementReport mortgageStatementReport,
        string aiAnalysisResultJson,
        string twinId,
        string homeId,
        string fileName = "",
        string filePath = "",
        string containerName = "",
        string documentUrl = "")
    {
        try
        {
            var mortgageDocId = Guid.NewGuid().ToString();
            
            _logger.LogInformation("💰 Saving mortgage analysis for Twin: {TwinId}, Home: {HomeId}, File: {FileName}",
                twinId, homeId, fileName);
            
            var mortgageDocument = new MortgageDocumentData
            {
                Id = mortgageDocId,
                TwinID = twinId,
                HomeId = homeId,
                FileName = fileName,
                FilePath = filePath,
                ContainerName = containerName,
                DocumentUrl = documentUrl,
                HtmlReport = mortgageStatementReport.htmlReport, // Guardar el HTML por separado
                MortgageStatementReport =  mortgageStatementReport, // Objeto completo con estructura
                AiAnalysisResultJson = aiAnalysisResultJson, // JSON original como backup
                FechaCreacion = DateTime.UtcNow,
                FechaActualizacion = DateTime.UtcNow,
                Type = "mortgage"
            };

            await _mortgageContainer.CreateItemAsync(mortgageDocument.ToDict(), new PartitionKey(twinId));
            
            _logger.LogInformation("✅ Mortgage analysis saved successfully with ID: {MortgageDocId} for Twin: {TwinId}",
                mortgageDocId, twinId);
            return mortgageDocId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save mortgage analysis for Twin: {TwinId}, File: {FileName}",
                twinId, fileName);
            return null;
        }
    }

    /// <summary>
    /// Obtener todos los documentos de hipoteca de un twin
    /// </summary>
    public async Task<List<MortgageDocumentData>> GetMortgageDocumentsByTwinIdAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("💰 Getting mortgage documents for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _mortgageContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var mortgageDocuments = new List<MortgageDocumentData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var mortgageDoc = MortgageDocumentDataFromDict(item);
                        mortgageDocuments.Add(mortgageDoc);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to MortgageDocumentData: {Id}", 
                            item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} mortgage documents for Twin ID: {TwinId}", 
                mortgageDocuments.Count, twinId);
            return mortgageDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get mortgage documents for Twin ID: {TwinId}", twinId);
            return new List<MortgageDocumentData>();
        }
    }

    /// <summary>
    /// Obtener documentos de hipoteca por Twin ID y Home ID con SAS URLs generadas
    /// </summary>
    public async Task<List<MortgageStatementReport>> GetMortgageDocumentsByHomeIdAsync(string twinId, string homeId)
    {
        try
        {
            _logger.LogInformation("💰 Getting mortgage statement reports with SAS URLs for Twin: {TwinId}, Home: {HomeId}", twinId, homeId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.homeId = @homeId ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@homeId", homeId);

            var iterator = _mortgageContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var reportsList = new List<MortgageStatementReport>();

            // Initialize DataLake factory if configuration is available
            DataLakeClientFactory dataLakeFactory = null;
            if (_configuration != null)
            {
                try
                {
                    dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                    _logger.LogInformation("✅ DataLake factory initialized for SAS URL generation");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Failed to initialize DataLake factory, SAS URLs will not be generated");
                }
            }

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();

                foreach (var item in response)
                {
                    try
                    {
                        // Extract mortgage statement report data
                        var mortgageStatementData = item.GetValueOrDefault("mortgageStatementReport");
                        var fileName = item.GetValueOrDefault("fileName")?.ToString() ?? "";
                        var filePath = item.GetValueOrDefault("filePath")?.ToString() ?? "";
                        var containerName = item.GetValueOrDefault("containerName")?.ToString() ?? twinId;

                        if (mortgageStatementData != null)
                        {
                            // Convert to JSON string and then deserialize
                            var jsonString = JsonConvert.SerializeObject(mortgageStatementData);
                            var recordFound = JsonConvert.DeserializeObject<MortgageStatementReport>(jsonString);
                            
                            if (recordFound != null)
                            {
                                // Set basic file information
                                recordFound.FileName = fileName;
                                recordFound.FilePath = filePath;

                                // Generate SAS URL if DataLake factory is available
                                if (dataLakeFactory != null && !string.IsNullOrEmpty(fileName) && !string.IsNullOrEmpty(filePath))
                                {
                                    try
                                    {
                                        var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                                        var fullFilePath = $"{filePath}/{fileName}";
                                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(24));
                                        
                                        if (!string.IsNullOrEmpty(sasUrl))
                                        {
                                            recordFound.FileURL = sasUrl;
                                            _logger.LogDebug("✅ Generated SAS URL for mortgage document: {FileName}", fileName);
                                        }
                                        else
                                        {
                                            recordFound.FileURL = "";
                                            _logger.LogWarning("⚠️ Could not generate SAS URL for: {FullFilePath}", fullFilePath);
                                        }
                                    }
                                    catch (Exception sasEx)
                                    {
                                        _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for mortgage document: {FileName}", fileName);
                                        recordFound.FileURL = "";
                                    }
                                }
                                else
                                {
                                    recordFound.FileURL = "";
                                    _logger.LogDebug("📋 SAS URL not generated for {FileName} (missing configuration or file info)", fileName);
                                }

                                reportsList.Add(recordFound);
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        _logger.LogWarning(parseEx, "⚠️ Error parsing mortgage statement report for item");
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} mortgage statement reports for Twin: {TwinId}, Home: {HomeId}", 
                reportsList.Count, twinId, homeId);

            return reportsList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get mortgage statement reports for Twin: {TwinId}, Home: {HomeId}", twinId, homeId);
            return new List<MortgageStatementReport>();
        }
    }

    /// <summary>
    /// Helper method to convert dictionary to MortgageDocumentData
    /// </summary>
    private static MortgageDocumentData MortgageDocumentDataFromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                if (value is T directValue)
                    return directValue;

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return new MortgageDocumentData
        {
            Id = GetValue("id", ""),
            TwinID = GetValue<string>("TwinID"),
            HomeId = GetValue<string>("homeId"),
            FileName = GetValue<string>("fileName"),
            FilePath = GetValue<string>("filePath"),
            ContainerName = GetValue<string>("containerName"),
            DocumentUrl = GetValue<string>("documentUrl"),
            AiAnalysisResultJson = GetValue<string>("aiAnalysisResultJson"),
            FechaCreacion = GetValue("fechaCreacion", DateTime.UtcNow),
            FechaActualizacion = GetValue("fechaActualizacion", DateTime.UtcNow),
            Type = GetValue("type", "mortgage")
        };
    }
}