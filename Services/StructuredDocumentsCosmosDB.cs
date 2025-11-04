using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinFx.Agents;

namespace TwinFx.Services
{
    /// <summary>
    /// CosmosDB service for handling structured documents data
    /// </summary>
    public class StructuredDocumentsCosmosDB
    {
        private readonly ILogger<StructuredDocumentsCosmosDB> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly string _databaseName = "TwinHumanDB";
        private readonly string _csvFilesContainerName = "TwinCSVFiles";

        public StructuredDocumentsCosmosDB(ILogger<StructuredDocumentsCosmosDB> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                var cosmosEndpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? _configuration["COSMOS_ENDPOINT"];
                var cosmosKey = _configuration["Values:COSMOS_KEY"] ?? _configuration["COSMOS_KEY"];

                if (string.IsNullOrEmpty(cosmosEndpoint) || string.IsNullOrEmpty(cosmosKey))
                {
                    throw new InvalidOperationException("COSMOS_ENDPOINT and COSMOS_KEY are required in configuration");
                }

                _cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
                _logger.LogInformation("✅ StructuredDocumentsCosmosDB initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize StructuredDocumentsCosmosDB");
                throw;
            }
        }

        /// <summary>
        /// Save CSV file data to TwinCSVFiles container
        /// </summary>
        /// <param name="csvFileData">CSV file data to save</param>
        /// <returns>True if saved successfully, false otherwise</returns>
        public async Task<bool> SaveCSVFileDataAsync(CSVFileData csvFileData)
        {
            try
            {
                _logger.LogInformation("💾 Saving CSV file data to CosmosDB - FileName: {FileName}, TwinId: {TwinId}", 
                    csvFileData.FileName, csvFileData.TwinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_csvFilesContainerName);

                // Create the document to save
                var document = new
                {
                    id = csvFileData.Id,
                    TwinId = csvFileData.TwinId,
                    FileName = csvFileData.FileName,
                    ContainerName = csvFileData.ContainerName,
                    FilePath = csvFileData.FilePath,
                    TotalRows = csvFileData.TotalRows,
                    TotalColumns = csvFileData.TotalColumns,
                    ColumnNames = csvFileData.ColumnNames,
                    Records = csvFileData.Records,
                    ProcessedAt = csvFileData.ProcessedAt,
                    Success = csvFileData.Success,
                    ErrorMessage = csvFileData.ErrorMessage,
                    DocumentType = "CSV",
                    CreatedAt = DateTime.UtcNow
                };

                // Save to CosmosDB using TwinId as partition key
                var response = await container.CreateItemAsync(document, new PartitionKey(csvFileData.TwinId));

                if (response.StatusCode == System.Net.HttpStatusCode.Created)
                {
                    _logger.LogInformation("✅ CSV file data saved successfully to CosmosDB - ID: {DocumentId}, Records: {RecordCount}", 
                        csvFileData.Id, csvFileData.TotalRows);
                    return true;
                }
                else
                {
                    _logger.LogError("❌ Unexpected status code when saving CSV data: {StatusCode}", response.StatusCode);
                    return false;
                }
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "❌ CosmosDB error saving CSV file data - StatusCode: {StatusCode}, Message: {Message}", 
                    ex.StatusCode, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error saving CSV file data to CosmosDB");
                return false;
            }
        }

        /// <summary>
        /// Get CSV file data by ID and TwinId using SQL SELECT query
        /// </summary>
        /// <param name="csvFileId">CSV file ID</param>
        /// <param name="twinId">Twin ID</param>
        /// <returns>CSV file data or null if not found</returns>
        public async Task<CSVFileData?> GetCSVFileDataAsync(string csvFileId, string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Retrieving CSV file data from CosmosDB using SELECT - ID: {CsvFileId}, TwinId: {TwinId}", 
                    csvFileId, twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_csvFilesContainerName);

                // Use SQL SELECT query with specific fields for better control and performance
                var query = new QueryDefinition(@"
                    SELECT 
                        c.id,
                        c.TwinId,
                        c.FileName,
                        c.ContainerName,
                        c.FilePath,
                        c.TotalRows,
                        c.TotalColumns,
                        c.ColumnNames,
                        c.Records,
                        c.ProcessedAt,
                        c.Success,
                        c.ErrorMessage,
                        c.DocumentType,
                        c.CreatedAt
                    FROM c 
                    WHERE c.id = @csvFileId AND c.TwinId = @twinId")
                    .WithParameter("@csvFileId", csvFileId)
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<dynamic>(query);
                
                if (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    var document = response.FirstOrDefault();
                    
                    if (document != null)
                    {
                        // Convert to CSVFileData with proper null handling
                        var csvFileData = new CSVFileData
                        {
                            Id = document.id ?? string.Empty,
                            TwinId = document.TwinId ?? string.Empty,
                            FileName = document.FileName ?? string.Empty,
                            ContainerName = document.ContainerName ?? string.Empty,
                            FilePath = document.FilePath ?? string.Empty,
                            TotalRows = (int)(document.TotalRows ?? 0),
                            TotalColumns = (int)(document.TotalColumns ?? 0),
                            ColumnNames = document.ColumnNames?.ToObject<List<string>>() ?? new List<string>(),
                            Records = document.Records?.ToObject<List<CSVRecord>>() ?? new List<CSVRecord>(),
                            ProcessedAt = document.ProcessedAt ?? DateTime.MinValue,
                            Success = document.Success ?? false,
                            ErrorMessage = document.ErrorMessage
                        };

                        _logger.LogInformation("✅ CSV file data retrieved successfully from CosmosDB using SELECT - Records: {RecordCount}", 
                            csvFileData.Records.Count);
                        return csvFileData;
                    }
                }

                _logger.LogWarning("⚠️ CSV file data not found using SELECT - ID: {CsvFileId}, TwinId: {TwinId}", csvFileId, twinId);
                return null;
            }
            catch (CosmosException ex)
            {
                _logger.LogError(ex, "❌ CosmosDB error retrieving CSV file data using SELECT - StatusCode: {StatusCode}, Message: {Message}", 
                    ex.StatusCode, ex.Message);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving CSV file data from CosmosDB using SELECT");
                return null;
            }
        }

        /// <summary>
        /// Get all CSV files for a specific Twin ID
        /// </summary>
        /// <param name="twinId">Twin ID</param>
        /// <returns>List of CSV file data</returns>
        public async Task<List<CSVFileData>> GetCSVFilesByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Retrieving all CSV files for TwinId: {TwinId}", twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_csvFilesContainerName);

                var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinId = @twinId ORDER BY c.ProcessedAt DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<dynamic>(query);
                var csvFiles = new List<CSVFileData>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var document in response)
                    {
                        var csvFileData = new CSVFileData
                        {
                            Id = document.id,
                            TwinId = document.TwinId,
                            FileName = document.FileName,
                            ContainerName = document.ContainerName,
                            FilePath = document.FilePath,
                            TotalRows = document.TotalRows,
                            TotalColumns = document.TotalColumns,
                            ColumnNames = document.ColumnNames?.ToObject<List<string>>() ?? new List<string>(),
                            Records = document.Records?.ToObject<List<CSVRecord>>() ?? new List<CSVRecord>(),
                            ProcessedAt = document.ProcessedAt,
                            Success = document.Success,
                            ErrorMessage = document.ErrorMessage
                        };

                        csvFiles.Add(csvFileData);
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} CSV files for TwinId: {TwinId}", csvFiles.Count, twinId);
                return csvFiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving CSV files from CosmosDB for TwinId: {TwinId}", twinId);
                return new List<CSVFileData>();
            }
        }

        /// <summary>
        /// Get CSV files metadata only (without Records) for faster access
        /// </summary>
        /// <param name="twinId">Twin ID</param>
        /// <returns>List of CSV file metadata without records</returns>
        public async Task<List<CSVFileMetadata>> GetCSVFilesMetadataByTwinIdAsync(string twinId)
        {
            try
            {
                _logger.LogInformation("🔍 Retrieving CSV files metadata (without records) for TwinId: {TwinId}", twinId);

                var database = _cosmosClient.GetDatabase(_databaseName);
                var container = database.GetContainer(_csvFilesContainerName);

                // Select only metadata fields, excluding the Records array for better performance
                var query = new QueryDefinition(@"
                    SELECT 
                        c.id, 
                        c.TwinId, 
                        c.FileName, 
                        c.ContainerName, 
                        c.FilePath, 
                        c.TotalRows, 
                        c.TotalColumns, 
                        c.ColumnNames, 
                        c.ProcessedAt, 
                        c.Success, 
                        c.ErrorMessage,
                        c.DocumentType,
                        c.CreatedAt
                    FROM c 
                    WHERE c.TwinId = @twinId 
                    ORDER BY c.ProcessedAt DESC")
                    .WithParameter("@twinId", twinId);

                var iterator = container.GetItemQueryIterator<dynamic>(query);
                var csvMetadata = new List<CSVFileMetadata>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var document in response)
                    {
                        var metadata = new CSVFileMetadata
                        {
                            Id = document.id,
                            TwinId = document.TwinId,
                            FileName = document.FileName,
                            ContainerName = document.ContainerName,
                            FilePath = document.FilePath,
                            TotalRows = document.TotalRows,
                            TotalColumns = document.TotalColumns,
                            ColumnNames = document.ColumnNames?.ToObject<List<string>>() ?? new List<string>(),
                            ProcessedAt = document.ProcessedAt,
                            Success = document.Success,
                            ErrorMessage = document.ErrorMessage,
                            DocumentType = document.DocumentType ?? "CSV",
                            CreatedAt = document.CreatedAt ?? document.ProcessedAt
                        };

                        csvMetadata.Add(metadata);
                    }
                }

                _logger.LogInformation("✅ Retrieved {Count} CSV files metadata for TwinId: {TwinId}", csvMetadata.Count, twinId);
                return csvMetadata;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error retrieving CSV files metadata from CosmosDB for TwinId: {TwinId}", twinId);
                return new List<CSVFileMetadata>();
            }
        }
    }

    /// <summary>
    /// CSV file metadata (without records data) for fast access
    /// </summary>
    public class CSVFileMetadata
    {
        /// <summary>
        /// Unique identifier for the CSV file record
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Twin ID (partition key)
        /// </summary>
        public string TwinId { get; set; } = string.Empty;

        /// <summary>
        /// Original file name of the CSV
        /// </summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>
        /// Container where the CSV file is stored
        /// </summary>
        public string ContainerName { get; set; } = string.Empty;

        /// <summary>
        /// Full path to the CSV file
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Total number of rows in the CSV (excluding header)
        /// </summary>
        public int TotalRows { get; set; }

        /// <summary>
        /// Total number of columns in the CSV
        /// </summary>
        public int TotalColumns { get; set; }

        /// <summary>
        /// List of column names from the CSV header
        /// </summary>
        public List<string> ColumnNames { get; set; } = new List<string>();

        /// <summary>
        /// When the CSV was processed
        /// </summary>
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// Whether the processing was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error message if processing failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Document type (always "CSV")
        /// </summary>
        public string DocumentType { get; set; } = "CSV";

        /// <summary>
        /// When the document was created in CosmosDB
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }
}
