using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;

namespace TwinFx.Agents
{
    public class ProcessDocumentDataAgent
    {
        private readonly ILogger<ProcessDocumentDataAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;

        public ProcessDocumentDataAgent(ILogger<ProcessDocumentDataAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("? DocumentIntelligenceService initialized successfully");

                // Initialize Semantic Kernel for AI processing
                var builder = Kernel.CreateBuilder();
                
                // Add Azure OpenAI chat completion
                var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                var apiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                var deploymentName = configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4";

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("? Semantic Kernel initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to initialize ProcessDocumentDataAgent");
                throw;
            }
        }

        public async Task<ProcessAiDocumentsResult> ProcessAiDocuments(string containerName, string filePath, string fileName, string documentType = "Invoice", string? educationId = null)
        {
            _logger.LogInformation("?? Starting ProcessAiDocuments for {DocumentType} document: {FileName}", documentType, fileName);

            if (documentType.ToUpperInvariant() == "EDUCATION" && !string.IsNullOrEmpty(educationId))
            {
                _logger.LogInformation("?? Processing Education document for education record: {EducationId}", educationId);
            }

            try
            {
                // Step 1: Extract data using Document Intelligence
                _logger.LogInformation("?? Step 1: Extracting data with Document Intelligence...");
                
                InvoiceAnalysisResult? extractionResult = null;
                EducationAnalysisResult? educationResult = null;
                DocumentAnalysisResult? generalResult = null;
                string processedText = "";

                switch (documentType.ToUpperInvariant())
                {
                    case "INVOICE":
                    case "FACTURA":
                        extractionResult = await _documentIntelligenceService.ExtractInvoiceDataAsync(containerName, filePath, fileName);
                        if (extractionResult.Success)
                        {
                            processedText = BuildInvoiceText(extractionResult);
                            _logger.LogInformation("? Invoice extraction completed successfully");
                        }
                        break;

                    case "EDUCATION":
                        educationResult = await _documentIntelligenceService.ExtractEducationDataAsync(containerName, filePath, fileName);
                        if (educationResult.Success)
                        {
                            processedText = educationResult.TextContent;
                            _logger.LogInformation("? Education extraction completed successfully");
                            
                            // If we have an educationId, save the analysis to that education record
                            if (!string.IsNullOrEmpty(educationId))
                            {
                                _logger.LogInformation("?? Saving education analysis to record: {EducationId}", educationId);
                                try
                                {
                                    var cosmosLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                                    var cosmosLogger = cosmosLoggerFactory.CreateLogger<CosmosDbTwinProfileService>();
                                    var cosmosService = new CosmosDbTwinProfileService(cosmosLogger, _configuration);
                                    
                                    var saved = await cosmosService.SaveEducationAnalysisAsync(educationResult, educationId, containerName, fileName);
                                    if (saved)
                                    {
                                        _logger.LogInformation("? Education analysis saved to Cosmos DB successfully");
                                    }
                                    else
                                    {
                                        _logger.LogWarning("?? Failed to save education analysis to Cosmos DB");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "? Error saving education analysis to Cosmos DB");
                                }
                            }
                        }
                        break;

                    default:
                        // Use general document analysis
                        var dataLakeFactory = new DataLakeClientFactory(LoggerFactory.Create(builder => builder.AddConsole()), _configuration);
                        var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync($"{filePath}/{fileName}", TimeSpan.FromHours(1));
                        
                        if (!string.IsNullOrEmpty(sasUrl))
                        {
                            generalResult = await _documentIntelligenceService.AnalyzeDocumentAsync(new Uri(sasUrl));
                            if (generalResult.Success)
                            {
                                processedText = generalResult.TextContent;
                                _logger.LogInformation("? General document extraction completed successfully");
                            }
                        }
                        break;
                }

                // Step 2: Process with AI for enhanced analysis
                _logger.LogInformation("?? Step 2: Processing with AI for enhanced analysis...");
                
                DocumentExtractionResult? agentResult = null;
                InvoiceExtractionResult? invoiceAnalysisResult = null;

                if (!string.IsNullOrEmpty(processedText))
                {
                    if (documentType.ToUpperInvariant() == "INVOICE" || documentType.ToUpperInvariant() == "FACTURA")
                    {
                        invoiceAnalysisResult = await ProcessInvoiceWithAI(processedText, extractionResult);
                        _logger.LogInformation("? AI invoice analysis completed");
                    }
                    else
                    {
                        agentResult = await ProcessDocumentWithAI(processedText, documentType, educationId);
                        _logger.LogInformation("? AI document analysis completed");
                    }
                }

                // Build comprehensive result
                var result = new ProcessAiDocumentsResult
                {
                    Success = true,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedText = processedText,
                    ProcessedAt = DateTime.UtcNow,
                    ExtractionResult = extractionResult,
                    AgentResult = agentResult,
                    InvoiceAnalysisResult = invoiceAnalysisResult
                };

                _logger.LogInformation("? ProcessAiDocuments completed successfully for {DocumentType}: {FileName}", documentType, fileName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error processing document {FileName} of type {DocumentType}", fileName, documentType);
                
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

        private async Task<InvoiceExtractionResult> ProcessInvoiceWithAI(string extractedText, InvoiceAnalysisResult? extractionResult)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var prompt = $@"
Analiza esta factura y proporciona un resumen ejecutivo detallado en espa�ol.

DATOS EXTRA�DOS:
{extractedText}

DATOS ESTRUCTURADOS:
{(extractionResult != null ? JsonSerializer.Serialize(extractionResult.InvoiceData, new JsonSerializerOptions { WriteIndented = true }) : "No disponible")}

TABLAS:
{(extractionResult?.Tables != null ? DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables) : "No hay tablas")}

Proporciona:
1. Resumen ejecutivo en HTML
2. Resumen en texto plano
3. An�lisis de l�neas de productos
4. Identificaci�n de impuestos y totales
5. Recomendaciones o alertas si hay discrepancias

Formato de respuesta en JSON:
{{
    ""textSummary"": ""resumen en texto plano"",
    ""htmlOutput"": ""<div>resumen ejecutivo en HTML</div>"",
    ""structuredData"": {{
        ""vendorAnalysis"": ""an�lisis del proveedor"",
        ""customerAnalysis"": ""an�lisis del cliente"",
        ""financialSummary"": ""resumen financiero"",
        ""lineItemsAnalysis"": ""an�lisis de productos/servicios"",
        ""taxAnalysis"": ""an�lisis de impuestos"",
        ""recommendations"": ""recomendaciones""
    }},
    ""textReport"": ""reporte detallado en texto"",
    ""tablesContent"": ""an�lisis de tablas encontradas""
}}";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Try to parse AI response
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse);

                return new InvoiceExtractionResult
                {
                    Success = true,
                    TextSummary = aiData?.GetValueOrDefault("textSummary")?.ToString() ?? "",
                    HtmlOutput = aiData?.GetValueOrDefault("htmlOutput")?.ToString() ?? "",
                    StructuredData = aiData?.GetValueOrDefault("structuredData") as Dictionary<string, object> ?? new(),
                    TextReport = aiData?.GetValueOrDefault("textReport")?.ToString() ?? "",
                    TablesContent = aiData?.GetValueOrDefault("tablesContent")?.ToString() ?? "",
                    RawResponse = aiResponse,
                    Metadata = new DocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = extractedText.Length,
                        OutputLength = aiResponse.Length,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "Invoice",
                        ExtractionSchemaUsed = true
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in AI invoice processing");
                return new InvoiceExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Metadata = new DocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        ErrorDetails = ex.Message
                    }
                };
            }
        }

        private async Task<DocumentExtractionResult> ProcessDocumentWithAI(string extractedText, string documentType, string? educationId = null)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var educationContext = !string.IsNullOrEmpty(educationId) && documentType.ToUpperInvariant() == "EDUCATION" 
                    ? $"\n\nIMPORTANTE: Este documento debe actualizarse en el registro de educaci�n con ID: {educationId}"
                    : "";

                var prompt = $@"
Analiza este documento de tipo '{documentType}' y extrae informaci�n estructurada relevante.

CONTENIDO DEL DOCUMENTO:
{extractedText}
{educationContext}

Proporciona un an�lisis completo incluyendo:
1. Tipo de documento identificado
2. Informaci�n clave extra�da
3. Datos estructurados relevantes
4. Resumen ejecutivo
5. Metadatos importantes

Formato de respuesta en JSON:
{{
    ""documentType"": ""{documentType}"",
    ""extractedData"": {{
        ""mainEntities"": ""entidades principales encontradas"",
        ""keyInformation"": ""informaci�n clave"",
        ""dates"": ""fechas importantes"",
        ""amounts"": ""cantidades o valores"",
        ""names"": ""nombres de personas/organizaciones"",
        ""addresses"": ""direcciones"",
        ""other"": ""otra informaci�n relevante""
    }},
    ""summary"": ""resumen ejecutivo del documento"",
    ""confidence"": ""nivel de confianza del an�lisis"",
    ""recommendations"": ""recomendaciones para el procesamiento""
}}";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Try to parse AI response
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse);

                return new DocumentExtractionResult
                {
                    Success = true,
                    ExtractedData = aiData?.GetValueOrDefault("extractedData") as Dictionary<string, object> ?? new(),
                    RawResponse = aiResponse,
                    Metadata = new DocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = extractedText.Length,
                        OutputLength = aiResponse.Length,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = documentType,
                        ExtractionSchemaUsed = true
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in AI document processing");
                return new DocumentExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Metadata = new DocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        ErrorDetails = ex.Message
                    }
                };
            }
        }

        private string BuildInvoiceText(InvoiceAnalysisResult extractionResult)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("=== INVOICE ANALYSIS RESULT ===");
            sb.AppendLine($"Processed At: {extractionResult.ProcessedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Pages: {extractionResult.TotalPages}");
            sb.AppendLine();

            var invoice = extractionResult.InvoiceData;
            
            sb.AppendLine("VENDOR INFORMATION:");
            sb.AppendLine($"  Name: {invoice.VendorName} (Confidence: {invoice.VendorNameConfidence:P1})");
            sb.AppendLine($"  Address: {invoice.VendorAddress}");
            sb.AppendLine();

            sb.AppendLine("CUSTOMER INFORMATION:");
            sb.AppendLine($"  Name: {invoice.CustomerName} (Confidence: {invoice.CustomerNameConfidence:P1})");
            sb.AppendLine($"  Address: {invoice.CustomerAddress}");
            sb.AppendLine();

            sb.AppendLine("INVOICE DETAILS:");
            sb.AppendLine($"  Number: {invoice.InvoiceNumber}");
            sb.AppendLine($"  Date: {invoice.InvoiceDate:yyyy-MM-dd}");
            sb.AppendLine($"  Due Date: {invoice.DueDate:yyyy-MM-dd}");
            sb.AppendLine();

            sb.AppendLine("FINANCIAL SUMMARY:");
            sb.AppendLine($"  Subtotal: ${invoice.SubTotal:F2} (Confidence: {invoice.SubTotalConfidence:P1})");
            sb.AppendLine($"  Total Tax: ${invoice.TotalTax:F2}");
            sb.AppendLine($"  Total: ${invoice.InvoiceTotal:F2} (Confidence: {invoice.InvoiceTotalConfidence:P1})");
            sb.AppendLine();

            if (invoice.LineItems.Count > 0)
            {
                sb.AppendLine($"LINE ITEMS ({invoice.LineItems.Count}):");
                foreach (var item in invoice.LineItems)
                {
                    sb.AppendLine($"  � {item.Description} - Qty: {item.Quantity}, Unit: ${item.UnitPrice:F2}, Amount: ${item.Amount:F2}");
                }
                sb.AppendLine();
            }

            if (extractionResult.Tables.Count > 0)
            {
                sb.AppendLine("TABLES FOUND:");
                sb.AppendLine(DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables));
            }

            return sb.ToString();
        }
    }

    public class ProcessAiDocumentsResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public InvoiceAnalysisResult? ExtractionResult { get; set; }
        public DocumentExtractionResult? AgentResult { get; set; }
        public InvoiceExtractionResult? InvoiceAnalysisResult { get; set; }
        public string ProcessedText { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }

        /// <summary>
        /// Get the full path of the document
        /// </summary>
        public string FullPath => $"{ContainerName}/{FilePath}/{FileName}";

        /// <summary>
        /// Get a comprehensive summary of all processing results
        /// </summary>
        public string GetComprehensiveSummary()
        {
            if (!Success)
            {
                return $"? Processing failed: {ErrorMessage}";
            }

            return $"? Successfully processed: {FileName}\n?? Location: {FullPath}\n?? Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC\n?? Text Length: {ProcessedText.Length} characters";
        }

        /// <summary>
        /// Get key insights from the document processing
        /// </summary>
        public Dictionary<string, object> GetKeyInsights()
        {
            var insights = new Dictionary<string, object>
            {
                ["success"] = Success,
                ["fileName"] = FileName,
                ["fullPath"] = FullPath,
                ["processedAt"] = ProcessedAt,
                ["textLength"] = ProcessedText.Length
            };

            if (ExtractionResult != null)
            {
                insights["documentIntelligence"] = new
                {
                    success = ExtractionResult.Success,
                    totalPages = ExtractionResult.TotalPages
                };
            }

            return insights;
        }
    }

    public class DocumentExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object>? ExtractedData { get; set; }
        public string? RawResponse { get; set; }
        public DocumentMetadata? Metadata { get; set; }
    }

    public class InvoiceExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string TextSummary { get; set; } = string.Empty;
        public string HtmlOutput { get; set; } = string.Empty;
        public Dictionary<string, object> StructuredData { get; set; } = new();
        public string TextReport { get; set; } = string.Empty;
        public string TablesContent { get; set; } = string.Empty;
        public string? RawResponse { get; set; }
        public DocumentMetadata? Metadata { get; set; }
    }

    public class DocumentMetadata
    {
        public DateTime Timestamp { get; set; }
        public int InputLength { get; set; }
        public int OutputLength { get; set; }
        public string AgentModel { get; set; } = "Azure OpenAI";
        public bool ExtractionSchemaUsed { get; set; }
        public string? AnalysisType { get; set; }
        public string? ErrorDetails { get; set; }
    }
}