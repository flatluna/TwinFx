using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using System.Text.Json;
using System.Text;
using System.Linq;
using TwinFx.Models;
using Microsoft.SemanticKernel.ChatCompletion;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agent specializing in extracting information from receipt images and documents using Azure OpenAI Vision
    /// ========================================================================
    /// 
    /// This agent processes receipt images from diary entries to extract structured information:
    /// - Restaurant receipts
    /// - Shopping receipts  
    /// - Entertainment tickets
    /// - Transportation receipts
    /// - Service bills
    /// - And other types of receipts/documents
    /// 
    /// Uses Azure OpenAI GPT-4 Vision to analyze images and return structured JSON data
    /// that can be used to populate diary entry fields automatically.
    /// 
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class DiaryAgent
    {
        private readonly ILogger<DiaryAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ChatClient _chatClient;
        private readonly AzureOpenAIClient _azureOpenAIClient;

        public DiaryAgent(ILogger<DiaryAgent> logger, IConfiguration configuration, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;

            try
            {
                // Initialize Azure OpenAI client
                var endpoint = configuration["AzureOpenAI:Endpoint"] ?? throw new InvalidOperationException("AzureOpenAI:Endpoint is required");
                var apiKey = configuration["AzureOpenAI:ApiKey"] ?? throw new InvalidOperationException("AzureOpenAI:ApiKey is required");
                var visionModelName = configuration["AzureOpenAI:VisionDeploymentName"] ?? configuration["AzureOpenAI:DeploymentName"] ?? "gpt-4o-mini";

                _azureOpenAIClient = new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(apiKey));
                _chatClient = _azureOpenAIClient.GetChatClient(visionModelName);

                _logger.LogInformation("✅ DiaryAgent initialized successfully with model: {ModelName}", visionModelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize DiaryAgent");
                throw;
            }
        }

        /// <summary>
        /// Extract structured information from a receipt image or document
        /// </summary>
        /// <param name="imageBase64">Base64 encoded image data</param>
        /// <param name="fileName">Original file name for context</param>
        /// <param name="activityType">Type of activity (comida, compra, viaje, etc.)</param>
        /// <returns>Structured receipt data</returns>
        public async Task<ReceiptExtractionResult> ExtractReceiptInformationAsync(
            string imageBase64, 
            string fileName, 
            string activityType = "general")
        {
            _logger.LogInformation("🧾 Starting receipt extraction for file: {FileName}, Activity: {ActivityType}", fileName, activityType);
            var startTime = DateTime.UtcNow;

            var result = new ReceiptExtractionResult
            {
                Success = false,
                FileName = fileName,
                ActivityType = activityType,
                ProcessedAt = startTime
            };

            try
            {
                // Validate input
                if (string.IsNullOrEmpty(imageBase64))
                {
                    result.ErrorMessage = "Image data is required";
                    return result;
                }

                // Create the vision prompt for receipt extraction
                var extractionPrompt = CreateExtractionPrompt(activityType);

                // Create the image input for Azure OpenAI Vision
                var imageMessage = ChatMessage.CreateUserMessage(
                    ChatMessageContentPart.CreateTextPart(extractionPrompt),
                    ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(Convert.FromBase64String(imageBase64)), "image/jpeg")
                );

                _logger.LogInformation("🤖 Sending image to Azure OpenAI Vision for analysis...");

                // Call Azure OpenAI Vision API
                var chatOptions = new ChatCompletionOptions
                {
                   MaxOutputTokenCount = 2000,
                    Temperature = 0.1f // Low temperature for consistent extraction
                };

                var response = await _chatClient.CompleteChatAsync(new[] { imageMessage }, chatOptions);

                if (response?.Value?.Content?.Count > 0)
                {
                    var aiResponse = response.Value.Content[0].Text;
                    _logger.LogInformation("✅ Received response from Azure OpenAI Vision, length: {Length} chars", aiResponse.Length);

                    // Parse the AI response
                    var extractedData = await ParseVisionResponse(aiResponse, activityType);
                    
                    result.Success = true;
                    result.ExtractedData = extractedData;
                    result.RawAIResponse = aiResponse;
                    result.ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                    _logger.LogInformation("✅ Receipt extraction completed successfully in {ProcessingTime}ms", result.ProcessingTimeMs);
                }
                else
                {
                    result.ErrorMessage = "No response received from Azure OpenAI Vision";
                    _logger.LogWarning("⚠️ Empty response from Azure OpenAI Vision");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting receipt information from {FileName}", fileName);
                result.ErrorMessage = ex.Message;
                result.ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                return result;
            }
        }

        /// <summary>
        /// Extract information from a receipt using a SAS URL (for already uploaded files)
        /// </summary>
        /// <param name="sasUrl">SAS URL to the image file</param>
        /// <param name="fileName">Original file name for context</param>
        /// <param name="activityType">Type of activity</param>
        /// <returns>Structured receipt data</returns>
        public async Task<ReceiptExtractionResult> ExtractReceiptInformationFromUrlAsync(
            string sasUrl, 
            string fileName, 
            string activityType = "general")
        {
            _logger.LogInformation("🔗 Starting receipt extraction from URL: {FileName}, Activity: {ActivityType}", fileName, activityType);

            try
            {
                // Download the image from the SAS URL
                using var httpClient = new HttpClient();
                var imageBytes = await httpClient.GetByteArrayAsync(sasUrl);
                var imageBase64 = Convert.ToBase64String(imageBytes);

                _logger.LogInformation("📥 Downloaded image from SAS URL, size: {Size} bytes", imageBytes.Length);

                // Process the downloaded image
                return await ExtractReceiptInformationAsync(imageBase64, fileName, activityType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error downloading image from SAS URL: {SasUrl}", sasUrl);
                return new ReceiptExtractionResult
                {
                    Success = false,
                    FileName = fileName,
                    ActivityType = activityType,
                    ErrorMessage = $"Failed to download image: {ex.Message}",
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Create the extraction prompt based on activity type
        /// </summary>
        private string CreateExtractionPrompt(string activityType)
        {
            var basePrompt = @"
Analiza esta imagen de recibo/documento y extrae toda la información visible. 
Este recibo puede ser de un restaurante, museo, cine, tienda, transporte, etc.

INSTRUCCIONES IMPORTANTES:
1. Extrae TODA la información visible en el recibo
2. Identifica el tipo de establecimiento y servicio
3. Busca fechas, montos, direcciones, números de teléfono
4. Identifica productos/servicios específicos
5. Responde ÚNICAMENTE en formato JSON válido, sin markdown ni comentarios adicionales

";

            var specificInstructions = activityType.ToLowerInvariant() switch
            {
                "comida" or "food" => @"
ENFOQUE ESPECIAL PARA COMIDA:
- Nombre del restaurante/establecimiento
- Tipo de cocina
- Platos específicos ordenados
- Precios individuales y total
- Propinas incluidas
- Dirección del restaurante
- Fecha y hora
",
                "compra" or "shopping" => @"
ENFOQUE ESPECIAL PARA COMPRAS:
- Nombre de la tienda
- Productos específicos comprados
- Categorías de productos
- Precios individuales y totales
- Descuentos aplicados
- Método de pago usado
- Dirección de la tienda
",
                "viaje" or "travel" => @"
ENFOQUE ESPECIAL PARA VIAJE:
- Tipo de transporte (taxi, uber, avión, tren, etc.)
- Origen y destino
- Distancia recorrida
- Costo del viaje
- Fecha y hora
- Número de confirmación
",
                "entretenimiento" or "entertainment" => @"
ENFOQUE ESPECIAL PARA ENTRETENIMIENTO:
- Tipo de evento (cine, teatro, concierto, museo, etc.)
- Nombre del evento/película/obra
- Venue/lugar
- Fecha y hora del evento
- Tipo de entrada/ticket
- Asientos específicos
",
                _ => @"
ANÁLISIS GENERAL:
- Tipo de establecimiento/servicio
- Productos/servicios específicos
- Montos y totales
- Fecha y hora
- Información de contacto
"
            };

            return basePrompt + specificInstructions + @"

FORMATO DE RESPUESTA JSON REQUERIDO:
{
    ""establecimiento"": ""Nombre del establecimiento"",
    ""tipoEstablecimiento"": ""restaurante/tienda/cine/museo/transporte/etc"",
    ""fecha"": ""YYYY-MM-DD"",
    ""hora"": ""HH:MM"",
    ""montoTotal"": 0.00,
    ""moneda"": ""MXN/USD/EUR"",
    ""productos"": [
        {
            ""nombre"": ""Producto/Servicio"",
            ""cantidad"": 1,
            ""precio"": 0.00
        }
    ],
    ""direccion"": ""Dirección del establecimiento"",
    ""telefono"": ""Número de teléfono"",
    ""metodoPago"": ""efectivo/tarjeta/digital"",
    ""propina"": 0.00,
    ""impuestos"": 0.00,
    ""descuentos"": 0.00,
    ""numeroTransaccion"": ""ID de transacción"",
    ""observaciones"": ""Información adicional relevante"",
    ""confianza"": 0.95,
    ""camposEspecificos"": {
        ""tipoCocina"": ""Solo para restaurantes"",
        ""categoriaProducto"": ""Solo para tiendas"",
        ""tipoTransporte"": ""Solo para viajes"",
        ""tipoEvento"": ""Solo para entretenimiento""
    }
}";
        }

        /// <summary>
        /// Parse the AI vision response and structure the data
        /// </summary>
        private async Task<ReceiptExtractedData> ParseVisionResponse(string aiResponse, string activityType)
        {
            try
            {
                // Clean the response (remove markdown if present)
                var cleanResponse = aiResponse.Trim();
                if (cleanResponse.StartsWith("```json"))
                {
                    cleanResponse = cleanResponse.Substring(7);
                }
                if (cleanResponse.EndsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                }
                cleanResponse = cleanResponse.Trim();

                _logger.LogInformation("🧹 Cleaned AI response for parsing, length: {Length}", cleanResponse.Length);

                // Parse the JSON response
                using var document = JsonDocument.Parse(cleanResponse);
                var root = document.RootElement;

                var extractedData = new ReceiptExtractedData
                {
                    Establecimiento = GetStringProperty(root, "establecimiento"),
                    TipoEstablecimiento = GetStringProperty(root, "tipoEstablecimiento"),
                    Fecha = GetDateProperty(root, "fecha"),
                    Hora = GetStringProperty(root, "hora"),
                    MontoTotal = GetDecimalProperty(root, "montoTotal"),
                    Moneda = GetStringProperty(root, "moneda", "MXN"),
                    Direccion = GetStringProperty(root, "direccion"),
                    Telefono = GetStringProperty(root, "telefono"),
                    MetodoPago = GetStringProperty(root, "metodoPago"),
                    Propina = GetDecimalProperty(root, "propina"),
                    Impuestos = GetDecimalProperty(root, "impuestos"),
                    Descuentos = GetDecimalProperty(root, "descuentos"),
                    NumeroTransaccion = GetStringProperty(root, "numeroTransaccion"),
                    Observaciones = GetStringProperty(root, "observaciones"),
                    Confianza = GetDoubleProperty(root, "confianza", 0.8),
                    ActivityType = activityType,
                    ExtractedAt = DateTime.UtcNow
                };

                // Extract products/services
                if (root.TryGetProperty("productos", out var productosElement) && productosElement.ValueKind == JsonValueKind.Array)
                {
                    extractedData.Productos = new List<ReceiptProduct>();
                    foreach (var productElement in productosElement.EnumerateArray())
                    {
                        var product = new ReceiptProduct
                        {
                            Nombre = GetStringProperty(productElement, "nombre"),
                            Cantidad = GetIntProperty(productElement, "cantidad", 1),
                            Precio = GetDecimalProperty(productElement, "precio")
                        };
                        extractedData.Productos.Add(product);
                    }
                }

                // Extract specific fields based on activity type
                if (root.TryGetProperty("camposEspecificos", out var specificFields))
                {
                    extractedData.CamposEspecificos = new Dictionary<string, string>();
                    foreach (var field in specificFields.EnumerateObject())
                    {
                        extractedData.CamposEspecificos[field.Name] = field.Value.GetString() ?? "";
                    }
                }

                _logger.LogInformation("✅ Parsed receipt data: {Establecimiento}, Total: {MontoTotal} {Moneda}", 
                    extractedData.Establecimiento, extractedData.MontoTotal, extractedData.Moneda);

                return extractedData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parsing AI vision response");
                
                // Return basic extracted data with error information
                return new ReceiptExtractedData
                {
                    Establecimiento = "Error al extraer información",
                    TipoEstablecimiento = "unknown",
                    Observaciones = $"Error de parsing: {ex.Message}. Respuesta original: {aiResponse}",
                    ActivityType = activityType,
                    ExtractedAt = DateTime.UtcNow,
                    Confianza = 0.1
                };
            }
        }

        // Helper methods for JSON parsing
        private static string GetStringProperty(JsonElement element, string propertyName, string defaultValue = "")
        {
            return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String 
                ? prop.GetString() ?? defaultValue 
                : defaultValue;
        }

        private static DateTime? GetDateProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var dateString = prop.GetString();
                if (DateTime.TryParse(dateString, out var date))
                    return date;
            }
            return null;
        }

        private static decimal GetDecimalProperty(JsonElement element, string propertyName, decimal defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDecimal();
                if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var decimalValue))
                    return decimalValue;
            }
            return defaultValue;
        }

        private static double GetDoubleProperty(JsonElement element, string propertyName, double defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetDouble();
                if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var doubleValue))
                    return doubleValue;
            }
            return defaultValue;
        }

        private static int GetIntProperty(JsonElement element, string propertyName, int defaultValue = 0)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetInt32();
                if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var intValue))
                    return intValue;
            }
            return defaultValue;
        }

        /// <summary>
        /// Generate comprehensive analysis combining diary entry data with receipt extraction results using Semantic Kernel
        /// </summary>
        /// <param name="extractionResult">Receipt extraction result from image analysis</param>
        /// <param name="diaryEntry">Diary entry containing activity information</param>
        /// <returns>Comprehensive analysis with executive summary and detailed HTML report</returns>
        public async Task<DiaryComprehensiveAnalysisResult> GenerateComprehensiveAnalysisAsync(
            ReceiptExtractionResult extractionResult, 
            DiaryEntry diaryEntry)
        {
            _logger.LogInformation("🧠 Starting comprehensive analysis for diary entry: {DiaryId}", diaryEntry.Id);
            var startTime = DateTime.UtcNow;

            var result = new DiaryComprehensiveAnalysisResult
            {
                Success = false,
                DiaryEntryId = diaryEntry.Id,
                ReceiptData = extractionResult.ExtractedData
            };

            try
            {
                // Validate inputs
                if (extractionResult?.ExtractedData == null && diaryEntry == null)
                {
                    result.ErrorMessage = "Receipt extraction data or diaryEntry is required";
                    return result;
                }

                if (extractionResult?.ExtractedData == null)
                {
                    extractionResult.ExtractedData = new ReceiptExtractedData();
                }

                // Get Semantic Kernel chat completion service
                var kernel = _serviceProvider.GetRequiredService<Microsoft.SemanticKernel.Kernel>();
                var chatCompletion = kernel.GetRequiredService<Microsoft.SemanticKernel.ChatCompletion.IChatCompletionService>();

                // Build comprehensive prompt
                var analysisPrompt = CreateComprehensiveAnalysisPrompt(diaryEntry, extractionResult.ExtractedData);

                _logger.LogInformation("🤖 Sending comprehensive analysis request to Azure OpenAI...");

                // Create chat history
                var history = new Microsoft.SemanticKernel.ChatCompletion.ChatHistory();
                history.AddUserMessage(analysisPrompt);

                // Get AI response
                var response = await chatCompletion.GetChatMessageContentAsync(
                    history, 
                    kernel: kernel);

                var aiResponse = response.Content ?? "{}";

                _logger.LogInformation("✅ Received response from Azure OpenAI, length: {Length} chars", aiResponse.Length);

                // Parse the AI response
                var analysisData = await ParseComprehensiveAnalysisResponse(aiResponse);
                
                result.Success = true;
                result.ExecutiveSummary = analysisData.ExecutiveSummary;
                result.DetailedHtmlReport = analysisData.DetailedHtmlReport;
                result.ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                result.Metadata = analysisData.Metadata;
                result.DetailedReporteTexto = analysisData.DetailedReporteTexto;

                _logger.LogInformation("🎯 Comprehensive analysis completed successfully in {ProcessingTime}ms", result.ProcessingTimeMs);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating comprehensive analysis for diary entry: {DiaryId}", diaryEntry.Id);
                result.ErrorMessage = $"Error generating comprehensive analysis: {ex.Message}";
                result.ProcessingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                return result;
            }
        }

        /// <summary>
        /// Create comprehensive analysis prompt combining diary and receipt data
        /// </summary>
        private string CreateComprehensiveAnalysisPrompt(DiaryEntry diaryEntry, ReceiptExtractedData receiptData)
        {
            var activityTypeColors = new Dictionary<string, string>
            {
                ["comida"] = "#FF6B6B",
                ["compra"] = "#4ECDC4", 
                ["viaje"] = "#45B7D1",
                ["entretenimiento"] = "#96CEB4",
                ["ejercicio"] = "#FFEAA7",
                ["estudio"] = "#DDA0DD",
                ["trabajo"] = "#98D8C8",
                ["salud"] = "#F7DC6F",
                ["general"] = "#AED6F1"
            };

            var activityColor = activityTypeColors.GetValueOrDefault(diaryEntry.TipoActividad.ToLowerInvariant(), "#AED6F1");

            return $@"
Eres un analista experto en finanzas personales y actividades de la vida diaria. Vas a analizar una entrada de diario junto con un recibo extraído para generar un análisis comprensivo e insights útiles.
Importante: Siempre contesata no inventes pero no me digas que no pudiste o dime que pasa?
DATOS DE LA ENTRADA DEL DIARIO:
=================================
📋 Información Básica:
• ID: {diaryEntry.Id}
• Título: {diaryEntry.Titulo}
• Descripción: {diaryEntry.Descripcion}
• Fecha: {diaryEntry.Fecha:yyyy-MM-dd HH:mm}
• Tipo de Actividad: {diaryEntry.TipoActividad}
• Ubicación: {diaryEntry.Ubicacion}
• Estado Emocional: {diaryEntry.EstadoEmocional}
• Nivel de Energía: {diaryEntry.NivelEnergia}/10

💰 Información Financiera del Diario:
• Gasto Total: ${diaryEntry.GastoTotal:F2}
• Costo Comida: ${diaryEntry.CostoComida:F2}
• Costo Viaje: ${diaryEntry.CostoViaje:F2}
• Costo Entretenimiento: ${diaryEntry.CostoEntretenimiento:F2}

🍽️ Detalles de Comida (si aplica):
• Restaurante: {diaryEntry.RestauranteLugar}
• Tipo de Cocina: {diaryEntry.TipoCocina}
• Platos Ordenados: {diaryEntry.PlatosOrdenados}
• Calificación: {diaryEntry.CalificacionComida}/10

👥 Participantes: {diaryEntry.Participantes}

DATOS EXTRAÍDOS DEL RECIBO:
===========================
🏪 Establecimiento: {receiptData.Establecimiento}
📍 Dirección: {receiptData.Direccion}
📅 Fecha del Recibo: {receiptData.Fecha?.ToString("yyyy-MM-dd") ?? "No detectada"} {receiptData.Hora}
💵 Monto Total: ${receiptData.MontoTotal:F2} {receiptData.Moneda}
🧾 Número de Transacción: {receiptData.NumeroTransaccion}
💳 Método de Pago: {receiptData.MetodoPago}

📦 Productos/Servicios:
{receiptData.GetProductsAsString()}

🏷️ Impuestos: ${receiptData.Impuestos:F2}
💵 Propina: ${receiptData.Propina:F2}
💸 Descuentos: ${receiptData.Descuentos:F2}

INSTRUCCIONES PARA EL ANÁLISIS:
===============================

Genera un análisis comprensivo que incluya EXACTAMENTE estos dos elementos en formato JSON:

1. **executiveSummary**: Un resumen ejecutivo conciso (2-3 párrafos) que incluya:
   - Correlación entre los datos del diario y del recibo
   - Análisis de coherencia financiera
   - Insights sobre patrones de gasto
   - Recomendaciones breves

2. **detailedHtmlReport**: Un reporte HTML detallado y visualmente atractivo que incluya:
   - Header con el color de actividad ({activityColor})
   - Sección de resumen de la actividad
   - Tabla comparativa de gastos (diario vs recibo)
   - Análisis de productos/servicios 
   - Alertas y recomendaciones con íconos
   - un grid con todos los gastos que se incurrieron en el lugar incluye impuestos todos los gastos colores usa
   - Footer con timestamp del análisis
    - Al final del HTML explica total gastado, experiencia vivida, personas que estuvieron ahi, recomendaciones para 
    -el futuro basado en la experiencia.



3. 

2. **detaileReporte**: Un reporte en json  detallado y visualmente atractivo que incluya:
   
   - Sección de resumen de la actividad
   - Tabla comparativa de gastos (diario vs recibo)
   - Análisis de productos/servicios 
   - un grid con todos los gastos que se incurrieron en el lugar incluye impuestos todos los gastos colores usa
   - Alertas y recomendaciones con íconos 
   - usa Json para crear cada campo que encuentres de la informacion que te pase. 

No pongas graficos de gastos.
 
FORMATO DE RESPUESTA REQUERIDO:
===============================
{{
      ""executiveSummary"": ""Texto del sumario ejecutivo
      ""detalleTexto"": ""Texto de todos los datos en formato json usa tu logica para crearlo cada campo , cada variable, etc. "",
    ""detailedHtmlReport"": ""<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto;'>...</div>"",
    ""metadata"": {{
        ""totalDiscrepancy"": 0.00,
        ""confidenceLevel"": ""high"",
        ""insights"": [""insight1"", ""insight2""],
        ""alerts"": [""alert1"", ""alert2""]
    }}
}}

IMPORTANTE:
- Responde SOLO con JSON válido
- Usa colores y estilos CSS inline en el HTML
- Incluye emojis relevantes en el HTML
- Analiza discrepancias entre montos del diario y recibo
- Genera insights financieros útiles
- Mantén el HTML responsive y profesional
- Todo el texto debe estar en español";
        }

        /// <summary>
        /// Parse the comprehensive analysis response from AI
        /// </summary>
        private async Task<(string ExecutiveSummary, string DetailedHtmlReport, Dictionary<string, object> Metadata, string DetailedReporteTexto)> ParseComprehensiveAnalysisResponse(string aiResponse)
        {
            try
            {
                _logger.LogInformation("📄 Parsing comprehensive analysis response...");

                // Clean response
                var cleanResponse = aiResponse.Trim();
                if (cleanResponse.StartsWith("```json"))
                {
                    cleanResponse = cleanResponse.Substring(7);
                }
                if (cleanResponse.EndsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                }

                // Parse JSON
                var analysisData = JsonSerializer.Deserialize<Dictionary<string, object>>(cleanResponse, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var executiveSummary = GetStringProperty(analysisData, "executiveSummary", "Análisis no disponible");
                var detailedHtmlReport = GetStringProperty(analysisData, "detailedHtmlReport", "<div>Reporte no disponible</div>");
                var detailedReporteTexto = GetStringProperty(analysisData, "detalleTexto", "Reporte detallado no disponible");
                var metadata = new Dictionary<string, object>();
                if (analysisData.TryGetValue("metadata", out var metadataObj) && metadataObj is JsonElement metadataElement)
                {
                    metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataElement.GetRawText()) ?? new();
                }

                _logger.LogInformation("✅ Successfully parsed comprehensive analysis response");
                
                return (executiveSummary, detailedHtmlReport, metadata, detailedReporteTexto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error parsing comprehensive analysis response");
                
                // Fallback response
                var fallbackSummary = "Error al generar el análisis comprensivo. Los datos están disponibles pero no se pudo procesar la respuesta de AI.";
                var fallbackHtml = "<div style='padding: 20px; border: 1px solid #ff6b6b; background: #ffe6e6; color: #cc0000;'><h3>❌ Error en el Análisis</h3><p>No se pudo generar el análisis comprensivo completo.</p></div>";
                var fallbackMetadata = new Dictionary<string, object> { ["error"] = "parsing_failed" };
                
                return (fallbackSummary, fallbackHtml, fallbackMetadata, "");
            }
        }

        /// <summary>
        /// Helper method to safely get string properties from parsed data
        /// </summary>
        private static string GetStringProperty(Dictionary<string, object> data, string key, string defaultValue = "")
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.String)
                return jsonElement.GetString() ?? defaultValue;

            return value.ToString() ?? defaultValue;
        }
    }

    /// <summary>
    /// Result of receipt extraction process
    /// </summary>
    public class ReceiptExtractionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ActivityType { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public double ProcessingTimeMs { get; set; }
        public string? RawAIResponse { get; set; }
        public ReceiptExtractedData? ExtractedData { get; set; }

        /// <summary>
        /// Get summary of extraction results
        /// </summary>
        public string GetSummary()
        {
            if (!Success)
                return $"❌ Extraction failed: {ErrorMessage}";

            if (ExtractedData == null)
                return "⚠️ No data extracted";

            return $"✅ Extracted from {ExtractedData.Establecimiento}: {ExtractedData.MontoTotal:C} {ExtractedData.Moneda} " +
                   $"({ExtractedData.Productos.Count} items, {ExtractedData.Confianza:P0} confidence)";
        }
    }

    /// <summary>
    /// Structured data extracted from receipt
    /// </summary>
    public class ReceiptExtractedData
    {
        public string Establecimiento { get; set; } = string.Empty;
        public string TipoEstablecimiento { get; set; } = string.Empty;
        public DateTime? Fecha { get; set; }
        public string Hora { get; set; } = string.Empty;
        public decimal MontoTotal { get; set; }
        public string Moneda { get; set; } = "MXN";
        public List<ReceiptProduct> Productos { get; set; } = new();
        public string Direccion { get; set; } = string.Empty;
        public string Telefono { get; set; } = string.Empty;
        public string MetodoPago { get; set; } = string.Empty;
        public decimal Propina { get; set; }
        public decimal Impuestos { get; set; }
        public decimal Descuentos { get; set; }
        public string NumeroTransaccion { get; set; } = string.Empty;
        public string Observaciones { get; set; } = string.Empty;
        public double Confianza { get; set; } = 0.8;
        public string ActivityType { get; set; } = string.Empty;
        public DateTime ExtractedAt { get; set; }
        public Dictionary<string, string> CamposEspecificos { get; set; } = new();

        /// <summary>
        /// Combined fecha and hora for compatibility
        /// </summary>
        public DateTime FechaHora => Fecha ?? DateTime.MinValue;

        /// <summary>
        /// Get formatted product list as string
        /// </summary>
        public string GetProductsAsString()
        {
            if (Productos.Count == 0)
                return "No se detectaron productos específicos";

            return string.Join(", ", Productos.Select(p => 
                p.Cantidad > 1 ? $"{p.Cantidad}x {p.Nombre} (${p.Precio:F2})" : $"{p.Nombre} (${p.Precio:F2})"));
        }

        /// <summary>
        /// Get complete summary as text
        /// </summary>
        public string GetCompleteSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🏪 Establecimiento: {Establecimiento}");
            sb.AppendLine($"📅 Fecha: {Fecha?.ToString("yyyy-MM-dd") ?? "No detectada"} {Hora}");
            sb.AppendLine($"💰 Total: {MontoTotal:C} {Moneda}");
            
            if (Productos.Count > 0)
            {
                sb.AppendLine($"🛒 Productos: {GetProductsAsString()}");
            }
            
            if (!string.IsNullOrEmpty(Direccion))
                sb.AppendLine($"📍 Dirección: {Direccion}");
                
            if (Propina > 0)
                sb.AppendLine($"💵 Propina: {Propina:C}");
                
            if (Impuestos > 0)
                sb.AppendLine($"🏛️ Impuestos: {Impuestos:C}");
                
            if (!string.IsNullOrEmpty(MetodoPago))
                sb.AppendLine($"💳 Método de pago: {MetodoPago}");
                
            sb.AppendLine($"🎯 Confianza: {Confianza:P0}");

            if (!string.IsNullOrEmpty(Observaciones))
                sb.AppendLine($"📝 Observaciones: {Observaciones}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Individual product/service from receipt
    /// </summary>
    public class ReceiptProduct
    {
        public string Nombre { get; set; } = string.Empty;
        public int Cantidad { get; set; } = 1;
        public decimal Precio { get; set; }
    }

    /// <summary>
    /// Result of comprehensive diary analysis
    /// </summary>
    public class DiaryComprehensiveAnalysisResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string DiaryEntryId { get; set; } = string.Empty;
        public ReceiptExtractedData? ReceiptData { get; set; }
        public string ExecutiveSummary { get; set; } = string.Empty;

        public string DetailedReporteTexto { get; set; } = string.Empty;
        public string DetailedHtmlReport { get; set; } = string.Empty;
        public double ProcessingTimeMs { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}