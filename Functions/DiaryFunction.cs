using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwinFx.Agents;
using TwinFx.Models;
using TwinFx.Services;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace TwinFx.Functions
{
    /// <summary>  
    /// Azure Functions para la gestión del diario de actividades  
    /// Proporciona endpoints RESTful para la gestión del diario personal:  
    /// - Crear nuevas entradas de diario  
    /// - Recuperar entradas con filtrado avanzado  
    /// - Actualizar entradas existentes  
    /// - Eliminar entradas  
    /// - Análisis y estadísticas del diario  
    /// Author: TwinFx Project  
    /// Date: January 15, 2025  
    /// </summary>  
    public class DiaryFunction
    {
        private readonly ILogger<DiaryFunction> _logger;
        private readonly DiaryCosmosDbService _diaryService;
        private readonly IConfiguration _configuration;
        private readonly DiaryAgent _diaryAgent;
        private readonly DiarySearchIndex _diarySearchIndex;
        public DiaryFunction(ILogger<DiaryFunction> logger, DiaryCosmosDbService diaryService,
            IConfiguration configuration, DiaryAgent diaryAgent, DiarySearchIndex diarySearchIndex)
        {
            _logger = logger;
            _diaryService = diaryService;
            _configuration = configuration;
            _diaryAgent = diaryAgent;
            _diarySearchIndex = diarySearchIndex;
        }

        // ========================================  
        // OPTIONS HANDLERS FOR CORS  
        // ========================================  
        [Function("DiaryOptions")]
        public async Task<HttpResponseData> HandleDiaryOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary")] HttpRequestData req, string twinId)
        {
            return await HandleOptions(req, twinId, "diary");
        }

        [Function("DiaryByIdOptions")]
        public async Task<HttpResponseData> HandleDiaryByIdOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/{entryId}")] HttpRequestData req, string twinId, string entryId)
        {
            return await HandleOptions(req, twinId, "diary/{entryId}");
        }

        [Function("DiaryReceiptUploadOptions")]
        public async Task<HttpResponseData> HandleDiaryReceiptUploadOptions([HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "twins/{twinId}/diary/{entryId}/upload-receipt")] HttpRequestData req, string twinId, string entryId)
        {
            return await HandleOptions(req, twinId, "diary/{entryId}/upload-receipt");
        }

        private async Task<HttpResponseData> HandleOptions(HttpRequestData req, string twinId, string route)
        {
            _logger.LogInformation("📔 OPTIONS preflight request for twins/{TwinId}/{Route}", twinId, route);
            var response = req.CreateResponse(HttpStatusCode.OK);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync("");
            return response;
        }

        // ========================================  
        // DIARY ENTRY CRUD OPERATIONS  
        // ========================================  
        [Function("CreateDiaryEntry")]
        public async Task<HttpResponseData> CreateDiaryEntry([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/diary")] HttpRequestData req, string twinId)
        {
            _logger.LogInformation("📔 CreateDiaryEntry function triggered for Twin: {TwinId}", twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                var contentType = GetContentType(req);
                CreateDiaryEntryRequest createRequest;

                if (contentType.Contains("multipart/form-data"))
                {
                    createRequest = await ProcessMultipartFormData(req, twinId);
                }
                else
                {
                    createRequest = await ProcessJsonRequest(req);
                }

                // Validate required fields  
                if (string.IsNullOrEmpty(createRequest.Titulo))
                {
                    return await CreateErrorResponse(req, "Diary entry title is required", HttpStatusCode.BadRequest);
                }

                _logger.LogInformation("📔 Creating diary entry: {Title} for Twin ID: {TwinId}", createRequest.Titulo, twinId);
                var diaryEntry = CreateDiaryEntryFromRequest(createRequest, twinId);

                // NUEVO: Procesar archivo si viene en multipart/form-data
                if (contentType.Contains("multipart/form-data") && createRequest.PathFile == "file_uploaded")
                {
                    var uploadResult = await ProcessFileUploadForCreate(req, twinId, diaryEntry.Id);
                    if (!string.IsNullOrEmpty(uploadResult))
                    {
                        diaryEntry.PathFile = uploadResult;
                        _logger.LogInformation("📎 File uploaded successfully: {FilePath}", uploadResult);
                    }
                    else
                    {
                        _logger.LogInformation("📎 No file uploaded or upload failed");
                        diaryEntry.PathFile = string.Empty;
                    }
                }

                var success = await _diaryService.CreateDiaryEntryAsync(diaryEntry);
                
                _logger.LogInformation("📝 Creación de entrada completada: Success={Success}, DiaryId={DiaryId}, PathFile={PathFile}", 
                    success, diaryEntry.Id, diaryEntry.PathFile ?? "NULL");

                // 🧠 NUEVO: Realizar análisis comprensivo si se creó exitosamente 
                // MODO DE PRUEBA: Ejecutar incluso sin archivo para probar la funcionalidad
                if (success)
                {
                    _logger.LogInformation("🧠 ===== INICIANDO ANÁLISIS COMPRENSIVO (MODO PRUEBA) ===== DiaryId: {DiaryId}", diaryEntry.Id);
                    
                    try
                    {
                        // Si HAY archivo, hacer análisis completo con recibo
                        if (!string.IsNullOrEmpty(diaryEntry.PathFile))
                        {
                            _logger.LogInformation("📎 Archivo detectado: {PathFile}, ejecutando análisis con recibo", diaryEntry.PathFile);
                            
                            // Obtener el archivo subido para análisis de recibo
                            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                            var sasUrl = await dataLakeClient.GenerateSasUrlAsync(diaryEntry.PathFile, TimeSpan.FromHours(24));
                            
                            if (!string.IsNullOrEmpty(sasUrl) && IsImageFile(diaryEntry.PathFile))
                            {
                                _logger.LogInformation("🤖 Starting comprehensive analysis for created diary entry: {DiaryId}", diaryEntry.Id);
                                _logger.LogInformation("🔗 SAS URL generado: {SasUrl}", sasUrl?.Substring(0, 50) + "...");
                                
                                var fileName = Path.GetFileName(diaryEntry.PathFile);
                                var activityType = DetermineActivityTypeFromFileName(fileName) ?? diaryEntry.TipoActividad ?? "general";
                                
                                _logger.LogInformation("📋 Parámetros del análisis: FileName={FileName}, ActivityType={ActivityType}", fileName, activityType);
                                
                                var extractionResult = await _diaryAgent.ExtractReceiptInformationFromUrlAsync(
                                    sasUrl, 
                                    fileName, 
                                    activityType
                                );

                                _logger.LogInformation("🧾 Resultado de extracción: Success={Success}, ErrorMessage={ErrorMessage}", 
                                    extractionResult.Success, extractionResult.ErrorMessage);

                                if (extractionResult.Success && extractionResult.ExtractedData != null)
                                {
                                    _logger.LogInformation("✅ Extracción exitosa: {Establecimiento}, Total: {MontoTotal} {Moneda}", 
                                        extractionResult.ExtractedData.Establecimiento, 
                                        extractionResult.ExtractedData.MontoTotal, 
                                        extractionResult.ExtractedData.Moneda);
                                    
                                    // 🧠 Análisis comprensivo se hace después de la creación en CreateDiaryEntry
                                    _logger.LogInformation("🧠 Receipt extraction completed, comprehensive analysis will be performed after diary entry creation");
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ Receipt extraction failed: {ErrorMessage}", extractionResult.ErrorMessage);
                                }
                            }
                            else
                            {
                                _logger.LogInformation("📄 File is not an image or SAS URL could not be generated, fallback to analysis without receipt. SasUrl: {HasSasUrl}, IsImage: {IsImage}", 
                                    !string.IsNullOrEmpty(sasUrl), IsImageFile(diaryEntry.PathFile));
                                await ExecuteAnalysisWithoutReceipt(diaryEntry);
                            }
                        }
                        else
                        {
                            // Si NO hay archivo, crear datos de recibo simulados para probar la funcionalidad
                            _logger.LogInformation("📝 No hay archivo, ejecutando análisis con datos simulados para prueba");
                            await ExecuteAnalysisWithoutReceipt(diaryEntry);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error durante análisis comprensivo para nueva entrada de diario, continuando normalmente");
                        // No fallar la creación por esto
                    }
                }
                else
                {
                    _logger.LogInformation("📝 No se ejecutará análisis comprensivo porque la creación falló: Success={Success}", success);
                }

                var finalResponse = req.CreateResponse(success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError);
                AddCorsHeaders(finalResponse, req);
                finalResponse.Headers.Add("Content-Type", "application/json");

                if (success)
                {
                    // Generar SAS URL si hay archivo
                    if (!string.IsNullOrEmpty(diaryEntry.PathFile))
                    {
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(diaryEntry.PathFile, TimeSpan.FromHours(24));
                        diaryEntry.SasUrl = sasUrl ?? string.Empty;
                    }

                    await finalResponse.WriteStringAsync(JsonSerializer.Serialize(new DiaryEntryResponse
                    {
                        Success = true,
                        Entry = diaryEntry,
                        Message = $"Diary entry '{createRequest.Titulo}' created successfully",
                        TwinId = twinId
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                }
                else
                {
                    await finalResponse.WriteStringAsync(JsonSerializer.Serialize(new DiaryEntryResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to save diary entry to database"
                    }));
                }

                return finalResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating diary entry");
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        [Function("GetDiaryEntries")]
        public async Task<HttpResponseData> GetDiaryEntries([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/diary")] HttpRequestData req, string twinId)
        {
            _logger.LogInformation("📔 GetDiaryEntries function triggered for Twin: {TwinId}", twinId);
            try
            {
                if (string.IsNullOrEmpty(twinId))
                {
                    return await CreateErrorResponse(req, "Twin ID parameter is required", HttpStatusCode.BadRequest);
                }

                var query = ParseDiaryQuery(req);
                var (entries, stats) = await _diaryService.GetDiaryEntriesAsync(twinId, query);

                // Generate SAS URLs for files in pathFile  
                await GenerateSasURLsForEntries(entries, twinId);

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                await response.WriteStringAsync(JsonSerializer.Serialize(new DiaryEntryResponse
                {
                    Success = true,
                    Entries = entries,
                    Stats = stats,
                    TotalEntries = entries.Count,
                    TwinId = twinId,
                    Message = $"Retrieved {entries.Count} diary entries"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting diary entries");
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        [Function("GetDiaryEntryById")]
        public async Task<HttpResponseData> GetDiaryEntryById([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/diary/{entryId}")] HttpRequestData req, string twinId, string entryId)
        {
            _logger.LogInformation("📔 GetDiaryEntryById function triggered for Entry: {EntryId}, Twin: {TwinId}", entryId, twinId);
            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(entryId))
                {
                    return await CreateErrorResponse(req, "Twin ID and Entry ID parameters are required", HttpStatusCode.BadRequest);
                }

                var diaryEntry = await _diaryService.GetDiaryEntryByIdAsync(entryId, twinId);
                if (diaryEntry == null)
                {
                    return await CreateErrorResponse(req, "Diary entry not found", HttpStatusCode.NotFound);
                }

                // Generate SAS URL if there's a file
                if (!string.IsNullOrEmpty(diaryEntry.PathFile))
                {
                    var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                    var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(diaryEntry.PathFile, TimeSpan.FromHours(24));
                    diaryEntry.SasUrl = sasUrl ?? string.Empty;
                }

                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                var DiaryIndex = await _diarySearchIndex.GetDiaryAnalysisByEntryIdAsync(diaryEntry.Id, diaryEntry.TwinId);
                diaryEntry.DiaryIndex = DiaryIndex.Analysis;

                string json = JsonConvert.SerializeObject(diaryEntry);
                string values = System.Text.Json.JsonSerializer.Serialize(diaryEntry);
                await response.WriteStringAsync(JsonSerializer.Serialize(new DiaryEntryResponse
                {
                    Success = true,
                    Entry = diaryEntry,
                    TwinId = twinId,
                    Message = $"Retrieved diary entry '{diaryEntry.Titulo}'"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting diary entry by ID");
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }
 

        // Additional functions for handling uploads, updates, deletions, etc.  
        [Function("UploadDiaryReceipt")]
        public async Task<HttpResponseData> UploadDiaryReceipt([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "twins/{twinId}/diary/{entryId}/upload-receipt")] HttpRequestData req, string twinId, string entryId)
        {
            _logger.LogInformation("📄 UploadDiaryReceipt function triggered for Entry: {EntryId}, Twin: {TwinId}", entryId, twinId);
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(entryId))
                {
                    return await CreateErrorResponse(req, "Twin ID and Entry ID parameters are required", HttpStatusCode.BadRequest);
                }

                var diaryEntry = await _diaryService.GetDiaryEntryByIdAsync(entryId, twinId);
                if (diaryEntry == null)
                {
                    return await CreateErrorResponse(req, "Diary entry not found", HttpStatusCode.NotFound);
                }

                var contentType = GetContentType(req);
                if (!contentType.Contains("multipart/form-data"))
                {
                    return await CreateErrorResponse(req, "Content-Type must be multipart/form-data", HttpStatusCode.BadRequest);
                }

                var boundary = GetBoundary(contentType);
                if (string.IsNullOrEmpty(boundary))
                {
                    return await CreateErrorResponse(req, "Invalid boundary in multipart data", HttpStatusCode.BadRequest);
                }

                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                var bodyBytes = memoryStream.ToArray();
                var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                var filePart = parts.FirstOrDefault(p => p.Name == "file" || p.Name == "receipt");

                if (filePart == null || filePart.Data == null || filePart.Data.Length == 0)
                {
                    return await CreateErrorResponse(req, "No file data found in request", HttpStatusCode.BadRequest);
                }

                var receiptTypePart = parts.FirstOrDefault(p => p.Name == "receiptType" || p.Name == "tipo");
                var receiptType = receiptTypePart?.StringValue ?? "general";

                var validReceiptTypes = new[] { "compra", "comida", "viaje", "entretenimiento", "ejercicio", "estudio", "salud", "general" };
                if (!validReceiptTypes.Contains(receiptType.ToLowerInvariant()))
                {
                    receiptType = "general";
                }

                var fileBytes = filePart.Data;
                var originalFileName = filePart.FileName ?? $"recibo_{receiptType}";
                var fileNamePart = parts.FirstOrDefault(p => p.Name == "fileName");
                var customFileName = fileNamePart?.StringValue;
                var fileName = customFileName ?? originalFileName;

                if (!IsValidReceiptFile(fileName, fileBytes))
                {
                    return await CreateErrorResponse(req, "Invalid file format. Only images (JPG, PNG, GIF, WEBP) and PDF files are allowed", HttpStatusCode.BadRequest);
                }

                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                var fileExtension = Path.GetExtension(fileName);
                if (string.IsNullOrEmpty(fileExtension))
                {
                    fileExtension = GetFileExtensionFromBytes(fileBytes);
                }

                var cleanFileName = $"recibo_{receiptType}_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
                var filePath = $"diary/{entryId}/{cleanFileName}";

                using var fileStream = new MemoryStream(fileBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(twinId.ToLowerInvariant(), $"diary/{entryId}", cleanFileName, fileStream, GetMimeTypeFromExtension(fileExtension));

                if (!uploadSuccess)
                {
                    return await CreateErrorResponse(req, "Failed to upload receipt to storage", HttpStatusCode.InternalServerError);
                }

                var sasUrl = await dataLakeClient.GenerateSasUrlAsync(filePath, TimeSpan.FromHours(24));
                var receiptPath = filePath;
                var updateSuccess = await UpdateDiaryEntryReceiptPath(diaryEntry, receiptType, receiptPath);

                if (!updateSuccess)
                {
                    _logger.LogWarning("⚠️ Failed to update diary entry with receipt path, but file was uploaded successfully");
                }

                _logger.LogInformation("✅ Receipt uploaded successfully in {ProcessingTime}ms", (DateTime.UtcNow - startTime).TotalMilliseconds);
                var response = req.CreateResponse(HttpStatusCode.OK);
                AddCorsHeaders(response, req);
                await response.WriteAsJsonAsync(new
                {
                    success = true,
                    message = "Receipt uploaded successfully",
                    twinId,
                    entryId,
                    receiptType,
                    fileName = cleanFileName,
                    filePath,
                    receiptUrl = sasUrl,
                    fileSize = fileBytes.Length,
                    mimeType = GetMimeTypeFromExtension(fileExtension),
                    uploadDate = DateTime.UtcNow.ToString("O"),
                    processingTimeSeconds = Math.Round((DateTime.UtcNow - startTime).TotalSeconds, 2)
                });

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in UploadDiaryReceipt");
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }

        // ========================================  
        // HELPER METHODS  
        // ========================================  
        private async Task<CreateDiaryEntryRequest> ProcessMultipartFormData(HttpRequestData req, string twinId)
        {
            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            var boundary = GetBoundary(contentTypeHeader.Value.FirstOrDefault() ?? "");
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();
            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            var createRequest = ParseDiaryEntryFromMultipart(parts);
            return createRequest;
        }

        private async Task<CreateDiaryEntryRequest> ProcessJsonRequest(HttpRequestData req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<CreateDiaryEntryRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private async Task<UpdateDiaryEntryRequest> ProcessMultipartFormDataForUpdate(HttpRequestData req, string twinId)
        {
            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            var boundary = GetBoundary(contentTypeHeader.Value.FirstOrDefault() ?? "");
            using var memoryStream = new MemoryStream();
            await req.Body.CopyToAsync(memoryStream);
            var bodyBytes = memoryStream.ToArray();
            var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
            var updateRequest = ParseDiaryEntryUpdateFromMultipart(parts);
            return updateRequest;
        }

        private async Task<UpdateDiaryEntryRequest> ProcessJsonRequestForUpdate(HttpRequestData req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            return JsonSerializer.Deserialize<UpdateDiaryEntryRequest>(requestBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }

        private void UpdateDiaryEntryFromRequest(DiaryEntry existingEntry, UpdateDiaryEntryRequest updateRequest)
        {
            // Only update fields that are provided (not null)
            if (updateRequest.Titulo != null) existingEntry.Titulo = updateRequest.Titulo;
            if (updateRequest.Descripcion != null) existingEntry.Descripcion = updateRequest.Descripcion;
            if (updateRequest.Fecha.HasValue) existingEntry.Fecha = updateRequest.Fecha.Value;
            if (updateRequest.TipoActividad != null) existingEntry.TipoActividad = updateRequest.TipoActividad;
            if (updateRequest.LabelActividad != null) existingEntry.LabelActividad = updateRequest.LabelActividad;
            if (updateRequest.Ubicacion != null) existingEntry.Ubicacion = updateRequest.Ubicacion;
            if (updateRequest.Latitud.HasValue) existingEntry.Latitud = updateRequest.Latitud;
            if (updateRequest.Longitud.HasValue) existingEntry.Longitud = updateRequest.Longitud;
            
            // Nuevos campos de ubicación detallada y contacto  
            if (updateRequest.Pais != null) existingEntry.Pais = updateRequest.Pais;
            if (updateRequest.Ciudad != null) existingEntry.Ciudad = updateRequest.Ciudad;
            if (updateRequest.Estado != null) existingEntry.Estado = updateRequest.Estado;
            if (updateRequest.CodigoPostal != null) existingEntry.CodigoPostal = updateRequest.CodigoPostal;
            if (updateRequest.DireccionEspecifica != null) existingEntry.DireccionEspecifica = updateRequest.DireccionEspecifica;
            if (updateRequest.Telefono != null) existingEntry.Telefono = updateRequest.Telefono;
            if (updateRequest.Website != null) existingEntry.Website = updateRequest.Website;
            if (updateRequest.DistritoColonia != null) existingEntry.DistritoColonia = updateRequest.DistritoColonia;
            
            if (updateRequest.EstadoEmocional != null) existingEntry.EstadoEmocional = updateRequest.EstadoEmocional;

            // Shopping fields
            if (updateRequest.GastoTotal.HasValue) existingEntry.GastoTotal = updateRequest.GastoTotal;
            if (updateRequest.ProductosComprados != null) existingEntry.ProductosComprados = updateRequest.ProductosComprados;
            if (updateRequest.TiendaLugar != null) existingEntry.TiendaLugar = updateRequest.TiendaLugar;
            if (updateRequest.MetodoPago != null) existingEntry.MetodoPago = updateRequest.MetodoPago;
            if (updateRequest.CategoriaCompra != null) existingEntry.CategoriaCompra = updateRequest.CategoriaCompra;
            if (updateRequest.SatisfaccionCompra.HasValue) existingEntry.SatisfaccionCompra = updateRequest.SatisfaccionCompra;

            // Food fields
            if (updateRequest.CostoComida.HasValue) existingEntry.CostoComida = updateRequest.CostoComida;
            if (updateRequest.RestauranteLugar != null) existingEntry.RestauranteLugar = updateRequest.RestauranteLugar;
            if (updateRequest.TipoCocina != null) existingEntry.TipoCocina = updateRequest.TipoCocina;
            if (updateRequest.PlatosOrdenados != null) existingEntry.PlatosOrdenados = updateRequest.PlatosOrdenados;
            if (updateRequest.CalificacionComida.HasValue) existingEntry.CalificacionComida = updateRequest.CalificacionComida;
            if (updateRequest.AmbienteComida != null) existingEntry.AmbienteComida = updateRequest.AmbienteComida;
            if (updateRequest.RecomendariaComida.HasValue) existingEntry.RecomendariaComida = updateRequest.RecomendariaComida;

            // Travel fields
            if (updateRequest.CostoViaje.HasValue) existingEntry.CostoViaje = updateRequest.CostoViaje;
            if (updateRequest.DestinoViaje != null) existingEntry.DestinoViaje = updateRequest.DestinoViaje;
            if (updateRequest.TransporteViaje != null) existingEntry.TransporteViaje = updateRequest.TransporteViaje;
            if (updateRequest.PropositoViaje != null) existingEntry.PropositoViaje = updateRequest.PropositoViaje;
            if (updateRequest.CalificacionViaje.HasValue) existingEntry.CalificacionViaje = updateRequest.CalificacionViaje;
            if (updateRequest.DuracionViaje.HasValue) existingEntry.DuracionViaje = updateRequest.DuracionViaje;

            // Entertainment fields
            if (updateRequest.CostoEntretenimiento.HasValue) existingEntry.CostoEntretenimiento = updateRequest.CostoEntretenimiento;
            if (updateRequest.CalificacionEntretenimiento.HasValue) existingEntry.CalificacionEntretenimiento = updateRequest.CalificacionEntretenimiento;
            if (updateRequest.TipoEntretenimiento != null) existingEntry.TipoEntretenimiento = updateRequest.TipoEntretenimiento;
            if (updateRequest.TituloNombre != null) existingEntry.TituloNombre = updateRequest.TituloNombre;
            if (updateRequest.LugarEntretenimiento != null) existingEntry.LugarEntretenimiento = updateRequest.LugarEntretenimiento;

            // Exercise fields
            if (updateRequest.CostoEjercicio.HasValue) existingEntry.CostoEjercicio = updateRequest.CostoEjercicio;
            if (updateRequest.EnergiaPostEjercicio.HasValue) existingEntry.EnergiaPostEjercicio = updateRequest.EnergiaPostEjercicio;
            if (updateRequest.CaloriasQuemadas.HasValue) existingEntry.CaloriasQuemadas = updateRequest.CaloriasQuemadas;
            if (updateRequest.TipoEjercicio != null) existingEntry.TipoEjercicio = updateRequest.TipoEjercicio;
            if (updateRequest.DuracionEjercicio.HasValue) existingEntry.DuracionEjercicio = updateRequest.DuracionEjercicio;
            if (updateRequest.IntensidadEjercicio.HasValue) existingEntry.IntensidadEjercicio = updateRequest.IntensidadEjercicio;
            if (updateRequest.LugarEjercicio != null) existingEntry.LugarEjercicio = updateRequest.LugarEjercicio;
            if (updateRequest.RutinaEspecifica != null) existingEntry.RutinaEspecifica = updateRequest.RutinaEspecifica;

            // Study fields
            if (updateRequest.CostoEstudio.HasValue) existingEntry.CostoEstudio = updateRequest.CostoEstudio;
            if (updateRequest.DificultadEstudio.HasValue) existingEntry.DificultadEstudio = updateRequest.DificultadEstudio;
            if (updateRequest.EstadoAnimoPost.HasValue) existingEntry.EstadoAnimoPost = updateRequest.EstadoAnimoPost;
            if (updateRequest.MateriaTema != null) existingEntry.MateriaTema = updateRequest.MateriaTema;
            if (updateRequest.MaterialEstudio != null) existingEntry.MaterialEstudio = updateRequest.MaterialEstudio;
            if (updateRequest.DuracionEstudio.HasValue) existingEntry.DuracionEstudio = updateRequest.DuracionEstudio;
            if (updateRequest.ProgresoEstudio.HasValue) existingEntry.ProgresoEstudio = updateRequest.ProgresoEstudio;

            // Work fields
            if (updateRequest.HorasTrabajadas.HasValue) existingEntry.HorasTrabajadas = updateRequest.HorasTrabajadas;
            if (updateRequest.ProyectoPrincipal != null) existingEntry.ProyectoPrincipal = updateRequest.ProyectoPrincipal;
            if (updateRequest.ReunionesTrabajo.HasValue) existingEntry.ReunionesTrabajo = updateRequest.ReunionesTrabajo;
            if (updateRequest.LogrosHoy != null) existingEntry.LogrosHoy = updateRequest.LogrosHoy;
            if (updateRequest.DesafiosTrabajo != null) existingEntry.DesafiosTrabajo = updateRequest.DesafiosTrabajo;
            if (updateRequest.MoodTrabajo.HasValue) existingEntry.MoodTrabajo = updateRequest.MoodTrabajo;

            // Health fields
            if (updateRequest.CostoSalud.HasValue) existingEntry.CostoSalud = updateRequest.CostoSalud;
            if (updateRequest.TipoConsulta != null) existingEntry.TipoConsulta = updateRequest.TipoConsulta;
            if (updateRequest.ProfesionalCentro != null) existingEntry.ProfesionalCentro = updateRequest.ProfesionalCentro;
            if (updateRequest.MotivoConsulta != null) existingEntry.MotivoConsulta = updateRequest.MotivoConsulta;
            if (updateRequest.TratamientoRecetado != null) existingEntry.TratamientoRecetado = updateRequest.TratamientoRecetado;
            if (updateRequest.ProximaCita.HasValue) existingEntry.ProximaCita = updateRequest.ProximaCita;

            // Call fields
            if (updateRequest.ContactoLlamada != null) existingEntry.ContactoLlamada = updateRequest.ContactoLlamada;
            if (updateRequest.DuracionLlamada.HasValue) existingEntry.DuracionLlamada = updateRequest.DuracionLlamada;
            if (updateRequest.MotivoLlamada != null) existingEntry.MotivoLlamada = updateRequest.MotivoLlamada;
            if (updateRequest.TemasConversacion != null) existingEntry.TemasConversacion = updateRequest.TemasConversacion;
            if (updateRequest.TipoLlamada != null) existingEntry.TipoLlamada = updateRequest.TipoLlamada;
            if (updateRequest.SeguimientoLlamada.HasValue) existingEntry.SeguimientoLlamada = updateRequest.SeguimientoLlamada;

            // Participants and file
            if (updateRequest.Participantes != null) existingEntry.Participantes = updateRequest.Participantes;
            if (updateRequest.PathFile != null) existingEntry.PathFile = updateRequest.PathFile;
        }

        private async Task<string> ProcessFileUploadForUpdate(HttpRequestData req, string twinId, string entryId)
        {
            try
            {
                var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
                var boundary = GetBoundary(contentTypeHeader.Value.FirstOrDefault() ?? "");
                
                if (string.IsNullOrEmpty(boundary))
                {
                    return string.Empty;
                }

                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                var bodyBytes = memoryStream.ToArray();
                var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                
                // Look for any file part
                var filePart = parts.FirstOrDefault(p => 
                    (p.Name == "file" || p.Name == "pathFile" || p.Name == "recibo" || p.Name.Contains("recibo")) && 
                    p.Data != null && p.Data.Length > 0);

                if (filePart == null)
                {
                    return string.Empty;
                }

                var fileBytes = filePart.Data;
                var originalFileName = filePart.FileName ?? "file";

                // Validate file
                if (!IsValidReceiptFile(originalFileName, fileBytes))
                {
                    _logger.LogWarning("⚠️ Invalid file format for update: {FileName}", originalFileName);
                    return string.Empty;
                }

                // Setup DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                
                var fileExtension = Path.GetExtension(originalFileName);
                if (string.IsNullOrEmpty(fileExtension))
                {
                    fileExtension = GetFileExtensionFromBytes(fileBytes);
                }

                var cleanFileName = $"file_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
                var filePath = $"diary/{entryId}/{cleanFileName}";

                // Upload file to DataLake
                using var fileStream = new MemoryStream(fileBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(), 
                    $"diary/{entryId}", 
                    cleanFileName, 
                    fileStream, 
                    GetMimeTypeFromExtension(fileExtension)
                );

                if (uploadSuccess)
                {
                    _logger.LogInformation("✅ File uploaded successfully for diary entry update: {FilePath}", filePath);
                    return filePath;
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to upload file for diary entry update");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error uploading file for update");
                return string.Empty;
            }
        }

        private async Task<string> ProcessFileUploadForCreate(HttpRequestData req, string twinId, string entryId)
        {
            try
            {
                var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
                var boundary = GetBoundary(contentTypeHeader.Value.FirstOrDefault() ?? "");
                
                if (string.IsNullOrEmpty(boundary))
                {
                    return string.Empty;
                }

                using var memoryStream = new MemoryStream();
                await req.Body.CopyToAsync(memoryStream);
                var bodyBytes = memoryStream.ToArray();
                var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                
                // Look for any file part
                var filePart = parts.FirstOrDefault(p => 
                    (p.Name == "file" || p.Name == "pathFile" || p.Name == "recibo" || p.Name.Contains("recibo")) && 
                    p.Data != null && p.Data.Length > 0);

                if (filePart == null)
                {
                    _logger.LogInformation("📎 No file found in multipart data for create");
                    return string.Empty;
                }

                var fileBytes = filePart.Data;
                var originalFileName = filePart.FileName ?? "file";

                _logger.LogInformation("📎 Processing file upload: {FileName}, Size: {Size} bytes", originalFileName, fileBytes.Length);

                // Validate file
                if (!IsValidReceiptFile(originalFileName, fileBytes))
                {
                    _logger.LogWarning("⚠️ Invalid file format for create: {FileName}", originalFileName);
                    return string.Empty;
                }

                // Setup DataLake client
                var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                
                var fileExtension = Path.GetExtension(originalFileName);
                if (string.IsNullOrEmpty(fileExtension))
                {
                    fileExtension = GetFileExtensionFromBytes(fileBytes);
                }

                var cleanFileName = $"file_{DateTime.UtcNow:yyyyMMdd_HHmmss}{fileExtension}";
                var filePath = $"diary/{entryId}/{cleanFileName}";

                _logger.LogInformation("📎 Uploading file to DataLake: Container={Container}, Path={Path}", twinId.ToLowerInvariant(), filePath);

                // Upload file to DataLake
                using var fileStream = new MemoryStream(fileBytes);
                var uploadSuccess = await dataLakeClient.UploadFileAsync(
                    twinId.ToLowerInvariant(), 
                    $"diary/{entryId}", 
                    cleanFileName, 
                    fileStream, 
                    GetMimeTypeFromExtension(fileExtension)
                );

                if (uploadSuccess)
                {
                    _logger.LogInformation("✅ File uploaded successfully for diary entry create: {FilePath}", filePath);

                    // 🧾 NUEVO: Llamar al DiaryAgent para extraer información del recibo después del upload exitoso
                    try
                    {
                        if (IsImageFile(originalFileName))
                        {
                            _logger.LogInformation("🤖 Starting receipt extraction for uploaded file: {FileName}", originalFileName);
                            
                            var imageBase64 = Convert.ToBase64String(fileBytes);
                            var activityType = DetermineActivityTypeFromFileName(originalFileName);
                            
                            var extractionResult = await _diaryAgent.ExtractReceiptInformationAsync(
                                imageBase64, 
                                originalFileName, 
                                activityType
                            );

                            if (extractionResult.Success && extractionResult.ExtractedData != null)
                            {
                                _logger.LogInformation("✅ Receipt extraction successful: {Summary}", extractionResult.GetSummary());
                                _logger.LogInformation("🏪 Establecimiento: {Establecimiento}, Total: {MontoTotal} {Moneda}", 
                                    extractionResult.ExtractedData.Establecimiento, 
                                    extractionResult.ExtractedData.MontoTotal, 
                                    extractionResult.ExtractedData.Moneda);

                                // 🧠 Análisis comprensivo se hace después de la creación en CreateDiaryEntry
                                _logger.LogInformation("🧠 Receipt extraction completed, comprehensive analysis will be performed after diary entry creation");
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Receipt extraction failed: {ErrorMessage}", extractionResult.ErrorMessage);
                            }
                        }
                        else
                        {
                            _logger.LogInformation("📄 File is not an image, skipping receipt extraction: {FileName}", originalFileName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "❌ Error during receipt extraction, continuing normally");
                        // No fallar el upload por esto, continuar normalmente
                    }

                    return filePath;
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to upload file");
                    return string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error processing file upload for create");
                return string.Empty;
            }
        }

        private string GetContentType(HttpRequestData req)
        {
            var contentTypeHeader = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
            return contentTypeHeader.Value?.FirstOrDefault() ?? "";
        }

        private async Task GenerateSasURLsForEntries(List<DiaryEntry> entries, string twinId)
        {
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.PathFile))
                {
                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(entry.PathFile, TimeSpan.FromHours(24));
                    entry.SasUrl = sasUrl ?? string.Empty;
                }
            }
        }

        private DiaryEntry CreateDiaryEntryFromRequest(CreateDiaryEntryRequest createRequest, string twinId)
        {
            return new DiaryEntry
            {
                Id = Guid.NewGuid().ToString(),
                TwinId = twinId,
                Titulo = createRequest.Titulo,
                Descripcion = createRequest.Descripcion,
                Fecha = createRequest.Fecha,
                TipoActividad = createRequest.TipoActividad,
                LabelActividad = createRequest.LabelActividad,
                Ubicacion = createRequest.Ubicacion,
                Latitud = createRequest.Latitud,
                Longitud = createRequest.Longitud,
                
                // Nuevos campos de ubicación detallada y contacto
                Pais = createRequest.Pais,
                Ciudad = createRequest.Ciudad,
                Estado = createRequest.EstadoProvincia,
                CodigoPostal = createRequest.CodigoPostal,
                DireccionEspecifica = createRequest.DireccionEspecifica,
                Telefono = createRequest.Telefono,
                Website = createRequest.Website,
                DistritoColonia = createRequest.DistritoColonia,
                
                EstadoEmocional = createRequest.EstadoEmocional,
                NivelEnergia = createRequest.NivelEnergia,
                GastoTotal = createRequest.GastoTotal,
                ProductosComprados = createRequest.ProductosComprados,
                TiendaLugar = createRequest.TiendaLugar,
                MetodoPago = createRequest.MetodoPago,
                CategoriaCompra = createRequest.CategoriaCompra,
                SatisfaccionCompra = createRequest.SatisfaccionCompra,

                // Food fields
                CostoComida = createRequest.CostoComida,
                RestauranteLugar = createRequest.RestauranteLugar,
                TipoCocina = createRequest.TipoCocina,
                PlatosOrdenados = createRequest.PlatosOrdenados,
                CalificacionComida = createRequest.CalificacionComida,
                AmbienteComida = createRequest.AmbienteComida,
                RecomendariaComida = createRequest.RecomendariaComida,

                // Travel fields
                CostoViaje = createRequest.CostoViaje,
                DestinoViaje = createRequest.DestinoViaje,
                TransporteViaje = createRequest.TransporteViaje,
                PropositoViaje = createRequest.PropositoViaje,
                CalificacionViaje = createRequest.CalificacionViaje,
                DuracionViaje = createRequest.DuracionViaje,

                // Entertainment fields
                CostoEntretenimiento = createRequest.CostoEntretenimiento,
                CalificacionEntretenimiento = createRequest.CalificacionEntretenimiento,
                TipoEntretenimiento = createRequest.TipoEntretenimiento,
                TituloNombre = createRequest.TituloNombre,
                LugarEntretenimiento = createRequest.LugarEntretenimiento,

                // Exercise fields
                CostoEjercicio = createRequest.CostoEjercicio,
                EnergiaPostEjercicio = createRequest.EnergiaPostEjercicio,
                CaloriasQuemadas = createRequest.CaloriasQuemadas,
                TipoEjercicio = createRequest.TipoEjercicio,
                DuracionEjercicio = createRequest.DuracionEjercicio,
                IntensidadEjercicio = createRequest.IntensidadEjercicio,
                LugarEjercicio = createRequest.LugarEjercicio,
                RutinaEspecifica = createRequest.RutinaEspecifica,

                // Study fields
                CostoEstudio = createRequest.CostoEstudio,
                DificultadEstudio = createRequest.DificultadEstudio,
                EstadoAnimoPost = createRequest.EstadoAnimoPost,
                MateriaTema = createRequest.MateriaTema,
                MaterialEstudio = createRequest.MaterialEstudio,
                DuracionEstudio = createRequest.DuracionEstudio,
                ProgresoEstudio = createRequest.ProgresoEstudio,

                // Work fields
                HorasTrabajadas = createRequest.HorasTrabajadas,
                ProyectoPrincipal = createRequest.ProyectoPrincipal,
                ReunionesTrabajo = createRequest.ReunionesTrabajo,
                LogrosHoy = createRequest.LogrosHoy,
                DesafiosTrabajo = createRequest.DesafiosTrabajo,
                MoodTrabajo = createRequest.MoodTrabajo,

                // Health fields
                CostoSalud = createRequest.CostoSalud,
                TipoConsulta = createRequest.TipoConsulta,
                ProfesionalCentro = createRequest.ProfesionalCentro,
                MotivoConsulta = createRequest.MotivoConsulta,
                TratamientoRecetado = createRequest.TratamientoRecetado,
                ProximaCita = createRequest.ProximaCita,

                // Call fields
                ContactoLlamada = createRequest.ContactoLlamada,
                DuracionLlamada = createRequest.DuracionLlamada,
                MotivoLlamada = createRequest.MotivoLlamada,
                TemasConversacion = createRequest.TemasConversacion,
                TipoLlamada = createRequest.TipoLlamada,
                SeguimientoLlamada = createRequest.SeguimientoLlamada,

                Participantes = createRequest.Participantes,
                PathFile = createRequest.PathFile,
                FechaCreacion = DateTime.UtcNow,
                FechaModificacion = DateTime.UtcNow
            };
        }

        private async Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, string errorMessage, HttpStatusCode statusCode)
        {
            var response = req.CreateResponse(statusCode);
            AddCorsHeaders(response, req);
            await response.WriteStringAsync(JsonSerializer.Serialize(new
            {
                success = false,
                errorMessage
            }));
            return response;
        }

        private void AddCorsHeaders(HttpResponseData response, HttpRequestData request)
        {
            var originHeader = request.Headers.FirstOrDefault(h => h.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase));
            var origin = originHeader.Key != null ? originHeader.Value?.FirstOrDefault() : null;

            var allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173", "http://127.0.0.1:3000" };

            if (!string.IsNullOrEmpty(origin) && allowedOrigins.Contains(origin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", origin);
            }
            else
            {
                response.Headers.Add("Access-Control-Allow-Origin", "*");
            }

            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization, Accept, Origin, User-Agent");
            response.Headers.Add("Access-Control-Max-Age", "3600");
        }

        private DiaryEntryQuery ParseDiaryQuery(HttpRequestData req)
        {
            var query = new DiaryEntryQuery();
            try
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
                // Activity type filter  
                if (!string.IsNullOrEmpty(queryParams["tipoActividad"]))
                {
                    query.TipoActividad = queryParams["tipoActividad"];
                }
                // Date range filters  
                if (DateTime.TryParse(queryParams["fechaDesde"], out var fechaDesde))
                {
                    query.FechaDesde = fechaDesde;
                }
                if (DateTime.TryParse(queryParams["fechaHasta"], out var fechaHasta))
                {
                    query.FechaHasta = fechaHasta;
                }
                // Location filter  
                if (!string.IsNullOrEmpty(queryParams["ubicacion"]))
                {
                    query.Ubicacion = queryParams["ubicacion"];
                }
                // Emotional state filter  
                if (!string.IsNullOrEmpty(queryParams["estadoEmocional"]))
                {
                    query.EstadoEmocional = queryParams["estadoEmocional"];
                }
                // Energy level filter  
                if (int.TryParse(queryParams["nivelEnergiaMin"], out var nivelEnergiaMin))
                {
                    query.NivelEnergiaMin = nivelEnergiaMin;
                }
                // Spending filter  
                if (decimal.TryParse(queryParams["gastoMaximo"], out var gastoMaximo))
                {
                    query.GastoMaximo = gastoMaximo;
                }
                // Search term  
                if (!string.IsNullOrEmpty(queryParams["searchTerm"]))
                {
                    query.SearchTerm = queryParams["searchTerm"];
                }
                // Pagination  
                if (int.TryParse(queryParams["page"], out var page) && page > 0)
                {
                    query.Page = page;
                }
                if (int.TryParse(queryParams["pageSize"], out var pageSize) && pageSize > 0)
                {
                    query.PageSize = Math.Min(pageSize, 100); // Max 100 items per page  
                }
                // Sorting  
                if (!string.IsNullOrEmpty(queryParams["sortBy"]))
                {
                    query.SortBy = queryParams["sortBy"];
                }
                if (!string.IsNullOrEmpty(queryParams["sortDirection"]))
                {
                    query.SortDirection = queryParams["sortDirection"];
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error parsing query parameters, using defaults");
            }
            return query;
        }

        private string GetBoundary(string contentType)
        {
            if (string.IsNullOrEmpty(contentType))
                return string.Empty;

            var boundaryMatch = Regex.Match(contentType, @"boundary=([^;]+)", RegexOptions.IgnoreCase);
            return boundaryMatch.Success ? boundaryMatch.Groups[1].Value.Trim('"', ' ') : string.Empty;
        }

        private async Task<List<MultipartFormDataPart>> ParseMultipartDataAsync(byte[] bodyBytes, string boundary)
        {
            var parts = new List<MultipartFormDataPart>();
            var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
            var partsList = SplitByteArray(bodyBytes, boundaryBytes);
            foreach (var partBytes in partsList)
            {
                if (partBytes.Length == 0)
                    continue;

                var headerSeparator = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };
                var headerEndIndex = FindBytePattern(partBytes, headerSeparator);
                if (headerEndIndex == -1)
                    continue;

                var headerBytes = partBytes.Take(headerEndIndex).ToArray();
                var contentBytes = partBytes.Skip(headerEndIndex + 4).ToArray();
                while (contentBytes.Length > 0 && (contentBytes[contentBytes.Length - 1] == 0x0A || contentBytes[contentBytes.Length - 1] == 0x0D || contentBytes[contentBytes.Length - 1] == 0x2D))
                {
                    Array.Resize(ref contentBytes, contentBytes.Length - 1);
                }

                var headers = Encoding.UTF8.GetString(headerBytes);
                var part = new MultipartFormDataPart();
                var dispositionMatch = Regex.Match(headers, @"Content-Disposition:\s*form-data;\s*name=""([^""]+)""(?:;\s*filename=""([^""]+)"")?", RegexOptions.IgnoreCase);
                if (dispositionMatch.Success)
                {
                    part.Name = dispositionMatch.Groups[1].Value;
                    if (dispositionMatch.Groups[2].Success)
                        part.FileName = dispositionMatch.Groups[2].Value;
                }

                var contentTypeMatch = Regex.Match(headers, @"Content-Type:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                if (contentTypeMatch.Success)
                    part.ContentType = contentTypeMatch.Groups[1].Value.Trim();

                if (!string.IsNullOrEmpty(part.FileName) || (!string.IsNullOrEmpty(part.ContentType) && (part.ContentType.StartsWith("image/") || part.ContentType.StartsWith("application/"))))
                {
                    part.Data = contentBytes;
                }
                else
                {
                    part.StringValue = Encoding.UTF8.GetString(contentBytes).Trim();
                }

                parts.Add(part);
            }
            return parts;
        }

        private List<byte[]> SplitByteArray(byte[] source, byte[] separator)
        {
            var result = new List<byte[]>();
            var index = 0;

            while (index < source.Length)
            {
                var nextIndex = FindBytePattern(source, separator, index);
                if (nextIndex == -1)
                {
                    if (index < source.Length)
                        result.Add(source.Skip(index).ToArray());
                    break;
                }
                if (nextIndex > index)
                    result.Add(source.Skip(index).Take(nextIndex - index).ToArray());
                index = nextIndex + separator.Length;
            }
            return result;
        }

        private int FindBytePattern(byte[] source, byte[] pattern, int startIndex = 0)
        {
            for (int i = startIndex; i <= source.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (source[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private bool IsValidReceiptFile(string fileName, byte[] fileData)
        {
            if (string.IsNullOrEmpty(fileName) || fileData == null || fileData.Length == 0)
                return false;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var validExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf" };
            if (!validExtensions.Contains(extension))
                return false;

            if (fileData.Length < 4)
                return false;

            if (fileData[0] == 0xFF && fileData[1] == 0xD8 && fileData[2] == 0xFF) return true; // JPEG  
            if (fileData[0] == 0x89 && fileData[1] == 0x50 && fileData[2] == 0x4E && fileData[3] == 0x47) return true; // PNG  
            if (fileData[0] == 0x47 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x38) return true; // GIF  
            if (fileData[0] == 0x25 && fileData[1] == 0x50 && fileData[2] == 0x44 && fileData[3] == 0x46) return true; // PDF  
            if (fileData.Length >= 12 && fileData[0] == 0x52 && fileData[1] == 0x49 && fileData[2] == 0x46 && fileData[3] == 0x46 && fileData[8] == 0x57 && fileData[9] == 0x45 && fileData[10] == 0x42 && fileData[11] == 0x50) return true; // WEBP  

            return true; // Allow other formats for now  
        }

        private string GetFileExtensionFromBytes(byte[] fileBytes)
        {
            if (fileBytes == null || fileBytes.Length == 0)
                return ".jpg";

            if (fileBytes.Length >= 3 && fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF) return ".jpg"; // JPEG  
            if (fileBytes.Length >= 8 && fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47) return ".png"; // PNG  
            if (fileBytes.Length >= 6 && fileBytes[0] == 0x47 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46) return ".gif"; // GIF  
            if (fileBytes.Length >= 4 && fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && fileBytes[2] == 0x44 && fileBytes[3] == 0x46) return ".pdf"; // PDF  
            if (fileBytes.Length >= 12 && fileBytes[0] == 0x52 && fileBytes[1] == 0x49 && fileBytes[2] == 0x46 && fileBytes[3] == 0x46 && fileBytes[8] == 0x57 && fileBytes[9] == 0x45 && fileBytes[10] == 0x42 && fileBytes[11] == 0x50) return ".webp"; // WEBP  

            return ".jpg"; // Default fallback  
        }

        private string GetMimeTypeFromExtension(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        private async Task<bool> UpdateDiaryEntryReceiptPath(DiaryEntry entry, string receiptType, string receiptPath)
        {
            try
            {
                entry.PathFile = receiptPath; // Update only the PathFile  
                return await _diaryService.UpdateDiaryEntryAsync(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating diary entry with file path");
                return false;
            }
        }

        private CreateDiaryEntryRequest ParseDiaryEntryFromMultipart(List<MultipartFormDataPart> parts)
        {
            var request = new CreateDiaryEntryRequest();

            string GetStringValue(string name) => parts.FirstOrDefault(p => p.Name == name)?.StringValue ?? string.Empty;
            int? GetIntValue(string name) => int.TryParse(GetStringValue(name), out var value) ? value : null;
            decimal? GetDecimalValue(string name) => decimal.TryParse(GetStringValue(name), out var value) ? value : null;
            double? GetDoubleValue(string name) => double.TryParse(GetStringValue(name), out var value) ? value : null;
            bool? GetBoolValue(string name) => bool.TryParse(GetStringValue(name), out var value) ? value : null;
            DateTime? GetDateTimeValue(string name) => DateTime.TryParse(GetStringValue(name), out var value) ? value : null;

            // Basic information
            request.Titulo = GetStringValue("titulo");
            request.Descripcion = GetStringValue("descripcion");
            if (DateTime.TryParse(GetStringValue("fecha"), out var fecha))
                request.Fecha = fecha;

            // Activity type and location
            request.TipoActividad = GetStringValue("tipoActividad");
            request.LabelActividad = GetStringValue("labelActividad");
            request.Ubicacion = GetStringValue("ubicacion");
            request.Latitud = GetDoubleValue("latitud");
            request.Longitud = GetDoubleValue("longitud");

            // Nuevos campos de ubicación detallada y contacto
            request.Pais = GetStringValue("pais");
            request.Ciudad = GetStringValue("ciudad");
            request.EstadoProvincia = GetStringValue("estadoProvincia");
            request.CodigoPostal = GetStringValue("codigoPostal");
            request.DireccionEspecifica = GetStringValue("direccionEspecifica");
            request.Telefono = GetStringValue("telefono");
            request.Website = GetStringValue("website");
            request.DistritoColonia = GetStringValue("distritoColonia");

            // Emotional state and energy
            request.EstadoEmocional = GetStringValue("estadoEmocional");
            request.NivelEnergia = GetIntValue("nivelEnergia");

            // Shopping fields
            request.GastoTotal = GetDecimalValue("gastoTotal");
            request.ProductosComprados = GetStringValue("productosComprados");
            request.TiendaLugar = GetStringValue("tiendaLugar");
            request.MetodoPago = GetStringValue("metodoPago");
            request.CategoriaCompra = GetStringValue("categoriaCompra");
            request.SatisfaccionCompra = GetIntValue("satisfaccionCompra");

            // Food fields
            request.CostoComida = GetDecimalValue("costoComida");
            request.RestauranteLugar = GetStringValue("restauranteLugar");
            request.TipoCocina = GetStringValue("tipoCocina");
            request.PlatosOrdenados = GetStringValue("platosOrdenados");
            request.CalificacionComida = GetIntValue("calificacionComida");
            request.AmbienteComida = GetStringValue("ambienteComida");
            request.RecomendariaComida = GetBoolValue("recomendariaComida");

            // Travel fields
            request.CostoViaje = GetDecimalValue("costoViaje");
            request.DestinoViaje = GetStringValue("destinoViaje");
            request.TransporteViaje = GetStringValue("transporteViaje");
            request.PropositoViaje = GetStringValue("propositoViaje");
            request.CalificacionViaje = GetIntValue("calificacionViaje");
            request.DuracionViaje = GetIntValue("duracionViaje");

            // Entertainment fields
            request.CostoEntretenimiento = GetDecimalValue("costoEntretenimiento");
            request.CalificacionEntretenimiento = GetIntValue("calificacionEntretenimiento");
            request.TipoEntretenimiento = GetStringValue("tipoEntretenimiento");
            request.TituloNombre = GetStringValue("tituloNombre");
            request.LugarEntretenimiento = GetStringValue("lugarEntretenimiento");

            // Exercise fields
            request.CostoEjercicio = GetDecimalValue("costoEjercicio");
            request.EnergiaPostEjercicio = GetIntValue("energiaPostEjercicio");
            request.CaloriasQuemadas = GetIntValue("caloriasQuemadas");
            request.TipoEjercicio = GetStringValue("tipoEjercicio");
            request.DuracionEjercicio = GetIntValue("duracionEjercicio");
            request.IntensidadEjercicio = GetIntValue("intensidadEjercicio");
            request.LugarEjercicio = GetStringValue("lugarEjercicio");
            request.RutinaEspecifica = GetStringValue("rutinaEspecifica");

            // Study fields
            request.CostoEstudio = GetDecimalValue("costoEstudio");
            request.DificultadEstudio = GetIntValue("dificultadEstudio");
            request.EstadoAnimoPost = GetIntValue("estadoAnimoPost");
            request.MateriaTema = GetStringValue("materiaTema");
            request.MaterialEstudio = GetStringValue("materialEstudio");
            request.DuracionEstudio = GetIntValue("duracionEstudio");
            request.ProgresoEstudio = GetIntValue("progresoEstudio");

            // Work fields
            request.HorasTrabajadas = GetIntValue("horasTrabajadas");
            request.ProyectoPrincipal = GetStringValue("proyectoPrincipal");
            request.ReunionesTrabajo = GetIntValue("reunionesTrabajo");
            request.LogrosHoy = GetStringValue("logrosHoy");
            request.DesafiosTrabajo = GetStringValue("desafiosTrabajo");
            request.MoodTrabajo = GetIntValue("moodTrabajo");

            // Health fields
            request.CostoSalud = GetDecimalValue("costoSalud");
            request.TipoConsulta = GetStringValue("tipoConsulta");
            request.ProfesionalCentro = GetStringValue("profesionalCentro");
            request.MotivoConsulta = GetStringValue("motivoConsulta");
            request.TratamientoRecetado = GetStringValue("tratamientoRecetado");
            request.ProximaCita = GetDateTimeValue("proximaCita");

            // Call fields
            request.ContactoLlamada = GetStringValue("contactoLlamada");
            request.DuracionLlamada = GetIntValue("duracionLlamada");
            request.MotivoLlamada = GetStringValue("motivoLlamada");
            request.TemasConversacion = GetStringValue("temasConversacion");
            request.TipoLlamada = GetStringValue("tipoLlamada");
            request.SeguimientoLlamada = GetBoolValue("seguimientoLlamada");

            // NUEVO: Campos adicionales para análisis comprensivo
            request.PathFile = parts.FirstOrDefault(p => p.Name == "pathFile")?.StringValue;
            request.Participantes = parts.FirstOrDefault(p => p.Name == "participantes")?.StringValue;

            return request;
        }

        private UpdateDiaryEntryRequest ParseDiaryEntryUpdateFromMultipart(List<MultipartFormDataPart> parts)
        {
            var request = new UpdateDiaryEntryRequest();

            string GetStringValue(string name) => parts.FirstOrDefault(p => p.Name == name)?.StringValue;
            int? GetIntValue(string name) => int.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            decimal? GetDecimalValue(string name) => decimal.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            double? GetDoubleValue(string name) => double.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            bool? GetBoolValue(string name) => bool.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            DateTime? GetDateTimeValue(string name) => DateTime.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;

            // NUEVO: Verificar si los datos vienen como JSON serializado en el campo 'diaryData'
            var diaryDataPart = parts.FirstOrDefault(p => p.Name == "diaryData");
            if (diaryDataPart != null && !string.IsNullOrEmpty(diaryDataPart.StringValue))
            {
                try
                {
                    _logger.LogInformation("📄 Parseando datos desde campo 'diaryData' JSON: {JsonLength} chars", diaryDataPart.StringValue.Length);
                    
                    // Deserializar el JSON del campo diaryData
                    var diaryData = JsonSerializer.Deserialize<Dictionary<string, object>>(diaryDataPart.StringValue, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    // Función helper para extraer valores del JSON deserializado
                    T GetJsonValue<T>(string key, T defaultValue = default!)
                    {
                        if (!diaryData.TryGetValue(key, out var value) || value == null)
                            return defaultValue;

                        try
                        {
                            if (value is JsonElement jsonElement)
                            {
                                return ParseJsonElementForUpdate<T>(jsonElement, defaultValue);
                            }
                            else if (value is T directValue)
                            {
                                return directValue;
                            }
                            else
                            {
                                return (T)Convert.ChangeType(value, typeof(T));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error parsing JSON field '{Key}', using default", key);
                            return defaultValue;
                        }
                    }

                    // Parsear desde JSON
                    var tituloJson = GetJsonValue<string>("titulo");
                    if (!string.IsNullOrEmpty(tituloJson)) request.Titulo = tituloJson;

                    var descripcionJson = GetJsonValue<string>("descripcion");
                    if (!string.IsNullOrEmpty(descripcionJson)) request.Descripcion = descripcionJson;

                    // Fecha con manejo especial
                    var fechaStr = GetJsonValue<string>("fecha");
                    if (!string.IsNullOrEmpty(fechaStr) && DateTime.TryParse(fechaStr, out var fecha))
                        request.Fecha = fecha;

                    // Actividad y ubicación
                    request.TipoActividad = GetJsonValue<string>("tipoActividad");
                    if (!string.IsNullOrEmpty(request.TipoActividad)) request.TipoActividad = request.TipoActividad;
            
                    request.LabelActividad = GetJsonValue<string>("labelActividad");
                    if (!string.IsNullOrEmpty(request.LabelActividad)) request.LabelActividad = request.LabelActividad;
            
                    request.Ubicacion = GetJsonValue<string>("ubicacion");
                    if (!string.IsNullOrEmpty(request.Ubicacion)) request.Ubicacion = request.Ubicacion;
            
                    // Coordenadas
                    request.Latitud = GetJsonValue<double?>("latitud");
                    if (request.Latitud.HasValue) request.Latitud = request.Latitud;

                    request.Longitud = GetJsonValue<double?>("longitud");
                    if (request.Longitud.HasValue) request.Longitud = request.Longitud;

                    // Estado emocional y energía
                    request.EstadoEmocional = GetJsonValue<string>("estadoEmocional");
                    if (!string.IsNullOrEmpty(request.EstadoEmocional)) request.EstadoEmocional = request.EstadoEmocional;
            
                    request.NivelEnergia = GetJsonValue<int?>("nivelEnergia");
                    if (request.NivelEnergia.HasValue) request.NivelEnergia = request.NivelEnergia;

                    // Campos de comida (más comunes)
                    request.CostoComida = GetJsonValue<decimal?>("costoComida");
                    if (request.CostoComida.HasValue) request.CostoComida = request.CostoComida;

                    request.RestauranteLugar = GetJsonValue<string>("restauranteLugar");
                    if (!string.IsNullOrEmpty(request.RestauranteLugar)) request.RestauranteLugar = request.RestauranteLugar;
            
                    request.TipoCocina = GetJsonValue<string>("tipoCocina");
                    if (!string.IsNullOrEmpty(request.TipoCocina)) request.TipoCocina = request.TipoCocina;
            
                    request.PlatosOrdenados = GetJsonValue<string>("platosOrdenados");
                    if (!string.IsNullOrEmpty(request.PlatosOrdenados)) request.PlatosOrdenados = request.PlatosOrdenados;
            
                    request.CalificacionComida = GetJsonValue<int?>("calificacionComida");
                    if (request.CalificacionComida.HasValue) request.CalificacionComida = request.CalificacionComida;

                    request.AmbienteComida = GetJsonValue<string>("ambienteComida");
                    if (!string.IsNullOrEmpty(request.AmbienteComida)) request.AmbienteComida = request.AmbienteComida;
            
                    request.RecomendariaComida = GetJsonValue<bool?>("recomendariaComida");
                    if (request.RecomendariaComida.HasValue) request.RecomendariaComida = request.RecomendariaComida;

                    // TODO: Agregar más campos según se necesiten (shopping, travel, etc.)
                    // Por ahora implementamos los más críticos para resolver el problema inmediato

                    var participantesJson = GetJsonValue<string>("participantes");
                    if (!string.IsNullOrEmpty(participantesJson)) request.Participantes = participantesJson;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error parseando JSON del campo 'diaryData', fallback a campos individuales");
                    // Continuar con el parsing individual como fallback
                }
            }

            // FALLBACK: Parsing individual de campos (método original)
            _logger.LogInformation("📄 Parseando datos desde campos individuales de FormData");

            // Basic information - only update if provided
            var titulo = GetStringValue("titulo");
            if (!string.IsNullOrEmpty(titulo)) request.Titulo = titulo;
            
            var descripcion = GetStringValue("descripcion");
            if (!string.IsNullOrEmpty(descripcion)) request.Descripcion = descripcion;
            
            request.Fecha = GetDateTimeValue("fecha");

            // Activity type and location
            var tipoActividad = GetStringValue("tipoActividad");
            if (!string.IsNullOrEmpty(tipoActividad)) request.TipoActividad = tipoActividad;
            
            var labelActividad = GetStringValue("labelActividad");
            if (!string.IsNullOrEmpty(labelActividad)) request.LabelActividad = labelActividad;
            
            var ubicacion = GetStringValue("ubicacion");
            if (!string.IsNullOrEmpty(ubicacion)) request.Ubicacion = ubicacion;
            
            request.Latitud = GetDoubleValue("latitud");
            request.Longitud = GetDoubleValue("longitud");

            // Nuevos campos de ubicación detallada y contacto
            request.Pais = GetStringValue("pais");
            request.Ciudad = GetStringValue("ciudad");
            request.Estado = GetStringValue("estado");
            request.CodigoPostal = GetStringValue("codigoPostal");
            request.DireccionEspecifica = GetStringValue("direccionEspecifica");
            request.Telefono = GetStringValue("telefono");
            request.Website = GetStringValue("website");
            request.DistritoColonia = GetStringValue("distritoColonia");

            // Emotional state and energy
            var estadoEmocional = GetStringValue("estadoEmocional");
            if (!string.IsNullOrEmpty(estadoEmocional)) request.EstadoEmocional = estadoEmocional;
            
            request.NivelEnergia = GetIntValue("nivelEnergia");

            // Shopping fields
            request.GastoTotal = GetDecimalValue("gastoTotal");
            var productosComprados = GetStringValue("productosComprados");
            if (!string.IsNullOrEmpty(productosComprados)) request.ProductosComprados = productosComprados;
            
            var tiendaLugar = GetStringValue("tiendaLugar");
            if (!string.IsNullOrEmpty(tiendaLugar)) request.TiendaLugar = tiendaLugar;
            
            var metodoPago = GetStringValue("metodoPago");
            if (!string.IsNullOrEmpty(metodoPago)) request.MetodoPago = metodoPago;
            
            var categoriaCompra = GetStringValue("categoriaCompra");
            if (!string.IsNullOrEmpty(categoriaCompra)) request.CategoriaCompra = categoriaCompra;
            
            request.SatisfaccionCompra = GetIntValue("satisfaccionCompra");

            // Food fields
            request.CostoComida = GetDecimalValue("costoComida");
            var restauranteLugar = GetStringValue("restauranteLugar");
            if (!string.IsNullOrEmpty(restauranteLugar)) request.RestauranteLugar = restauranteLugar;
            
            var tipoCocina = GetStringValue("tipoCocina");
            if (!string.IsNullOrEmpty(tipoCocina)) request.TipoCocina = tipoCocina;
            
            var platosOrdenados = GetStringValue("platosOrdenados");
            if (!string.IsNullOrEmpty(platosOrdenados)) request.PlatosOrdenados = platosOrdenados;
            
            request.CalificacionComida = GetIntValue("calificacionComida");
            var ambienteComida = GetStringValue("ambienteComida");
            if (!string.IsNullOrEmpty(ambienteComida)) request.AmbienteComida = ambienteComida;
            
            request.RecomendariaComida = GetBoolValue("recomendariaComida");

            // Travel fields
            request.CostoViaje = GetDecimalValue("costoViaje");
            var destinoViaje = GetStringValue("destinoViaje");
            if (!string.IsNullOrEmpty(destinoViaje)) request.DestinoViaje = destinoViaje;
            
            var transporteViaje = GetStringValue("transporteViaje");
            if (!string.IsNullOrEmpty(transporteViaje)) request.TransporteViaje = transporteViaje;
            
            var propositoViaje = GetStringValue("propositoViaje");
            if (!string.IsNullOrEmpty(propositoViaje)) request.PropositoViaje = propositoViaje;
            
            request.CalificacionViaje = GetIntValue("calificacionViaje");
            request.DuracionViaje = GetIntValue("duracionViaje");

            // Entertainment fields
            request.CostoEntretenimiento = GetDecimalValue("costoEntretenimiento");
            request.CalificacionEntretenimiento = GetIntValue("calificacionEntretenimiento");
            var tipoEntretenimiento = GetStringValue("tipoEntretenimiento");
            if (!string.IsNullOrEmpty(tipoEntretenimiento)) request.TipoEntretenimiento = tipoEntretenimiento;
            
            var tituloNombre = GetStringValue("tituloNombre");
            if (!string.IsNullOrEmpty(tituloNombre)) request.TituloNombre = tituloNombre;
            
            var lugarEntretenimiento = GetStringValue("lugarEntretenimiento");
            if (!string.IsNullOrEmpty(lugarEntretenimiento)) request.LugarEntretenimiento = lugarEntretenimiento;

            // Exercise fields
            request.CostoEjercicio = GetDecimalValue("costoEjercicio");
            request.EnergiaPostEjercicio = GetIntValue("energiaPostEjercicio");
            request.CaloriasQuemadas = GetIntValue("caloriasQuemadas");
            var tipoEjercicio = GetStringValue("tipoEjercicio");
            if (!string.IsNullOrEmpty(tipoEjercicio)) request.TipoEjercicio = tipoEjercicio;
            
            request.DuracionEjercicio = GetIntValue("duracionEjercicio");
            request.IntensidadEjercicio = GetIntValue("intensidadEjercicio");
            var lugarEjercicio = GetStringValue("lugarEjercicio");
            if (!string.IsNullOrEmpty(lugarEjercicio)) request.LugarEjercicio = lugarEjercicio;
            
            var rutinaEspecifica = GetStringValue("rutinaEspecifica");
            if (!string.IsNullOrEmpty(rutinaEspecifica)) request.RutinaEspecifica = rutinaEspecifica;

            // Study fields
            request.CostoEstudio = GetDecimalValue("costoEstudio");
            request.DificultadEstudio = GetIntValue("dificultadEstudio");
            request.EstadoAnimoPost = GetIntValue("estadoAnimoPost");
            var materiaTema = GetStringValue("materiaTema");
            if (!string.IsNullOrEmpty(materiaTema)) request.MateriaTema = materiaTema;
            
            var materialEstudio = GetStringValue("materialEstudio");
            if (!string.IsNullOrEmpty(materialEstudio)) request.MaterialEstudio = materialEstudio;
            
            request.DuracionEstudio = GetIntValue("duracionEstudio");
            request.ProgresoEstudio = GetIntValue("progresoEstudio");

            // Work fields
            request.HorasTrabajadas = GetIntValue("horasTrabajadas");
            var proyectoPrincipal = GetStringValue("proyectoPrincipal");
            if (!string.IsNullOrEmpty(proyectoPrincipal)) request.ProyectoPrincipal = proyectoPrincipal;
            
            request.ReunionesTrabajo = GetIntValue("reunionesTrabajo");
            var logrosHoy = GetStringValue("logrosHoy");
            if (!string.IsNullOrEmpty(logrosHoy)) request.LogrosHoy = logrosHoy;
            
            var desafiosTrabajo = GetStringValue("desafiosTrabajo");
            if (!string.IsNullOrEmpty(desafiosTrabajo)) request.DesafiosTrabajo = desafiosTrabajo;
            
            request.MoodTrabajo = GetIntValue("moodTrabajo");

            // Health fields
            request.CostoSalud = GetDecimalValue("costoSalud");
            var tipoConsulta = GetStringValue("tipoConsulta");
            if (!string.IsNullOrEmpty(tipoConsulta)) request.TipoConsulta = tipoConsulta;
            
            var profesionalCentro = GetStringValue("profesionalCentro");
            if (!string.IsNullOrEmpty(profesionalCentro)) request.ProfesionalCentro = profesionalCentro;
            
            var motivoConsulta = GetStringValue("motivoConsulta");
            if (!string.IsNullOrEmpty(motivoConsulta)) request.MotivoConsulta = motivoConsulta;
            
            var tratamientoRecetado = GetStringValue("tratamientoRecetado");
            if (!string.IsNullOrEmpty(tratamientoRecetado)) request.TratamientoRecetado = tratamientoRecetado;
            
            request.ProximaCita = GetDateTimeValue("proximaCita");

            // Call fields
            var contactoLlamada = GetStringValue("contactoLlamada");
            if (!string.IsNullOrEmpty(contactoLlamada)) request.ContactoLlamada = contactoLlamada;
            
            request.DuracionLlamada = GetIntValue("duracionLlamada");
            var motivoLlamada = GetStringValue("motivoLlamada");
            if (!string.IsNullOrEmpty(motivoLlamada)) request.MotivoLlamada = motivoLlamada;
            
            var temasConversacion = GetStringValue("temasConversacion");
            if (!string.IsNullOrEmpty(temasConversacion)) request.TemasConversacion = temasConversacion;
            
            var tipoLlamada = GetStringValue("tipoLlamada");
            if (!string.IsNullOrEmpty(tipoLlamada)) request.TipoLlamada = tipoLlamada;
            
            request.SeguimientoLlamada = GetBoolValue("seguimientoLlamada");

            // Participants
            var participantes = GetStringValue("participantes");
            if (!string.IsNullOrEmpty(participantes)) request.Participantes = participantes;

            // File handling - look for any file part
            var filePart = parts.FirstOrDefault(p => 
                (p.Name == "file" || p.Name == "pathFile" || p.Name == "recibo" || p.Name.Contains("recibo")) && 
                p.Data != null && p.Data.Length > 0);

            if (filePart != null)
            {
                // Store the fact that a file was uploaded - the actual file handling is done separately
                request.PathFile = "file_uploaded"; // Placeholder
            }

            return request;
        }

        // Método helper para parsing de JsonElement en updates
        private T ParseJsonElementForUpdate<T>(JsonElement jsonElement, T defaultValue)
        {
            try
            {
                var targetType = typeof(T);
                var underlyingType = Nullable.GetUnderlyingType(targetType);
                var actualType = underlyingType ?? targetType;

                if (jsonElement.ValueKind == JsonValueKind.Null)
                {
                    return defaultValue;
                }

                if (actualType == typeof(string))
                {
                    return (T)(object)(jsonElement.GetString() ?? defaultValue?.ToString() ?? string.Empty);
                }

                if (actualType == typeof(int))
                {
                    if (jsonElement.TryGetInt32(out var intValue))
                        return (T)(object)intValue;
                    if (jsonElement.TryGetDouble(out var doubleValue))
                        return (T)(object)(int)doubleValue;
                    return defaultValue;
                }

                if (actualType == typeof(decimal))
                {
                    if (jsonElement.TryGetDecimal(out var decimalValue))
                        return (T)(object)decimalValue;
                    if (jsonElement.TryGetDouble(out var doubleValue))
                        return (T)(object)(decimal)doubleValue;
                    return defaultValue;
                }

                if (actualType == typeof(double))
                {
                    return jsonElement.TryGetDouble(out var doubleValue) ? (T)(object)doubleValue : defaultValue;
                }

                if (actualType == typeof(bool))
                {
                    if (jsonElement.ValueKind == JsonValueKind.True) return (T)(object)true;
                    if (jsonElement.ValueKind == JsonValueKind.False) return (T)(object)false;
                    return defaultValue;
                }

                if (actualType == typeof(DateTime))
                {
                    return jsonElement.TryGetDateTime(out var dateValue) ? (T)(object)dateValue : defaultValue;
                }

                // Try generic conversion
                var stringValue = jsonElement.GetString();
                if (!string.IsNullOrEmpty(stringValue))
                {
                    return (T)Convert.ChangeType(stringValue, actualType);
                }

                return defaultValue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Error in ParseJsonElementForUpdate for type {Type}", typeof(T).Name);
                return defaultValue;
            }
        }
 
        
 
        private bool IsImageFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
            return imageExtensions.Contains(extension);
        }

        private string DetermineActivityTypeFromFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "general";

            var lowerFileName = fileName.ToLowerInvariant();

            // Buscar palabras clave en el nombre del archivo
            if (lowerFileName.Contains("comida") || lowerFileName.Contains("restaurante") || lowerFileName.Contains("food"))
                return "comida";
            
            if (lowerFileName.Contains("compra") || lowerFileName.Contains("tienda") || lowerFileName.Contains("shop"))
                return "compra";
            
            if (lowerFileName.Contains("viaje") || lowerFileName.Contains("taxi") || lowerFileName.Contains("uber") || lowerFileName.Contains("travel"))
                return "viaje";
            
            if (lowerFileName.Contains("entretenimiento") || lowerFileName.Contains("cine") || lowerFileName.Contains("teatro"))
                return "entretenimiento";
            
            if (lowerFileName.Contains("salud") || lowerFileName.Contains("hospital") || lowerFileName.Contains("doctor"))
                return "salud";

            // Default para recibos generales
            return "general";
        }

        private async Task ExecuteAnalysisWithoutReceipt(DiaryEntry diaryEntry)
        {
            try
            {
                _logger.LogInformation("🧪 Ejecutando análisis comprensivo SIN recibo para prueba de funcionalidad");
                
                // Crear datos simulados de recibo para probar la funcionalidad
                var simulatedReceiptData = new ReceiptExtractedData
                {
                    Establecimiento = $"Establecimiento simulado para {diaryEntry.TipoActividad}",
                    TipoEstablecimiento = diaryEntry.TipoActividad?.ToLowerInvariant() ?? "general",
                    Fecha = diaryEntry.Fecha.Date,
                    Hora = diaryEntry.Fecha.ToString("HH:mm"),
                    MontoTotal = diaryEntry.GastoTotal ?? 0,
                    Moneda = "MXN",
                    Direccion = diaryEntry.Ubicacion ?? "Ubicación no especificada",
                    MetodoPago = diaryEntry.MetodoPago ?? "No especificado",
                    ActivityType = diaryEntry.TipoActividad ?? "general",
                    ExtractedAt = DateTime.UtcNow,
                    Confianza = 0.5, // Baja confianza porque son datos simulados
                    Observaciones = "Datos simulados para prueba de análisis comprensivo sin recibo real"
                };

                // Agregar productos basados en el tipo de actividad
                if (diaryEntry.TipoActividad?.ToLowerInvariant() == "comida" && !string.IsNullOrEmpty(diaryEntry.PlatosOrdenados))
                {
                    simulatedReceiptData.Productos.Add(new ReceiptProduct
                    {
                        Nombre = diaryEntry.PlatosOrdenados,
                        Cantidad = 1,
                        Precio = diaryEntry.CostoComida ?? 0
                    });
                }
                else if (diaryEntry.TipoActividad?.ToLowerInvariant() == "compra" && !string.IsNullOrEmpty(diaryEntry.ProductosComprados))
                {
                    simulatedReceiptData.Productos.Add(new ReceiptProduct
                    {
                        Nombre = diaryEntry.ProductosComprados,
                        Cantidad = 1,
                        Precio = diaryEntry.GastoTotal ?? 0
                    });
                }
                else
                {
                    simulatedReceiptData.Productos.Add(new ReceiptProduct
                    {
                        Nombre = $"Producto/Servicio de {diaryEntry.TipoActividad}",
                        Cantidad = 1,
                        Precio = diaryEntry.GastoTotal ?? 0
                    });
                }

                var simulatedExtractionResult = new ReceiptExtractionResult
                {
                    Success = true,
                    FileName = "datos_simulados.jpg",
                    ActivityType = diaryEntry.TipoActividad ?? "general",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTimeMs = 100,
                    RawAIResponse = "Datos simulados para prueba",
                    ExtractedData = simulatedReceiptData
                };

                _logger.LogInformation("🎯 Llamando a GenerateComprehensiveAnalysisAsync con datos simulados...");
                
                var comprehensiveAnalysis = await _diaryAgent.GenerateComprehensiveAnalysisAsync(
                    simulatedExtractionResult, 
                    diaryEntry
                );

                if (comprehensiveAnalysis.Success)
                {
                    // ✅ USAR LA INSTANCIA YA INYECTADA con TwinId del diaryEntry:
                    var indexResult = await _diarySearchIndex.IndexDiaryAnalysisAsync(comprehensiveAnalysis, diaryEntry.TwinId);

                     _logger.LogInformation("✅ ===== ANÁLISIS COMPRENSIVO COMPLETADO CON DATOS SIMULADOS ===== Tiempo: {ProcessingTime}ms", 
                        comprehensiveAnalysis.ProcessingTimeMs);
                }
                else
                {
                    _logger.LogWarning("⚠️ ===== ANÁLISIS COMPRENSIVO CON DATOS SIMULADOS FALLÓ ===== Error: {ErrorMessage}", 
                        comprehensiveAnalysis.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en análisis comprensivo con datos simulados");
            }
        }

        [Function("GetDiaryAnalysis")]
        public async Task<HttpResponseData> GetDiaryAnalysis([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "twins/{twinId}/diary/{entryId}/analysis")] HttpRequestData req, string twinId, string entryId)
        {
            _logger.LogInformation("🧠 GetDiaryAnalysis function triggered for DiaryEntryId: {EntryId}, TwinId: {TwinId}", entryId, twinId);
            
            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(entryId))
                {
                    return await CreateErrorResponse(req, "TwinId and EntryId parameters are required", HttpStatusCode.BadRequest);
                }

                // Get the comprehensive analysis from the search index
                var analysisResponse = await _diarySearchIndex.GetDiaryAnalysisByEntryIdAsync(entryId, twinId);

                var response = req.CreateResponse(analysisResponse.Success ? HttpStatusCode.OK : HttpStatusCode.NotFound);
                AddCorsHeaders(response, req);
                response.Headers.Add("Content-Type", "application/json");

                if (analysisResponse.Success && analysisResponse.Analysis != null)
                {
                    // Return the comprehensive analysis in the format requested
                    var responseData = new
                    {
                        success = true,
                        diaryEntryId = analysisResponse.Analysis.DiaryEntryId,
                        twinId = analysisResponse.Analysis.TwinId,
                        executiveSummary = analysisResponse.Analysis.ExecutiveSummary,
                        detailedHtmlReport = analysisResponse.Analysis.DetailedHtmlReport,
                        processingTimeMs = analysisResponse.Analysis.ProcessingTimeMs,
                        analyzedAt = analysisResponse.Analysis.AnalyzedAt,
                        metadataKeys = analysisResponse.Analysis.MetadataKeys,
                        metadataValues = analysisResponse.Analysis.MetadataValues,
                        contenidoCompleto = analysisResponse.Analysis.ContenidoCompleto,
                        errorMessage = analysisResponse.Analysis.ErrorMessage
                    };

                    await response.WriteStringAsync(JsonSerializer.Serialize(responseData, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = true
                    }));

                    _logger.LogInformation("✅ Diary analysis retrieved successfully for DiaryEntryId: {EntryId}", entryId);
                }
                else
                {
                    var errorResponse = new
                    {
                        success = false,
                        diaryEntryId = entryId,
                        twinId = twinId,
                        error = analysisResponse.Error ?? "Comprehensive analysis not found for this diary entry",
                        message = "No analysis available. The diary entry might not have been processed yet or analysis failed."
                    };

                    await response.WriteStringAsync(JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
                    }));

                    _logger.LogInformation("📭 No analysis found for DiaryEntryId: {EntryId}, TwinId: {TwinId}", entryId, twinId);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting diary analysis for DiaryEntryId: {EntryId}, TwinId: {TwinId}", entryId, twinId);
                return await CreateErrorResponse(req, $"Internal server error: {ex.Message}", HttpStatusCode.InternalServerError);
            }
        }
 
    }
}