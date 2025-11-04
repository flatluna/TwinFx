using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TwinFx.Models;
using TwinFx.Services;
using TwinFx.Agents;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
    public class DiaryFunctionUpdates
    {
        private readonly ILogger<DiaryFunctionUpdates> _logger;
        private readonly DiaryCosmosDbService _diaryService;
        private readonly IConfiguration _configuration;
        private readonly DiaryAgent _diaryAgent;
        private readonly DiarySearchIndex _diarySearchIndex;
        public DiaryFunctionUpdates(ILogger<DiaryFunctionUpdates> logger, DiaryCosmosDbService diaryService,
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
       

       

        [Function("UpdateDiaryEntry")]
        public async Task<HttpResponseData> UpdateDiaryEntry([HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "twins/{twinId}/diary/{entryId}")] HttpRequestData req, string twinId, string entryId)
        {
            _logger.LogInformation("📔 UpdateDiaryEntry function triggered for Entry: {EntryId}, Twin: {TwinId}", entryId, twinId);
            var startTime = DateTime.UtcNow;
          
             
            try
            {
                if (string.IsNullOrEmpty(twinId) || string.IsNullOrEmpty(entryId))
                {
                    return await CreateErrorResponse(req, "Twin ID and Entry ID parameters are required", HttpStatusCode.BadRequest);
                }

                // First check if the entry exists
                var existingEntry = await _diaryService.GetDiaryEntryByIdAsync(entryId, twinId);
                if (existingEntry == null)
                {
                    return await CreateErrorResponse(req, "Diary entry not found", HttpStatusCode.NotFound);
                }

                var contentType = GetContentType(req);
                UpdateDiaryEntryRequest updateRequest;

                if (contentType.Contains("multipart/form-data"))
                {
                    // ENFOQUE OPTIMIZADO: Leer el body UNA SOLA VEZ y procesar todo
                    var bodyBytes = await GetRequestBodyBytes(req);
                    var boundary = GetBoundary(contentType);
                    var parts = await ParseMultipartDataAsync(bodyBytes, boundary);
                    
                    // Buscar el campo diaryData directamente en los parts parseados
                    var diaryDataPart = parts.FirstOrDefault(p => p.Name == "diaryData");
                    if (diaryDataPart != null && !string.IsNullOrEmpty(diaryDataPart.StringValue))
                    {
                        try
                        {
                            updateRequest =  Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateDiaryEntryRequest>(diaryDataPart.StringValue);
                            
                            _logger.LogInformation("📋 Diary data extracted from diaryData field: {DiaryDataLength} chars", diaryDataPart.StringValue.Length);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error deserializing diary data from diaryData field, using multipart parsing fallback");
                            // Fallback al método anterior
                            updateRequest = ParseDiaryEntryUpdateFromMultipart(parts);
                        }
                    }
                    else
                    {
                        _logger.LogInformation("📄 No diaryData field found, using standard multipart parsing");
                        // Fallback al método anterior
                        updateRequest = ParseDiaryEntryUpdateFromMultipart(parts);
                    }
                    
                    // Procesar archivos si existen usando los mismos parts
                    var filePart = parts.FirstOrDefault(p => 
                        (p.Name == "file" || p.Name == "pathFile" || p.Name == "recibo" || p.Name.Contains("recibo")) && 
                        p.Data != null && p.Data.Length > 0);

                    if (filePart != null)
                    {
                        var uploadResult = await UploadFileFromPart(filePart, twinId, entryId, existingEntry);
                        if (!string.IsNullOrEmpty(uploadResult))
                        {
                           
                            updateRequest.PathFile = uploadResult;
                           
                            _logger.LogInformation("📎 File uploaded successfully during update: {FilePath}", uploadResult);
                        }
                        else
                        {
                            _logger.LogInformation("📎 File upload failed or no valid file found");
                        }
                    }
                    else
                    {
                        _logger.LogInformation("📎 No file found in multipart data for update");
                    }
                }
                else
                {
                    updateRequest = await ProcessJsonRequestForUpdate(req);
                }

                _logger.LogInformation("📔 Updating diary entry: {EntryId} for Twin ID: {TwinId}", entryId, twinId);
                
                // Update the existing entry with new values
                UpdateDiaryEntryFromRequest(existingEntry, updateRequest);
                existingEntry.FechaModificacion = DateTime.UtcNow;

                var success = await _diaryService.UpdateDiaryEntryAsync(existingEntry);
                var finalResponse = req.CreateResponse(success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError);
                AddCorsHeaders(finalResponse, req);
                finalResponse.Headers.Add("Content-Type", "application/json");

                if (success)
                {
                    // Generate SAS URL if there's a file
                    if (!string.IsNullOrEmpty(existingEntry.PathFile))
                    {
                        var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
                        var dataLakeClient = dataLakeFactory.CreateClient(twinId);
                        var sasUrl = await dataLakeClient.GenerateSasUrlAsync(existingEntry.PathFile, TimeSpan.FromHours(24));
                        existingEntry.SasUrl = sasUrl ?? string.Empty;
                        
                    }
                 
                    await ExecuteAnalysisReceipt(existingEntry);
                    await finalResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new DiaryEntryResponse
                    {
                        Success = true,
                        Entry = existingEntry,
                        Message = $"Diary entry '{existingEntry.Titulo}' updated successfully",
                        TwinId = twinId
                    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                }
                else
                {
                    await finalResponse.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new DiaryEntryResponse
                    {
                        Success = false,
                        ErrorMessage = "Failed to update diary entry in database"
                    }));
                }

                return finalResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating diary entry");
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

       
 

        private async Task<UpdateDiaryEntryRequest> ProcessJsonRequestForUpdate(HttpRequestData req)
        {
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            return System.Text.Json.JsonSerializer.Deserialize<UpdateDiaryEntryRequest>(requestBody, new JsonSerializerOptions
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
            if (updateRequest.NivelEnergia.HasValue) existingEntry.NivelEnergia = updateRequest.NivelEnergia;

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
                EstadoEmocional = createRequest.EstadoEmocional,
                NivelEnergia = createRequest.NivelEnergia,
                GastoTotal = createRequest.GastoTotal,
                ProductosComprados = createRequest.ProductosComprados,
                TiendaLugar = createRequest.TiendaLugar,
                MetodoPago = createRequest.MetodoPago,
                CategoriaCompra = createRequest.CategoriaCompra,
                SatisfaccionCompra = createRequest.SatisfaccionCompra,
                CostoComida = createRequest.CostoComida,
                RestauranteLugar = createRequest.RestauranteLugar,
                TipoCocina = createRequest.TipoCocina,
                PlatosOrdenados = createRequest.PlatosOrdenados,
                CalificacionComida = createRequest.CalificacionComida,
                AmbienteComida = createRequest.AmbienteComida,
                RecomendariaComida = createRequest.RecomendariaComida,
                CostoViaje = createRequest.CostoViaje,
                DestinoViaje = createRequest.DestinoViaje,
                TransporteViaje = createRequest.TransporteViaje,
                PropositoViaje = createRequest.PropositoViaje,
                CalificacionViaje = createRequest.CalificacionViaje,
                DuracionViaje = createRequest.DuracionViaje,
                CostoEntretenimiento = createRequest.CostoEntretenimiento,
                CalificacionEntretenimiento = createRequest.CalificacionEntretenimiento,
                TipoEntretenimiento = createRequest.TipoEntretenimiento,
                TituloNombre = createRequest.TituloNombre,
                LugarEntretenimiento = createRequest.LugarEntretenimiento,
                CostoEjercicio = createRequest.CostoEjercicio,
                EnergiaPostEjercicio = createRequest.EnergiaPostEjercicio,
                CaloriasQuemadas = createRequest.CaloriasQuemadas,
                TipoEjercicio = createRequest.TipoEjercicio,
                DuracionEjercicio = createRequest.DuracionEjercicio,
                IntensidadEjercicio = createRequest.IntensidadEjercicio,
                LugarEjercicio = createRequest.LugarEjercicio,
                RutinaEspecifica = createRequest.RutinaEspecifica,
                CostoEstudio = createRequest.CostoEstudio,
                DificultadEstudio = createRequest.DificultadEstudio,
                EstadoAnimoPost = createRequest.EstadoAnimoPost,
                MateriaTema = createRequest.MateriaTema,
                MaterialEstudio = createRequest.MaterialEstudio,
                DuracionEstudio = createRequest.DuracionEstudio,
                ProgresoEstudio = createRequest.ProgresoEstudio,
                HorasTrabajadas = createRequest.HorasTrabajadas,
                ProyectoPrincipal = createRequest.ProyectoPrincipal,
                ReunionesTrabajo = createRequest.ReunionesTrabajo,
                LogrosHoy = createRequest.LogrosHoy,
                DesafiosTrabajo = createRequest.DesafiosTrabajo,
                MoodTrabajo = createRequest.MoodTrabajo,
                CostoSalud = createRequest.CostoSalud,
                TipoConsulta = createRequest.TipoConsulta,
                ProfesionalCentro = createRequest.ProfesionalCentro,
                MotivoConsulta = createRequest.MotivoConsulta,
                TratamientoRecetado = createRequest.TratamientoRecetado,
                ProximaCita = createRequest.ProximaCita,
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
            await response.WriteStringAsync(System.Text.Json.JsonSerializer.Serialize(new
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
                ".web" => "image/webp",
                ".pdf" => "application/pdf",
                _ => "application/octet-stream"
            };
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

            // Emotional state and energy
            request.EstadoEmocional = GetStringValue("estadoEmocional");
            request.NivelEnergia = GetIntValue("nivelEnergia");

            // Shopping activities
            request.GastoTotal = GetDecimalValue("gastoTotal");
            request.ProductosComprados = GetStringValue("productosComprados");
            request.TiendaLugar = GetStringValue("tiendaLugar");
            request.MetodoPago = GetStringValue("metodoPago");
            request.CategoriaCompra = GetStringValue("categoriaCompra");
            request.SatisfaccionCompra = GetIntValue("satisfaccionCompra");

            // Food activities
            request.CostoComida = GetDecimalValue("costoComida");
            request.RestauranteLugar = GetStringValue("restauranteLugar");
            request.TipoCocina = GetStringValue("tipoCocina");
            request.PlatosOrdenados = GetStringValue("platosOrdenados");
            request.CalificacionComida = GetIntValue("calificacionComida");
            request.AmbienteComida = GetStringValue("ambienteComida");
            request.RecomendariaComida = GetBoolValue("recomendariaComida");

            // Travel activities
            request.CostoViaje = GetDecimalValue("costoViaje");
            request.DestinoViaje = GetStringValue("destinoViaje");
            request.TransporteViaje = GetStringValue("transporteViaje");
            request.PropositoViaje = GetStringValue("propositoViaje");
            request.CalificacionViaje = GetIntValue("calificacionViaje");
            request.DuracionViaje = GetIntValue("duracionViaje");

            // Entertainment activities
            request.CostoEntretenimiento = GetDecimalValue("costoEntretenimiento");
            request.CalificacionEntretenimiento = GetIntValue("calificacionEntretenimiento");
            request.TipoEntretenimiento = GetStringValue("tipoEntretenimiento");
            request.TituloNombre = GetStringValue("tituloNombre");
            request.LugarEntretenimiento = GetStringValue("lugarEntretenimiento");

            // Exercise activities
            request.CostoEjercicio = GetDecimalValue("costoEjercicio");
            request.EnergiaPostEjercicio = GetIntValue("energiaPostEjercicio");
            request.CaloriasQuemadas = GetIntValue("caloriasQuemadas");
            request.TipoEjercicio = GetStringValue("tipoEjercicio");
            request.DuracionEjercicio = GetIntValue("duracionEjercicio");
            request.IntensidadEjercicio = GetIntValue("intensidadEjercicio");
            request.LugarEjercicio = GetStringValue("lugarEjercicio");
            request.RutinaEspecifica = GetStringValue("rutinaEspecifica");

            // Study activities
            request.CostoEstudio = GetDecimalValue("costoEstudio");
            request.DificultadEstudio = GetIntValue("dificultadEstudio");
            request.EstadoAnimoPost = GetIntValue("estadoAnimoPost");
            request.MateriaTema = GetStringValue("materiaTema");
            request.MaterialEstudio = GetStringValue("materialEstudio");
            request.DuracionEstudio = GetIntValue("duracionEstudio");
            request.ProgresoEstudio = GetIntValue("progresoEstudio");

            // Work activities
            request.HorasTrabajadas = GetIntValue("horasTrabajadas");
            request.ProyectoPrincipal = GetStringValue("proyectoPrincipal");
            request.ReunionesTrabajo = GetIntValue("reunionesTrabajo");
            request.LogrosHoy = GetStringValue("logrosHoy");
            request.DesafiosTrabajo = GetStringValue("desafiosTrabajo");
            request.MoodTrabajo = GetIntValue("moodTrabajo");

            // Health activities
            request.CostoSalud = GetDecimalValue("costoSalud");
            request.TipoConsulta = GetStringValue("tipoConsulta");
            request.ProfesionalCentro = GetStringValue("profesionalCentro");
            request.MotivoConsulta = GetStringValue("motivoConsulta");
            request.TratamientoRecetado = GetStringValue("tratamientoRecetado");
            request.ProximaCita = GetDateTimeValue("proximaCita");

            // Call activities
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
            // ENFOQUE SIMPLIFICADO: Buscar primero el campo diaryData para deserialización directa
            var diaryDataPart = parts.FirstOrDefault(p => p.Name == "diaryData");
            if (diaryDataPart != null && !string.IsNullOrEmpty(diaryDataPart.StringValue))
            {
                try
                {
                    _logger.LogInformation("📄 Deserializando datos desde campo 'diaryData' JSON: {JsonLength} chars", diaryDataPart.StringValue.Length);
                    
                    // DESERIALIZACIÓN DIRECTA con configuración para manejar strings como números
                    var jsonSettings = new Newtonsoft.Json.JsonSerializerSettings
                    {
                        NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                        Converters = { new StringToDoubleConverter() }
                    };
                    
                    var updateRequest = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateDiaryEntryRequest>(diaryDataPart.StringValue, jsonSettings);
                    
                    if (updateRequest != null)
                    {
                        _logger.LogInformation("✅ Deserialización JSON exitosa - usando datos del campo diaryData");
                        return updateRequest;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Error deserializando JSON del campo 'diaryData', usando parsing manual como fallback");
                }
            }

            // FALLBACK: Parsing manual individual solo si la deserialización JSON falla
            _logger.LogInformation("📄 Usando parsing manual de campos individuales como fallback");
            return ParseManualFieldsForUpdate(parts);
        }

        // Método helper para parsing manual (extraído para mayor claridad)
        private UpdateDiaryEntryRequest ParseManualFieldsForUpdate(List<MultipartFormDataPart> parts)
        {
            var request = new UpdateDiaryEntryRequest();

            string GetStringValue(string name) => parts.FirstOrDefault(p => p.Name == name)?.StringValue;
            int? GetIntValue(string name) => int.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            decimal? GetDecimalValue(string name) => decimal.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            double? GetDoubleValue(string name) => double.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            bool? GetBoolValue(string name) => bool.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;
            DateTime? GetDateTimeValue(string name) => DateTime.TryParse(GetStringValue(name) ?? "", out var value) ? value : null;

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
            var pais = GetStringValue("pais");
            if (!string.IsNullOrEmpty(pais)) request.Pais = pais;
            
            var ciudad = GetStringValue("ciudad");
            if (!string.IsNullOrEmpty(ciudad)) request.Ciudad = ciudad;
            var estado = GetStringValue("estado");
            if (!string.IsNullOrEmpty(estado)) request.Estado = estado;

            var codigoPostal = GetStringValue("codigoPostal");
            if (!string.IsNullOrEmpty(codigoPostal)) request.CodigoPostal = codigoPostal;
            
            var direccionEspecifica = GetStringValue("direccionEspecifica");
            if (!string.IsNullOrEmpty(direccionEspecifica)) request.DireccionEspecifica = direccionEspecifica;
            
            var telefono = GetStringValue("telefono");
            if (!string.IsNullOrEmpty(telefono)) request.Telefono = telefono;
            
            var website = GetStringValue("website");
            if (!string.IsNullOrEmpty(website)) request.Website = website;
            
            var distritoColonia = GetStringValue("distritoColonia");
            if (!string.IsNullOrEmpty(distritoColonia)) request.DistritoColonia = distritoColonia;

            // Emotional state and energy
            var estadoEmocional = GetStringValue("estadoEmocional");
            if (!string.IsNullOrEmpty(estadoEmocional)) request.EstadoEmocional = estadoEmocional;
            
            request.NivelEnergia = GetIntValue("nivelEnergia");

            // Food activities (campos más comunes)
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

            // Shopping activities
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

            // Travel activities
            request.CostoViaje = GetDecimalValue("costoViaje");
            var destinoViaje = GetStringValue("destinoViaje");
            if (!string.IsNullOrEmpty(destinoViaje)) request.DestinoViaje = destinoViaje;
            
            var transporteViaje = GetStringValue("transporteViaje");
            if (!string.IsNullOrEmpty(transporteViaje)) request.TransporteViaje = transporteViaje;
            
            var propositoViaje = GetStringValue("propositoViaje");
            if (!string.IsNullOrEmpty(propositoViaje)) request.PropositoViaje = propositoViaje;
            
            request.CalificacionViaje = GetIntValue("calificacionViaje");
            request.DuracionViaje = GetIntValue("duracionViaje");

            // Entertainment activities
            request.CostoEntretenimiento = GetDecimalValue("costoEntretenimiento");
            request.CalificacionEntretenimiento = GetIntValue("calificacionEntretenimiento");
            var tipoEntretenimiento = GetStringValue("tipoEntretenimiento");
            if (!string.IsNullOrEmpty(tipoEntretenimiento)) request.TipoEntretenimiento = tipoEntretenimiento;
            
            var tituloNombre = GetStringValue("tituloNombre");
            if (!string.IsNullOrEmpty(tituloNombre)) request.TituloNombre = tituloNombre;
            
            var lugarEntretenimiento = GetStringValue("lugarEntretenimiento");
            if (!string.IsNullOrEmpty(lugarEntretenimiento)) request.LugarEntretenimiento = lugarEntretenimiento;

            // Exercise activities
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

            // Study activities
            request.CostoEstudio = GetDecimalValue("costoEstudio");
            request.DificultadEstudio = GetIntValue("dificultadEstudio");
            request.EstadoAnimoPost = GetIntValue("estadoAnimoPost");
            var materiaTema = GetStringValue("materiaTema");
            if (!string.IsNullOrEmpty(materiaTema)) request.MateriaTema = materiaTema;
            
            var materialEstudio = GetStringValue("materialEstudio");
            if (!string.IsNullOrEmpty(materialEstudio)) request.MaterialEstudio = materialEstudio;
            
            request.DuracionEstudio = GetIntValue("duracionEstudio");
            request.ProgresoEstudio = GetIntValue("progresoEstudio");

            // Work activities
            request.HorasTrabajadas = GetIntValue("horasTrabajadas");
            var proyectoPrincipal = GetStringValue("proyectoPrincipal");
            if (!string.IsNullOrEmpty(proyectoPrincipal)) request.ProyectoPrincipal = proyectoPrincipal;
            
            request.ReunionesTrabajo = GetIntValue("reunionesTrabajo");
            var logrosHoy = GetStringValue("logrosHoy");
            if (!string.IsNullOrEmpty(logrosHoy)) request.LogrosHoy = logrosHoy;
            
            var desafiosTrabajo = GetStringValue("desafiosTrabajo");
            if (!string.IsNullOrEmpty(desafiosTrabajo)) request.DesafiosTrabajo = desafiosTrabajo;
            
            request.MoodTrabajo = GetIntValue("moodTrabajo");

            // Health activities
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

            // Call activities
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

        private async Task<byte[]> GetRequestBodyBytes(HttpRequestData req)
        {
            using var memoryStream = new MemoryStream();
            // Reset stream position if possible (may not work with all stream types)
            if (req.Body.CanSeek)
            {
                req.Body.Position = 0;
            }
            await req.Body.CopyToAsync(memoryStream);
            return memoryStream.ToArray();
        }

        /// <summary>
        /// Upload file from multipart form data part and perform comprehensive analysis if diary entry is provided
        /// </summary>
        /// <param name="filePart">Multipart form data containing file</param>
        /// <param name="twinId">Twin ID for file storage</param>
        /// <param name="entryId">Diary entry ID for file organization</param>
        /// <param name="diaryEntry">Optional diary entry for comprehensive analysis</param>
        /// <returns>File path if successful, empty string if failed</returns>
        private async Task<string> UploadFileFromPart(MultipartFormDataPart filePart, 
            string twinId, string entryId, DiaryEntry? diaryEntry = null)
        {
            try
            {
                var fileBytes = filePart.Data;
                var originalFileName = filePart.FileName ?? "file";

                _logger.LogInformation("📎 Processing file upload from part: {FileName}, Size: {Size} bytes", originalFileName, fileBytes.Length);

                // Validate file
                if (!IsValidReceiptFile(originalFileName, fileBytes))
                {
                    _logger.LogWarning("⚠️ Invalid file format: {FileName}", originalFileName);
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

                var cleanFileName = originalFileName;
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

                    _logger.LogInformation("✅ File uploaded successfully: {FilePath}", filePath);
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
 
        private async Task ExecuteAnalysisReceipt(DiaryEntry diaryEntry)
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

                var extractionResult = await _diaryAgent.ExtractReceiptInformationFromUrlAsync(
                                       diaryEntry.SasUrl,
                                       diaryEntry.PathFile,
                                       "general"
                                   );
                _logger.LogInformation("🎯 Llamando a GenerateComprehensiveAnalysisAsync con datos simulados...");
                
                var comprehensiveAnalysis = await _diaryAgent.GenerateComprehensiveAnalysisAsync(
                   extractionResult, 
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
    }
}

/// <summary>
/// Custom JSON converter para convertir strings a double? para latitud/longitud
/// </summary>
public class StringToDoubleConverter : Newtonsoft.Json.JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(double?) || objectType == typeof(double);
    }

    public override object? ReadJson(Newtonsoft.Json.JsonReader reader, Type objectType, object? existingValue, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (reader.TokenType == Newtonsoft.Json.JsonToken.Null)
        {
            return null;
        }

        if (reader.TokenType == Newtonsoft.Json.JsonToken.String)
        {
            var stringValue = reader.Value?.ToString();
            if (string.IsNullOrEmpty(stringValue))
            {
                return null;
            }

            if (double.TryParse(stringValue, out var doubleValue))
            {
                return doubleValue;
            }
        }

        if (reader.TokenType == Newtonsoft.Json.JsonToken.Float || reader.TokenType == Newtonsoft.Json.JsonToken.Integer)
        {
            return Convert.ToDouble(reader.Value);
        }

        return null;
    }

    public override void WriteJson(Newtonsoft.Json.JsonWriter writer, object? value, Newtonsoft.Json.JsonSerializer serializer)
    {
        if (value is double doubleValue)
        {
            writer.WriteValue(doubleValue);
        }
        else
        {
            writer.WriteNull();
        }
    }
}