using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TwinFx.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;

namespace TwinFx.Agents
{
    /// <summary>
    /// AI Agent specialized in processing travel documents
    /// ====================================================
    /// 
    /// Processes travel-related documents including:
    /// - Travel receipts and invoices
    /// - Hotel bookings and confirmations
    /// - Flight tickets and boarding passes
    /// - Restaurant bills and meal receipts
    /// - Transportation tickets
    /// - Travel insurance documents
    /// - Activity and attraction tickets
    /// - Car rental agreements
    /// - Travel itineraries
    /// 
    /// Features:
    /// - Document Intelligence integration
    /// - AI-powered content extraction
    /// - Travel-specific data structuring
    /// - Expense categorization for travel
    /// - HTML visualization of travel documents
    /// - Integration with travel records
    /// 
    /// Author: TwinFx Project
    /// Date: January 17, 2025
    /// </summary>
    public class TravelAgentAI
    {
        private readonly ILogger<TravelAgentAI> _logger;
        private readonly IConfiguration _configuration;
        private readonly DocumentIntelligenceService _documentIntelligenceService;
        private readonly Kernel _kernel;

        public TravelAgentAI(ILogger<TravelAgentAI> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Document Intelligence Service
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                _documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, configuration);
                _logger.LogInformation("? DocumentIntelligenceService initialized successfully for travel processing");

                // Initialize Semantic Kernel for AI processing
                var builder = Kernel.CreateBuilder();
                
                // Add Azure OpenAI chat completion
                var endpoint = configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                              configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? 
                              throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                
                var apiKey = configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                            configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? 
                            throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                
                var deploymentName = configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                                    configuration.GetValue<string>("AzureOpenAI:DeploymentName") ?? 
                                    "gpt4mini";

                builder.AddAzureOpenAIChatCompletion(deploymentName, endpoint, apiKey);
                _kernel = builder.Build();
                _logger.LogInformation("? Semantic Kernel initialized successfully for travel AI processing");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Failed to initialize TravelAgentAI");
                throw;
            }
        }

        /// <summary>
        /// Process travel documents using Document Intelligence and AI
        /// </summary>
        /// <param name="containerName">Container name where the document is stored</param>
        /// <param name="filePath">Path to the document within the container</param>
        /// <param name="fileName">Original file name for reference</param>
        /// <param name="travelId">Optional travel ID to associate the document with</param>
        /// <param name="itineraryId">Optional itinerary ID to associate the document with</param>
        /// <param name="activityId">Optional activity ID to associate the document with</param>
        /// <returns>Processed travel document result</returns>
        public async Task<ProcessTravelDocumentResult> ProcessTravelDocument(
            string containerName, 
            string filePath, 
            string fileName, 
            string? travelId = null, 
            string? itineraryId = null, 
            string? activityId = null)
        {
            _logger.LogInformation("?? Starting ProcessTravelDocument for: {FileName}", fileName);
            _logger.LogInformation("?? Travel Context - TravelId: {TravelId}, ItineraryId: {ItineraryId}, ActivityId: {ActivityId}", 
                travelId, itineraryId, activityId);

            try
            {
                // Step 1: Extract data using Document Intelligence
                _logger.LogInformation("?? Step 1: Extracting travel document data with Document Intelligence...");
                
                string processedText = "";
                InvoiceAnalysisResult? extractionResult = null;

                // Extract document data using invoice model (works well for receipts and travel documents)
                extractionResult = await _documentIntelligenceService.ExtractInvoiceDataAsync(containerName, filePath, fileName);
                if (extractionResult.Success)
                {
                    processedText = BuildTravelDocumentText(extractionResult);
                    _logger.LogInformation("? Travel document extraction completed successfully");
                }
                else
                {
                    _logger.LogWarning("?? Document Intelligence extraction failed, trying general analysis");
                    
                    // Fallback to general document analysis
                    var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(builder => builder.AddConsole()));
                    var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync($"{filePath}/{fileName}", TimeSpan.FromHours(1));
                    
                    if (!string.IsNullOrEmpty(sasUrl))
                    {
                        var generalResult = await _documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);
                        if (generalResult.Success)
                        {
                            processedText = generalResult.TextContent;
                            _logger.LogInformation("? General document extraction completed successfully");
                        }
                    }
                }

                // Step 2: Process with AI for travel-specific analysis
                _logger.LogInformation("?? Step 2: Processing with AI for travel-specific analysis...");
                
                TravelDocumentExtractionResult? travelAnalysisResult = null;
                if (!string.IsNullOrEmpty(processedText))
                {
                    travelAnalysisResult = await ProcessTravelDocumentWithAI(processedText, extractionResult, travelId, itineraryId, activityId);
                    _logger.LogInformation("? AI travel analysis completed");
                }

                // Step 3: Save to Cosmos DB if travel context is provided
                if (!string.IsNullOrEmpty(travelId) && travelAnalysisResult?.Success == true)
                {
                    _logger.LogInformation("?? Saving travel document analysis to Cosmos DB...");
                    try
                    {
                        var cosmosLoggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var cosmosLogger = cosmosLoggerFactory.CreateLogger<CosmosDbService>();
                        var cosmosService = _configuration.CreateCosmosService(cosmosLogger);

                        // Create travel document record
                        var travelDocument = new Models.TravelDocument
                        {
                            Id = Guid.NewGuid().ToString(),
                            TwinId = containerName,
                            FileName = fileName,
                            FilePath = $"{filePath}/{fileName}",
                            TravelId = travelId,
                            ItineraryId = itineraryId,
                            ActivityId = activityId,
                            DocumentType = Models.TravelDocumentType.Receipt, // Default, could be enhanced
                            EstablishmentType = Models.EstablishmentType.Restaurant, // Default, could be enhanced
                            ExtractedText = processedText,
                            HtmlContent = travelAnalysisResult.HtmlOutput,
                            AiSummary = travelAnalysisResult.TextSummary,
                            FileSize = 0, // Will be set by caller
                            MimeType = GetMimeTypeFromFileName(fileName),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            DocType = "TravelDocument"
                        };

                        // Extract financial data if available
                        if (extractionResult?.Success == true && extractionResult.InvoiceData != null)
                        {
                            travelDocument.TotalAmount = extractionResult.InvoiceData.InvoiceTotal;
                            travelDocument.Currency = "USD"; // Default, could be enhanced
                            travelDocument.DocumentDate = extractionResult.InvoiceData.InvoiceDate;
                            travelDocument.VendorName = extractionResult.InvoiceData.VendorName;
                            travelDocument.VendorAddress = extractionResult.InvoiceData.VendorAddress;
                        }

                        var saved = await cosmosService.SaveTravelDocumentAsync(travelDocument);
                        if (saved)
                        {
                            _logger.LogInformation("? Travel document saved to Cosmos DB successfully");
                        }
                        else
                        {
                            _logger.LogWarning("?? Failed to save travel document to Cosmos DB");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "? Error saving travel document to Cosmos DB");
                    }
                }

                // Build comprehensive result
                var result = new ProcessTravelDocumentResult
                {
                    Success = true,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedText = processedText,
                    ProcessedAt = DateTime.UtcNow,
                    DocumentIntelligenceResult = extractionResult,
                    TravelAnalysisResult = travelAnalysisResult,
                    TravelId = travelId,
                    ItineraryId = itineraryId,
                    ActivityId = activityId
                };

                _logger.LogInformation("? ProcessTravelDocument completed successfully for: {FileName}", fileName);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error processing travel document {FileName}", fileName);
                
                return new ProcessTravelDocumentResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    ContainerName = containerName,
                    FilePath = filePath,
                    FileName = fileName,
                    ProcessedAt = DateTime.UtcNow,
                    TravelId = travelId,
                    ItineraryId = itineraryId,
                    ActivityId = activityId
                };
            }
        }

        /// <summary>
        /// Process travel document with AI for enhanced travel-specific analysis
        /// </summary>
        private async Task<TravelDocumentExtractionResult> ProcessTravelDocumentWithAI(
            string extractedText, 
            InvoiceAnalysisResult? extractionResult, 
            string? travelId = null, 
            string? itineraryId = null, 
            string? activityId = null)
        {
            try
            {
                var chatCompletion = _kernel.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var travelContext = BuildTravelContext(travelId, itineraryId, activityId);
                var structuredData = extractionResult != null ? 
                    JsonSerializer.Serialize(extractionResult.InvoiceData, new JsonSerializerOptions { WriteIndented = true }) : 
                    "No hay datos estructurados disponibles";

                var tablesData = extractionResult?.Tables != null ? 
                    DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables) : 
                    "No hay tablas disponibles";

                var prompt = $@"
Analiza este documento de viaje y extrae información estructurada relevante para gestión de viajes y gastos.

CONTEXTO DEL VIAJE:
{travelContext}

CONTENIDO DEL DOCUMENTO:
{extractedText}

DATOS ESTRUCTURADOS EXTRAÍDOS:
{structuredData}

TABLAS ENCONTRADAS:
{tablesData}

INSTRUCCIONES ESPECÍFICAS PARA DOCUMENTOS DE VIAJE:
Extrae y analiza información relevante para viajes, incluyendo:

1. **IDENTIFICACIÓN DEL DOCUMENTO**
   - Tipo de documento (recibo, boleto, confirmación, etc.)
   - Establecimiento o proveedor
   - Número de documento/confirmación
   - Fecha y hora

2. **INFORMACIÓN FINANCIERA**
   - Montos totales y subtotales
   - Impuestos y propinas
   - Método de pago
   - Moneda utilizada
   - Conversión de moneda si aplica

3. **CATEGORIZACIÓN DE GASTOS DE VIAJE**
   - Alojamiento (hoteles, Airbnb)
   - Transporte (vuelos, taxis, transporte público, alquiler de autos)
   - Alimentación (restaurantes, comida rápida, groceries)
   - Entretenimiento y actividades
   - Compras y souvenirs
   - Otros gastos de viaje

4. **DETALLES ESPECÍFICOS DEL SERVICIO**
   - Ubicación/dirección del establecimiento
   - Descripción de productos/servicios
   - Horarios y fechas de servicio
   - Personas involucradas
   - Calificaciones o reviews si están disponibles

5. **INFORMACIÓN GEOGRÁFICA**
   - Ciudad y país
   - Direcciones específicas
   - Coordenadas si están disponibles

6. **RECOMENDACIONES Y ALERTAS**
   - Gastos inusuales o altos
   - Oportunidades de ahorro
   - Recomendaciones para futuros viajes
   - Alertas de presupuesto

FORMATO DE RESPUESTA EN JSON (NO uses ```json ni ``` al inicio o final):
{{
    ""documentType"": ""tipo de documento identificado"",
    ""establishmentName"": ""nombre del establecimiento o proveedor"",
    ""establishmentType"": ""categoría del establecimiento"",
    ""location"": {{
        ""address"": ""dirección completa"",
        ""city"": ""ciudad"",
        ""country"": ""país"",
        ""coordinates"": ""coordenadas si están disponibles""
    }},
    ""financial"": {{
        ""totalAmount"": ""monto total"",
        ""subtotal"": ""subtotal"",
        ""taxes"": ""impuestos"",
        ""tips"": ""propinas"",
        ""currency"": ""moneda"",
        ""paymentMethod"": ""método de pago""
    }},
    ""travelCategory"": ""categoría de gasto de viaje"",
    ""serviceDetails"": {{
        ""description"": ""descripción detallada del servicio"",
        ""dateTime"": ""fecha y hora del servicio"",
        ""duration"": ""duración si aplica"",
        ""participants"": ""número de personas""
    }},
    ""extractedItems"": [
        {{
            ""description"": ""descripción del item"",
            ""quantity"": ""cantidad"",
            ""unitPrice"": ""precio unitario"",
            ""totalPrice"": ""precio total""
        }}
    ],
    ""expenseAnalysis"": {{
        ""budgetCategory"": ""categoría de presupuesto"",
        ""costPerPerson"": ""costo por persona"",
        ""valueRating"": ""calificación de valor (1-5)"",
        ""recommendations"": ""recomendaciones""
    }},
    ""textSummary"": ""resumen ejecutivo en texto plano"",
    ""htmlOutput"": ""<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; background: #f9f9f9; border-radius: 10px;'><h2 style='color: #2c3e50; border-bottom: 2px solid #3498db; padding-bottom: 10px;'>?? Análisis de Documento de Viaje</h2><div style='background: white; padding: 15px; border-radius: 8px; margin: 10px 0; box-shadow: 0 2px 4px rgba(0,0,0,0.1);'>AQUÍ va el contenido HTML completo del documento con colores, listas, tablas y toda la información extraída de manera visualmente atractiva</div></div>"",
    ""structuredData"": {{
        ""rawDocumentData"": ""datos crudos del documento"",
        ""extractedFields"": ""campos extraídos"",
        ""confidence"": ""nivel de confianza""
    }},
    ""travelInsights"": {{
        ""spendingPattern"": ""patrón de gasto"",
        ""locationInsights"": ""insights de ubicación"",
        ""budgetImpact"": ""impacto en presupuesto"",
        ""futureRecommendations"": ""recomendaciones futuras""
    }}
}}

IMPORTANTE: 
- No uses ```json al inicio ni ``` al final
- Genera HTML visualmente atractivo con colores, listas y formato profesional
- Incluye toda la información del documento, no omitas detalles
- Categoriza correctamente el gasto de viaje
- Proporciona insights útiles para gestión de viajes";

                history.AddUserMessage(prompt);
                var response = await chatCompletion.GetChatMessageContentAsync(history);

                var aiResponse = response.Content ?? "{}";
                
                // Clean the response
                aiResponse = aiResponse.Trim().Trim('`');
                aiResponse = aiResponse.Replace("```json", "", StringComparison.OrdinalIgnoreCase).Trim();
                aiResponse = aiResponse.Replace("```", "", StringComparison.OrdinalIgnoreCase).Trim();

                // Try to parse AI response
                var aiData = JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new TravelDocumentExtractionResult
                {
                    Success = true,
                    DocumentType = aiData?.GetValueOrDefault("documentType")?.ToString() ?? "",
                    EstablishmentName = aiData?.GetValueOrDefault("establishmentName")?.ToString() ?? "",
                    EstablishmentType = aiData?.GetValueOrDefault("establishmentType")?.ToString() ?? "",
                    TravelCategory = aiData?.GetValueOrDefault("travelCategory")?.ToString() ?? "",
                    TextSummary = aiData?.GetValueOrDefault("textSummary")?.ToString() ?? "",
                    HtmlOutput = aiData?.GetValueOrDefault("htmlOutput")?.ToString() ?? "",
                    StructuredData = aiData?.GetValueOrDefault("structuredData") as Dictionary<string, object> ?? new(),
                    TravelInsights = aiData?.GetValueOrDefault("travelInsights") as Dictionary<string, object> ?? new(),
                    Financial = aiData?.GetValueOrDefault("financial") as Dictionary<string, object> ?? new(),
                    Location = aiData?.GetValueOrDefault("location") as Dictionary<string, object> ?? new(),
                    ServiceDetails = aiData?.GetValueOrDefault("serviceDetails") as Dictionary<string, object> ?? new(),
                    ExpenseAnalysis = aiData?.GetValueOrDefault("expenseAnalysis") as Dictionary<string, object> ?? new(),
                    RawResponse = aiResponse,
                    Metadata = new TravelDocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = extractedText.Length,
                        OutputLength = aiResponse.Length,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "TravelDocument",
                        TravelId = travelId,
                        ItineraryId = itineraryId,
                        ActivityId = activityId,
                        ProcessingSuccessful = true
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? Error in AI travel document processing");
                return new TravelDocumentExtractionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message,
                    Metadata = new TravelDocumentMetadata
                    {
                        Timestamp = DateTime.UtcNow,
                        InputLength = 0,
                        OutputLength = 0,
                        AgentModel = "Azure OpenAI",
                        AnalysisType = "TravelDocument",
                        TravelId = travelId,
                        ItineraryId = itineraryId,
                        ActivityId = activityId,
                        ProcessingSuccessful = false
                    }
                };
            }
        }

        /// <summary>
        /// Build text representation of travel document for processing
        /// </summary>
        private string BuildTravelDocumentText(InvoiceAnalysisResult extractionResult)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine("=== TRAVEL DOCUMENT ANALYSIS ===");
            sb.AppendLine($"Processed At: {extractionResult.ProcessedAt:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Pages: {extractionResult.TotalPages}");
            sb.AppendLine();

            var document = extractionResult.InvoiceData;
            
            sb.AppendLine("?? ESTABLISHMENT INFORMATION:");
            sb.AppendLine($"  Name: {document.VendorName} (Confidence: {document.VendorNameConfidence:P1})");
            sb.AppendLine($"  Address: {document.VendorAddress}");
            sb.AppendLine();

            sb.AppendLine("?? CUSTOMER INFORMATION:");
            sb.AppendLine($"  Name: {document.CustomerName} (Confidence: {document.CustomerNameConfidence:P1})");
            sb.AppendLine($"  Address: {document.CustomerAddress}");
            sb.AppendLine();

            sb.AppendLine("?? DOCUMENT DETAILS:");
            sb.AppendLine($"  Number: {document.InvoiceNumber}");
            sb.AppendLine($"  Date: {document.InvoiceDate:yyyy-MM-dd}");
            sb.AppendLine($"  Due Date: {document.DueDate:yyyy-MM-dd}");
            sb.AppendLine();

            sb.AppendLine("?? FINANCIAL SUMMARY:");
            sb.AppendLine($"  Subtotal: ${document.SubTotal:F2} (Confidence: {document.SubTotalConfidence:P1})");
            sb.AppendLine($"  Total Tax: ${document.TotalTax:F2}");
            sb.AppendLine($"  Total: ${document.InvoiceTotal:F2} (Confidence: {document.InvoiceTotalConfidence:P1})");
            sb.AppendLine();

            if (document.LineItems.Count > 0)
            {
                sb.AppendLine($"??? ITEMS/SERVICES ({document.LineItems.Count}):");
                foreach (var item in document.LineItems)
                {
                    sb.AppendLine($"  • {item.Description} - Qty: {item.Quantity}, Unit: ${item.UnitPrice:F2}, Amount: ${item.Amount:F2}");
                }
                sb.AppendLine();
            }

            if (extractionResult.Tables.Count > 0)
            {
                sb.AppendLine("?? TABLES FOUND:");
                sb.AppendLine(DocumentIntelligenceService.GetSimpleTablesAsText(extractionResult.Tables));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Build travel context for AI processing
        /// </summary>
        private string BuildTravelContext(string? travelId, string? itineraryId, string? activityId)
        {
            var context = new StringBuilder();
            context.AppendLine("CONTEXTO DEL VIAJE:");
            
            if (!string.IsNullOrEmpty(travelId))
            {
                context.AppendLine($"  ?? ID del Viaje: {travelId}");
            }
            
            if (!string.IsNullOrEmpty(itineraryId))
            {
                context.AppendLine($"  ??? ID del Itinerario: {itineraryId}");
            }
            
            if (!string.IsNullOrEmpty(activityId))
            {
                context.AppendLine($"  ?? ID de la Actividad: {activityId}");
            }

            if (string.IsNullOrEmpty(travelId) && string.IsNullOrEmpty(itineraryId) && string.IsNullOrEmpty(activityId))
            {
                context.AppendLine("  ?? Documento de viaje independiente (sin asociación específica)");
            }

            return context.ToString();
        }

        /// <summary>
        /// Get MIME type from file name
        /// </summary>
        private string GetMimeTypeFromFileName(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",
                ".txt" => "text/plain",
                _ => "application/octet-stream"
            };
        }
    }

    /// <summary>
    /// Result of travel document processing
    /// </summary>
    public class ProcessTravelDocumentResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string ContainerName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ProcessedText { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        
        // Travel-specific properties
        public string? TravelId { get; set; }
        public string? ItineraryId { get; set; }
        public string? ActivityId { get; set; }
        
        // Processing results
        public InvoiceAnalysisResult? DocumentIntelligenceResult { get; set; }
        public TravelDocumentExtractionResult? TravelAnalysisResult { get; set; }

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
                return $"? Travel document processing failed: {ErrorMessage}";
            }

            var summary = new StringBuilder();
            summary.AppendLine($"? Successfully processed travel document: {FileName}");
            summary.AppendLine($"?? Location: {FullPath}");
            summary.AppendLine($"?? Processed: {ProcessedAt:yyyy-MM-dd HH:mm} UTC");
            summary.AppendLine($"?? Text Length: {ProcessedText.Length} characters");
            
            if (!string.IsNullOrEmpty(TravelId))
                summary.AppendLine($"?? Associated with Travel: {TravelId}");
            
            if (!string.IsNullOrEmpty(ItineraryId))
                summary.AppendLine($"??? Associated with Itinerary: {ItineraryId}");
            
            if (!string.IsNullOrEmpty(ActivityId))
                summary.AppendLine($"?? Associated with Activity: {ActivityId}");

            return summary.ToString();
        }

        /// <summary>
        /// Get travel-specific insights from the document processing
        /// </summary>
        public Dictionary<string, object> GetTravelInsights()
        {
            var insights = new Dictionary<string, object>
            {
                ["success"] = Success,
                ["fileName"] = FileName,
                ["fullPath"] = FullPath,
                ["processedAt"] = ProcessedAt,
                ["textLength"] = ProcessedText.Length,
                ["travelContext"] = new
                {
                    travelId = TravelId,
                    itineraryId = ItineraryId,
                    activityId = ActivityId
                }
            };

            if (DocumentIntelligenceResult != null)
            {
                insights["documentIntelligence"] = new
                {
                    success = DocumentIntelligenceResult.Success,
                    totalPages = DocumentIntelligenceResult.TotalPages,
                    totalAmount = DocumentIntelligenceResult.InvoiceData?.InvoiceTotal,
                    vendor = DocumentIntelligenceResult.InvoiceData?.VendorName,
                    date = DocumentIntelligenceResult.InvoiceData?.InvoiceDate
                };
            }

            if (TravelAnalysisResult != null && TravelAnalysisResult.Success)
            {
                insights["travelAnalysis"] = new
                {
                    documentType = TravelAnalysisResult.DocumentType,
                    establishmentName = TravelAnalysisResult.EstablishmentName,
                    travelCategory = TravelAnalysisResult.TravelCategory,
                    hasFinancialData = TravelAnalysisResult.Financial.Any(),
                    hasLocationData = TravelAnalysisResult.Location.Any(),
                    hasInsights = TravelAnalysisResult.TravelInsights.Any()
                };
            }

            return insights;
        }
    }

    /// <summary>
    /// Result of AI travel document analysis
    /// </summary>
    public class TravelDocumentExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public string EstablishmentName { get; set; } = string.Empty;
        public string EstablishmentType { get; set; } = string.Empty;
        public string TravelCategory { get; set; } = string.Empty;
        public string TextSummary { get; set; } = string.Empty;
        public string HtmlOutput { get; set; } = string.Empty;
        public Dictionary<string, object> StructuredData { get; set; } = new();
        public Dictionary<string, object> TravelInsights { get; set; } = new();
        public Dictionary<string, object> Financial { get; set; } = new();
        public Dictionary<string, object> Location { get; set; } = new();
        public Dictionary<string, object> ServiceDetails { get; set; } = new();
        public Dictionary<string, object> ExpenseAnalysis { get; set; } = new();
        public string? RawResponse { get; set; }
        public TravelDocumentMetadata? Metadata { get; set; }
    }

    /// <summary>
    /// Metadata for travel document processing
    /// </summary>
    public class TravelDocumentMetadata
    {
        public DateTime Timestamp { get; set; }
        public int InputLength { get; set; }
        public int OutputLength { get; set; }
        public string AgentModel { get; set; } = "Azure OpenAI";
        public string AnalysisType { get; set; } = "TravelDocument";
        public string? TravelId { get; set; }
        public string? ItineraryId { get; set; }
        public string? ActivityId { get; set; }
        public bool ProcessingSuccessful { get; set; }
    }
}