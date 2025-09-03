using CsvHelper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Models;
using TwinFx.Services;
using Agents;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TwinFx.Agents;

public class InvoicesAgent 
{
    private readonly ILogger<InvoicesAgent> _logger;
    private readonly IConfiguration _configuration;
    private readonly CosmosDbTwinProfileService _cosmosService;
    private readonly AgentCodeInt _agentCodeInt;
    private Kernel? _kernel;

    public InvoicesAgent(ILogger<InvoicesAgent> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _cosmosService = new CosmosDbTwinProfileService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CosmosDbTwinProfileService>(),
            _configuration);
        _agentCodeInt = new AgentCodeInt(LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<AgentCodeInt>());
        
        _logger.LogInformation("📊 InvoicesAgent initialized with AgentCodeInt");
    }

    public async Task<string> ProcessInvoiceQuestionAsync(string question, string twinId, bool requiereCalculo = false, bool requiereFiltro = false, bool useDynamicCsv = false)
    {
        try
        {
            _logger.LogInformation("🔍 Processing invoice question: {Question} for Twin ID: {TwinId}, RequiereCalculo: {RequiereCalculo}, RequiereFiltro: {RequiereFiltro}, UseDynamicCsv: {UseDynamicCsv}", 
                question, twinId, requiereCalculo, requiereFiltro, useDynamicCsv);

            List<InvoiceDocument> cosmosInvoiceDocuments;

            if (requiereFiltro)
            {
                // Crear filtro SQL usando OpenAI para construir query específico
                var sqlQuery = await GenerateCosmosDBFilterAsync(question, twinId);
                _logger.LogInformation("🔍 Generated SQL filter: {SqlQuery}", sqlQuery);
                cosmosInvoiceDocuments = await _cosmosService.GetFilteredInvoiceDocumentsAsync(twinId, sqlQuery);
            }
            else
            {
                // Obtener todas las facturas sin filtros
                cosmosInvoiceDocuments = await _cosmosService.GetInvoiceDocumentsByTwinIdAsync(twinId);
            }
            
            if (cosmosInvoiceDocuments.Count == 0)
            {
                return $"📭 No se encontraron facturas para el Twin ID: {twinId}" + 
                       (requiereFiltro ? " con los filtros especificados" : "");
            }

            string rawResult;
            
            if (requiereCalculo)
            {
                // FLUJO CON CÁLCULO: Usar AgentCodeInt para análisis complejos con CSV
                _logger.LogInformation("🧮 Using AgentCodeInt for complex calculations and analysis");
                
                string csvContent;
                if (useDynamicCsv)
                {
                    // ⭐ NEW: Generate dynamic CSV with ALL LineItems
                    csvContent = ConvertInvoicesToDynamicCsv(cosmosInvoiceDocuments);
                    _logger.LogInformation("📊 Using DYNAMIC CSV with ALL LineItems (no 10-item limit)");
                }
                else
                {
                    // Legacy: Generate static CSV with 10 LineItems limit
                    var cosmosInvoices = cosmosInvoiceDocuments.Select(doc => ConvertDocumentToRecord(doc)).ToList();
                    csvContent = ConvertInvoicesToCsv(cosmosInvoices);
                    _logger.LogInformation("📄 Using LEGACY CSV with 10 LineItems limit");
                }
                
                using var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));
                rawResult = await _agentCodeInt.AnalyzeCSVFileUsingAzureAIAgentAsync(csvStream, question);
            }
            else
            {
                // FLUJO SIN CÁLCULO: Usar datos completos de InvoiceDocument (NO CSV truncado)
                _logger.LogInformation("📊 Using complete InvoiceDocument data without truncation (optimized for non-calculation)");
                rawResult = await GenerateDirectResponseFromInvoiceDataAsync(cosmosInvoiceDocuments, question);
            }

            // Post-process the result to make it more user-friendly
            var enhancedResult = await EnhanceResponseWithAIAsync(rawResult, question, cosmosInvoiceDocuments);

            var finalResult = $"""
📊 **Análisis de Facturas**

{enhancedResult}

📈 **Resumen:**
   • Twin ID: {twinId}
   • Total de facturas: {cosmosInvoiceDocuments.Count}
   • Rango de fechas: {GetDateRangeFromDocuments(cosmosInvoiceDocuments)}
   • Monto total: ${cosmosInvoiceDocuments.Sum(i => i.InvoiceTotal):F2}
   • Filtros aplicados: {(requiereFiltro ? "Sí" : "No")}
   • Cálculos requeridos: {(requiereCalculo ? "Sí" : "No")}
   • CSV dinámico: {(useDynamicCsv && requiereCalculo ? "Sí" : "No")}
""";

            _logger.LogInformation("✅ Invoice question processed successfully");
            return finalResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing invoice question");
            return $"❌ Error: {ex.Message}";
        }
    }

    public async Task<string> GetInvoiceAnalyticsAsync(string twinId, string? userQuestion = null)
    {
        var question = userQuestion ?? "Dame un análisis completo de todas las facturas incluyendo totales, promedios, proveedores principales, y tendencias por fechas";
        return await ProcessInvoiceQuestionAsync(question, twinId);
    }

    public async Task<string> GetAllInvoicesAsync(string twinId)
    {
        var question = "Muéstrame todas las facturas con sus detalles principales: número, proveedor, monto, fecha";
        return await ProcessInvoiceQuestionAsync(question, twinId);
    }

    public async Task<string> SearchInvoicesAsync(string twinId, decimal? minAmount = null, decimal? maxAmount = null, 
        string? vendorName = null, string? fromDate = null, string? toDate = null)
    {
        var criteria = new List<string>();
        
        if (minAmount.HasValue)
            criteria.Add($"monto mínimo de ${minAmount}");
        if (maxAmount.HasValue)
            criteria.Add($"monto máximo de ${maxAmount}");
        if (!string.IsNullOrEmpty(vendorName))
            criteria.Add($"proveedor que contenga '{vendorName}'");
        if (!string.IsNullOrEmpty(fromDate))
            criteria.Add($"fecha desde {fromDate}");
        if (!string.IsNullOrEmpty(toDate))
            criteria.Add($"fecha hasta {toDate}");

        string question = criteria.Any() 
            ? $"Busca facturas con los siguientes criterios: {string.Join(", ", criteria)}"
            : "Muéstrame todas las facturas disponibles";

        return await ProcessInvoiceQuestionAsync(question, twinId);
    }

    /// <summary>
    /// Convert InvoiceDocument to InvoiceRecord for CSV compatibility
    /// UPDATED: Now supports both static (10 items) and dynamic (unlimited) CSV generation
    /// </summary>
    private InvoiceRecord ConvertDocumentToRecord(InvoiceDocument document)
    {
        var record = new InvoiceRecord
        {
            Id = document.Id,
            TwinID = document.TwinID,
            FileName = document.FileName,
            CreatedAt = document.CreatedAt, // ⭐ FIXED: Was missing
            
            // Vendor Information
            VendorName = document.VendorName,
            VendorNameConfidence = (decimal)document.VendorNameConfidence, // ⭐ FIXED: Proper conversion
            VendorAddress = document.InvoiceData.VendorAddress,
            
            // Customer Information
            CustomerName = document.CustomerName,
            CustomerNameConfidence = (decimal)document.CustomerNameConfidence, // ⭐ FIXED: Proper conversion
            CustomerAddress = document.InvoiceData.CustomerAddress,
            
            // Invoice Details
            InvoiceNumber = document.InvoiceNumber,
            InvoiceDate = document.InvoiceDate,
            DueDate = document.InvoiceData.DueDate ?? DateTime.MinValue, // ⭐ FIXED: Was missing
            
            // Financial Information - ⭐ CRITICAL FIXES
            SubTotal = document.SubTotal, // ⭐ FIXED: Was missing
            SubTotalConfidence = (decimal)document.SubTotalConfidence, // ⭐ FIXED: Was missing
            TotalTax = document.TotalTax, // ⭐ FIXED: Was missing - THIS IS KEY for tax calculations
            InvoiceTotal = document.InvoiceTotal,
            InvoiceTotalConfidence = (decimal)document.InvoiceTotalConfidence,
            
            // Metadata
            LineItemsCount = document.LineItemsCount,
            TablesCount = document.TablesCount,
            RawFieldsCount = document.RawFieldsCount,
            InvoiceData = document.InvoiceData
        };

        // ⭐ STILL LIMITING TO 10 for LEGACY CSV compatibility (AgentCodeInt needs this)
        // But the complete data is available in GenerateDirectResponseFromInvoiceDataAsync
        for (int i = 0; i < Math.Min(document.InvoiceData.LineItems.Count, 10); i++)
        {
            var lineItem = document.InvoiceData.LineItems[i];
            switch (i)
            {
                case 0:
                    record.LineItem1_Description = lineItem.Description;
                    record.LineItem1_Amount = lineItem.Amount;
                    record.LineItem1_Quantity = lineItem.Quantity;
                    record.LineItem1_UnitPrice = lineItem.UnitPrice; // ⭐ FIXED: Was missing UnitPrice
                    break;
                case 1:
                    record.LineItem2_Description = lineItem.Description;
                    record.LineItem2_Amount = lineItem.Amount;
                    record.LineItem2_Quantity = lineItem.Quantity;
                    record.LineItem2_UnitPrice = lineItem.UnitPrice;
                    break;
                case 2:
                    record.LineItem3_Description = lineItem.Description;
                    record.LineItem3_Amount = lineItem.Amount;
                    record.LineItem3_Quantity = lineItem.Quantity;
                    record.LineItem3_UnitPrice = lineItem.UnitPrice;
                    break;
                case 3:
                    record.LineItem4_Description = lineItem.Description;
                    record.LineItem4_Amount = lineItem.Amount;
                    record.LineItem4_Quantity = lineItem.Quantity;
                    record.LineItem4_UnitPrice = lineItem.UnitPrice;
                    break;
                case 4:
                    record.LineItem5_Description = lineItem.Description;
                    record.LineItem5_Amount = lineItem.Amount;
                    record.LineItem5_Quantity = lineItem.Quantity;
                    record.LineItem5_UnitPrice = lineItem.UnitPrice;
                    break;
                case 5:
                    record.LineItem6_Description = lineItem.Description;
                    record.LineItem6_Amount = lineItem.Amount;
                    record.LineItem6_Quantity = lineItem.Quantity;
                    record.LineItem6_UnitPrice = lineItem.UnitPrice;
                    break;
                case 6:
                    record.LineItem7_Description = lineItem.Description;
                    record.LineItem7_Amount = lineItem.Amount;
                    record.LineItem7_Quantity = lineItem.Quantity;
                    record.LineItem7_UnitPrice = lineItem.UnitPrice;
                    break;
                case 7:
                    record.LineItem8_Description = lineItem.Description;
                    record.LineItem8_Amount = lineItem.Amount;
                    record.LineItem8_Quantity = lineItem.Quantity;
                    record.LineItem8_UnitPrice = lineItem.UnitPrice;
                    break;
                case 8:
                    record.LineItem9_Description = lineItem.Description;
                    record.LineItem9_Amount = lineItem.Amount;
                    record.LineItem9_Quantity = lineItem.Quantity;
                    record.LineItem9_UnitPrice = lineItem.UnitPrice;
                    break;
                case 9:
                    record.LineItem10_Description = lineItem.Description;
                    record.LineItem10_Amount = lineItem.Amount;
                    record.LineItem10_Quantity = lineItem.Quantity;
                    record.LineItem10_UnitPrice = lineItem.UnitPrice;
                    break;
            }
        }

        return record;
    }

    /// <summary>
    /// Generate dynamic CSV with ALL LineItems (no 10-item limitation)
    /// ⭐ NEW: Supports unlimited LineItems - AT&T gets all 30, Microsoft gets all 9
    /// </summary>
    private string ConvertInvoicesToDynamicCsv(List<InvoiceDocument> invoices)
    {
        _logger.LogInformation("📊 Converting {Count} invoices to DYNAMIC CSV (no LineItem limits)", invoices.Count);
        
        // Use the new dynamic CSV generator
        var csvContent = DynamicInvoiceCsvGenerator.GenerateDynamicCsv(invoices);
        
        // Log analysis
        var analysis = DynamicInvoiceCsvGenerator.AnalyzeDynamicCsvStructure(invoices);
        _logger.LogInformation("📋 Dynamic CSV Analysis:\n{Analysis}", analysis);
        
        return csvContent;
    }

    private string ConvertInvoicesToCsv(List<InvoiceRecord> invoices)
    {
        _logger.LogInformation("📄 Converting {Count} invoices to CSV", invoices.Count);
        
        using var stringWriter = new StringWriter();
        using var csv = new CsvWriter(stringWriter, CultureInfo.InvariantCulture);
        
        csv.WriteRecords(invoices);
        return stringWriter.ToString();
    }

    private string GetDateRangeFromDocuments(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
            return "No hay fechas";
        
        var validDates = invoices.Where(i => i.InvoiceDate != DateTime.MinValue).ToList();
        if (!validDates.Any())
            return "Sin fechas válidas";
            
        var minDate = validDates.Min(i => i.InvoiceDate);
        var maxDate = validDates.Max(i => i.InvoiceDate);
        
        return $"{minDate:yyyy-MM-dd} a {maxDate:yyyy-MM-dd}";
    }

    /// <summary>
    /// Enhance the raw AI response to make it more user-friendly and elegant
    /// </summary>
    private async Task<string> EnhanceResponseWithAIAsync(string rawResponse, string originalQuestion, List<InvoiceDocument> invoices)
    {
        try
        {
            _logger.LogInformation("🎨 Enhancing AI response for better user experience");

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Create enhanced context with InvoiceDocument data
            var contextSummary = GenerateContextSummary(invoices);

            // Create a prompt to enhance the response
            var enhancementPrompt = $"""
Por favor, convierte la siguiente respuesta técnica en una respuesta elegante y fácil de entender para un usuario final:

PREGUNTA ORIGINAL: {originalQuestion}

ANÁLISIS TÉCNICO OBTENIDO:
{rawResponse}

CONTEXTO ADICIONAL DE FACTURAS:
{contextSummary}

INSTRUCCIONES:
1. Crea una respuesta clara y profesional
2. Organiza la información de manera lógica
3. Usa emojis apropiados para mejorar la lectura
4. Explica brevemente el proceso de análisis
5. Resalta los datos más importantes
6. Mantén un tono amigable pero profesional
7. Si hay una tabla de datos, mantén su formato
8. Evita repetir información técnica innecesaria
9. NO incluyas código Python ni referencias técnicas
10. NO repitas encabezados como "CSV Analysis Results" o "Agent (CSV Data Analyzer)"
Crea la respuesta en formato HTML con colores, listas, grids que se vea muy profesional y elegante.
Responde directamente con la versión mejorada, sin explicaciones adicionales.
""";

            // Use Semantic Kernel for direct text completion
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            
            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(enhancementPrompt);

            // Create execution settings
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 2000 },
                    { "temperature", 0.3 }
                }
            };

            // Get the enhanced response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var enhancedResponse = response.Content ?? rawResponse;
            
            _logger.LogInformation("✨ Response enhanced successfully using Semantic Kernel");
            return enhancedResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Failed to enhance response, returning original");
            return rawResponse;
        }
    }

    /// <summary>
    /// Generate context summary from InvoiceDocument list
    /// </summary>
    private string GenerateContextSummary(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
            return "No hay facturas disponibles.";

        var summary = new StringBuilder();
        summary.AppendLine($"Total de facturas analizadas: {invoices.Count}");
        
        if (invoices.Any())
        {
            var totalAmount = invoices.Sum(i => i.InvoiceTotal);
            var avgAmount = invoices.Average(i => i.InvoiceTotal);
            var vendors = invoices.Select(i => i.VendorName).Where(v => !string.IsNullOrEmpty(v)).Distinct().ToList();
            var totalLineItems = invoices.Sum(i => i.InvoiceData.LineItems.Count);
            
            summary.AppendLine($"Monto total: ${totalAmount:F2}");
            summary.AppendLine($"Monto promedio: ${avgAmount:F2}");
            summary.AppendLine($"Proveedores únicos: {string.Join(", ", vendors.Take(5))}");
            summary.AppendLine($"Total de line items: {totalLineItems}");
            
            // Sample line items from first invoice
            var firstInvoice = invoices.First();
            if (firstInvoice.InvoiceData.LineItems.Any())
            {
                summary.AppendLine("Ejemplos de servicios/productos:");
                foreach (var item in firstInvoice.InvoiceData.LineItems.Take(3))
                {
                    if (!string.IsNullOrEmpty(item.Description))
                    {
                        summary.AppendLine($"  • {item.Description}: ${item.Amount:F2}");
                    }
                }
            }
        }
        
        return summary.ToString();
    }

    /// <summary>
    /// Initialize Semantic Kernel for text enhancement
    /// </summary>
    private async Task InitializeKernelAsync()
    {
        if (_kernel != null)
            return; // Already initialized

        try
        {
            // Create kernel builder
            IKernelBuilder builder = Kernel.CreateBuilder();

            // Get Azure OpenAI configuration
            var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ?? 
                          _configuration.GetValue<string>("AzureOpenAI:Endpoint") ?? 
                          throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

            var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ?? 
                        _configuration.GetValue<string>("AzureOpenAI:ApiKey") ?? 
                        throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

            var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ?? 
                                _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ?? 
                                "gpt4mini";

            // Add Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Build the kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized for response enhancement");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Generar filtro SQL para Cosmos DB usando OpenAI basado en la pregunta del usuario
    /// FIXED: Prompt mejorado con estructura completa del documento
    /// </summary>
    private async Task<string> GenerateCosmosDBFilterAsync(string userQuestion, string twinId)
    {
        try
        {
            _logger.LogInformation("🧠 Generating Cosmos DB filter for question: {Question}", userQuestion);

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            var filterPrompt = $$"""
Genera un filtro SQL para Cosmos DB basado en la pregunta del usuario.

PREGUNTA DEL USUARIO: {{userQuestion}}

📊 ESTRUCTURA COMPLETA DEL DOCUMENTO EN COSMOS DB:
```json
{
  "id": "string",                    // ID del documento
  "TwinID": "string",               // ⭐ SIEMPRE FILTRAR POR ESTO
  "fileName": "string",             // Nombre del archivo
  "createdAt": "2025-08-28T01:25:08", // Fecha de creación
  "processedAt": "datetime",        // Fecha de procesamiento
  
  // 🏢 INFORMACIÓN DEL PROVEEDOR (RAÍZ)
  "vendorName": "Microsoft",        // ⭐ BUSCAR AQUÍ - NO en invoiceData
  "vendorNameConfidence": 0.948,
  "vendorAddress": "One Microsoft Way...",
  
  // 👤 INFORMACIÓN DEL CLIENTE (RAÍZ)
  "customerName": "FlatBit Inc",
  "customerNameConfidence": 0.938,
  
  // 📄 DETALLES DE FACTURA (RAÍZ)
  "invoiceNumber": "G055864003",
  "invoiceDate": "2024-08-09T00:00:00",  // ⭐ BUSCAR FECHAS AQUÍ - NO en LineItems
  "dueDate": "2024-08-09T00:00:00",
  
  // 💰 INFORMACIÓN FINANCIERA (RAÍZ)
  "subTotal": 247.90,               // ⭐ TOTALES EN LA RAÍZ
  "totalTax": 16.81,               // ⭐ IMPUESTOS EN LA RAÍZ
  "invoiceTotal": 264.71,          // ⭐ TOTAL EN LA RAÍZ
  "invoiceTotalConfidence": 0.92,
  
  // 📋 METADATOS
  "lineItemsCount": 9,
  "tablesCount": 5,
  
  // 🛍️ DATOS ESTRUCTURADOS (ANIDADO)
  "invoiceData": {
    "LineItems": [                  // ⭐ ARRAY DE PRODUCTOS/SERVICIOS
      {
        "Description": "AI + Machine Learning",  // ⭐ BUSCAR SERVICIOS AQUÍ
        "Amount": 90.48,
        "Quantity": 1,
        "UnitPrice": 90.48
      },
      {
        "Description": "Databases",
        "Amount": 41.13,
        "Quantity": 1,
        "UnitPrice": 41.13
      }
    ]
  }
}
```

⚡ SINTAXIS COSMOS DB CORRECTA:

🔍 **Para búsquedas SIN JOIN (más eficientes):**
- Solo proveedor: c.TwinID = '{{twinId}}' AND CONTAINS(LOWER(c.vendorName), 'microsoft')
- Solo fechas: c.TwinID = '{{twinId}}' AND c.invoiceDate >= '2024-01-01T00:00:00' AND c.invoiceDate < '2025-01-01T00:00:00'
- Solo totales: c.TwinID = '{{twinId}}' AND c.invoiceTotal > 200

🔗 **Para búsquedas CON JOIN (solo cuando necesites LineItems):**
- Con servicios: c.TwinID = '{{twinId}}' AND IS_DEFINED(li.Description) AND CONTAINS(LOWER(li.Description), 'databases')
- Proveedor + servicio: c.TwinID = '{{twinId}}' AND CONTAINS(LOWER(c.vendorName), 'microsoft') AND IS_DEFINED(li.Description) AND CONTAINS(LOWER(li.Description), 'databases')

🚨 **REGLAS CRÍTICAS:**
1. ⭐ vendorName está en la RAÍZ (c.vendorName) - NO en invoiceData
2. ⭐ invoiceDate está en la RAÍZ (c.invoiceDate) - NO en LineItems
3. ⭐ invoiceTotal, totalTax están en la RAÍZ - NO en invoiceData
4. ⭐ Solo usar JOIN si necesitas buscar en li.Description
5. ⭐ SIEMPRE incluir c.TwinID = '{{twinId}}' como primer criterio
6. ⭐ Para fechas usar formato ISO: '2024-01-01T00:00:00'
7. ⭐ SIEMPRE usar LOWER() con CONTAINS() para case-insensitive

🎯 **EJEMPLOS POR TIPO DE PREGUNTA:**

**Pregunta de totales Microsoft:**
c.TwinID = '{{twinId}}' AND CONTAINS(LOWER(c.vendorName), 'microsoft')

**Pregunta de servicios específicos:**
c.TwinID = '{{twinId}}' AND CONTAINS(LOWER(c.vendorName), 'microsoft') AND IS_DEFINED(li.Description) AND CONTAINS(LOWER(li.Description), 'databases')

**Pregunta de fechas:**
c.TwinID = '{{twinId}}' AND c.invoiceDate >= '2025-01-01T00:00:00' AND c.invoiceDate < '2026-01-01T00:00:00'

**Pregunta de montos:**
c.TwinID = '{{twinId}}' AND c.invoiceTotal > 200

IMPORTANTE: Responde ÚNICAMENTE con la cláusula WHERE válida sin formato markdown.
""";
            
            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(filterPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 500 },
                    { "temperature", 0.1 }
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var sqlFilter = response.Content?.Trim() ?? $"c.TwinID = '{twinId}'";
            
            // Clean any markdown formatting
            if (sqlFilter.StartsWith("```sql"))
                sqlFilter = sqlFilter.Substring(6).Trim();
            if (sqlFilter.StartsWith("```"))
                sqlFilter = sqlFilter.Substring(3).Trim();
            if (sqlFilter.EndsWith("```"))
                sqlFilter = sqlFilter.Substring(0, sqlFilter.Length - 3).Trim();
            
            sqlFilter = sqlFilter.Replace("\r", "").Replace("\n", " ").Trim();
            
            _logger.LogInformation("✅ Generated and cleaned SQL filter: {SqlFilter}", sqlFilter);
            return sqlFilter;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating Cosmos DB filter");
            return $"c.TwinID = '{twinId}'"; // Fallback to basic filter
        }
    }

    /// <summary>
    /// Migrate existing invoices to add flattened lineItem fields for SQL searchability
    

    /// <summary>
    /// Generate direct response from CSV data without using AgentCodeInt (for non-calculation scenarios)
    /// </summary>
    private async Task<string> GenerateDirectResponseFromCsvDataAsync(string csvContent, string originalQuestion, List<InvoiceDocument> invoices)
    {
        try
        {
            _logger.LogInformation("📊 Generating direct response from CSV data without calculations");

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Create comprehensive context from invoices
            var detailedContext = GenerateDetailedInvoiceContext(invoices);
            
            // Create prompt for direct CSV analysis
            var directAnalysisPrompt = $"""
Eres un analista de datos experto. El usuario ha hecho esta pregunta y tenemos estos registros de facturas encontrados.

PREGUNTA ORIGINAL: {originalQuestion}

DATOS ENCONTRADOS (CSV):
{csvContent}

CONTEXTO DETALLADO:
{detailedContext}

INSTRUCCIONES IMPORTANTES:
1. Analiza los datos CSV proporcionados y responde la pregunta específica del usuario
2. Presenta los datos de manera clara y organizada en formato HTML profesional
3. Incluye tablas, listas y visualización de datos relevantes
4. NO necesitas hacer cálculos complejos - solo presenta y organiza los datos encontrados
5. Utiliza colores, emojis y formato HTML elegante para mejorar la presentación
6. Enfócate en mostrar los detalles de las facturas que responden a la pregunta
7. Incluye información como: proveedores, montos, fechas, servicios/productos principales
8. Si hay servicios específicos (LineItems), muéstralos en detalle
9. Organiza la información de manera lógica y fácil de leer
10. Mantén un tono profesional pero amigable

FORMATO DE RESPUESTA:
- Usa HTML con estilos CSS inline para colores y formato
- Crea tablas cuando sea apropiado
- Usa listas para organizar información
- Incluye emojis relevantes
- Destaca datos importantes con colores y negritas

Responde directamente con el análisis HTML sin explicaciones adicionales.
""";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            
            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(directAnalysisPrompt);

            // Create execution settings
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 3000 },
                    { "temperature", 0.2 }
                }
            };

            // Get the direct response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var directResponse = response.Content ?? "No se pudo generar respuesta directa.";
            
            _logger.LogInformation("✅ Direct CSV response generated successfully");
            return directResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating direct CSV response");
            
            // Fallback to basic summary if AI fails
            return GenerateBasicInvoiceSummary(invoices, originalQuestion);
        }
    }

    /// <summary>
    /// Generate direct response from InvoiceDocument data without using AgentCodeInt (for non-calculation scenarios)
    /// OPTIMIZED: Uses complete InvoiceDocument data instead of truncated CSV
    /// </summary>
    private async Task<string> GenerateDirectResponseFromInvoiceDataAsync(List<InvoiceDocument> invoices, string originalQuestion)
    {
        try
        {
            _logger.LogInformation("📊 Generating direct response from complete InvoiceDocument data (no CSV truncation)");

            // Initialize Semantic Kernel if not already done
            await InitializeKernelAsync();

            // Create comprehensive context from complete invoices data
            var detailedContext = GenerateDetailedInvoiceContext(invoices);
            
            // Create JSON representation of ALL invoice data for AI analysis
            var completeInvoiceDataJson = GenerateCompleteInvoiceDataForAI(invoices);
            
            // Create prompt for direct invoice analysis with COMPLETE data
            var directAnalysisPrompt = $"""
Eres un analista de datos experto. El usuario ha hecho esta pregunta y tienes acceso COMPLETO a todos los datos de facturas.

PREGUNTA ORIGINAL: {originalQuestion}

DATOS COMPLETOS DE FACTURAS (JSON):
{completeInvoiceDataJson}

CONTEXTO DETALLADO:
{detailedContext}

INSTRUCCIONES IMPORTANTES:
1. Tienes acceso a TODOS los LineItems (no hay límite de 10)
2. Analiza TODOS los campos incluyendo impuestos (TotalTax), subtotales, etc.
3. Responde la pregunta específica del usuario con datos completos y precisos
4. Presenta los datos de manera clara y organizada en formato HTML profesional
5. Para preguntas sobre impuestos, busca en TotalTax y también en LineItems que contengan "tax", "Tax", "impuesto"
6. Utiliza colores, emojis y formato HTML elegante para mejorar la presentación
7. Incluye tablas detalladas cuando sea apropiado
8. Si hay servicios específicos (LineItems), muéstralos TODOS los relevantes
9. Para análisis financieros, suma correctamente TODOS los elementos
10. Mantén un tono profesional pero amigable

EJEMPLOS DE ANÁLISIS QUE PUEDES HACER:
- Sumar TODOS los impuestos de TODOS los LineItems que contengan "tax"
- Analizar TODAS las líneas de servicios, no solo las primeras 10
- Calcular totales reales basados en datos completos
- Mostrar detalles específicos de proveedores con información completa

FORMATO DE RESPUESTA:
- Usa HTML con estilos CSS inline para colores y formato
- Crea tablas detalladas cuando sea apropiado
- Usa listas para organizar información
- Incluye emojis relevantes
- Destaca datos importantes con colores y negritas
- Si la pregunta es sobre impuestos, busca en TotalTax Y en descripciones de LineItems

Responde directamente con el análisis HTML completo y preciso.
""";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            
            // Create chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(directAnalysisPrompt);

            // Create execution settings
            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    { "max_tokens", 4000 }, // Increased for complete data analysis
                    { "temperature", 0.2 }
                }
            };

            // Get the direct response
            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var directResponse = response.Content ?? "No se pudo generar respuesta directa.";
            
            _logger.LogInformation("✅ Direct InvoiceDocument response generated successfully with complete data");
            return directResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating direct InvoiceDocument response");
            
            // Fallback to basic summary if AI fails
            return GenerateBasicInvoiceSummary(invoices, originalQuestion);
        }
    }

    /// <summary>
    /// Generate complete invoice data in JSON format for AI analysis (no truncation)
    /// </summary>
    private string GenerateCompleteInvoiceDataForAI(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
            return "[]";

        try
        {
            var completeData = invoices.Select(invoice => new
            {
                Id = invoice.Id,
                FileName = invoice.FileName,
                TwinID = invoice.TwinID,
                CreatedAt = invoice.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                ProcessedAt = invoice.ProcessedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                
                // Vendor Information
                VendorName = invoice.VendorName,
                VendorNameConfidence = invoice.VendorNameConfidence,
                VendorAddress = invoice.InvoiceData.VendorAddress,
                
                // Customer Information  
                CustomerName = invoice.CustomerName,
                CustomerNameConfidence = invoice.CustomerNameConfidence,
                CustomerAddress = invoice.InvoiceData.CustomerAddress,
                
                // Invoice Details
                InvoiceNumber = invoice.InvoiceNumber,
                InvoiceDate = invoice.InvoiceDate,
                DueDate = invoice.InvoiceData.DueDate?.ToString("yyyy-MM-dd") ?? "",
                
                // CRITICAL: Complete Financial Information
                SubTotal = invoice.SubTotal,
                SubTotalConfidence = invoice.SubTotalConfidence,
                TotalTax = invoice.TotalTax, // ⭐ ESTO ES LO QUE FALTABA
                InvoiceTotal = invoice.InvoiceTotal,
                InvoiceTotalConfidence = invoice.InvoiceTotalConfidence,
                
                // Metadata
                LineItemsCount = invoice.LineItemsCount,
                TablesCount = invoice.TablesCount,
                RawFieldsCount = invoice.RawFieldsCount,
                
                // COMPLETE LineItems (NO TRUNCATION TO 10)
                LineItems = invoice.InvoiceData.LineItems.Select(li => new
                {
                    Description = li.Description,
                    DescriptionConfidence = li.DescriptionConfidence,
                    Quantity = li.Quantity,
                    UnitPrice = li.UnitPrice,
                    Amount = li.Amount,
                    AmountConfidence = li.AmountConfidence
                }).ToList(),
                
                // AI Analysis fields if available
                AiExecutiveSummaryHtml = invoice.AiExecutiveSummaryHtml,
                AiTextSummary = invoice.AiTextSummary,
                AiCompleteSummary = invoice.AiCompleteSummary
            }).ToList();

            var jsonString = System.Text.Json.JsonSerializer.Serialize(completeData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            _logger.LogInformation("📄 Generated complete invoice JSON with {InvoiceCount} invoices and {LineItemsCount} total line items", 
                invoices.Count, invoices.Sum(i => i.InvoiceData.LineItems.Count));
            
            return jsonString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating complete invoice JSON");
            return "[]";
        }
    }

    /// <summary>
    /// Generate detailed context from invoice documents for AI analysis
    /// </summary>
    private string GenerateDetailedInvoiceContext(List<InvoiceDocument> invoices)
    {
        if (!invoices.Any())
            return "No hay facturas disponibles para analizar.";

        var context = new StringBuilder();
        context.AppendLine($"📊 ANÁLISIS DETALLADO DE {invoices.Count} FACTURAS:");
        context.AppendLine();
        
        // Financial summary
        var totalAmount = invoices.Sum(i => i.InvoiceTotal);
        var avgAmount = invoices.Average(i => i.InvoiceTotal);
        var minAmount = invoices.Min(i => i.InvoiceTotal);
        var maxAmount = invoices.Max(i => i.InvoiceTotal);
        var totalTax = invoices.Sum(i => i.TotalTax); // ⭐ AGREGAR TOTAL DE IMPUESTOS
        
        context.AppendLine("💰 RESUMEN FINANCIERO:");
        context.AppendLine($"   • Total: ${totalAmount:F2}");
        context.AppendLine($"   • Promedio: ${avgAmount:F2}");
        context.AppendLine($"   • Mínimo: ${minAmount:F2}");
        context.AppendLine($"   • Máximo: ${maxAmount:F2}");
        context.AppendLine($"   • Total Impuestos: ${totalTax:F2}"); // ⭐ MOSTRAR IMPUESTOS
        context.AppendLine();
        
        // Vendor analysis
        var vendorGroups = invoices
            .Where(i => !string.IsNullOrEmpty(i.VendorName))
            .GroupBy(i => i.VendorName)
            .OrderByDescending(g => g.Sum(i => i.InvoiceTotal))
            .Take(5);
            
        context.AppendLine("🏢 PRINCIPALES PROVEEDORES:");
        foreach (var group in vendorGroups)
        {
            var vendorTotal = group.Sum(i => i.InvoiceTotal);
            var vendorTax = group.Sum(i => i.TotalTax); // ⭐ IMPUESTOS POR PROVEEDOR
            var vendorCount = group.Count();
            context.AppendLine($"   • {group.Key}: {vendorCount} facturas, ${vendorTotal:F2} (impuestos: ${vendorTax:F2})");
        }
        context.AppendLine();
        
        // Date range
        var validDates = invoices.Where(i => i.InvoiceDate != DateTime.MinValue).ToList();
        if (validDates.Any())
        {
            var dateRange = GetDateRangeFromDocuments(validDates);
            context.AppendLine($"📅 RANGO DE FECHAS: {dateRange}");
            context.AppendLine();
        }
        
        // Sample line items from first few invoices (showing ALL, not limited to 10)
        context.AppendLine("🛍️ EJEMPLOS DE SERVICIOS/PRODUCTOS (datos completos):");
        var sampleLineItems = invoices
            .SelectMany(i => i.InvoiceData.LineItems.Take(3)) // Take 3 from each invoice
            .Where(li => !string.IsNullOrEmpty(li.Description))
            .Take(15) // Show up to 15 examples
            .ToList();
            
        foreach (var item in sampleLineItems)
        {
            context.AppendLine($"   • {item.Description}: ${item.Amount:F2}");
        }
        context.AppendLine();
        
        // TAX ANALYSIS - ⭐ NUEVA SECCIÓN PARA IMPUESTOS
        context.AppendLine("💸 ANÁLISIS DE IMPUESTOS:");
        var taxLineItems = invoices
            .SelectMany(i => i.InvoiceData.LineItems)
            .Where(li => !string.IsNullOrEmpty(li.Description) && 
                        (li.Description.ToLower().Contains("tax") || 
                         li.Description.ToLower().Contains("impuesto") ||
                         li.Description.ToLower().Contains("charge")))
            .ToList();
            
        if (taxLineItems.Any())
        {
            var totalTaxFromLineItems = taxLineItems.Sum(li => li.Amount);
            context.AppendLine($"   • Total impuestos en LineItems: ${totalTaxFromLineItems:F2}");
            context.AppendLine($"   • Número de cargos fiscales: {taxLineItems.Count}");
            context.AppendLine("   • Ejemplos de cargos fiscales:");
            
            foreach (var taxItem in taxLineItems.Take(10))
            {
                context.AppendLine($"      - {taxItem.Description}: ${taxItem.Amount:F2}");
            }
        }
        else
        {
            context.AppendLine("   • No se encontraron LineItems específicos de impuestos");
        }
        context.AppendLine();
        
        // Recent invoices details
        context.AppendLine("📋 DETALLES DE FACTURAS RECIENTES:");
        var recentInvoices = invoices.OrderByDescending(i => i.InvoiceDate).Take(3);
        
        foreach (var invoice in recentInvoices)
        {
            context.AppendLine($"   📄 {invoice.FileName}:");
            context.AppendLine($"      • Proveedor: {invoice.VendorName}");
            context.AppendLine($"      • Total: ${invoice.InvoiceTotal:F2}");
            context.AppendLine($"      • Impuestos: ${invoice.TotalTax:F2}"); // ⭐ MOSTRAR IMPUESTOS
            context.AppendLine($"      • Fecha: {invoice.InvoiceDate:yyyy-MM-dd}");
            context.AppendLine($"      • Items totales: {invoice.InvoiceData.LineItems.Count} (sin límite)");
            
            // Show tax-related line items for this invoice
            var invoiceTaxItems = invoice.InvoiceData.LineItems
                .Where(li => !string.IsNullOrEmpty(li.Description) && 
                           (li.Description.ToLower().Contains("tax") || 
                            li.Description.ToLower().Contains("impuesto") ||
                            li.Description.ToLower().Contains("charge")))
                .Take(5);
                
            if (invoiceTaxItems.Any())
            {
                context.AppendLine($"      • Cargos fiscales en esta factura:");
                foreach (var taxItem in invoiceTaxItems)
                {
                    context.AppendLine($"         - {taxItem.Description}: ${taxItem.Amount:F2}");
                }
            }
            context.AppendLine();
        }
        
        return context.ToString();
    }

    /// <summary>
    /// Generate basic invoice summary as fallback when AI processing fails
    /// </summary>
    private string GenerateBasicInvoiceSummary(List<InvoiceDocument> invoices, string originalQuestion)
    {
        if (!invoices.Any())
            return "📭 No se encontraron facturas para analizar.";

        var summary = new StringBuilder();
        summary.AppendLine("<div style='font-family: Arial, sans-serif; line-height: 1.6;'>");
        summary.AppendLine($"<h2 style='color: #2E86C1;'>📊 Análisis de Facturas</h2>");
        summary.AppendLine($"<p><strong>Pregunta:</strong> {originalQuestion}</p>");
        
        summary.AppendLine("<div style='background: #F8F9FA; padding: 15px; border-radius: 8px; margin: 10px 0;'>");
        summary.AppendLine("<h3 style='color: #27AE60;'>💰 Resumen Financiero</h3>");
        summary.AppendLine("<ul>");
        summary.AppendLine($"<li>Total de facturas: <strong>{invoices.Count}</strong></li>");
        summary.AppendLine($"<li>Monto total: <strong>${invoices.Sum(i => i.InvoiceTotal):F2}</strong></li>");
        summary.AppendLine($"<li>Total impuestos: <strong>${invoices.Sum(i => i.TotalTax):F2}</strong></li>"); // ⭐ IMPUESTOS
        summary.AppendLine($"<li>Monto promedio: <strong>${invoices.Average(i => i.InvoiceTotal):F2}</strong></li>");
        summary.AppendLine($"<li>Rango de fechas: <strong>{GetDateRangeFromDocuments(invoices)}</strong></li>");
        summary.AppendLine("</ul>");
        summary.AppendLine("</div>");
        
        var vendors = invoices.Where(i => !string.IsNullOrEmpty(i.VendorName))
            .GroupBy(i => i.VendorName)
            .OrderByDescending(g => g.Sum(i => i.InvoiceTotal))
            .Take(5);
            
        if (vendors.Any())
        {
            summary.AppendLine("<div style='background: #FDF2E9; padding: 15px; border-radius: 8px; margin: 10px 0;'>");
            summary.AppendLine("<h3 style='color: #E67E22;'>🏢 Principales Proveedores</h3>");
            summary.AppendLine("<ul>");
            foreach (var vendor in vendors)
            {
                var vendorTax = vendor.Sum(i => i.TotalTax);
                summary.AppendLine($"<li><strong>{vendor.Key}</strong>: {vendor.Count()} facturas - ${vendor.Sum(i => i.InvoiceTotal):F2} (impuestos: ${vendorTax:F2})</li>");
            }
            summary.AppendLine("</ul>");
            summary.AppendLine("</div>");
        }
        
        summary.AppendLine("</div>");
        return summary.ToString();
    }

    /// <summary>
    /// Smart CSV generation that automatically chooses best approach
    /// ⭐ AUTO-DETECTION: Uses dynamic CSV when beneficial, legacy when sufficient
    /// </summary>
    public async Task<string> ProcessInvoiceQuestionSmartAsync(string question, string twinId, bool requiereCalculo = false, bool requiereFiltro = false)
    {
        // Auto-detect if dynamic CSV is beneficial
        bool useDynamicCsv = false;
        
        if (requiereCalculo)
        {
            // Get invoices to analyze LineItems distribution
            List<InvoiceDocument> testInvoices;
            if (requiereFiltro)
            {
                var sqlQuery = await GenerateCosmosDBFilterAsync(question, twinId);
                testInvoices = await _cosmosService.GetFilteredInvoiceDocumentsAsync(twinId, sqlQuery);
            }
            else
            {
                testInvoices = await _cosmosService.GetInvoiceDocumentsByTwinIdAsync(twinId);
            }
            
            if (testInvoices.Any())
            {
                var maxLineItems = testInvoices.Max(i => i.InvoiceData.LineItems.Count);
                var avgLineItems = testInvoices.Average(i => i.InvoiceData.LineItems.Count);
                
                // ⭐ SMART LOGIC: Use dynamic CSV when there's significant benefit
                useDynamicCsv = maxLineItems > 10 || // Has invoices with >10 items
                              avgLineItems > 7 ||     // Average is high
                              StringExtensions.ContainsAny(question.ToLower(), "todos", "all", "complete", "detallado", "tax", "impuesto", "charge") || // Critical analysis
                              StringExtensions.ContainsAny(question.ToLower(), "estadistic", "trend", "analysis", "audit"); // Statistical analysis
                              
                _logger.LogInformation("🤖 Smart CSV Detection: MaxItems={MaxItems}, AvgItems={AvgItems:F1}, UseDynamic={UseDynamic}", 
                    maxLineItems, avgLineItems, useDynamicCsv);
            }
        }
        
        return await ProcessInvoiceQuestionAsync(question, twinId, requiereCalculo, requiereFiltro, useDynamicCsv);
    }
}

/// <summary>
/// String extension methods for InvoicesAgent
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Helper extension method for string contains any
    /// </summary>
    public static bool ContainsAny(this string source, params string[] values)
    {
        return values.Any(value => source.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}