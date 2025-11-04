using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TwinFx.Services;

namespace TwinFx.Agents
{
    public class StructuredDocumentsAgent  
    {
        private readonly ILogger<StructuredDocumentsAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;

        public StructuredDocumentsAgent(ILogger<StructuredDocumentsAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("✅ DocumentIntelligenceService initialized successfully");

                // Initialize Semantic Kernel for AI processing
                var builder = Kernel.CreateBuilder();

                // Add Azure OpenAI chat completion
                var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                var apiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                var deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("✅ Semantic Kernel initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize StructuredDocumentsAgent");
                throw;
            }
        }

        /// <summary>
        /// Analyze CSV file using Semantic Kernel for advanced analysis
        /// </summary>
        /// <param name="ContainerName">Container name (TwinId)</param>
        /// <param name="PathName">Directory path within container</param>
        /// <param name="FileName">CSV file name</param>
        /// <param name="question">Question to ask about the CSV data</param>
        /// <param name="PathID">CSV file ID from CosmosDB</param>
        /// <returns>Analysis results as HTML string</returns>
        public async Task<string> AnalyzeCSVFileUsing(string ContainerName, string PathName,
            string FileName, string question, string PathID)
        {
            _logger.LogInformation("📊 Starting CSV analysis for TwinId: {TwinId}, FileName: {FileName}, Question: {Question}", 
                ContainerName, FileName, question);

            try
            {
                // Step 1: Get CSV data from CosmosDB using PathID
                _logger.LogInformation("📊 Step 1: Retrieving CSV data from CosmosDB using PathID: {PathID}", PathID);
                
                var cosmosService = CreateStructuredDocumentsCosmosService();
                var csvFileData = await cosmosService.GetCSVFileDataAsync(PathID, ContainerName);

                if (csvFileData == null)
                {
                    var errorMsg = $"❌ CSV file not found in CosmosDB - PathID: {PathID}, TwinId: {ContainerName}";
                    _logger.LogError(errorMsg);
                    return errorMsg;
                }

                _logger.LogInformation("✅ CSV data retrieved - {TotalRows} rows, {TotalColumns} columns", 
                    csvFileData.TotalRows, csvFileData.TotalColumns);

                // Step 2: Use Semantic Kernel for analysis instead of Azure AI Agents
                return await AnalyzeCSVWithSemanticKernel(csvFileData, question);
            }
            catch (Exception ex)
            {
                var errorResult = $"❌ Error during CSV analysis: {ex.Message}";
                _logger.LogError(ex, "❌ Error during CSV analysis: {Message}", ex.Message);

                // Try fallback analysis
                try
                {
                    var cosmosService = CreateStructuredDocumentsCosmosService();
                    var csvFileData = await cosmosService.GetCSVFileDataAsync(PathID, ContainerName);
                    
                    if (csvFileData != null)
                    {
                        var fallbackResult = await ProvideFallbackAnalysis(csvFileData, question);
                        return fallbackResult;
                    }
                }
                catch (Exception fallbackEx)
                {
                    _logger.LogError(fallbackEx, "❌ Fallback analysis failed: {Message}", fallbackEx.Message);
                    return $"❌ Fallback analysis failed: {fallbackEx.Message}";
                }

                return errorResult;
            }
        }

        /// <summary>
        /// Analyze CSV data using Semantic Kernel
        /// </summary>
        private async Task<string> AnalyzeCSVWithSemanticKernel(CSVFileData csvFileData, string question)
        {
            try
            {
                _logger.LogInformation("🤖 Analyzing CSV with Semantic Kernel");

                var chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
                var chatHistory = new ChatHistory();

                // Create detailed CSV context
                var csvContext = CreateCSVContextForAnalysis(csvFileData);
                
                var prompt = $"""
                Eres un analista de datos experto especializado en análisis de archivos CSV.
                
                📊 **INFORMACIÓN DEL ARCHIVO CSV:**
                {csvContext}
                
                🎯 **PREGUNTA DEL USUARIO:** {question}
                
                **INSTRUCCIONES:**
                1. Analiza los datos CSV proporcionados y responde específicamente a la pregunta del usuario
                2. Proporciona insights basados en los datos reales
                3. Usa formato HTML con colores y estilos para una presentación profesional
                4. Incluye tablas, gráficos conceptuales y estadísticas relevantes
                5. Si la pregunta requiere visualizaciones, describe qué gráficos serían apropiados
                6. Mantén un tono profesional y educativo
                
                **FORMATO DE RESPUESTA:**
                - Usa HTML con estilos CSS inline
                - Incluye emojis relevantes para mejorar la presentación
                - Crea secciones claras con títulos
                - Destaca datos importantes con colores y negritas
                - Si hay números, presenta estadísticas descriptivas
                
                Responde directamente con el análisis HTML profesional.
                """;
                
                chatHistory.AddUserMessage(prompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_tokens"] = 4000,
                        ["temperature"] = 0.3
                    }
                };

                var response = await chatCompletionService.GetChatMessageContentAsync(
                    chatHistory,
                    executionSettings,
                    _kernel);

                var analysisResult = response.Content ?? "No se pudo generar el análisis.";
                
                _logger.LogInformation("✅ CSV analysis completed with Semantic Kernel");
                return analysisResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in Semantic Kernel CSV analysis");
                return await ProvideFallbackAnalysis(csvFileData, question);
            }
        }

        /// <summary>
        /// Create detailed CSV context for analysis
        /// </summary>
        private string CreateCSVContextForAnalysis(CSVFileData csvFileData)
        {
            var context = new StringBuilder();
            
            context.AppendLine($"**Archivo:** {csvFileData.FileName}");
            context.AppendLine($"**Total de filas:** {csvFileData.TotalRows:N0}");
            context.AppendLine($"**Total de columnas:** {csvFileData.TotalColumns}");
            context.AppendLine($"**Procesado:** {csvFileData.ProcessedAt:yyyy-MM-dd HH:mm}");
            context.AppendLine();
            
            context.AppendLine("**Columnas disponibles:**");
            foreach (var column in csvFileData.ColumnNames)
            {
                context.AppendLine($"- {column}");
            }
            context.AppendLine();
            
            if (csvFileData.Records.Any())
            {
                context.AppendLine("**Muestra de datos (primeras 3 filas):**");
                
                var sampleRecords = csvFileData.Records.Take(3);
                foreach (var record in sampleRecords)
                {
                    context.AppendLine("---");
                    foreach (var column in csvFileData.ColumnNames.Take(10)) // Limitar a 10 columnas para evitar texto muy largo
                    {
                        var value = record.Data.ContainsKey(column) ? record.Data[column] : "";
                        if (!string.IsNullOrEmpty(value))
                        {
                            context.AppendLine($"{column}: {value}");
                        }
                    }
                }
            }
            
            return context.ToString();
        }

        /// <summary>
        /// Provide fallback analysis when advanced analysis is not available
        /// </summary>
        private async Task<string> ProvideFallbackAnalysis(CSVFileData csvFileData, string question)
        {
            _logger.LogInformation("🔄 Providing fallback CSV analysis");

            await Task.Delay(10); // Simulate async processing

            try
            {
                var fallbackResult = new StringBuilder();
                
                fallbackResult.AppendLine("<div style='font-family: Arial, sans-serif; line-height: 1.6; background: #f8f9fa; padding: 20px; border-radius: 10px;'>");
                fallbackResult.AppendLine("<h2 style='color: #2E86C1; margin: 0 0 20px 0;'>📊 Análisis Básico de CSV</h2>");
                
                fallbackResult.AppendLine("<div style='background: white; padding: 15px; border-radius: 8px; margin: 10px 0; border-left: 4px solid #3498db;'>");
                fallbackResult.AppendLine("<h3 style='color: #2c3e50; margin: 0 0 10px 0;'>📋 Información del Dataset</h3>");
                fallbackResult.AppendLine($"<p><strong>Archivo:</strong> {csvFileData.FileName}</p>");
                fallbackResult.AppendLine($"<p><strong>Total de filas:</strong> {csvFileData.TotalRows:N0}</p>");
                fallbackResult.AppendLine($"<p><strong>Total de columnas:</strong> {csvFileData.TotalColumns}</p>");
                fallbackResult.AppendLine($"<p><strong>Procesado:</strong> {csvFileData.ProcessedAt:yyyy-MM-dd HH:mm}</p>");
                fallbackResult.AppendLine("</div>");

                fallbackResult.AppendLine("<div style='background: white; padding: 15px; border-radius: 8px; margin: 10px 0; border-left: 4px solid #e74c3c;'>");
                fallbackResult.AppendLine("<h3 style='color: #2c3e50; margin: 0 0 10px 0;'>📄 Columnas Disponibles</h3>");
                fallbackResult.AppendLine("<ul style='margin: 0; padding-left: 20px;'>");
                foreach (var column in csvFileData.ColumnNames.Take(20))
                {
                    fallbackResult.AppendLine($"<li>{column}</li>");
                }
                if (csvFileData.ColumnNames.Count > 20)
                {
                    fallbackResult.AppendLine($"<li><em>... y {csvFileData.ColumnNames.Count - 20} columnas más</em></li>");
                }
                fallbackResult.AppendLine("</ul>");
                fallbackResult.AppendLine("</div>");

                // Sample data
                if (csvFileData.Records.Any())
                {
                    fallbackResult.AppendLine("<div style='background: white; padding: 15px; border-radius: 8px; margin: 10px 0; border-left: 4px solid #f39c12;'>");
                    fallbackResult.AppendLine("<h3 style='color: #2c3e50; margin: 0 0 10px 0;'>📝 Muestra de Datos</h3>");
                    
                    var firstRecord = csvFileData.Records.First();
                    fallbackResult.AppendLine("<table style='width: 100%; border-collapse: collapse;'>");
                    fallbackResult.AppendLine("<tr style='background: #ecf0f1;'><th style='border: 1px solid #bdc3c7; padding: 8px; text-align: left;'>Campo</th><th style='border: 1px solid #bdc3c7; padding: 8px; text-align: left;'>Valor</th></tr>");
                    
                    foreach (var column in csvFileData.ColumnNames.Take(10))
                    {
                        var value = firstRecord.Data.ContainsKey(column) ? firstRecord.Data[column] : "";
                        if (!string.IsNullOrEmpty(value))
                        {
                            fallbackResult.AppendLine($"<tr><td style='border: 1px solid #bdc3c7; padding: 8px; font-weight: bold;'>{column}</td><td style='border: 1px solid #bdc3c7; padding: 8px;'>{value}</td></tr>");
                        }
                    }
                    fallbackResult.AppendLine("</table>");
                    fallbackResult.AppendLine("</div>");
                }

                // Question and recommendations
                fallbackResult.AppendLine("<div style='background: white; padding: 15px; border-radius: 8px; margin: 10px 0; border-left: 4px solid #27ae60;'>");
                fallbackResult.AppendLine("<h3 style='color: #2c3e50; margin: 0 0 10px 0;'>❓ Pregunta Analizada</h3>");
                fallbackResult.AppendLine($"<p><strong>Su pregunta:</strong> {question}</p>");
                fallbackResult.AppendLine("<p><strong>Análisis básico:</strong> Para un análisis completo de esta pregunta, se requiere procesamiento avanzado con herramientas de análisis de datos.</p>");
                fallbackResult.AppendLine("</div>");

                fallbackResult.AppendLine("<div style='background: #fff3cd; padding: 15px; border-radius: 8px; margin: 10px 0; border: 1px solid #ffeaa7;'>");
                fallbackResult.AppendLine("<h4 style='color: #856404; margin: 0 0 10px 0;'>⚠️ Limitaciones del Análisis Básico</h4>");
                fallbackResult.AppendLine("<p style='margin: 0; color: #856404;'>Este es un análisis básico de metadatos. Para cálculos, visualizaciones y análisis estadísticos avanzados, se recomienda usar herramientas especializadas de análisis de datos o Azure AI con Code Interpreter.</p>");
                fallbackResult.AppendLine("</div>");
                
                fallbackResult.AppendLine("</div>");

                return fallbackResult.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in fallback analysis: {Message}", ex.Message);
                return $"<div style='color: red; font-family: Arial, sans-serif;'><h3>❌ Error en análisis básico</h3><p>{ex.Message}</p></div>";
            }
        }

        public async Task<ProcessAiDocumentsResult> ProcessAiCSVDocuments(string containerName, string filePath, string fileName, string documentType = "CSV", string? educationId = null)
        {
            _logger.LogInformation("🚀 Starting ProcessAiCSVDocuments for {DocumentType} document: {FileName}", documentType, fileName);

            try
            {
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync($"{filePath}/{fileName}", TimeSpan.FromHours(1));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    _logger.LogError("❌ Failed to generate SAS URL for CSV file");
                    return new ProcessAiDocumentsResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to generate SAS URL for CSV file",
                        ContainerName = containerName,
                        FilePath = filePath,
                        FileName = fileName,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                var csvResult = await ExtractRecordsFromCSV(sasUrl);
                
                if (!csvResult.Success)
                {
                    _logger.LogError("❌ Failed to extract CSV data: {ErrorMessage}", csvResult.ErrorMessage);
                    return new ProcessAiDocumentsResult
                    {
                        Success = false,
                        ErrorMessage = $"CSV extraction failed: {csvResult.ErrorMessage}",
                        ContainerName = containerName,
                        FilePath = filePath,
                        FileName = fileName,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                var csvFileData = new CSVFileData
                {
                    Id = Guid.NewGuid().ToString(),
                    TwinId = containerName,
                    FileName = csvResult.NombreArchivo,
                    ContainerName = containerName,
                    FilePath = $"{filePath}/{fileName}",
                    TotalRows = csvResult.TotalRecords,
                    TotalColumns = csvResult.TotalColumns,
                    ColumnNames = csvResult.ColumnNames,
                    Records = csvResult.Records,
                    ProcessedAt = DateTime.UtcNow,
                    Success = true
                };

                var cosmosService = CreateStructuredDocumentsCosmosService();
                var saveResult = await cosmosService.SaveCSVFileDataAsync(csvFileData);
                
                if (!saveResult)
                {
                    _logger.LogError("❌ Failed to save CSV data to CosmosDB");
                    return new ProcessAiDocumentsResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to save CSV data to CosmosDB",
                        ContainerName = containerName,
                        FilePath = filePath,
                        FileName = fileName,
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                return new ProcessAiDocumentsResult
                {
                    Success = true,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedText = $"CSV file processed successfully - {csvResult.TotalRecords} records, {csvResult.TotalColumns} columns",
                    ProcessedAt = DateTime.UtcNow,
                    ExtractionResult = null,
                    AgentResult = null,
                    InvoiceAnalysisResult = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing CSV document {FileName}", fileName);
                
                return new ProcessAiDocumentsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        private StructuredDocumentsCosmosDB CreateStructuredDocumentsCosmosService()
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var cosmosLogger = loggerFactory.CreateLogger<StructuredDocumentsCosmosDB>();
            return new StructuredDocumentsCosmosDB(cosmosLogger, _configuration);
        }

        public async Task<CSVExtractionResult> ExtractRecordsFromCSV(string SASURL)
        {
            _logger.LogInformation("📊 Starting CSV extraction from SAS URL: {SASURL}", SASURL.Substring(0, Math.Min(100, SASURL.Length)) + "...");

            try
            {
                var urlInfo = ParseSasUrlForDataLake(SASURL);
                if (!urlInfo.Success)
                {
                    _logger.LogError("❌ Failed to parse SAS URL: {Error}", urlInfo.ErrorMessage);
                    return new CSVExtractionResult
                    {
                        Success = false,
                        ErrorMessage = $"Invalid SAS URL format: {urlInfo.ErrorMessage}"
                    };
                }

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(urlInfo.TwinId);
                var fileBytes = await dataLakeClient.DownloadFileAsync(urlInfo.FilePath);
                
                if (fileBytes == null)
                {
                    return new CSVExtractionResult
                    {
                        Success = false,
                        ErrorMessage = "File not found in DataLake or access denied"
                    };
                }

                using var csvStream = new MemoryStream(fileBytes);
                using var reader = new StreamReader(csvStream, Encoding.UTF8);
                
                var records = new List<CSVRecord>();
                var columnNames = new List<string>();
                int totalRecords = 0;
                int totalColumns = 0;

                string? line;
                bool isFirstLine = true;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var columns = ParseCSVLine(line);

                    if (isFirstLine)
                    {
                        columnNames = columns;
                        totalColumns = columns.Count;
                        isFirstLine = false;
                    }
                    else
                    {
                        var record = new CSVRecord();
                        for (int i = 0; i < Math.Min(columns.Count, columnNames.Count); i++)
                        {
                            record.Data[columnNames[i]] = columns[i];
                        }
                        records.Add(record);
                        totalRecords++;
                    }
                }

                return new CSVExtractionResult
                {
                    Success = true,
                    NombreArchivo = urlInfo.FileName,
                    TotalRecords = totalRecords,
                    TotalColumns = totalColumns,
                    ColumnNames = columnNames,
                    Records = records
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting records from CSV");
                return new CSVExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private List<string> ParseCSVLine(string line)
        {
            var columns = new List<string>();
            var currentColumn = new StringBuilder();
            bool inQuotes = false;
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        currentColumn.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    columns.Add(currentColumn.ToString().Trim());
                    currentColumn.Clear();
                }
                else
                {
                    currentColumn.Append(c);
                }
            }
            
            columns.Add(currentColumn.ToString().Trim());
            return columns;
        }

        public async Task<ProcessAiDocumentsResult> ProcessAiStructuredDocuments(string containerName, string filePath, string fileName, string documentType = "Structured", string? educationId = null)
        {
            _logger.LogInformation("🚀 Starting ProcessAiDocuments for {DocumentType} document: {FileName}", documentType, fileName);

            try
            {
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync($"{filePath}/{fileName}", TimeSpan.FromHours(1));

                string processedText = "";
                InvoiceExtractionResult? invoiceAnalysisResult = null;

                if (!string.IsNullOrEmpty(sasUrl))
                {
                    var generalResult = await _documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
                    if (generalResult.Success)
                    {
                        processedText = generalResult.TextContent;
                        _logger.LogInformation("✅ General document extraction completed successfully");
                    }
                }

                if (!string.IsNullOrEmpty(processedText))
                {
                    if (documentType.ToUpperInvariant() == "INVOICE" || documentType.ToUpperInvariant() == "FACTURA")
                    {
                        invoiceAnalysisResult = await ProcessInvoiceWithAI(processedText, null);
                        _logger.LogInformation("✅ AI invoice analysis completed");
                    }
                }

                return new ProcessAiDocumentsResult
                {
                    Success = true,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedText = processedText,
                    ProcessedAt = DateTime.UtcNow,
                    ExtractionResult = null,
                    AgentResult = null,
                    InvoiceAnalysisResult = invoiceAnalysisResult
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing document {FileName} of type {DocumentType}", fileName, documentType);

                return new ProcessAiDocumentsResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Process invoice with AI (placeholder implementation)
        /// </summary>
        private async Task<InvoiceExtractionResult> ProcessInvoiceWithAI(string processedText, InvoiceAnalysisResult? extractionResult)
        {
            _logger.LogInformation("🤖 Processing invoice with AI...");
            
            await Task.Delay(100);
            
            return new InvoiceExtractionResult
            {
                Success = true,
                TextSummary = "Invoice processed successfully",
                HtmlOutput = "<div>Invoice analysis completed</div>",
                StructuredData = new Dictionary<string, object>(),
                TextReport = processedText,
                TablesContent = "No tables processed",
                RawResponse = "{}",
                Metadata = new DocumentMetadataInvoices
                {
                    Timestamp = DateTime.UtcNow,
                    InputLength = processedText.Length,
                    OutputLength = 0,
                    AgentModel = "StructuredDocumentsAgent",
                    AnalysisType = "Invoice",
                    ExtractionSchemaUsed = false
                }
            };
        }

        private SasUrlInfo ParseSasUrlForDataLake(string sasUrl)
        {
            try
            {
                var uri = new Uri(sasUrl);
                var pathSegments = uri.AbsolutePath.Trim('/').Split('/');
                
                if (pathSegments.Length < 2)
                {
                    return new SasUrlInfo 
                    { 
                        Success = false, 
                        ErrorMessage = "Invalid URL path structure" 
                    };
                }

                var twinId = Uri.UnescapeDataString(pathSegments[0]);
                var remainingSegments = pathSegments.Skip(1).ToArray();
                var filePath = string.Join("/", remainingSegments);
                var fileName = remainingSegments[remainingSegments.Length - 1];

                return new SasUrlInfo
                {
                    Success = true,
                    TwinId = twinId,
                    FilePath = filePath,
                    FileName = fileName
                };
            }
            catch (Exception ex)
            {
                return new SasUrlInfo 
                { 
                    Success = false, 
                    ErrorMessage = $"Failed to parse URL: {ex.Message}" 
                };
            }
        }
    }

    /// <summary>
    /// Result of CSV extraction operation
    /// </summary>
    public class CSVExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string NombreArchivo { get; set; } = string.Empty;
        public int TotalRecords { get; set; }
        public int TotalColumns { get; set; }
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<CSVRecord> Records { get; set; } = new List<CSVRecord>();
    }

    /// <summary>
    /// Represents a single record from CSV with dynamic columns
    /// </summary>
    public class CSVRecord
    {
        public Dictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Information extracted from SAS URL for DataLake access
    /// </summary>
    public class SasUrlInfo
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TwinId { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// CSV file data to be saved in CosmosDB TwinCSVFiles container
    /// </summary>
    public class CSVFileData
    {
        public string Id { get; set; } = string.Empty;
        public string TwinId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int TotalRows { get; set; }
        public int TotalColumns { get; set; }
        public List<string> ColumnNames { get; set; } = new List<string>();
        public List<CSVRecord> Records { get; set; } = new List<CSVRecord>();
        public DateTime ProcessedAt { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }
}