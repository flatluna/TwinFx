using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TwinFx.Models;
using TwinFx.Services;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agente especializado en gestión inteligente de hipotecas/mortgages
    /// ================================================================
    /// 
    /// Este agente utiliza AI para:
    /// - Procesamiento inteligente de documentos de hipoteca
    /// - Extracción y análisis de información de mortgage statements
    /// - Generación de reportes financieros de hipotecas
    /// - Análisis de términos y condiciones de préstamos hipotecarios
    /// 
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class AgentMortgage
    {
        public JsonData jsonData { get; set; }
        public string htmlReport { get; set; }

        private readonly ILogger<AgentMortgage> _logger;
        private readonly IConfiguration _configuration;
        private Kernel? _kernel;

        public AgentMortgage(ILogger<AgentMortgage> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            _logger.LogInformation("💰 AgentMortgage initialized for intelligent mRsaveortgage management");
        }

        /// <summary>
        /// Procesa documentos de hipoteca usando Document Intelligence y AI para extraer información específica
        /// </summary>
        /// <param name="containerName">Nombre del contenedor DataLake (twinId)</param>
        /// <param name="filePath">Ruta dentro del contenedor</param>
        /// <param name="fileName">Nombre del archivo del documento</param>
        /// <param name="homeId">ID de la casa relacionada con la hipoteca</param>
        /// <returns>Resultado del análisis como string JSON</returns>
        public async Task<string> AiHomeMortgage(
            string containerName,
            string filePath,
            string fileName,
            string homeId)
        {
            _logger.LogInformation("🏠💰📄 Starting Home Mortgage analysis for: {FileName}, HomeId: {HomeId}", fileName, homeId);

            var startTime = DateTime.UtcNow;

            try
            {
                // PASO 1: Generar SAS URL para acceso al documento
                _logger.LogInformation("🔗 STEP 1: Generating SAS URL for document access...");

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(containerName);
                var fullFilePath = $"{filePath}/{fileName}";
                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(fullFilePath, TimeSpan.FromHours(2));

                if (string.IsNullOrEmpty(sasUrl))
                {
                    var errorResult = new
                    {
                        success = false,
                        errorMessage = "Failed to generate SAS URL for document access",
                        containerName,
                        filePath,
                        fileName,
                        homeId,
                        processedAt = DateTime.UtcNow
                    };
                    _logger.LogError("❌ Failed to generate SAS URL for: {FullFilePath}", fullFilePath);
                    return JsonSerializer.Serialize(errorResult);
                }

                _logger.LogInformation("✅ SAS URL generated successfully");

                // PASO 2: Análisis con Document Intelligence
                _logger.LogInformation("🧠 STEP 2: Extracting data with Document Intelligence...");

                // Inicializar DocumentIntelligenceService
                var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                var documentIntelligenceService = new DocumentIntelligenceService(loggerFactory, _configuration);

                var documentAnalysis = await documentIntelligenceService.AnalyzeDocumentAsync(sasUrl);

                if (!documentAnalysis.Success)
                {
                    var errorResult = new
                    {
                        success = false,
                        errorMessage = $"Document Intelligence extraction failed: {documentAnalysis.ErrorMessage}",
                        containerName,
                        filePath,
                        fileName,
                        homeId,
                        processedAt = DateTime.UtcNow
                    };
                    _logger.LogError("❌ Document Intelligence extraction failed: {Error}", documentAnalysis.ErrorMessage);
                    return JsonSerializer.Serialize(errorResult);
                }

                _logger.LogInformation("✅ Document Intelligence extraction completed - {Pages} pages, {TextLength} chars",
                    documentAnalysis.TotalPages, documentAnalysis.TextContent.Length);

                // PASO 3: Procesamiento con AI especializado en hipotecas
                _logger.LogInformation("🤖 STEP 3: Processing with AI specialized in mortgage analysis...");

                var aiAnalysisResult = await ProcessHomeMortgageWithAI(documentAnalysis, containerName, homeId, fileName, filePath);

                var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Resultado exitoso
                var successResult = new
                {
                    success = true,
                    containerName,
                    filePath,
                    fileName,
                    homeId,
                    documentUrl = sasUrl,
                    textContent = documentAnalysis.TextContent,
                    totalPages = documentAnalysis.TotalPages,
                    aiAnalysis = aiAnalysisResult,
                    processingTimeMs,
                    processedAt = DateTime.UtcNow
                };

                _logger.LogInformation("✅ Home mortgage analysis completed successfully in {ProcessingTime}ms", processingTimeMs);

                return JsonSerializer.Serialize(successResult, new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing home mortgage document {FileName}", fileName);

                var errorResult = new
                {
                    success = false,
                    errorMessage = ex.Message,
                    containerName,
                    filePath,
                    fileName,
                    homeId,
                    processedAt = DateTime.UtcNow
                };

                return JsonSerializer.Serialize(errorResult);
            }
        }

        /// <summary>
        /// Procesa documento con AI para extraer información específica de hipotecas
        /// </summary>
        private async Task<string> ProcessHomeMortgageWithAI(DocumentAnalysisResult
            documentAnalysis, string TwinID, string HomeID, string FileName, string Path)
        {
            try
            {
                // Asegurar que el kernel esté inicializado
                await InitializeKernelAsync();

                var chatCompletion = _kernel!.GetRequiredService<IChatCompletionService>();
                var history = new ChatHistory();

                var prompt = $@"
Analiza este documento de hipoteca/mortgage y extrae información estructurada específica de préstamos hipotecarios.

CONTENIDO COMPLETO DEL DOCUMENTO:
{documentAnalysis.TextContent}

TOTAL DE PÁGINAS: {documentAnalysis.TotalPages}
 INSTRUCCIONES ESPECÍFICAS PARA DOCUMENTOS DE HIPOTECA:

IMPORTANTE: en el estado de cuenta de la hipoteca puede decir:  On or after 09/16/25, a late charge of $58.25 may apply.
esto no quiere decir que tiene cargos solo dice que si no paga no los pongas como LateCharge



Vas a crear un HTML que contenga todos los detalles del estado de cuenta hipotecario o documento de mortgage. Cada campo, variable, es para el cliente, explica qué es el documento, qué contiene, y todas sus partes. El HTML debe ser visualmente atractivo y fácil de entender.

Estructura del HTML basada en las clases MortgageInformation existentes:

Encabezado Principal:
Un título principal que diga ""Reporte de Estado de Cuenta Hipotecario"".

Es muy importante que no inventes datos y que leas cuidadosamente los cargos en ingles al traducir ten cuidado 
por ejemplo si dice may charge xx dolars no significa que te estan cobrando solo si no pagas a tiempo , 
enfocate en datos reales y no inventes.
Secciones a extraer:

INFORMACIÓN DEL ESTADO DE CUENTA:
Fecha del estado de cuenta (StatementDate)
Número de préstamo (LoanNumber)
Cantidad total adeudada (TotalAmountDue)
Cargos por atraso (LateCharge)
Dirección de la propiedad (PropertyAddress)
SERVICIO AL CLIENTE:
Teléfono (Telephone)
Fax (Fax)
Horarios de atención (HoursOfOperation)
INFORMACIÓN DE PAGO:
Fecha de vencimiento del pago (PaymentDueDate)
Dirección para devolver correspondencia (ReturnMailOperations)
Dirección de correspondencia (Correspondence)
Opciones de pago disponibles (PaymentOptions)
RESUMEN DEL SALDO:
Balance de capital no pagado (UnpaidPrincipalBalance)
Tasa de interés (InterestRate)
Fecha de vencimiento del préstamo (MaturityDate)
Balance de escrow (EscrowBalance)
DESGLOSE DE PAGOS ANTERIORES:
Capital desde último estado de cuenta (SinceLastStatementPrincipal)
Capital del año hasta la fecha (YearToDatePrincipal)
Interés desde último estado de cuenta (SinceLastStatementInterest)
Interés del año hasta la fecha (YearToDateInterest)
Escrow desde último estado de cuenta (SinceLastStatementEscrow)
Escrow del año hasta la fecha (YearToDateEscrow)
EXPLICACIÓN DEL MONTO ADEUDADO:
Capital (Principal)
Interés (Interest)
Escrow (Escrow)
Pago actual (CurrentPayment)
ACTIVIDAD DESDE ÚLTIMO ESTADO DE CUENTA:
Fecha (Date)
Descripción (Description)
Total (Total)
Desglose: Principal, Interés, Escrow
ANÁLISIS FINANCIERO:
Progreso del pago del préstamo
Equity acumulado estimado
Proyecciones de pago
Recomendaciones financieras
Diseño del HTML:

Utiliza grids y tablas para organizar la información de forma clara y accesible.
Emplea colores financieros profesionales (azules, verdes para positivos, rojos para alertas).
Incluye iconos y emojis relevantes para hipotecas (🏠💰📊💳).
Cada sección debe ser claramente etiquetada y explicada.
Agrega gráficos conceptuales usando CSS para mostrar progreso del préstamo.
Incluye alertas importantes sobre fechas de vencimiento.
IMPORTANTE:

Extrae TODA la información disponible, no inventes datos.
Si no encuentras información específica, usa ""No especificado"".
Enfócate en datos financieros precisos: montos, fechas, tasas.
Identifica términos del préstamo y condiciones importantes.
Calcula métricas útiles como porcentaje pagado del préstamo.
Todo el texto debe estar en español.
Proporciona insights financieros útiles sobre el estado de la hipoteca.
Tu respuesta debe incluir:

jsonData: un objeto JSON que contenga todos los datos explicados. Este josn debe tener la estructura que tines
en el HTML pero en json. no me pongas ```json marks
htmlReport: una cadena HTML que incluya los mismos datos en detalle.
Ejemplo en joson respuesta:
{{  
  ""jsonData"": {{  
    
  }},  
  ""htmlReport""una cadena HTML que incluya los mismos datos en detalle. "" 
}}  

usa este ejemplo exactamente usando estos nombres no inventes otros es importante para el .net que los va a leer.
 {{  
  ""jsonData"": {{  
    ""ReporteEstadoCuentaHipotecario"": {{  
      ""INFORMACIÓN_DEL_ESTADO_DE_CUENTA"": {{  
        ""StatementDate"": ""08/29/25"",  
        ""LoanNumber"": ""1234567890"",  
        ""TotalAmountDue"": ""$1,000.00"",  
        ""LateCharge"": ""$00"",  Solo si existe no lo inventes . Si dice may charge eso no significa que esta tarde. ten cuidado
        ""PropertyAddress"": ""123 MAIN ST, ANYTOWN, USA""  
      }},  
      ""SERVICIO_AL_CLIENTE"": {{  
        ""Telephone"": ""1-800-111-2222"",  
        ""Fax"": ""1-866-111-2222"",  
        ""HoursOfOperation"": ""Mon - Fri 9 a.m. - 5 p.m.""  
      }},  
      ""INFORMACIÓN_DE_PAGO"": {{  
        ""PaymentDueDate"": ""09/01/25"",  
        ""ReturnMailOperations"": ""PO Box 12345 Anytown, USA"",  
        ""Correspondence"": ""PO Box 67890 Anytown, USA"",  
        ""PaymentOptions"": [  
          ""Online Payment"",  
          ""Phone Payment""  
        ]  
      }},  
      ""RESUMEN_DEL_SALDO"": {{  
        ""UnpaidPrincipalBalance"": ""$50,000.00"",  
        ""InterestRate"": ""4.500%"",  
        ""MaturityDate"": ""12/30"",  
        ""EscrowBalance"": ""$2,000.00""  
      }},  
      ""DESGLOSE_DE_PAGOS_ANTERIORES"": {{  
        ""SinceLastStatementPrincipal"": ""$200.00"",  
        ""YearToDatePrincipal"": ""$1,500.00"",  
        ""SinceLastStatementInterest"": ""$25.00"",  
        ""YearToDateInterest"": ""$250.00"",  
        ""SinceLastStatementEscrow"": ""$30.00"",  
        ""YearToDateEscrow"": ""$300.00""  
      }},  
      ""EXPLICACIÓN_DEL_MONTO_ADEUDADO"": {{  
        ""Principal"": ""$200.00"",  
        ""Interest"": ""$25.00"",  
        ""Escrow"": ""$30.00"",  
        ""CurrentPayment"": ""$1,000.00""  
      }},  
      ""ACTIVIDAD_DESDE_ÚLTIMO_ESTADO_DE_CUENTA"": {{  
        ""Date"": ""08/29"",  
        ""Description"": ""Payment Received"",  
        ""Total"": ""$1,000.00"",  
        ""Desglose"": {{  
          ""Principal"": ""$200.00"",  
          ""Interest"": ""$25.00"",  
          ""Escrow"": ""$30.00""  
        }}  
      }},  
      ""ANÁLISIS_FINANCIERO"": {{  
        ""ProgresoDelPagoDelPréstamo"": ""On track"",  
        ""EquityAcumuladoEstimado"": ""$5,000.00"",  
        ""ProyeccionesDePago"": ""Continue current payments"",  
        ""RecomendacionesFinancieras"": ""Consider refinancing""  
      }}  
    }}  
  }},  
  ""htmlReport""<!-- INFORMACIÓN DEL ESTADO DE CUENTA -->  
<div class=""grid"">  
  <div class=""card full"">  
    <div class=""section-title""><h2>INFORMACIÓN DEL ESTADO DE CUENTA</h2></div>  
    <table>  
      <tr><th>Fecha del estado de cuenta</th><td>09/15/25</td></tr>  
      <tr><th>Número de préstamo</th><td class=""emphasis"">9812764302</td></tr>  
      <tr><th>Cantidad total adeudada</th><td class=""emphasis"">$1,975.82</td></tr>  
      <tr><th>Cargos por atraso</th><td>No especificado (el estado indica que podría aplicarse $65.00 si no paga a tiempo a partir del 11/15/25)</td></tr>  
      <tr><th>Dirección de la propiedad</th><td>987 EJEMPLO ST, DEMOLAND, XX 99999</td></tr>  
    </table>  
  </div>  

  <!-- SERVICIO AL CLIENTE -->  
  <div class=""card"">  
    <div class=""section-title""><h2>SERVICIO AL CLIENTE</h2></div>  
    <table>  
      <tr><th>Teléfono</th><td>1-555-123-4567 📞</td></tr>  
      <tr><th>Fax</th><td>1-555-765-4321</td></tr>  
      <tr><th>Horarios de atención</th><td>Mon - Fri 8 a.m. - 8 p.m.; Sat 9 a.m. - 1 p.m. CT</td></tr>  
    </table>  
  </div>  

  <!-- INFORMACIÓN DE PAGO -->  
  <div class=""card"">  
    <div class=""section-title""><h2>INFORMACIÓN DE PAGO</h2></div>  
    <table>  
      <tr><th>Fecha de vencimiento del pago</th><td class=""emphasis"">11/01/25</td></tr>  
      <tr><th>Dirección para devolver correspondencia</th><td>Acme Mortgage Servicing Return Mail Operations PO Box 55555 Demo City, ST 55555-5555</td></tr>  
      <tr><th>Dirección de correspondencia</th><td>Correspondence PO Box 55556 Demo City, ST 55556</td></tr>  
      <tr><th>Opciones de pago disponibles</th><td>  
        <ul class=""small"">  
          <li>En línea en acmemortgage.example (sitio de demostración)</li>  
          <li>Aplicación móvil (demo)</li>  
          <li>Por correo (usar cupón adjunto)</li>  
          <li>Teléfono: 1-555-246-8000 (asistencia automatizada)</li>  
          <li>En persona en sucursal autorizada</li>  
          <li>Domiciliación / pagos automáticos</li>  
        </ul>  
      </td></tr>  
    </table>  
  </div>  

  <!-- RESUMEN DEL SALDO -->  
  <div class=""card full"">  
    <div class=""section-title""><h2>RESUMEN DEL SALDO</h2></div>  
    <table>  
      <tr><th>Balance de capital no pagado</th><td class=""emphasis"">$198,362.11</td></tr>  
      <tr><th>Tasa de interés</th><td>4.125%</td></tr>  
      <tr><th>Fecha de vencimiento del préstamo (mes/año)</th><td>03/47</td></tr>  
      <tr><th>Balance de escrow</th><td>$7,812.54</td></tr>  
      <tr><th>Insurance disbursed (YTD)</th><td>$1,275.00</td></tr>  
      <tr><th>Total recibido (YTD)</th><td>$21,342.10</td></tr>  
    </table>  
  </div>  

  <!-- DESGLOSE DE PAGOS ANTERIORES -->  
  <div class=""card"">  
    <div class=""section-title""><h2>DESGLOSE DE PAGOS ANTERIORES</h2></div>  
    <table>  
      <tr><th>Capital desde último estado</th><td>$512.34</td></tr>  
      <tr><th>Capital año hasta la fecha</th><td>$3,921.45</td></tr>  
      <tr><th>Interés desde último estado</th><td>$497.22</td></tr>  
      <tr><th>Interés año hasta la fecha</th><td>$5,923.11</td></tr>  
      <tr><th>Escrow desde último estado</th><td>$966.26</td></tr>  
      <tr><th>Escrow año hasta la fecha</th><td>$8,489.36</td></tr>  
    </table>  
  </div>  

  <!-- EXPLICACIÓN DEL MONTO ADEUDADO -->  
  <div class=""card"">  
    <div class=""section-title""><h2>EXPLICACIÓN DEL MONTO ADEUDADO</h2></div>  
    <table>  
      <tr><th>Capital</th><td>$512.34</td></tr>  
      <tr><th>Interés</th><td>$497.22</td></tr>  
      <tr><th>Escrow</th><td>$966.26</td></tr>  
      <tr><th>Pago actual</th><td class=""emphasis"">$1,975.82</td></tr>  
    </table>  
    <p class=""small"">Nota: el estado muestra dos desgloses ligeramente distintos (ver sección de actividad). Los valores anteriores corresponden al apartado ""Explanation of amount due"" del estado (ejemplo inventado).</p>  
  </div>  

  <!-- ACTIVIDAD DESDE ÚLTIMO ESTADO DE CUENTA -->  
  <div class=""card full"">  
    <div class=""section-title""><h2>ACTIVIDAD DESDE ÚLTIMO ESTADO DE CUENTA</h2></div>  
    <table>  
      <tr><th>Fecha</th><td>09/15</td></tr>  
      <tr><th>Descripción</th><td>Payment 10/2025</td></tr>  
      <tr><th>Total</th><td>$1,975.82</td></tr>  
      <tr><th>Desglose</th><td>Principal: $512.34; Interés: $497.22; Escrow: $966.26</td></tr>  
    </table>  
  </div>  

  <!-- ANÁLISIS FINANCIERO -->  
  <div class=""card full"">  
    <div class=""section-title""><h2>ANÁLISIS FINANCIERO</h2></div>  
    <div style=""display:flex;gap:16px;flex-wrap:wrap"">  
      <div style=""flex:1;min-width:240px"">  
        <p class=""small"">🏦 Progreso del pago del préstamo</p>  
        <div class=""progress"" aria-hidden=""true"">  
          <div class=""bar"" style=""width:25%;"">25% pagado</div>  
        </div>  
        <p class=""small"">Estimación básica: 25% del capital amortizado desde inicio (ejemplo generado). No es un cálculo oficial.</p>  
      </div>  
      <div style=""flex:1;min-width:240px"">  
        <p class=""small"">💰 Equity acumulado estimado</p>  
        <p class=""small"">Estimado no proporcionado — se requiere valor de tasación actual para cálculo fiable.</p>  
      </div>  
      <div style=""flex:1;min-width:240px"">  
        <p class=""small"">📊 Proyecciones de pago</p>  
        <p class=""small"">Mantener pagos de $1,975.82 conforme al cronograma. Vigilar variaciones en escrow que puedan ajustar pagos futuros.</p>  
      </div>  
    </div>  

    <div style=""margin-top:12px"">  
      <p class=""small"">✅ Recomendaciones financieras:</p>  
      <ul class=""small"">  
        <li>Pagar a tiempo antes del 11/01/25 para evitar posibles cargos por atraso (posible $65.00 a partir del 11/15/25).</li>  
        <li>Revisar movimientos de escrow ($7,812.54) y YTD para planificar ajustes en impuestos/seguros.</li>  
        <li>Si considera refinanciar, compare la tasa actual de 4.125% con ofertas y costos asociados; consulte con un asesor.</li>  
        <li>Si tiene dificultades, contacte al servicio al cliente o a un consejero de vivienda (datos arriba).</li>  
      </ul>  
    </div>  
  </div>  

  <!-- ALERTAS IMPORTANTES -->  
  <div class=""card full"">  
    <div class=""section-title""><h2>ALERTAS IMPORTANTES</h2></div>  
    <div class=""notice"">IMPORTANTE: Fecha de vencimiento del pago: <span class=""emphasis"">11/01/25</span>. El estado indica que <strong>podría</strong> aplicarse un cargo por atraso de <strong>$65.00</strong> si el pago no se realiza a tiempo (a partir del 11/15/25). Esto es informativo: no significa que ya se le haya cobrado.</div>  
    <div class=""success"" style=""margin-top:10px"">Para evitar errores de procesamiento, use los métodos de pago indicados (en línea, móvil, teléfono, correo con cupón o sucursal).</div>  
  </div>  

  <div class=""footer-note"">Documento de ejemplo: Acme Mortgage Servicing — estado de cuenta del 09/15/25. Todos los datos en este archivo son inventados para fines de demostración y pruebas con modelos de lenguaje. Si requiere información real, contacte al servicer correspondiente.</div>  
</div>   "" 
}}  
""LateCharge"": ""$00"",  solo si esta en la table de cargos de atraso en la hipoteca no lo pongas si esta como comentario 
El htmloReport es solo un ejemplo usa los datos reales
htmlReport pon colores no lo hagas todo en negro. Usa exactamente el ejemplo del HTML con los datos reales por supuesto. 
nunca termines con ``` no comiences con ```json
";

                history.AddUserMessage(prompt);

                var executionSettings = new PromptExecutionSettings
                {
                    ExtensionData = new Dictionary<string, object>
                    {
                        ["max_completion_tokens'"] = 40000,
                          // Temperatura muy baja para análisis financiero preciso
                    }
                };

                var response = await chatCompletion.GetChatMessageContentAsync(
                    history,
                    executionSettings,
                    _kernel);

                var aiResponse = response.Content ?? "{}";

                MortgageStatementReport MortgageData = JsonConvert.DeserializeObject<MortgageStatementReport>(aiResponse);

                // PASO 4: Guardar en Cosmos DB
                try
                {
                    var loggerFactoryCosmos = LoggerFactory.Create(builder => builder.AddConsole());
                    var mortgageCosmosLogger = loggerFactoryCosmos.CreateLogger<TwinFx.Services.MortgageCosmosDbService>();

                    var cosmosOptions = Microsoft.Extensions.Options.Options.Create(new CosmosDbSettings
                    {
                        Endpoint = _configuration["Values:COSMOS_ENDPOINT"] ?? "",
                        Key = _configuration["Values:COSMOS_KEY"] ?? "",
                        DatabaseName = _configuration["Values:COSMOS_DATABASE_NAME"] ?? "TwinHumanDB"
                    });

                    var mortgageCosmosService = new TwinFx.Services.MortgageCosmosDbService(mortgageCosmosLogger, cosmosOptions, _configuration);
                    string containerName = TwinID;
                    string homeId = HomeID;
                    if (MortgageData != null)
                    {
                        await mortgageCosmosService.SaveMortgageAnalysisAsync(
                            MortgageData,
                            aiResponse,
                            containerName, // twinId
                            homeId,
                            FileName,
                            Path,
                            containerName,
                            "" // documentUrl - se pasará desde MortgageFunctions
                        );

                        _logger.LogInformation("✅ Mortgage data saved to Cosmos DB successfully");
                    }
                }
                catch (Exception cosmosEx)
                {
                    _logger.LogWarning(cosmosEx, "⚠️ Failed to save mortgage data to Cosmos DB, but AI analysis was successful");
                }

                // Limpiar respuesta de cualquier formato markdown
                aiResponse = aiResponse.Trim().Trim('`');
                if (aiResponse.StartsWith("json", StringComparison.OrdinalIgnoreCase))
                {
                    aiResponse = aiResponse.Substring(4).Trim();
                }

                _logger.LogInformation("💰 AI home mortgage analysis completed successfully");
                _logger.LogInformation("📊 AI Response Length: {Length} characters", aiResponse.Length);

                return aiResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "💰 Error in AI home mortgage processing");

                // Retornar error en formato JSON
                var errorResponse = new
                {
                    success = false,
                    errorMessage = ex.Message,
                    processedAt = DateTime.UtcNow
                };

                return null;
            }
        }

        /// <summary>
        /// Inicializa Semantic Kernel para operaciones de AI
        /// </summary>
        private async Task InitializeKernelAsync()
        {
            if (_kernel != null)
                return; // Ya está inicializado

            try
            {
                _logger.LogInformation("🧠 Initializing Semantic Kernel for AgentMortgage");

                // Crear kernel builder
                IKernelBuilder builder = Kernel.CreateBuilder();

                // Obtener configuración de Azure OpenAI
                var endpoint = _configuration.GetValue<string>("Values:AzureOpenAI:Endpoint") ??
                              _configuration.GetValue<string>("AzureOpenAI:Endpoint") ??
                              throw new InvalidOperationException("AzureOpenAI:Endpoint not found");

                var apiKey = _configuration.GetValue<string>("Values:AzureOpenAI:ApiKey") ??
                            _configuration.GetValue<string>("AzureOpenAI:ApiKey") ??
                            throw new InvalidOperationException("AzureOpenAI:ApiKey not found");

                var deploymentName = _configuration.GetValue<string>("Values:AzureOpenAI:DeploymentName") ??
                                    _configuration.GetValue<string>("AzureOpenAI:DeploymentName") ??
                                    "gpt-5-mini";
                deploymentName = "gpt-5-mini";

                // Agregar Azure OpenAI chat completion
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: deploymentName,
                    endpoint: endpoint,
                    apiKey: apiKey);

                // Construir el kernel
                _kernel = builder.Build();

                _logger.LogInformation("✅ Semantic Kernel initialized successfully for AgentMortgage");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for AgentMortgage");
                throw;
            }

            await Task.CompletedTask;
        }
    }


    // ========================================
    // MODELS Y RESPONSE CLASSES - MANTENER EXISTENTES   


    public class MortgageStatementReport
    {
        [JsonProperty("fileName")]
        public string FileName { get; set; }

        [JsonProperty("filePath")]
        public string FilePath { get; set; }

        [JsonProperty("fileURL")]
        public string FileURL { get; set; }

        [JsonProperty("jsonData")]
        public JsonData jsonData { get; set; }

        [JsonProperty("htmlReport")]
        public string htmlReport { get; set; }
    }

    public class JsonData
    {
        [JsonProperty("ReporteEstadoCuentaHipotecario")]
        public ReporteEstadoCuentaHipotecario ReporteEstadoCuentaHipotecario { get; set; }
    }

    public class ReporteEstadoCuentaHipotecario
    {
        [JsonProperty("INFORMACIÓN_DEL_ESTADO_DE_CUENTA")]
        public InformacionEstadoCuenta InformacionEstadoCuenta { get; set; }

        [JsonProperty("SERVICIO_AL_CLIENTE")]
        public ServicioAlCliente ServicioAlCliente { get; set; }

        [JsonProperty("INFORMACIÓN_DE_PAGO")]
        public InformacionPago InformacionPago { get; set; }

        [JsonProperty("RESUMEN_DEL_SALDO")]
        public ResumenSaldo ResumenSaldo { get; set; }

        [JsonProperty("DESGLOSE_DE_PAGOS_ANTERIORES")]
        public DesglosePagosAnteriores DesglosePagosAnteriores { get; set; }

        [JsonProperty("EXPLICACIÓN_DEL_MONTO_ADEUDADO")]
        public ExplicacionMontoAdeudado ExplicacionMontoAdeudado { get; set; }

        [JsonProperty("ACTIVIDAD_DESDE_ÚLTIMO_ESTADO_DE_CUENTA")]
        public ActividadDesdeUltimoEstadoCuenta ActividadDesdeUltimoEstadoCuenta { get; set; }

        [JsonProperty("ANÁLISIS_FINANCIERO")]
        public AnalisisFinanciero AnalisisFinanciero { get; set; }
    }

    public class InformacionEstadoCuenta
    {
        [JsonProperty("StatementDate")]
        public string StatementDate { get; set; }

        [JsonProperty("LoanNumber")]
        public string LoanNumber { get; set; }

        [JsonProperty("TotalAmountDue")]
        public string TotalAmountDue { get; set; }

        [JsonProperty("LateCharge")]
        public string LateCharge { get; set; }

        [JsonProperty("PropertyAddress")]
        public string PropertyAddress { get; set; }
    }

    public class ServicioAlCliente
    {
        [JsonProperty("Telephone")]
        public string Telephone { get; set; }

        [JsonProperty("Fax")]
        public string Fax { get; set; }

        [JsonProperty("HoursOfOperation")]
        public string HoursOfOperation { get; set; }
    }

    public class InformacionPago
    {
        [JsonProperty("PaymentDueDate")]
        public string PaymentDueDate { get; set; }

        [JsonProperty("ReturnMailOperations")]
        public string ReturnMailOperations { get; set; }

        [JsonProperty("Correspondence")]
        public string Correspondence { get; set; }

        [JsonProperty("PaymentOptions")]
        public List<string> PaymentOptions { get; set; }
    }

    public class ResumenSaldo
    {
        [JsonProperty("UnpaidPrincipalBalance")]
        public string UnpaidPrincipalBalance { get; set; }

        [JsonProperty("InterestRate")]
        public string InterestRate { get; set; }

        [JsonProperty("MaturityDate")]
        public string MaturityDate { get; set; }

        [JsonProperty("EscrowBalance")]
        public string EscrowBalance { get; set; }
    }

    public class DesglosePagosAnteriores
    {
        [JsonProperty("SinceLastStatementPrincipal")]
        public string SinceLastStatementPrincipal { get; set; }

        [JsonProperty("YearToDatePrincipal")]
        public string YearToDatePrincipal { get; set; }

        [JsonProperty("SinceLastStatementInterest")]
        public string SinceLastStatementInterest { get; set; }

        [JsonProperty("YearToDateInterest")]
        public string YearToDateInterest { get; set; }

        [JsonProperty("SinceLastStatementEscrow")]
        public string SinceLastStatementEscrow { get; set; }

        [JsonProperty("YearToDateEscrow")]
        public string YearToDateEscrow { get; set; }
    }

    public class ExplicacionMontoAdeudado
    {
        [JsonProperty("Principal")]
        public string Principal { get; set; }

        [JsonProperty("Interest")]
        public string Interest { get; set; }

        [JsonProperty("Escrow")]
        public string Escrow { get; set; }

        [JsonProperty("CurrentPayment")]
        public string CurrentPayment { get; set; }
    }

    public class ActividadDesdeUltimoEstadoCuenta
    {
        [JsonProperty("Date")]
        public string Date { get; set; }

        [JsonProperty("Description")]
        public string Description { get; set; }

        [JsonProperty("Total")]
        public string Total { get; set; }

        [JsonProperty("Desglose")]
        public Desglose Desgloses { get; set; }

        public class Desglose
        {
            [JsonProperty("Principal")]
            public string Principal { get; set; }

            [JsonProperty("Interest")]
            public string Interest { get; set; }

            [JsonProperty("Escrow")]
            public string Escrow { get; set; }
        }
    }

    public class AnalisisFinanciero
    {
        [JsonProperty("ProgresoDelPagoDelPréstamo")]
        public string ProgresoDelPagoDelPrestamo { get; set; }

        [JsonProperty("EquityAcumuladoEstimado")]
        public string EquityAcumuladoEstimado { get; set; }

        [JsonProperty("ProyeccionesDePago")]
        public string ProyeccionesDePago { get; set; }

        [JsonProperty("RecomendacionesFinancieras")]
        public string RecomendacionesFinancieras { get; set; }
    }

}