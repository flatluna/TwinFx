using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Text;
using TwinFx.Models;

namespace TwinFx.Agents
{
    /// <summary>
    /// Agent for managing travel records and experiences.
    /// ========================================================================
    /// 
    /// TravelAgent provides comprehensive travel management capabilities including:
    /// - Creating and managing travel plans
    /// - Tracking travel experiences and memories
    /// - Statistical analysis of travel patterns
    /// - Integration with TwinFx ecosystem
    /// 
    /// Features:
    /// - Full CRUD operations for travel records
    /// - Advanced filtering and search capabilities
    /// - Travel statistics and analytics
    /// - Support for multiple travel types and statuses
    /// - Budget tracking and analysis
    /// - Integration with Cosmos DB TwinTravel container
    /// 
    /// Author: TwinFx Project
    /// Date: January 15, 2025
    /// </summary>
    public class TravelAgent : IDisposable
    {
        private readonly ILogger<TravelAgent> _logger;
        private readonly IConfiguration _configuration;
        private readonly CosmosClient _cosmosClient;
        private readonly Database _database;
        private readonly Container _travelContainer;
        private bool _disposed = false;

        public TravelAgent(ILogger<TravelAgent> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;

            try
            {
                // Initialize Cosmos DB client
                var accountName = _configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ??
                                 _configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";

                var databaseName = _configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ??
                                  _configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

                var endpoint = $"https://{accountName}.documents.azure.com:443/";
                var key = _configuration.GetValue<string>("Values:COSMOS_KEY") ??
                         _configuration.GetValue<string>("COSMOS_KEY");

                if (string.IsNullOrEmpty(key))
                {
                    throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
                }

                _cosmosClient = new CosmosClient(endpoint, key);
                _database = _cosmosClient.GetDatabase(databaseName);
                _travelContainer = _database.GetContainer("TwinTravel");

                _logger.LogInformation("✅ TravelAgent initialized successfully");
                _logger.LogInformation("📍 Database: {DatabaseName}, Container: TwinTravel", databaseName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to initialize TravelAgent");
                throw;
            }
        }

        /// <summary>
        /// Create a new travel record
        /// </summary>
        /// <param name="twinId">Twin ID for the travel record</param>
        /// <param name="request">Travel creation request</param>
        /// <returns>Travel creation response</returns>
        public async Task<TravelResponse> CreateTravelAsync(string twinId, CreateTravelRequest request)
        {
            _logger.LogInformation("🌍 Creating new travel record for Twin: {TwinId}, Title: {Title}", 
                twinId, request.Titulo);

            var response = new TravelResponse();

            try
            {
                // Create travel data object
                var travelData = new TravelData
                {
                    Id = Guid.NewGuid().ToString(),
                    TwinID = twinId,
                    Titulo = request.Titulo,
                    Descripcion = request.Descripcion,
                    PaisDestino = request.PaisDestino,
                    CiudadDestino = request.CiudadDestino,
                    FechaInicio = request.FechaInicio,
                    FechaFin = request.FechaFin,
                    DuracionDias = request.DuracionDias ?? CalculateDuration(request.FechaInicio, request.FechaFin),
                    Presupuesto = request.Presupuesto,
                    Moneda = request.Moneda ?? "USD",
                    TipoViaje = request.TipoViaje,
                    Estado = request.Estado,
                    Status = request.Status
                };

                // Save to Cosmos DB
                await _travelContainer.CreateItemAsync(travelData.ToDict(), new PartitionKey(twinId));

                response.Success = true;
                response.Message = $"Travel '{request.Titulo}' created successfully";
                response.Data = travelData;

                _logger.LogInformation("✅ Travel record created successfully: {TravelId} for Twin: {TwinId}", 
                    travelData.Id, twinId);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating travel record for Twin: {TwinId}", twinId);
                
                response.Success = false;
                response.Message = $"Error creating travel record: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Get travel records for a specific Twin with optional filtering
        /// </summary>
        /// <param name="twinId">Twin ID to search for</param>
        /// <param name="query">Optional query parameters for filtering</param>
        /// <returns>List of travel records with statistics</returns>
        public async Task<GetTravelsResponse> GetTravelsByTwinIdAsync(string twinId, TravelQuery? query = null)
        {
            _logger.LogInformation("📋 Getting travel records for Twin: {TwinId}", twinId);

            var response = new GetTravelsResponse
            {
                TwinID = twinId
            };

            try
            {
                query ??= new TravelQuery();

                // Build SQL query with filters
                var sqlBuilder = new StringBuilder();
                sqlBuilder.Append("SELECT * FROM c WHERE c.TwinID = @twinId AND c.documentType = 'travel'");

                var parameters = new List<(string, object)> { ("@twinId", twinId) };

                // Apply filters
                if (query.Estado.HasValue)
                {
                    sqlBuilder.Append(" AND c.estado = @estado");
                    parameters.Add(("@estado", query.Estado.Value.ToString().ToLowerInvariant()));
                }

                if (query.TipoViaje.HasValue)
                {
                    sqlBuilder.Append(" AND c.tipoViaje = @tipoViaje");
                    parameters.Add(("@tipoViaje", query.TipoViaje.Value.ToString().ToLowerInvariant()));
                }

                if (!string.IsNullOrEmpty(query.PaisDestino))
                {
                    sqlBuilder.Append(" AND CONTAINS(UPPER(c.paisDestino), @paisDestino)");
                    parameters.Add(("@paisDestino", query.PaisDestino.ToUpperInvariant()));
                }

                if (!string.IsNullOrEmpty(query.CiudadDestino))
                {
                    sqlBuilder.Append(" AND CONTAINS(UPPER(c.ciudadDestino), @ciudadDestino)");
                    parameters.Add(("@ciudadDestino", query.CiudadDestino.ToUpperInvariant()));
                }

                if (query.FechaDesde.HasValue)
                {
                    sqlBuilder.Append(" AND c.fechaInicio >= @fechaDesde");
                    parameters.Add(("@fechaDesde", query.FechaDesde.Value.ToString("O")));
                }

                if (query.FechaHasta.HasValue)
                {
                    sqlBuilder.Append(" AND c.fechaInicio <= @fechaHasta");
                    parameters.Add(("@fechaHasta", query.FechaHasta.Value.ToString("O")));
                }

                if (query.CalificacionMin.HasValue)
                {
                    sqlBuilder.Append(" AND c.calificacion >= @calificacionMin");
                    parameters.Add(("@calificacionMin", query.CalificacionMin.Value));
                }

                if (query.PresupuestoMax.HasValue)
                {
                    sqlBuilder.Append(" AND c.presupuesto <= @presupuestoMax");
                    parameters.Add(("@presupuestoMax", query.PresupuestoMax.Value));
                }

                if (!string.IsNullOrEmpty(query.SearchTerm))
                {
                    sqlBuilder.Append(@" AND (CONTAINS(UPPER(c.titulo), @searchTerm) 
                                            OR CONTAINS(UPPER(c.descripcion), @searchTerm)
                                            OR CONTAINS(UPPER(c.notas), @searchTerm)
                                            OR CONTAINS(UPPER(c.actividades), @searchTerm))");
                    parameters.Add(("@searchTerm", query.SearchTerm.ToUpperInvariant()));
                }

                // Add sorting
                var sortBy = query.SortBy?.ToLowerInvariant() switch
                {
                    "fechainicio" => "c.fechaInicio",
                    "titulo" => "c.titulo",
                    "presupuesto" => "c.presupuesto",
                    "calificacion" => "c.calificacion",
                    _ => "c.fechaCreacion"
                };

                var sortDirection = query.SortDirection?.ToUpperInvariant() == "ASC" ? "ASC" : "DESC";
                sqlBuilder.Append($" ORDER BY {sortBy} {sortDirection}");

                // Apply pagination
                var offset = (query.Page - 1) * query.PageSize;
                sqlBuilder.Append($" OFFSET {offset} LIMIT {Math.Min(query.PageSize, 100)}");

                // Execute query
                var queryDefinition = new QueryDefinition(sqlBuilder.ToString());
                foreach (var (name, value) in parameters)
                {
                    queryDefinition = queryDefinition.WithParameter(name, value);
                }

                var viajes = new List<Viaje>();
                using var iterator = _travelContainer.GetItemQueryIterator<Dictionary<string, object?>>(queryDefinition);

                while (iterator.HasMoreResults)
                {
                    var cosmosResponse = await iterator.ReadNextAsync();
                    foreach (var item in cosmosResponse)
                    {
                        string json = JsonConvert.SerializeObject(item);
                        Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);
                        viajes.Add(viaje);
                    }
                }

                // Convert Viaje to TravelData for response
                var travels = viajes.Select(v => ConvertViajeToTravelData(v)).ToList();

                // Calculate statistics
                var stats = CalculateTravelStats(travels);

                response.Success = true;
                response.Message = $"Found {travels.Count} travel records for Twin {twinId}";
                response.Travels = travels;
                response.TotalTravels = travels.Count;
                response.Stats = stats;

                _logger.LogInformation("✅ Retrieved {Count} travel records for Twin: {TwinId}", 
                    travels.Count, twinId);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting travel records for Twin: {TwinId}", twinId);
                
                response.Success = false;
                response.Message = $"Error retrieving travel records: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Get a specific travel record by ID
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <returns>Travel response with data</returns>
        public async Task<TravelResponse> GetTravelByIdAsync(string travelId, string twinId)
        {
            _logger.LogInformation("🔍 Getting travel record: {TravelId} for Twin: {TwinId}", 
                travelId, twinId);

            var response = new TravelResponse();

            try
            {
                var cosmosResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                var travel = TravelData.FromDict(cosmosResponse.Resource);

                response.Success = true;
                response.Message = "Travel record retrieved successfully";
                response.Data = travel;

                _logger.LogInformation("✅ Travel record retrieved: {TravelId} - {Title}", 
                    travelId, travel.Titulo);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting travel record: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error retrieving travel record: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Update an existing travel record
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="request">Update request</param>
        /// <returns>Travel update response</returns>
        public async Task<TravelResponse> UpdateTravelAsync(string travelId, string twinId, UpdateTravelRequest request)
        {
            _logger.LogInformation("📝 Updating travel record: {TravelId} for Twin: {TwinId}", 
                travelId, twinId);

            var response = new TravelResponse();

            try
            {
                // Get existing travel record
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                var existingTravel = TravelData.FromDict(existingResponse.Resource);

                // Update fields if provided
                if (!string.IsNullOrEmpty(request.Titulo))
                    existingTravel.Titulo = request.Titulo;

                if (!string.IsNullOrEmpty(request.Descripcion))
                    existingTravel.Descripcion = request.Descripcion;

                if (request.PaisDestino != null)
                    existingTravel.PaisDestino = request.PaisDestino;

                if (request.CiudadDestino != null)
                    existingTravel.CiudadDestino = request.CiudadDestino;

                if (request.FechaInicio.HasValue)
                    existingTravel.FechaInicio = request.FechaInicio;

                if (request.FechaFin.HasValue)
                    existingTravel.FechaFin = request.FechaFin;

                if (request.DuracionDias.HasValue)
                    existingTravel.DuracionDias = request.DuracionDias;

                if (request.Presupuesto.HasValue)
                    existingTravel.Presupuesto = request.Presupuesto;

                if (!string.IsNullOrEmpty(request.Moneda))
                    existingTravel.Moneda = request.Moneda;

                if (request.TipoViaje.HasValue)
                    existingTravel.TipoViaje = request.TipoViaje.Value;

                if (request.Estado.HasValue)
                    existingTravel.Estado = request.Estado.Value;

                if (request.Status.HasValue)
                    existingTravel.Status = request.Status.Value;

                if (request.Transporte != null)
                    existingTravel.Transporte = request.Transporte;

                if (request.Alojamiento != null)
                    existingTravel.Alojamiento = request.Alojamiento;

                if (request.Compañeros != null)
                    existingTravel.Compañeros = request.Compañeros;

                if (request.Actividades != null)
                    existingTravel.Actividades = request.Actividades;

                if (request.Notas != null)
                    existingTravel.Notas = request.Notas;

                if (request.Calificacion.HasValue)
                    existingTravel.Calificacion = request.Calificacion;

                if (request.Highlights != null)
                    existingTravel.Highlights = request.Highlights;

                // Update timestamp
                existingTravel.FechaActualizacion = DateTime.UtcNow;

                // Recalculate duration if dates changed
                if (request.FechaInicio.HasValue || request.FechaFin.HasValue)
                {
                    existingTravel.DuracionDias = CalculateDuration(existingTravel.FechaInicio, existingTravel.FechaFin);
                }

                // Save updated record
                await _travelContainer.UpsertItemAsync(existingTravel.ToDict(), new PartitionKey(twinId));

                response.Success = true;
                response.Message = "Travel record updated successfully";
                response.Data = existingTravel;

                _logger.LogInformation("✅ Travel record updated: {TravelId} - {Title}", 
                    travelId, existingTravel.Titulo);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for update: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating travel record: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error updating travel record: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Delete a travel record
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <returns>Travel deletion response</returns>
        public async Task<TravelResponse> DeleteTravelAsync(string travelId, string twinId)
        {
            _logger.LogInformation("🗑️ Deleting travel record: {TravelId} for Twin: {TwinId}", 
                travelId, twinId);

            var response = new TravelResponse();

            try
            {
                // Get travel record first to include in response
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                var existingTravel = TravelData.FromDict(existingResponse.Resource);

                // Delete the record
                await _travelContainer.DeleteItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                response.Success = true;
                response.Message = $"Travel record '{existingTravel.Titulo}' deleted successfully";
                response.Data = existingTravel;

                _logger.LogInformation("✅ Travel record deleted: {TravelId} - {Title}", 
                    travelId, existingTravel.Titulo);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for deletion: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting travel record: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error deleting travel record: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Add a new itinerary to an existing travel record
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="request">Itinerary creation request</param>
        /// <returns>Itinerary creation response</returns>
        public async Task<ItineraryResponse> CreateItineraryAsync(string travelId, string twinId, CreateItineraryRequest request)
        {
            _logger.LogInformation("🗺️ Creating new itinerary for Travel: {TravelId}, Twin: {TwinId}", 
                travelId, twinId);

            var response = new ItineraryResponse();

            try
            {
                // Get existing travel record
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                var existingTravel = TravelData.FromDict(existingResponse.Resource);

                // Create new itinerary
                var newItinerary = new TravelItinerary
                {
                    Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    Titulo = request.Titulo,
                    Descripcion = request.Descripcion,
                    CiudadOrigen = request.CiudadOrigen,
                    PaisOrigen = request.PaisOrigen,
                    CiudadDestino = request.CiudadDestino,
                    PaisDestino = request.PaisDestino,
                    FechaInicio = request.FechaInicio,
                    FechaFin = request.FechaFin,
                    FechaCreacion = DateTime.UtcNow,
                    MedioTransporte = request.MedioTransporte,
                    PresupuestoEstimado = request.PresupuestoEstimado,
                    Moneda = request.Moneda ?? "USD",
                    TipoAlojamiento = request.TipoAlojamiento,
                    Notas = request.Notas,
                    TwinId = twinId,
                    ViajeId = travelId,
                    DocumentType = "itinerary",
                    ViajeInfo = new TravelContext
                    {
                        Titulo = existingTravel.Titulo,
                        Descripcion = existingTravel.Descripcion,
                        TipoViaje = existingTravel.TipoViaje.ToString().ToLowerInvariant(),
                        Estado = existingTravel.Estado.ToString().ToLowerInvariant()
                    }
                };

                // Add itinerary to existing travel
                existingTravel.Itinerarios.Add(newItinerary);
                existingTravel.FechaActualizacion = DateTime.UtcNow;

                // Save updated travel record with new itinerary
                await _travelContainer.UpsertItemAsync(existingTravel.ToDict(), new PartitionKey(twinId));

                response.Success = true;
                response.Message = $"Itinerary '{request.Titulo}' added successfully to travel '{existingTravel.Titulo}'";
                response.Data = newItinerary;
                response.TravelData = existingTravel;

                _logger.LogInformation("✅ Itinerary created successfully: {ItineraryId} for Travel: {TravelId}", 
                    newItinerary.Id, travelId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for itinerary creation: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating itinerary for Travel: {TravelId}, Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error creating itinerary: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Update an existing itinerary within a travel record
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID to update</param>
        /// <param name="request">Itinerary update request</param>
        /// <returns>Itinerary update response</returns>
        public async Task<ItineraryResponse> UpdateItineraryAsync(string travelId, string twinId, string itineraryId, UpdateItineraryRequest request)
        {
            _logger.LogInformation("📝 Updating itinerary: {ItineraryId} for Travel: {TravelId}, Twin: {TwinId}", 
                itineraryId, travelId, twinId);

            var response = new ItineraryResponse();

            try
            {
                // Get existing travel record using simple approach
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary to update
                var itineraryToUpdate = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itineraryToUpdate == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Update itinerary fields if provided
                if (!string.IsNullOrEmpty(request.Titulo))
                    itineraryToUpdate.titulo = request.Titulo;

                if (request.CiudadOrigen != null)
                    itineraryToUpdate.ciudadOrigen = request.CiudadOrigen;

                if (request.PaisOrigen != null)
                    itineraryToUpdate.paisOrigen = request.PaisOrigen;

                if (request.CiudadDestino != null)
                    itineraryToUpdate.ciudadDestino = request.CiudadDestino;

                if (request.PaisDestino != null)
                    itineraryToUpdate.paisDestino = request.PaisDestino;

                if (request.FechaInicio.HasValue)
                    itineraryToUpdate.fechaInicio = request.FechaInicio.Value;

                if (request.FechaFin.HasValue)
                    itineraryToUpdate.fechaFin = request.FechaFin.Value;

                if (request.MedioTransporte.HasValue)
                    itineraryToUpdate.medioTransporte = request.MedioTransporte.Value.ToString().ToLowerInvariant();

                if (request.PresupuestoEstimado.HasValue)
                    itineraryToUpdate.presupuestoEstimado = request.PresupuestoEstimado.Value;

                if (!string.IsNullOrEmpty(request.Moneda))
                    itineraryToUpdate.moneda = request.Moneda;

                if (request.TipoAlojamiento.HasValue)
                    itineraryToUpdate.tipoAlojamiento = request.TipoAlojamiento.Value.ToString().ToLowerInvariant();

                if (request.Notas != null)
                    itineraryToUpdate.notas = request.Notas;

                // Update travel's last modified date
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert updated itinerary to TravelItinerary for response
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itineraryToUpdate);
                var updatedTravel = ConvertViajeToTravelData(viaje);

                response.Success = true;
                response.Message = $"Itinerary '{itineraryToUpdate.titulo}' updated successfully";
                response.Data = updatedItinerary;
                response.TravelData = updatedTravel;

                _logger.LogInformation("✅ Itinerary updated successfully: {ItineraryId} in Travel: {TravelId}", 
                    itineraryId, travelId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for itinerary update: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating itinerary: {ItineraryId} for Travel: {TravelId}, Twin: {TwinId}", 
                    itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error updating itinerary: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Delete an existing itinerary from a travel record
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID to delete</param>
        /// <returns>Itinerary deletion response</returns>
        public async Task<ItineraryResponse> DeleteItineraryAsync(string travelId, string twinId, string itineraryId)
        {
            _logger.LogInformation("🗑️ Deleting itinerary: {ItineraryId} from Travel: {TravelId}, Twin: {TwinId}", 
                itineraryId, travelId, twinId);

            var response = new ItineraryResponse();

            try
            {
                // Get existing travel record
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                var existingTravel = TravelData.FromDict(existingResponse.Resource);

                // Remove itinerary from travel record
                existingTravel.Itinerarios.RemoveAll(i => i.Id == itineraryId);

                // Update travel's last modified date
                existingTravel.FechaActualizacion = DateTime.UtcNow;

                // Save updated travel record
                await _travelContainer.UpsertItemAsync(existingTravel.ToDict(), new PartitionKey(twinId));

                response.Success = true;
                response.Message = "Itinerary deleted successfully";
                response.TravelData = existingTravel;

                _logger.LogInformation("✅ Itinerary deleted: {ItineraryId} from Travel: {TravelId}", 
                    itineraryId, travelId);

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting itinerary: {ItineraryId} from Travel: {TravelId}, Twin: {TwinId}", 
                    itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error deleting itinerary: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Create a new booking within an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="request">Booking creation request</param>
        /// <returns>Booking creation response</returns>
        public async Task<BookingResponse> CreateBookingAsync(string travelId, string twinId, string itineraryId, CreateBookingRequest request)
        {
            _logger.LogInformation("📅 Creating new booking for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                itineraryId, travelId, twinId);

            var response = new BookingResponse();

            try
            {
                // Get existing travel record using simple approach
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary
                var itinerary = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itinerary == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Create new booking
                var newBooking = new Booking
                {
                    id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    tipo = request.Tipo.ToString().ToLowerInvariant(),
                    titulo = request.Titulo,
                    descripcion = request.Descripcion,
                    fechaInicio = request.FechaInicio ?? DateTime.MinValue,
                    fechaFin = request.FechaFin,
                    horaInicio = request.HoraInicio,
                    horaFin = request.HoraFin,
                    proveedor = request.Proveedor,
                    precio = request.Precio ?? 0,
                    moneda = request.Moneda ?? "USD",
                    numeroConfirmacion = request.NumeroConfirmacion,
                    estado = request.Estado.ToString().ToLowerInvariant(),
                    notas = request.Notas,
                    fechaCreacion = DateTime.UtcNow,
                    fechaActualizacion = DateTime.UtcNow,
                    itinerarioId = itineraryId,
                    twinId = twinId,
                    viajeId = travelId,
                    documentType = "booking"
                };

                // Add contact if provided
                if (request.Contacto != null)
                {
                    newBooking.contacto = new ContactoBooking
                    {
                        telefono = request.Contacto.Telefono,
                        email = request.Contacto.Email,
                        direccion = request.Contacto.Direccion
                    };
                }

                // Initialize bookings list if null
                if (itinerary.bookings == null)
                    itinerary.bookings = new List<Booking>();

                // Add booking to itinerary
                itinerary.bookings.Add(newBooking);

                // Update travel's last modified date
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert booking to TravelBooking for response
                var createdBooking = ConvertBookingToTravelBooking(newBooking);
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itinerary);

                response.Success = true;
                response.Message = $"Booking '{request.Titulo}' created successfully";
                response.Data = createdBooking;
                response.ItineraryData = updatedItinerary;

                _logger.LogInformation("✅ Booking created successfully: {BookingId} for Itinerary: {ItineraryId}", 
                    newBooking.id, itineraryId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for booking creation: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating booking for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error creating booking: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Update an existing booking within an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="bookingId">Booking ID to update</param>
        /// <param name="request">Booking update request</param>
        /// <returns>Booking update response</returns>
        public async Task<BookingResponse> UpdateBookingAsync(string travelId, string twinId, string itineraryId, string bookingId, UpdateBookingRequest request)
        {
            _logger.LogInformation("📅 Updating booking: {BookingId} for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                bookingId, itineraryId, travelId, twinId);

            var response = new BookingResponse();

            try
            {
                // Get existing travel record using simple approach
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary
                var itinerary = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itinerary == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Find the booking to update
                var bookingToUpdate = itinerary.bookings?.FirstOrDefault(b => b.id == bookingId);
                if (bookingToUpdate == null)
                {
                    _logger.LogWarning("⚠️ Booking not found: {BookingId} in Itinerary: {ItineraryId}", bookingId, itineraryId);
                    response.Success = false;
                    response.Message = "Booking not found in itinerary";
                    return response;
                }

                // Update booking fields if provided
                if (request.Tipo.HasValue)
                    bookingToUpdate.tipo = request.Tipo.Value.ToString().ToLowerInvariant();

                if (!string.IsNullOrEmpty(request.Titulo))
                    bookingToUpdate.titulo = request.Titulo;

                if (request.Descripcion != null)
                    bookingToUpdate.descripcion = request.Descripcion;

                if (request.FechaInicio.HasValue)
                    bookingToUpdate.fechaInicio = request.FechaInicio.Value;

                if (request.FechaFin.HasValue)
                    bookingToUpdate.fechaFin = request.FechaFin;

                if (request.HoraInicio != null)
                    bookingToUpdate.horaInicio = request.HoraInicio;

                if (request.HoraFin != null)
                    bookingToUpdate.horaFin = request.HoraFin;

                if (request.Proveedor != null)
                    bookingToUpdate.proveedor = request.Proveedor;

                if (request.Precio.HasValue)
                    bookingToUpdate.precio = request.Precio.Value;

                if (!string.IsNullOrEmpty(request.Moneda))
                    bookingToUpdate.moneda = request.Moneda;

                if (request.NumeroConfirmacion != null)
                    bookingToUpdate.numeroConfirmacion = request.NumeroConfirmacion;

                if (request.Estado.HasValue)
                    bookingToUpdate.estado = request.Estado.Value.ToString().ToLowerInvariant();

                if (request.Notas != null)
                    bookingToUpdate.notas = request.Notas;

                // Update contact if provided
                if (request.Contacto != null)
                {
                    if (bookingToUpdate.contacto == null)
                        bookingToUpdate.contacto = new ContactoBooking();

                    if (request.Contacto.Telefono != null)
                        bookingToUpdate.contacto.telefono = request.Contacto.Telefono;

                    if (request.Contacto.Email != null)
                        bookingToUpdate.contacto.email = request.Contacto.Email;

                    if (request.Contacto.Direccion != null)
                        bookingToUpdate.contacto.direccion = request.Contacto.Direccion;
                }

                // Update timestamps
                bookingToUpdate.fechaActualizacion = DateTime.UtcNow;
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert updated booking to TravelBooking for response
                var updatedBooking = ConvertBookingToTravelBooking(bookingToUpdate);
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itinerary);

                response.Success = true;
                response.Message = $"Booking '{bookingToUpdate.titulo}' updated successfully";
                response.Data = updatedBooking;
                response.ItineraryData = updatedItinerary;

                _logger.LogInformation("✅ Booking updated successfully: {BookingId} in Itinerary: {ItineraryId}", 
                    bookingId, itineraryId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for booking update: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating booking: {BookingId} for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    bookingId, itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error updating booking: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Delete a booking from an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="bookingId">Booking ID to delete</param>
        /// <returns>Booking deletion response</returns>
        public async Task<BookingResponse> DeleteBookingAsync(string travelId, string twinId, string itineraryId, string bookingId)
        {
            _logger.LogInformation("🗑️ Deleting booking: {BookingId} from Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                bookingId, itineraryId, travelId, twinId);

            var response = new BookingResponse();

            try
            {
                // Get existing travel record using simple approach
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary
                var itinerary = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itinerary == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Find the booking to delete
                var bookingToDelete = itinerary.bookings?.FirstOrDefault(b => b.id == bookingId);
                if (bookingToDelete == null)
                {
                    _logger.LogWarning("⚠️ Booking not found: {BookingId} in Itinerary: {ItineraryId}", bookingId, itineraryId);
                    response.Success = false;
                    response.Message = "Booking not found in itinerary";
                    return response;
                }

                // Remove booking from itinerary
                itinerary.bookings.Remove(bookingToDelete);

                // Update travel's last modified date
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert deleted booking to TravelBooking for response
                var deletedBooking = ConvertBookingToTravelBooking(bookingToDelete);
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itinerary);

                response.Success = true;
                response.Message = $"Booking '{deletedBooking.Titulo}' deleted successfully";
                response.Data = deletedBooking;
                response.ItineraryData = updatedItinerary;

                _logger.LogInformation("✅ Booking deleted successfully: {BookingId} from Itinerary: {ItineraryId}", 
                    bookingId, itineraryId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for booking deletion: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting booking: {BookingId} for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    bookingId, itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error deleting booking: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Get all bookings for a specific itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <returns>List of bookings</returns>
        public async Task<GetBookingsResponse> GetBookingsByItineraryAsync(string travelId, string twinId, string itineraryId)
        {
            _logger.LogInformation("📅 Getting bookings for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                itineraryId, travelId, twinId);

            var response = new GetBookingsResponse();

            try
            {
                // OPTIMIZED: Use SQL query to get only the specific itinerary with bookings
                // This avoids loading the entire travel document and is much more efficient
                var sqlQuery = @"
                    SELECT VALUE i 
                    FROM c 
                    JOIN i IN c.itinerarios 
                    WHERE c.TwinID = @twinId 
                    AND c.id = @travelId 
                    AND i.id = @itineraryId";

                var queryDefinition = new QueryDefinition(sqlQuery)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@travelId", travelId)
                    .WithParameter("@itineraryId", itineraryId);

                var bookings = new List<TravelBooking>();
                
                using var iterator = _travelContainer.GetItemQueryIterator<Dictionary<string, object?>>(queryDefinition);

                while (iterator.HasMoreResults)
                {
                    var cosmosResponse = await iterator.ReadNextAsync();
                    
                    foreach (var itineraryDict in cosmosResponse)
                    {
                        // Parse the itinerary from the query result
                        string itineraryJson = JsonConvert.SerializeObject(itineraryDict);
                        Itinerario itinerario = JsonConvert.DeserializeObject<Itinerario>(itineraryJson);

                        // Extract bookings if they exist
                        if (itinerario?.bookings != null && itinerario.bookings.Any())
                        {
                            bookings = itinerario.bookings.Select(ConvertBookingToTravelBooking).ToList();
                        }
                        
                        break; // We only expect one itinerary
                    }
                }

                response.Success = true;
                response.Message = $"Found {bookings.Count} bookings for itinerary";
                response.Bookings = bookings;
                response.TotalBookings = bookings.Count;

                _logger.LogInformation("✅ Retrieved {Count} bookings for Itinerary: {ItineraryId} (OPTIMIZED QUERY)", 
                    bookings.Count, itineraryId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel or itinerary not found for booking retrieval: Travel: {TravelId}, Itinerary: {ItineraryId}, Twin: {TwinId}", 
                    travelId, itineraryId, twinId);
                
                response.Success = false;
                response.Message = "Travel record or itinerary not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting bookings for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error retrieving bookings: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Create a new daily activity within an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="request">Daily activity creation request</param>
        /// <returns>Daily activity creation response</returns>
        public async Task<DailyActivityResponse> CreateDailyActivityAsync(string travelId, string twinId, string itineraryId, CreateDailyActivityRequest request)
        {
            _logger.LogInformation("🎯 Creating new daily activity for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                itineraryId, travelId, twinId);

            var response = new DailyActivityResponse();

            try
            {
                // Get existing travel record using simple approach
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary
                var itinerary = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itinerary == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Create new daily activity
                var newActivity = new ActividadDiaria
                {
                    id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    fecha = request.Fecha,
                    horaInicio = request.HoraInicio,
                    horaFin = request.HoraFin,
                    tipoActividad = request.TipoActividad.ToString().ToLowerInvariant(),
                    titulo = request.Titulo,
                    descripcion = request.Descripcion,
                    ubicacion = request.Ubicacion,
                    participantes = request.Participantes ?? new List<string>(),
                    calificacion = request.Calificacion,
                    notas = request.Notas,
                    costo = request.Costo,
                    moneda = request.Moneda ?? "USD",
                    fechaCreacion = DateTime.UtcNow,
                    fechaActualizacion = DateTime.UtcNow,
                    itinerarioId = itineraryId,
                    twinId = twinId,
                    viajeId = travelId,
                    documentType = "dailyActivity"
                };

                // Add coordinates if provided
                if (request.Coordenadas != null)
                {
                    newActivity.coordenadas = new CoordenadasActividad
                    {
                        latitud = request.Coordenadas.Latitud,
                        longitud = request.Coordenadas.Longitud
                    };
                }

                // Initialize activities list if null
                if (itinerary.actividadesDiarias == null)
                    itinerary.actividadesDiarias = new List<ActividadDiaria>();

                // Add activity to itinerary
                itinerary.actividadesDiarias.Add(newActivity);

                // Update travel's last modified date
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert activity to DailyActivity for response
                var createdActivity = ConvertActividadDiariaToDaily(newActivity);
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itinerary);

                response.Success = true;
                response.Message = $"Daily activity '{request.Titulo}' created successfully";
                response.Data = createdActivity;
                response.ItineraryData = updatedItinerary;

                _logger.LogInformation("✅ Daily activity created successfully: {ActivityId} for Itinerary: {ItineraryId}", 
                    newActivity.id, itineraryId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel record not found for daily activity creation: {TravelId} for Twin: {TwinId}", 
                    travelId, twinId);
                
                response.Success = false;
                response.Message = "Travel record not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error creating daily activity for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error creating daily activity: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Update an existing daily activity within an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="activityId">Daily activity ID</param>
        /// <param name="updateRequest">Update data for the daily activity</param>
        /// <returns>Daily activity response</returns>
        public async Task<DailyActivityResponse> UpdateDailyActivityAsync(string travelId, string twinId, string itineraryId, string activityId, UpdateDailyActivityRequest updateRequest)
        {
            _logger.LogInformation("📝 Updating daily activity: {ActivityId} for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                activityId, itineraryId, travelId, twinId);

            var response = new DailyActivityResponse();

            try
            {
                // Get existing travel record using simple approach - SAME AS CREATE METHOD
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary
                var itinerary = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itinerary == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Find the specific daily activity to update
                var activityToUpdate = itinerary.actividadesDiarias?.FirstOrDefault(a => a.id == activityId);
                if (activityToUpdate == null)
                {
                    _logger.LogWarning("⚠️ Daily activity not found: {ActivityId} in Itinerary: {ItineraryId}", activityId, itineraryId);
                    response.Success = false;
                    response.Message = "Daily activity not found";
                    return response;
                }

                // Update activity fields if provided
                if (updateRequest.Fecha.HasValue)
                    activityToUpdate.fecha = updateRequest.Fecha.Value;

                if (!string.IsNullOrEmpty(updateRequest.HoraInicio))
                    activityToUpdate.horaInicio = updateRequest.HoraInicio;

                if (!string.IsNullOrEmpty(updateRequest.HoraFin))
                    activityToUpdate.horaFin = updateRequest.HoraFin;

                if (updateRequest.TipoActividad.HasValue)
                    activityToUpdate.tipoActividad = updateRequest.TipoActividad.Value.ToString().ToLowerInvariant();

                if (!string.IsNullOrEmpty(updateRequest.Titulo))
                    activityToUpdate.titulo = updateRequest.Titulo;

                if (updateRequest.Descripcion != null) // Allow setting to empty string
                    activityToUpdate.descripcion = updateRequest.Descripcion;

                if (updateRequest.Ubicacion != null)
                    activityToUpdate.ubicacion = updateRequest.Ubicacion;

                if (updateRequest.Participantes != null)
                    activityToUpdate.participantes = updateRequest.Participantes;

                if (updateRequest.Calificacion.HasValue)
                    activityToUpdate.calificacion = updateRequest.Calificacion;

                if (updateRequest.Notas != null)
                    activityToUpdate.notas = updateRequest.Notas;

                if (updateRequest.Costo.HasValue)
                    activityToUpdate.costo = updateRequest.Costo;

                if (!string.IsNullOrEmpty(updateRequest.Moneda))
                    activityToUpdate.moneda = updateRequest.Moneda;

                // Update coordinates if provided
                if (updateRequest.Coordenadas != null)
                {
                    activityToUpdate.coordenadas = new CoordenadasActividad
                    {
                        latitud = updateRequest.Coordenadas.Latitud,
                        longitud = updateRequest.Coordenadas.Longitud
                    };
                }

                // Update timestamps
                activityToUpdate.fechaActualizacion = DateTime.UtcNow;
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save - SAME AS CREATE METHOD
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert updated activity to DailyActivity for response
                var updatedActivity = ConvertActividadDiariaToDaily(activityToUpdate);
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itinerary);

                response.Success = true;
                response.Message = $"Daily activity '{updatedActivity.Titulo}' updated successfully";
                response.Data = updatedActivity;
                response.ItineraryData = updatedItinerary;

                _logger.LogInformation("✅ Daily activity updated successfully: {Title} (ID: {ActivityId})", 
                    updatedActivity.Titulo, activityId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel or itinerary not found for daily activity update: Travel: {TravelId}, Itinerary: {ItineraryId}, Twin: {TwinId}", 
                    travelId, itineraryId, twinId);
                
                response.Success = false;
                response.Message = "Travel record or itinerary not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating daily activity: {ActivityId}, Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    activityId, itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error updating daily activity: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Delete a daily activity from an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="activityId">Daily activity ID</param>
        /// <returns>Daily activity response</returns>
        public async Task<DailyActivityResponse> DeleteDailyActivityAsync(string travelId, string twinId, string itineraryId, string activityId)
        {
            _logger.LogInformation("🗑️ Deleting daily activity: {ActivityId} from Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                activityId, itineraryId, travelId, twinId);

            var response = new DailyActivityResponse();

            try
            {
                // Get existing travel record using simple approach - SAME AS CREATE METHOD
                var existingResponse = await _travelContainer.ReadItemAsync<Dictionary<string, object?>>(
                    travelId, new PartitionKey(twinId));

                // Convert to Viaje for easy manipulation
                string json = JsonConvert.SerializeObject(existingResponse.Resource);
                Viaje viaje = JsonConvert.DeserializeObject<Viaje>(json);

                if (viaje == null)
                {
                    _logger.LogWarning("⚠️ Travel record not found: {TravelId} for Twin: {TwinId}", travelId, twinId);
                    response.Success = false;
                    response.Message = "Travel record not found";
                    return response;
                }

                // Find the itinerary
                var itinerary = viaje.itinerarios?.FirstOrDefault(i => i.id == itineraryId);
                if (itinerary == null)
                {
                    _logger.LogWarning("⚠️ Itinerary not found: {ItineraryId} in Travel: {TravelId}", itineraryId, travelId);
                    response.Success = false;
                    response.Message = "Itinerary not found in travel record";
                    return response;
                }

                // Find the specific daily activity to delete
                var activityToDelete = itinerary.actividadesDiarias?.FirstOrDefault(a => a.id == activityId);
                if (activityToDelete == null)
                {
                    _logger.LogWarning("⚠️ Daily activity not found: {ActivityId} in Itinerary: {ItineraryId}", activityId, itineraryId);
                    response.Success = false;
                    response.Message = "Daily activity not found";
                    return response;
                }

                // Convert to external model before deletion
                var deletedActivity = ConvertActividadDiariaToDaily(activityToDelete);

                // Remove the activity from the itinerary
                itinerary.actividadesDiarias.Remove(activityToDelete);

                // Update travel's last modified date
                viaje.fechaActualizacion = DateTime.UtcNow;

                // Convert back to dictionary and save - SAME AS CREATE METHOD
                string updatedJson = JsonConvert.SerializeObject(viaje);
                var updatedDict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(updatedJson);

                await _travelContainer.UpsertItemAsync(updatedDict, new PartitionKey(twinId));

                // Convert updated itinerary for response
                var updatedItinerary = ConvertItinerarioToTravelItinerary(itinerary);

                response.Success = true;
                response.Message = $"Daily activity '{deletedActivity.Titulo}' deleted successfully";
                response.Data = deletedActivity;
                response.ItineraryData = updatedItinerary;

                _logger.LogInformation("✅ Daily activity deleted successfully: {Title} (ID: {ActivityId})", 
                    deletedActivity.Titulo, activityId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel or itinerary not found for daily activity deletion: Travel: {TravelId}, Itinerary: {ItineraryId}, Twin: {TwinId}", 
                    travelId, itineraryId, twinId);
                
                response.Success = false;
                response.Message = "Travel record or itinerary not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error deleting daily activity: {ActivityId}, Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    activityId, itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error deleting daily activity: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Get all daily activities for a specific itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <returns>List of daily activities response</returns>
        public async Task<GetDailyActivitiesResponse> GetDailyActivitiesByItineraryAsync(string travelId, string twinId, string itineraryId)
        {
            _logger.LogInformation("🎯 Getting daily activities for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                itineraryId, travelId, twinId);

            var response = new GetDailyActivitiesResponse();

            try
            {
                // Use optimized SQL query to get only the specific itinerary with activities
                var sqlQuery = @"
                    SELECT VALUE i 
                    FROM c 
                    JOIN i IN c.itinerarios 
                    WHERE c.TwinID = @twinId 
                    AND c.id = @travelId 
                    AND i.id = @itineraryId";

                var queryDefinition = new QueryDefinition(sqlQuery)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@travelId", travelId)
                    .WithParameter("@itineraryId", itineraryId);

                var activities = new List<DailyActivity>();
                
                using var iterator = _travelContainer.GetItemQueryIterator<Dictionary<string, object?>>(queryDefinition);

                while (iterator.HasMoreResults)
                {
                    var cosmosResponse = await iterator.ReadNextAsync();
                    
                    foreach (var itineraryDict in cosmosResponse)
                    {
                        // Parse the itinerary from the query response
                        string itineraryJson = JsonConvert.SerializeObject(itineraryDict);
                        Itinerario itinerario = JsonConvert.DeserializeObject<Itinerario>(itineraryJson);

                        // Extract daily activities if they exist
                        if (itinerario?.actividadesDiarias != null && itinerario.actividadesDiarias.Any())
                        {
                            activities = itinerario.actividadesDiarias.Select(ConvertActividadDiariaToDaily).ToList();
                        }
                        
                        break; // We only expect one itinerary
                    }
                }

                response.Success = true;
                response.Message = $"Found {activities.Count} daily activities for itinerary";
                response.Activities = activities;
                response.TotalActivities = activities.Count;

                _logger.LogInformation("✅ Retrieved {Count} daily activities for Itinerary: {ItineraryId} (OPTIMIZED QUERY)", 
                    activities.Count, itineraryId);

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel or itinerary not found for daily activities retrieval: Travel: {TravelId}, Itinerary: {ItineraryId}, Twin: {TwinId}", 
                    travelId, itineraryId, twinId);
                
                response.Success = false;
                response.Message = "Travel record or itinerary not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting daily activities for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error retrieving daily activities: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Get a specific daily activity by ID within an itinerary
        /// </summary>
        /// <param name="travelId">Travel record ID</param>
        /// <param name="twinId">Twin ID for partition key</param>
        /// <param name="itineraryId">Itinerary ID</param>
        /// <param name="activityId">Daily activity ID</param>
        /// <returns>Daily activity response</returns>
        public async Task<DailyActivityResponse> GetDailyActivityByIdAsync(string travelId, string twinId, string itineraryId, string activityId)
        {
            _logger.LogInformation("🎯 Getting daily activity: {ActivityId} for Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                activityId, itineraryId, travelId, twinId);

            var response = new DailyActivityResponse();

            try
            {
                // Use optimized SQL query to get only the specific itinerary with activities
                var sqlQuery = @"
                    SELECT VALUE i 
                    FROM c 
                    JOIN i IN c.itinerarios 
                    WHERE c.TwinID = @twinId 
                    AND c.id = @travelId 
                    AND i.id = @itineraryId";

                var queryDefinition = new QueryDefinition(sqlQuery)
                    .WithParameter("@twinId", twinId)
                    .WithParameter("@travelId", travelId)
                    .WithParameter("@itineraryId", itineraryId);

                DailyActivity? foundActivity = null;
                
                using var iterator = _travelContainer.GetItemQueryIterator<Dictionary<string, object?>>(queryDefinition);

                while (iterator.HasMoreResults)
                {
                    var cosmosResponse = await iterator.ReadNextAsync();
                    
                    foreach (var itineraryDict in cosmosResponse)
                    {
                        // Parse the itinerary from the query response
                        string itineraryJson = JsonConvert.SerializeObject(itineraryDict);
                        Itinerario itinerario = JsonConvert.DeserializeObject<Itinerario>(itineraryJson);

                        // Find the specific activity
                        if (itinerario?.actividadesDiarias != null)
                        {
                            var activity = itinerario.actividadesDiarias.FirstOrDefault(a => a.id == activityId);
                            if (activity != null)
                            {
                                foundActivity = ConvertActividadDiariaToDaily(activity);
                            }
                        }
                        
                        break; // We only expect one itinerary
                    }
                }

                if (foundActivity != null)
                {
                    response.Success = true;
                    response.Message = $"Daily activity '{foundActivity.Titulo}' retrieved successfully";
                    response.Data = foundActivity;

                    _logger.LogInformation("✅ Daily activity found: {Title} (ID: {ActivityId})", 
                        foundActivity.Titulo, activityId);
                }
                else
                {
                    response.Success = false;
                    response.Message = "Daily activity not found";
                    
                    _logger.LogWarning("⚠️ Daily activity not found: {ActivityId} in Itinerary: {ItineraryId}", 
                        activityId, itineraryId);
                }

                return response;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("⚠️ Travel or itinerary not found for daily activity retrieval: Travel: {TravelId}, Itinerary: {ItineraryId}, Twin: {TwinId}", 
                    travelId, itineraryId, twinId);
                
                response.Success = false;
                response.Message = "Travel record or itinerary not found";
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting daily activity by ID: {ActivityId}, Itinerary: {ItineraryId}, Travel: {TravelId}, Twin: {TwinId}", 
                    activityId, itineraryId, travelId, twinId);
                
                response.Success = false;
                response.Message = $"Error retrieving daily activity: {ex.Message}";
                return response;
            }
        }

        /// <summary>
        /// Calculate travel statistics for a list of travels
        /// </summary>
        /// <param name="travels">List of travel records</param>
        /// <returns>Travel statistics</returns>
        private TravelStats CalculateTravelStats(List<TravelData> travels)
        {
            var stats = new TravelStats
            {
                Total = travels.Count
            };

            foreach (var travel in travels)
            {
                // Count by status
                switch (travel.Estado)
                {
                    case TravelStatus.Planeando:
                        stats.Planeando++;
                        break;
                    case TravelStatus.Confirmado:
                        stats.Confirmado++;
                        break;
                    case TravelStatus.EnProgreso:
                        stats.EnProgreso++;
                        break;
                    case TravelStatus.Completado:
                        stats.Completado++;
                        break;
                    case TravelStatus.Cancelado:
                        stats.Cancelado++;
                        break;
                }

                // Count by type
                var typeKey = travel.TipoViaje.ToString().ToLowerInvariant();
                if (stats.ByType.ContainsKey(typeKey))
                    stats.ByType[typeKey]++;
                else
                    stats.ByType[typeKey] = 1;

                // Sum budget
                if (travel.Presupuesto.HasValue)
                    stats.TotalBudget += travel.Presupuesto.Value;

                // Count countries
                if (!string.IsNullOrEmpty(travel.PaisDestino))
                {
                    if (stats.TopCountries.ContainsKey(travel.PaisDestino))
                        stats.TopCountries[travel.PaisDestino]++;
                    else
                        stats.TopCountries[travel.PaisDestino] = 1;
                }
            }

            return stats;
        }

        /// <summary>
        /// Calculate travel duration in days between two dates
        /// </summary>
        /// <param name="fechaInicio">Start date</param>
        /// <param name="fechaFin">End date</param>
        /// <returns>Duration in days or null if dates are invalid</returns>
        private int? CalculateDuration(DateTime? fechaInicio, DateTime? fechaFin)
        {
            if (fechaInicio.HasValue && fechaFin.HasValue && fechaFin > fechaInicio)
            {
                return (int)(fechaFin.Value - fechaInicio.Value).TotalDays + 1;
            }
            return null;
        }

        /// <summary>
        /// Convert Viaje to TravelData
        /// </summary>
        /// <param name="viaje">Viaje object from Cosmos DB</param>
        /// <returns>TravelData object</returns>
        private TravelData ConvertViajeToTravelData(Viaje viaje)
        {
            var travelData = new TravelData
            {
                Id = viaje.id,
                Titulo = viaje.titulo,
                Descripcion = viaje.descripcion,
                TwinID = viaje.TwinID,
                DocumentType = viaje.documentType,
                FechaCreacion = viaje.fechaCreacion,
                FechaActualizacion = viaje.fechaActualizacion,
                FechaInicio = viaje.fechaInicio,
                FechaFin = viaje.fechaFin,
                Moneda = viaje.moneda
            };

            // Parse enums safely
            if (Enum.TryParse<TravelType>(viaje.tipoViaje, true, out var tipoViaje))
                travelData.TipoViaje = tipoViaje;

            if (Enum.TryParse<TravelStatus>(viaje.estado, true, out var estado))
                travelData.Estado = estado;

            if (Enum.TryParse<TravelStatusType>(viaje.status, true, out var status))
                travelData.Status = status;

            // Convert itinerarios
            if (viaje.itinerarios != null && viaje.itinerarios.Any())
            {
                travelData.Itinerarios = viaje.itinerarios.Select(ConvertItinerarioToTravelItinerary).ToList();
            }

            return travelData;
        }

        /// <summary>
        /// Convert Itinerario to TravelItinerary
        /// </summary>
        /// <param name="itinerario">Itinerario object</param>
        /// <returns>TravelItinerary object</returns>
        private TravelItinerary ConvertItinerarioToTravelItinerary(Itinerario itinerario)
        {
            var travelItinerary = new TravelItinerary
            {
                Id = itinerario.id,
                Titulo = itinerario.titulo,
                TwinId = itinerario.twinId,
                ViajeId = itinerario.viajeId,
                DocumentType = itinerario.documentType,
                FechaCreacion = itinerario.fechaCreacion,
                CiudadOrigen = itinerario.ciudadOrigen,
                PaisOrigen = itinerario.paisOrigen,
                CiudadDestino = itinerario.ciudadDestino,
                PaisDestino = itinerario.paisDestino,
                FechaInicio = itinerario.fechaInicio,
                FechaFin = itinerario.fechaFin,
                PresupuestoEstimado = itinerario.presupuestoEstimado,
                Moneda = itinerario.moneda,
                Notas = itinerario.notas
            };

            // Parse enums safely
            if (Enum.TryParse<TransportationType>(itinerario.medioTransporte, true, out var transporte))
                travelItinerary.MedioTransporte = transporte;

            if (Enum.TryParse<AccommodationType>(itinerario.tipoAlojamiento, true, out var alojamiento))
                travelItinerary.TipoAlojamiento = alojamiento;

            // Convert viajeInfo if exists
            if (itinerario.viajeInfo != null)
            {
                travelItinerary.ViajeInfo = new TravelContext
                {
                    Titulo = itinerario.viajeInfo.titulo,
                    Descripcion = itinerario.viajeInfo.descripcion,
                    TipoViaje = itinerario.viajeInfo.tipoViaje,
                    Estado = itinerario.viajeInfo.estado
                };
            }

            // Convert bookings if exist
            if (itinerario.bookings != null && itinerario.bookings.Any())
            {
                travelItinerary.Bookings = itinerario.bookings.Select(ConvertBookingToTravelBooking).ToList();
            }

            // Convert daily activities if exist
            if (itinerario.actividadesDiarias != null && itinerario.actividadesDiarias.Any())
            {
                travelItinerary.ActividadesDiarias = itinerario.actividadesDiarias.Select(ConvertActividadDiariaToDaily).ToList();
            }

            return travelItinerary;
        }

        /// <summary>
        /// Convert Booking to TravelBooking
        /// </summary>
        /// <param name="booking">Booking object</param>
        /// <returns>TravelBooking object</returns>
        private TravelBooking ConvertBookingToTravelBooking(Booking booking)
        {
            var travelBooking = new TravelBooking
            {
                Id = booking.id,
                Titulo = booking.titulo,
                Descripcion = booking.descripcion,
                FechaInicio = booking.fechaInicio == DateTime.MinValue ? null : booking.fechaInicio,
                FechaFin = booking.fechaFin,
                HoraInicio = booking.horaInicio,
                HoraFin = booking.horaFin,
                Proveedor = booking.proveedor,
                Precio = booking.precio == 0 ? null : booking.precio,
                Moneda = booking.moneda,
                NumeroConfirmacion = booking.numeroConfirmacion,
                Notas = booking.notas,
                FechaCreacion = booking.fechaCreacion,
                FechaActualizacion = booking.fechaActualizacion,
                ItinerarioId = booking.itinerarioId,
                TwinId = booking.twinId,
                ViajeId = booking.viajeId,
                DocumentType = booking.documentType
            };

            // Parse enums safely
            if (Enum.TryParse<BookingType>(booking.tipo, true, out var tipo))
                travelBooking.Tipo = tipo;

            if (Enum.TryParse<BookingStatus>(booking.estado, true, out var estado))
                travelBooking.Estado = estado;

            // Convert contact if exists
            if (booking.contacto != null)
            {
                travelBooking.Contacto = new BookingContact
                {
                    Telefono = booking.contacto.telefono,
                    Email = booking.contacto.email,
                    Direccion = booking.contacto.direccion
                };
            }

            return travelBooking;
        }

        /// <summary>
        /// Convert ActividadDiaria to DailyActivity
        /// </summary>
        /// <param name="actividad">ActividadDiaria object</param>
        /// <returns>DailyActivity object</returns>
        private DailyActivity ConvertActividadDiariaToDaily(ActividadDiaria actividad)
        {
            var dailyActivity = new DailyActivity
            {
                Id = actividad.id,
                Fecha = actividad.fecha,
                HoraInicio = actividad.horaInicio,
                HoraFin = actividad.horaFin,
                Titulo = actividad.titulo,
                Descripcion = actividad.descripcion,
                Ubicacion = actividad.ubicacion,
                Participantes = actividad.participantes ?? new List<string>(),
                Calificacion = actividad.calificacion,
                Notas = actividad.notas,
                Costo = actividad.costo,
                Moneda = actividad.moneda,
                FechaCreacion = actividad.fechaCreacion,
                FechaActualizacion = actividad.fechaActualizacion,
                ItinerarioId = actividad.itinerarioId,
                TwinId = actividad.twinId,
                ViajeId = actividad.viajeId,
                DocumentType = actividad.documentType
            };

            // Parse enum safely
            if (Enum.TryParse<ActivityType>(actividad.tipoActividad, true, out var tipo))
                dailyActivity.TipoActividad = tipo;

            // Convert coordinates if exists
            if (actividad.coordenadas != null)
            {
                dailyActivity.Coordenadas = new ActivityCoordinates
                {
                    Latitud = actividad.coordenadas.latitud,
                    Longitud = actividad.coordenadas.longitud
                };
            }

            return dailyActivity;
        }

        /// <summary>
        /// Dispose resources
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cosmosClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    public class Viaje
    {
        public string id { get; set; }
        public string titulo { get; set; }
        public string descripcion { get; set; }
        public string TwinID { get; set; }
        public string documentType { get; set; }
        public DateTime fechaCreacion { get; set; }
        public DateTime fechaActualizacion { get; set; }
        public string tipoViaje { get; set; }
        public string estado { get; set; }
        public string status { get; set; }
        public DateTime fechaInicio { get; set; }
        public DateTime fechaFin { get; set; }
        public string moneda { get; set; }
        public List<Itinerario> itinerarios { get; set; } = new List<Itinerario>();
        public string _rid { get; set; }
        public string _self { get; set; }
        public string _etag { get; set; }
        public string _attachments { get; set; }
        public long _ts { get; set; }
    }

    public class Itinerario
    {
        public string id { get; set; }
        public string titulo { get; set; }
        public string twinId { get; set; }
        public string viajeId { get; set; }
        public string documentType { get; set; }
        public DateTime fechaCreacion { get; set; }
        public string medioTransporte { get; set; }
        public string tipoAlojamiento { get; set; }
        public string ciudadOrigen { get; set; }
        public string paisOrigen { get; set; }
        public string ciudadDestino { get; set; }
        public string paisDestino { get; set; }
        public DateTime fechaInicio { get; set; }
        public DateTime fechaFin { get; set; }
        public decimal presupuestoEstimado { get; set; }
        public string moneda { get; set; }
        public string notas { get; set; }
        public ViajeInfo viajeInfo { get; set; }
        public List<Booking> bookings { get; set; } = new List<Booking>();
        public List<ActividadDiaria> actividadesDiarias { get; set; } = new List<ActividadDiaria>();
    }

    public class ViajeInfo
    {
        public string titulo { get; set; }
        public string descripcion { get; set; }
        public string tipoViaje { get; set; }
        public string estado { get; set; }
    }

    public class Booking
    {
        public string id { get; set; }
        public string tipo { get; set; }
        public string titulo { get; set; }
        public string descripcion { get; set; }
        public DateTime fechaInicio { get; set; }
        public DateTime? fechaFin { get; set; }
        public string horaInicio { get; set; }
        public string horaFin { get; set; }
        public string proveedor { get; set; }
        public ContactoBooking contacto { get; set; }
        public decimal precio { get; set; }
        public string moneda { get; set; }
        public string numeroConfirmacion { get; set; }
        public string estado { get; set; }
        public string notas { get; set; }
        public DateTime fechaCreacion { get; set; }
        public DateTime fechaActualizacion { get; set; }
        public string itinerarioId { get; set; }
        public string twinId { get; set; }
        public string viajeId { get; set; }
        public string documentType { get; set; }
    }

    public class ContactoBooking
    {
        public string telefono { get; set; }
        public string email { get; set; }
        public string direccion { get; set; }
    }

    public class ActividadDiaria
    {
        public string id { get; set; }
        public DateTime fecha { get; set; }
        public string horaInicio { get; set; }
        public string horaFin { get; set; }
        public string tipoActividad { get; set; }
        public string titulo { get; set; }
        public string descripcion { get; set; }
        public string ubicacion { get; set; }
        public List<string> participantes { get; set; }
        public int? calificacion { get; set; }
        public string notas { get; set; }
        public decimal? costo { get; set; }
        public string moneda { get; set; }
        public CoordenadasActividad coordenadas { get; set; }
        public DateTime fechaCreacion { get; set; }
        public DateTime fechaActualizacion { get; set; }
        public string itinerarioId { get; set; }
        public string twinId { get; set; }
        public string viajeId { get; set; }
        public string documentType { get; set; }
    }

    public class CoordenadasActividad
    {
        public decimal latitud { get; set; }
        public decimal longitud { get; set; }
    }
}