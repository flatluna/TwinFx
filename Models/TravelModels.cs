using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwinFx.Models;

// ========================================================================================
// TRAVEL DATA MODELS
// ========================================================================================

/// <summary>
/// Travel data model for tracking trips and travel experiences
/// </summary>
public class TravelData
{
    /// <summary>
    /// Unique identifier for the travel record
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Travel title/name (required, minimum 2 characters)
    /// </summary>
    [JsonPropertyName("titulo")]
    [Required(ErrorMessage = "Travel title is required")]
    [MinLength(2, ErrorMessage = "Travel title must be at least 2 characters")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Travel description (required)
    /// </summary>
    [JsonPropertyName("descripcion")]
    [Required(ErrorMessage = "Travel description is required")]
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>
    /// Destination country (optional)
    /// </summary>
    [JsonPropertyName("paisDestino")]
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Destination city (optional)
    /// </summary>
    [JsonPropertyName("ciudadDestino")]
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Travel start date (optional)
    /// </summary>
    [JsonPropertyName("fechaInicio")]
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Travel end date (optional)
    /// </summary>
    [JsonPropertyName("fechaFin")]
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Travel duration in days (calculated from dates or manual input)
    /// </summary>
    [JsonPropertyName("duracionDias")]
    public int? DuracionDias { get; set; }

    /// <summary>
    /// Travel budget (optional)
    /// </summary>
    [JsonPropertyName("presupuesto")]
    public decimal? Presupuesto { get; set; }

    /// <summary>
    /// Currency for budget (optional, default USD)
    /// </summary>
    [JsonPropertyName("moneda")]
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// Travel type: 'vacaciones', 'negocios', 'familiar', 'aventura', 'cultural', 'otro'
    /// </summary>
    [JsonPropertyName("tipoViaje")]
    public TravelType TipoViaje { get; set; } = TravelType.Vacaciones;

    /// <summary>
    /// Travel status: 'planeando', 'confirmado', 'en_progreso', 'completado', 'cancelado'
    /// </summary>
    [JsonPropertyName("estado")]
    public TravelStatus Estado { get; set; } = TravelStatus.Planeando;

    /// <summary>
    /// Overall travel status: 'activo', 'planeando', 'inactivo', 'suspendido', 'archivado'
    /// </summary>
    [JsonPropertyName("status")]
    public TravelStatusType Status { get; set; } = TravelStatusType.Activo;

    /// <summary>
    /// Transportation method (optional)
    /// </summary>
    [JsonPropertyName("transporte")]
    public string? Transporte { get; set; }

    /// <summary>
    /// Accommodation details (optional)
    /// </summary>
    [JsonPropertyName("alojamiento")]
    public string? Alojamiento { get; set; }

    /// <summary>
    /// Travel companions (optional)
    /// </summary>
    [JsonPropertyName("compañeros")]
    public string? Compañeros { get; set; }

    /// <summary>
    /// Activities planned/done (optional)
    /// </summary>
    [JsonPropertyName("actividades")]
    public string? Actividades { get; set; }

    /// <summary>
    /// Travel notes and observations (optional)
    /// </summary>
    [JsonPropertyName("notas")]
    public string? Notas { get; set; }

    /// <summary>
    /// Travel rating (1-5 stars, optional)
    /// </summary>
    [JsonPropertyName("calificacion")]
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Calificacion { get; set; }

    /// <summary>
    /// Travel highlights (optional)
    /// </summary>
    [JsonPropertyName("highlights")]
    public string? Highlights { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    [JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Twin ID for integration with TwinFx ecosystem (required, partition key)
    /// </summary>
    [JsonPropertyName("TwinID")]
    [Required(ErrorMessage = "Twin ID is required")]
    public string TwinID { get; set; } = string.Empty;

    /// <summary>
    /// Document type for Cosmos DB organization
    /// </summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = "travel";

    /// <summary>
    /// List of itineraries associated with this travel
    /// </summary>
    [JsonPropertyName("itinerarios")]
    public List<TravelItinerary> Itinerarios { get; set; } = new();

    /// <summary>
    /// Convert Dictionary from Cosmos DB to TravelData
    /// </summary>
    public static TravelData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key) =>
            data.ContainsKey(key) ? (T) Convert.ChangeType(data[key], typeof(T)) : default!;

        return new TravelData
        {
            Id = GetValue<string>("id"),
            Titulo = GetValue<string>("titulo"),
            Descripcion = GetValue<string>("descripcion"),
            PaisDestino = GetValue<string?>("paisDestino"),
            CiudadDestino = GetValue<string?>("ciudadDestino"),
            FechaInicio = GetValue<DateTime?>("fechaInicio"),
            FechaFin = GetValue<DateTime?>("fechaFin"),
            DuracionDias = GetValue<int?>("duracionDias"),
            Presupuesto = GetValue<decimal?>("presupuesto"),
            Moneda = GetValue<string?>("moneda"),
            TipoViaje = GetValue<TravelType>("tipoViaje"),
            Estado = GetValue<TravelStatus>("estado"),
            Status = GetValue<TravelStatusType>("status"),
            Transporte = GetValue<string?>("transporte"),
            Alojamiento = GetValue<string?>("alojamiento"),
            Compañeros = GetValue<string?>("compañeros"),
            Actividades = GetValue<string?>("actividades"),
            Notas = GetValue<string?>("notas"),
            Calificacion = GetValue<int?>("calificacion"),
            Highlights = GetValue<string?>("highlights"),
            FechaCreacion = GetValue<DateTime>("fechaCreacion"),
            FechaActualizacion = GetValue<DateTime>("fechaActualizacion"),
            TwinID = GetValue<string>("TwinID"),
            DocumentType = GetValue<string>("documentType"),
            Itinerarios = GetValue<List<TravelItinerary>>("itinerarios")
        };
    }

    /// <summary>
    /// Convert TravelData to Dictionary for Cosmos DB
    /// </summary>
    public Dictionary<string, object?> ToDict()
    {
        var dict = new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["titulo"] = Titulo,
            ["descripcion"] = Descripcion,
            ["paisDestino"] = PaisDestino,
            ["ciudadDestino"] = CiudadDestino,
            ["fechaInicio"] = FechaInicio,
            ["fechaFin"] = FechaFin,
            ["duracionDias"] = DuracionDias,
            ["presupuesto"] = Presupuesto,
            ["moneda"] = Moneda,
            ["tipoViaje"] = TipoViaje,
            ["estado"] = Estado,
            ["status"] = Status,
            ["transporte"] = Transporte,
            ["alojamiento"] = Alojamiento,
            ["compañeros"] = Compañeros,
            ["actividades"] = Actividades,
            ["notas"] = Notas,
            ["calificacion"] = Calificacion,
            ["highlights"] = Highlights,
            ["fechaCreacion"] = FechaCreacion,
            ["fechaActualizacion"] = FechaActualizacion,
            ["TwinID"] = TwinID,
            ["documentType"] = DocumentType,
            ["itinerarios"] = Itinerarios
        };

        return dict;
    }
}

/// <summary>
/// Activity type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActivityType
{
    [JsonPropertyName("museo")]
    Museo,

    [JsonPropertyName("restaurante")]
    Restaurante,

    [JsonPropertyName("tour")]
    Tour,

    [JsonPropertyName("compras")]
    Compras,

    [JsonPropertyName("naturaleza")]
    Naturaleza,

    [JsonPropertyName("entretenimiento")]
    Entretenimiento,

    [JsonPropertyName("deportes")]
    Deportes,

    [JsonPropertyName("cultura")]
    Cultura,

    [JsonPropertyName("gastronomia")]
    Gastronomia,

    [JsonPropertyName("relax")]
    Relax,

    [JsonPropertyName("aventura")]
    Aventura,

    [JsonPropertyName("otro")]
    Otro
}

/// <summary>
/// Travel type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TravelType
{
    [JsonPropertyName("vacaciones")]
    Vacaciones,

    [JsonPropertyName("negocios")]
    Negocios,

    [JsonPropertyName("familiar")]
    Familiar,

    [JsonPropertyName("aventura")]
    Aventura,

    [JsonPropertyName("cultural")]
    Cultural,

    [JsonPropertyName("otro")]
    Otro
}

/// <summary>
/// Travel status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TravelStatus
{
    [JsonPropertyName("planeando")]
    Planeando,

    [JsonPropertyName("confirmado")]
    Confirmado,

    [JsonPropertyName("en_progreso")]
    EnProgreso,

    [JsonPropertyName("completado")]
    Completado,

    [JsonPropertyName("cancelado")]
    Cancelado
}

/// <summary>
/// Overall travel status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TravelStatusType
{
    [JsonPropertyName("activo")]
    Activo,

    [JsonPropertyName("planeando")]
    Planeando,

    [JsonPropertyName("inactivo")]
    Inactivo,

    [JsonPropertyName("suspendido")]
    Suspendido,

    [JsonPropertyName("archivado")]
    Archivado
}

// ========================================================================================
// API REQUEST/RESPONSE MODELS
// ========================================================================================

/// <summary>
/// Request model for creating a new travel record
/// </summary>
public class CreateTravelRequest
{
    /// <summary>
    /// Travel title (required)
    /// </summary>
    [Required(ErrorMessage = "Travel title is required")]
    [MinLength(2, ErrorMessage = "Travel title must be at least 2 characters")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Travel description (required)
    /// </summary>
    [Required(ErrorMessage = "Travel description is required")]
    public string Descripcion { get; set; } = string.Empty;

    /// <summary>
    /// Destination country (optional)
    /// </summary>
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Destination city (optional)
    /// </summary>
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Travel start date (optional)
    /// </summary>
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Travel end date (optional)
    /// </summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Travel duration in days (optional)
    /// </summary>
    public int? DuracionDias { get; set; }

    /// <summary>
    /// Travel budget (optional)
    /// </summary>
    public decimal? Presupuesto { get; set; }

    /// <summary>
    /// Currency for budget (optional)
    /// </summary>
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// Travel type (optional, defaults to 'vacaciones')
    /// </summary>
    public TravelType TipoViaje { get; set; } = TravelType.Vacaciones;

    /// <summary>
    /// Travel status (optional, defaults to 'planeando')
    /// </summary>
    public TravelStatus Estado { get; set; } = TravelStatus.Planeando;

    /// <summary>
    /// Overall travel status (optional, defaults to 'activo')
    /// </summary>
    public TravelStatusType Status { get; set; } = TravelStatusType.Activo;

    /// <summary>
    /// Transportation method (optional)
    /// </summary>
    public string? Transporte { get; set; }

    /// <summary>
    /// Accommodation details (optional)
    /// </summary>
    public string? Alojamiento { get; set; }

    /// <summary>
    /// Travel companions (optional)
    /// </summary>
    public string? Compañeros { get; set; }

    /// <summary>
    /// Activities planned/done (optional)
    /// </summary>
    public string? Actividades { get; set; }

    /// <summary>
    /// Travel notes (optional)
    /// </summary>
    public string? Notas { get; set; }

    /// <summary>
    /// Travel rating (optional)
    /// </summary>
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Calificacion { get; set; }

    /// <summary>
    /// Travel highlights (optional)
    /// </summary>
    public string? Highlights { get; set; }
}

/// <summary>
/// Request model for updating an existing travel record
/// </summary>
public class UpdateTravelRequest
{
    /// <summary>
    /// Travel title (optional in update)
    /// </summary>
    [MinLength(2, ErrorMessage = "Travel title must be at least 2 characters")]
    public string? Titulo { get; set; }

    /// <summary>
    /// Travel description (optional in update)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Destination country (optional)
    /// </summary>
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Destination city (optional)
    /// </summary>
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Travel start date (optional)
    /// </summary>
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Travel end date (optional)
    /// </summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Travel duration in days (optional)
    /// </summary>
    public int? DuracionDias { get; set; }

    /// <summary>
    /// Travel budget (optional)
    /// </summary>
    public decimal? Presupuesto { get; set; }

    /// <summary>
    /// Currency for budget (optional)
    /// </summary>
    public string? Moneda { get; set; }

    /// <summary>
    /// Travel type (optional)
    /// </summary>
    public TravelType? TipoViaje { get; set; }

    /// <summary>
    /// Travel status (optional)
    /// </summary>
    public TravelStatus? Estado { get; set; }

    /// <summary>
    /// Overall travel status (optional)
    /// </summary>
    public TravelStatusType? Status { get; set; }

    /// <summary>
    /// Transportation method (optional)
    /// </summary>
    public string? Transporte { get; set; }

    /// <summary>
    /// Accommodation details (optional)
    /// </summary>
    public string? Alojamiento { get; set; }

    /// <summary>
    /// Travel companions (optional)
    /// </summary>
    public string? Compañeros { get; set; }

    /// <summary>
    /// Activities planned/done (optional)
    /// </summary>
    public string? Actividades { get; set; }

    /// <summary>
    /// Travel notes (optional)
    /// </summary>
    public string? Notas { get; set; }

    /// <summary>
    /// Travel rating (optional)
    /// </summary>
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Calificacion { get; set; }

    /// <summary>
    /// Travel highlights (optional)
    /// </summary>
    public string? Highlights { get; set; }
}

/// <summary>
/// Response model for travel operations
/// </summary>
public class TravelResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Travel data (if applicable)
    /// </summary>
    public TravelData? Data { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response model for getting multiple travel records
/// </summary>
public class GetTravelsResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID used in the request
    /// </summary>
    public string TwinID { get; set; } = string.Empty;

    /// <summary>
    /// Total number of travels found
    /// </summary>
    public int TotalTravels { get; set; }

    /// <summary>
    /// Travel records data
    /// </summary>
    public List<TravelData> Travels { get; set; } = new();

    /// <summary>
    /// Statistics by status and type
    /// </summary>
    public TravelStats Stats { get; set; } = new();

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Travel statistics
/// </summary>
public class TravelStats
{
    /// <summary>
    /// Number of travels being planned
    /// </summary>
    public int Planeando { get; set; }

    /// <summary>
    /// Number of confirmed travels
    /// </summary>
    public int Confirmado { get; set; }

    /// <summary>
    /// Number of travels in progress
    /// </summary>
    public int EnProgreso { get; set; }

    /// <summary>
    /// Number of completed travels
    /// </summary>
    public int Completado { get; set; }

    /// <summary>
    /// Number of cancelled travels
    /// </summary>
    public int Cancelado { get; set; }

    /// <summary>
    /// Total count
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Statistics by travel type
    /// </summary>
    public Dictionary<string, int> ByType { get; set; } = new();

    /// <summary>
    /// Total budget across all travels
    /// </summary>
    public decimal TotalBudget { get; set; }

    /// <summary>
    /// Most visited countries
    /// </summary>
    public Dictionary<string, int> TopCountries { get; set; } = new();
}

/// <summary>
/// Query parameters for filtering travel records
/// </summary>
public class TravelQuery
{
    /// <summary>
    /// Filter by travel status
    /// </summary>
    public TravelStatus? Estado { get; set; }

    /// <summary>
    /// Filter by travel type
    /// </summary>
    public TravelType? TipoViaje { get; set; }

    /// <summary>
    /// Filter by destination country (partial match)
    /// </summary>
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Filter by destination city (partial match)
    /// </summary>
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Filter by travel start date from
    /// </summary>
    public DateTime? FechaDesde { get; set; }

    /// <summary>
    /// Filter by travel start date to
    /// </summary>
    public DateTime? FechaHasta { get; set; }

    /// <summary>
    /// Filter by minimum rating
    /// </summary>
    public int? CalificacionMin { get; set; }

    /// <summary>
    /// Filter by maximum budget
    /// </summary>
    public decimal? PresupuestoMax { get; set; }

    /// <summary>
    /// Search term for title, description, notes, or activities
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Page number for pagination (1-based)
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size for pagination (default 20, max 100)
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Sort field (fechaInicio, fechaCreacion, titulo, presupuesto, calificacion)
    /// </summary>
    public string? SortBy { get; set; } = "fechaCreacion";

    /// <summary>
    /// Sort direction (asc, desc)
    /// </summary>
    public string? SortDirection { get; set; } = "desc";
}

// ========================================================================================
// TRAVEL ITINERARY MODELS
// ========================================================================================

/// <summary>
/// Travel itinerary data model for detailed trip planning
/// </summary>
public class TravelItinerary
{
    /// <summary>
    /// Unique identifier for the itinerary (timestamp-based)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    /// <summary>
    /// Itinerary title (required)
    /// </summary>
    [JsonPropertyName("titulo")]
    [Required(ErrorMessage = "Itinerary title is required")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Itinerary description (optional)
    /// </summary>
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Origin city
    /// </summary>
    [JsonPropertyName("ciudadOrigen")]
    public string? CiudadOrigen { get; set; }

    /// <summary>
    /// Origin country
    /// </summary>
    [JsonPropertyName("paisOrigen")]
    public string? PaisOrigen { get; set; }

    /// <summary>
    /// Destination city
    /// </summary>
    [JsonPropertyName("ciudadDestino")]
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Destination country
    /// </summary>
    [JsonPropertyName("paisDestino")]
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Itinerary start date
    /// </summary>
    [JsonPropertyName("fechaInicio")]
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Itinerary end date
    /// </summary>
    [JsonPropertyName("fechaFin")]
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    [JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Transportation method
    /// </summary>
    [JsonPropertyName("medioTransporte")]
    public TransportationType MedioTransporte { get; set; } = TransportationType.Avion;

    /// <summary>
    /// Estimated budget for this itinerary
    /// </summary>
    [JsonPropertyName("presupuestoEstimado")]
    public decimal? PresupuestoEstimado { get; set; }

    /// <summary>
    /// Currency for budget
    /// </summary>
    [JsonPropertyName("moneda")]
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// Accommodation type
    /// </summary>
    [JsonPropertyName("tipoAlojamiento")]
    public AccommodationType TipoAlojamiento { get; set; } = AccommodationType.Hotel;

    /// <summary>
    /// Additional notes
    /// </summary>
    [JsonPropertyName("notas")]
    public string? Notas { get; set; }

    /// <summary>
    /// Twin ID (from parent travel)
    /// </summary>
    [JsonPropertyName("twinId")]
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Parent travel ID
    /// </summary>
    [JsonPropertyName("viajeId")]
    public string ViajeId { get; set; } = string.Empty;

    /// <summary>
    /// Document type for identification
    /// </summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = "itinerary";

    /// <summary>
    /// Context information from parent travel
    /// </summary>
    [JsonPropertyName("viajeInfo")]
    public TravelContext? ViajeInfo { get; set; }

    /// <summary>
    /// List of bookings associated with this itinerary
    /// </summary>
    [JsonPropertyName("bookings")]
    public List<TravelBooking> Bookings { get; set; } = new();

    /// <summary>
    /// List of daily activities associated with this itinerary
    /// </summary>
    [JsonPropertyName("actividadesDiarias")]
    public List<DailyActivity> ActividadesDiarias { get; set; } = new();
}

/// <summary>
/// Transportation type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TransportationType
{
    [JsonPropertyName("avion")]
    Avion,

    [JsonPropertyName("auto")]
    Auto,

    [JsonPropertyName("tren")]
    Tren,

    [JsonPropertyName("autobus")]
    Autobus,

    [JsonPropertyName("barco")]
    Barco,

    [JsonPropertyName("bicicleta")]
    Bicicleta,

    [JsonPropertyName("caminando")]
    Caminando,

    [JsonPropertyName("otro")]
    Otro
}

/// <summary>
/// Accommodation type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AccommodationType
{
    [JsonPropertyName("hotel")]
    Hotel,

    [JsonPropertyName("hostal")]
    Hostal,

    [JsonPropertyName("apartamento")]
    Apartamento,

    [JsonPropertyName("casa")]
    Casa,

    [JsonPropertyName("camping")]
    Camping,

    [JsonPropertyName("resort")]
    Resort,

    [JsonPropertyName("otro")]
    Otro
}

/// <summary>
/// Travel context information for itineraries
/// </summary>
public class TravelContext
{
    [JsonPropertyName("titulo")]
    public string? Titulo { get; set; }

    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    [JsonPropertyName("tipoViaje")]
    public string? TipoViaje { get; set; }

    [JsonPropertyName("estado")]
    public string? Estado { get; set; }
}

// ========================================================================================
// ITINERARY REQUEST/RESPONSE MODELS
// ========================================================================================

/// <summary>
/// Request model for creating a new travel itinerary
/// </summary>
public class CreateItineraryRequest
{
    /// <summary>
    /// Itinerary title (required)
    /// </summary>
    [Required(ErrorMessage = "Itinerary title is required")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Itinerary description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Origin city
    /// </summary>
    public string? CiudadOrigen { get; set; }

    /// <summary>
    /// Origin country
    /// </summary>
    public string? PaisOrigen { get; set; }

    /// <summary>
    /// Destination city
    /// </summary>
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Destination country
    /// </summary>
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Itinerary start date
    /// </summary>
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Itinerary end date
    /// </summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Transportation method
    /// </summary>
    public TransportationType MedioTransporte { get; set; } = TransportationType.Avion;

    /// <summary>
    /// Estimated budget for this itinerary
    /// </summary>
    public decimal? PresupuestoEstimado { get; set; }

    /// <summary>
    /// Currency for budget
    /// </summary>
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// Accommodation type
    /// </summary>
    public AccommodationType TipoAlojamiento { get; set; } = AccommodationType.Hotel;

    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Request model for updating an existing itinerary
/// </summary>
public class UpdateItineraryRequest
{
    /// <summary>
    /// Itinerary title (optional for update)
    /// </summary>
    public string? Titulo { get; set; }

    /// <summary>
    /// Itinerary description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Origin city
    /// </summary>
    public string? CiudadOrigen { get; set; }

    /// <summary>
    /// Origin country
    /// </summary>
    public string? PaisOrigen { get; set; }

    /// <summary>
    /// Destination city
    /// </summary>
    public string? CiudadDestino { get; set; }

    /// <summary>
    /// Destination country
    /// </summary>
    public string? PaisDestino { get; set; }

    /// <summary>
    /// Itinerary start date
    /// </summary>
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Itinerary end date
    /// </summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Transportation method
    /// </summary>
    public TransportationType? MedioTransporte { get; set; }

    /// <summary>
    /// Estimated budget for this itinerary
    /// </summary>
    public decimal? PresupuestoEstimado { get; set; }

    /// <summary>
    /// Currency for budget
    /// </summary>
    public string? Moneda { get; set; }

    /// <summary>
    /// Accommodation type
    /// </summary>
    public AccommodationType? TipoAlojamiento { get; set; }

    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Response model for itinerary operations
/// </summary>
public class ItineraryResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Itinerary data (if applicable)
    /// </summary>
    public TravelItinerary? Data { get; set; }

    /// <summary>
    /// Updated travel data with new itinerary
    /// </summary>
    public TravelData? TravelData { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

// ========================================================================================
// TRAVEL BOOKING MODELS
// ========================================================================================

/// <summary>
/// Travel booking data model for managing reservations within itineraries
/// </summary>
public class TravelBooking
{
    /// <summary>
    /// Unique identifier for the booking (timestamp-based)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    /// <summary>
    /// Booking type: vuelo, hotel, actividad, transporte, restaurante, otro
    /// </summary>
    [JsonPropertyName("tipo")]
    [Required(ErrorMessage = "Booking type is required")]
    public BookingType Tipo { get; set; } = BookingType.Vuelo;

    /// <summary>
    /// Booking title (required)
    /// </summary>
    [JsonPropertyName("titulo")]
    [Required(ErrorMessage = "Booking title is required")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Booking description (optional)
    /// </summary>
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Booking start date
    /// </summary>
    [JsonPropertyName("fechaInicio")]
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Booking end date (optional)
    /// </summary>
    [JsonPropertyName("fechaFin")]
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Start time (optional)
    /// </summary>
    [JsonPropertyName("horaInicio")]
    public string? HoraInicio { get; set; }

    /// <summary>
    /// End time (optional)
    /// </summary>
    [JsonPropertyName("horaFin")]
    public string? HoraFin { get; set; }

    /// <summary>
    /// Service provider
    /// </summary>
    [JsonPropertyName("proveedor")]
    public string? Proveedor { get; set; }

    /// <summary>
    /// Contact information for the booking
    /// </summary>
    [JsonPropertyName("contacto")]
    public BookingContact? Contacto { get; set; }

    /// <summary>
    /// Booking price
    /// </summary>
    [JsonPropertyName("precio")]
    public decimal? Precio { get; set; }

    /// <summary>
    /// Currency for price
    /// </summary>
    [JsonPropertyName("moneda")]
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// Confirmation number (optional)
    /// </summary>
    [JsonPropertyName("numeroConfirmacion")]
    public string? NumeroConfirmacion { get; set; }

    /// <summary>
    /// Booking status
    /// </summary>
    [JsonPropertyName("estado")]
    public BookingStatus Estado { get; set; } = BookingStatus.Pendiente;

    /// <summary>
    /// Additional notes
    /// </summary>
    [JsonPropertyName("notas")]
    public string? Notas { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    [JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Parent itinerary ID
    /// </summary>
    [JsonPropertyName("itinerarioId")]
    public string ItinerarioId { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID (from parent travel)
    /// </summary>
    [JsonPropertyName("twinId")]
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Travel ID (from parent travel)
    /// </summary>
    [JsonPropertyName("viajeId")]
    public string ViajeId { get; set; } = string.Empty;

    /// <summary>
    /// Document type for identification
    /// </summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = "booking";
}

/// <summary>
/// Contact information for bookings
/// </summary>
public class BookingContact
{
    /// <summary>
    /// Contact phone number (optional)
    /// </summary>
    [JsonPropertyName("telefono")]
    public string? Telefono { get; set; }

    /// <summary>
    /// Contact email address (optional)
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Contact address (optional)
    /// </summary>
    [JsonPropertyName("direccion")]
    public string? Direccion { get; set; }
}

/// <summary>
/// Booking type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BookingType
{
    [JsonPropertyName("vuelo")]
    Vuelo,

    [JsonPropertyName("hotel")]
    Hotel,

    [JsonPropertyName("actividad")]
    Actividad,

    [JsonPropertyName("transporte")]
    Transporte,

    [JsonPropertyName("restaurante")]
    Restaurante,

    [JsonPropertyName("otro")]
    Otro
}

/// <summary>
/// Booking status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BookingStatus
{
    [JsonPropertyName("pendiente")]
    Pendiente,

    [JsonPropertyName("confirmado")]
    Confirmado,

    [JsonPropertyName("cancelado")]
    Cancelado,

    [JsonPropertyName("completado")]
    Completado
}

// ========================================================================================
// BOOKING REQUEST/RESPONSE MODELS
// ========================================================================================

/// <summary>
/// Request model for creating a new booking
/// </summary>
public class CreateBookingRequest
{
    /// <summary>
    /// Booking type (required)
    /// </summary>
    [Required(ErrorMessage = "Booking type is required")]
    public BookingType Tipo { get; set; } = BookingType.Vuelo;

    /// <summary>
    /// Booking title (required)
    /// </summary>
    [Required(ErrorMessage = "Booking title is required")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Booking description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Booking start date
    /// </summary>
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Booking end date (optional)
    /// </summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Start time (optional)
    /// </summary>
    public string? HoraInicio { get; set; }

    /// <summary>
    /// End time (optional)
    /// </summary>
    public string? HoraFin { get; set; }

    /// <summary>
    /// Service provider
    /// </summary>
    public string? Proveedor { get; set; }

    /// <summary>
    /// Contact information
    /// </summary>
    public BookingContact? Contacto { get; set; }

    /// <summary>
    /// Booking price
    /// </summary>
    public decimal? Precio { get; set; }

    /// <summary>
    /// Currency for price
    /// </summary>
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// Confirmation number (optional)
    /// </summary>
    public string? NumeroConfirmacion { get; set; }

    /// <summary>
    /// Booking status
    /// </summary>
    public BookingStatus Estado { get; set; } = BookingStatus.Pendiente;

    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Request model for updating an existing booking
/// </summary>
public class UpdateBookingRequest
{
    /// <summary>
    /// Booking type (optional for update)
    /// </summary>
    public BookingType? Tipo { get; set; }

    /// <summary>
    /// Booking title (optional for update)
    /// </summary>
    public string? Titulo { get; set; }

    /// <summary>
    /// Booking description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Booking start date
    /// </summary>
    public DateTime? FechaInicio { get; set; }

    /// <summary>
    /// Booking end date (optional)
    /// </summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>
    /// Start time (optional)
    /// </summary>
    public string? HoraInicio { get; set; }

    /// <summary>
    /// End time (optional)
    /// </summary>
    public string? HoraFin { get; set; }

    /// <summary>
    /// Service provider
    /// </summary>
    public string? Proveedor { get; set; }

    /// <summary>
    /// Contact information
    /// </summary>
    public BookingContact? Contacto { get; set; }

    /// <summary>
    /// Booking price
    /// </summary>
    public decimal? Precio { get; set; }

    /// <summary>
    /// Currency for price
    /// </summary>
    public string? Moneda { get; set; }

    /// <summary>
    /// Confirmation number (optional)
    /// </summary>
    public string? NumeroConfirmacion { get; set; }

    /// <summary>
    /// Booking status
    /// </summary>
    public BookingStatus? Estado { get; set; }

    /// <summary>
    /// Additional notes
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Response model for booking operations
/// </summary>
public class BookingResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Booking data (if applicable)
    /// </summary>
    public TravelBooking? Data { get; set; }

    /// <summary>
    /// Updated itinerary data with bookings
    /// </summary>
    public TravelItinerary? ItineraryData { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response model for getting multiple bookings
/// </summary>
public class GetBookingsResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// List of bookings
    /// </summary>
    public List<TravelBooking> Bookings { get; set; } = new();

    /// <summary>
    /// Total number of bookings found
    /// </summary>
    public int TotalBookings { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

// ========================================================================================
// DAILY ACTIVITY MODELS
// ========================================================================================

/// <summary>
/// Daily activity data model for detailed trip planning
/// </summary>
public class DailyActivity
{
    /// <summary>
    /// Unique identifier for the activity (timestamp-based)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

    /// <summary>
    /// Activity date
    /// </summary>
    [JsonPropertyName("fecha")]
    public DateTime Fecha { get; set; }

    /// <summary>
    /// Start time
    /// </summary>
    [JsonPropertyName("horaInicio")]
    public string? HoraInicio { get; set; }

    /// <summary>
    /// End time
    /// </summary>
    [JsonPropertyName("horaFin")]
    public string? HoraFin { get; set; }

    /// <summary>
    /// Activity type
    /// </summary>
    [JsonPropertyName("tipoActividad")]
    public ActivityType TipoActividad { get; set; } = ActivityType.Museo;

    /// <summary>
    /// Activity title (required)
    /// </summary>
    [JsonPropertyName("titulo")]
    [Required(ErrorMessage = "Activity title is required")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Activity description (optional)
    /// </summary>
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Activity location
    /// </summary>
    [JsonPropertyName("ubicacion")]
    public string? Ubicacion { get; set; }

    /// <summary>
    /// List of participants
    /// </summary>
    [JsonPropertyName("participantes")]
    public List<string> Participantes { get; set; } = new();

    /// <summary>
    /// Activity rating (1-5 stars, optional)
    /// </summary>
    [JsonPropertyName("calificacion")]
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Calificacion { get; set; }

    /// <summary>
    /// Additional notes
    /// </summary>
    [JsonPropertyName("notas")]
    public string? Notas { get; set; }

    /// <summary>
    /// Activity cost
    /// </summary>
    [JsonPropertyName("costo")]
    public decimal? Costo { get; set; }

    /// <summary>
    /// Currency for cost
    /// </summary>
    [JsonPropertyName("moneda")]
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// GPS coordinates
    /// </summary>
    [JsonPropertyName("coordenadas")]
    public ActivityCoordinates? Coordenadas { get; set; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    [JsonPropertyName("fechaCreacion")]
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("fechaActualizacion")]
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Parent itinerary ID
    /// </summary>
    [JsonPropertyName("itinerarioId")]
    public string ItinerarioId { get; set; } = string.Empty;

    /// <summary>
    /// Twin ID (from parent travel)
    /// </summary>
    [JsonPropertyName("twinId")]
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Travel ID (from parent travel)
    /// </summary>
    [JsonPropertyName("viajeId")]
    public string ViajeId { get; set; } = string.Empty;

    /// <summary>
    /// Document type for identification
    /// </summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = "dailyActivity";
}

/// <summary>
/// GPS coordinates for activities
/// </summary>
public class ActivityCoordinates
{
    /// <summary>
    /// Latitude coordinate
    /// </summary>
    [JsonPropertyName("latitud")]
    public decimal Latitud { get; set; }

    /// <summary>
    /// Longitude coordinate
    /// </summary>
    [JsonPropertyName("longitud")]
    public decimal Longitud { get; set; }
}

// ========================================================================================
// DAILY ACTIVITY REQUEST/RESPONSE MODELS
// ========================================================================================

/// <summary>
/// Request model for creating a new daily activity
/// </summary>
public class CreateDailyActivityRequest
{
    /// <summary>
    /// Activity date (required)
    /// </summary>
    [Required(ErrorMessage = "Activity date is required")]
    public DateTime Fecha { get; set; }

    /// <summary>
    /// Start time (optional)
    /// </summary>
    public string? HoraInicio { get; set; }

    /// <summary>
    /// End time (optional)
    /// </summary>
    public string? HoraFin { get; set; }

    /// <summary>
    /// Activity type (required)
    /// </summary>
    [Required(ErrorMessage = "Activity type is required")]
    public ActivityType TipoActividad { get; set; } = ActivityType.Museo;

    /// <summary>
    /// Activity title (required)
    /// </summary>
    [Required(ErrorMessage = "Activity title is required")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Activity description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Activity location (optional)
    /// </summary>
    public string? Ubicacion { get; set; }

    /// <summary>
    /// List of participants (optional)
    /// </summary>
    public List<string>? Participantes { get; set; }

    /// <summary>
    /// Activity rating (optional)
    /// </summary>
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Calificacion { get; set; }

    /// <summary>
    /// Additional notes (optional)
    /// </summary>
    public string? Notas { get; set; }

    /// <summary>
    /// Activity cost (optional)
    /// </summary>
    public decimal? Costo { get; set; }

    /// <summary>
    /// Currency for cost (optional)
    /// </summary>
    public string? Moneda { get; set; } = "USD";

    /// <summary>
    /// GPS coordinates (optional)
    /// </summary>
    public ActivityCoordinates? Coordenadas { get; set; }
}

/// <summary>
/// Request model for updating an existing daily activity
/// </summary>
public class UpdateDailyActivityRequest
{
    /// <summary>
    /// Activity date (optional for update)
    /// </summary>
    public DateTime? Fecha { get; set; }

    /// <summary>
    /// Start time (optional)
    /// </summary>
    public string? HoraInicio { get; set; }

    /// <summary>
    /// End time (optional)
    /// </summary>
    public string? HoraFin { get; set; }

    /// <summary>
    /// Activity type (optional for update)
    /// </summary>
    public ActivityType? TipoActividad { get; set; }

    /// <summary>
    /// Activity title (optional for update)
    /// </summary>
    public string? Titulo { get; set; }

    /// <summary>
    /// Activity description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Activity location (optional)
    /// </summary>
    public string? Ubicacion { get; set; }

    /// <summary>
    /// List of participants (optional)
    /// </summary>
    public List<string>? Participantes { get; set; }

    /// <summary>
    /// Activity rating (optional)
    /// </summary>
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int? Calificacion { get; set; }

    /// <summary>
    /// Additional notes (optional)
    /// </summary>
    public string? Notas { get; set; }

    /// <summary>
    /// Activity cost (optional)
    /// </summary>
    public decimal? Costo { get; set; }

    /// <summary>
    /// Currency for cost (optional)
    /// </summary>
    public string? Moneda { get; set; }

    /// <summary>
    /// GPS coordinates (optional)
    /// </summary>
    public ActivityCoordinates? Coordenadas { get; set; }
}

/// <summary>
/// Response model for daily activity operations
/// </summary>
public class DailyActivityResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Daily activity data (if applicable)
    /// </summary>
    public DailyActivity? Data { get; set; }

    /// <summary>
    /// Updated itinerary data with activities
    /// </summary>
    public TravelItinerary? ItineraryData { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Response model for getting multiple daily activities
/// </summary>
public class GetDailyActivitiesResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// List of daily activities
    /// </summary>
    public List<DailyActivity> Activities { get; set; } = new();

    /// <summary>
    /// Total number of activities found
    /// </summary>
    public int TotalActivities { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

// ========================================================================================
// TRAVEL DOCUMENT MODELS
// ========================================================================================

/// <summary>
/// Travel document data model for storing receipts, invoices, and travel-related documents
/// </summary>
public class TravelDocument
{
    /// <summary>
    /// Unique identifier for the document
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Document title/name
    /// </summary>
    [JsonPropertyName("titulo")]
    public string Titulo { get; set; } = string.Empty;

    /// <summary>
    /// Document description
    /// </summary>
    [JsonPropertyName("descripcion")]
    public string? Descripcion { get; set; }

    /// <summary>
    /// Original file name
    /// </summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// File path in DataLake storage
    /// </summary>
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Document type (Receipt, Invoice, Ticket, etc.)
    /// </summary>
    [JsonPropertyName("documentType")]
    public TravelDocumentType DocumentType { get; set; } = TravelDocumentType.Receipt;

    /// <summary>
    /// Type of establishment (Museum, Restaurant, Hotel, etc.)
    /// </summary>
    [JsonPropertyName("establishmentType")]
    public EstablishmentType EstablishmentType { get; set; } = EstablishmentType.Restaurant;

    /// <summary>
    /// Vendor/business name
    /// </summary>
    [JsonPropertyName("vendorName")]
    public string? VendorName { get; set; }

    /// <summary>
    /// Business address
    /// </summary>
    [JsonPropertyName("vendorAddress")]
    public string? VendorAddress { get; set; }

    /// <summary>
    /// Document date
    /// </summary>
    [JsonPropertyName("documentDate")]
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Total amount
    /// </summary>
    [JsonPropertyName("totalAmount")]
    public decimal? TotalAmount { get; set; }

    /// <summary>
    /// Currency
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; } = "USD";

    /// <summary>
    /// Tax amount
    /// </summary>
    [JsonPropertyName("taxAmount")]
    public decimal? TaxAmount { get; set; }

    /// <summary>
    /// Items or services purchased
    /// </summary>
    [JsonPropertyName("items")]
    public List<TravelDocumentItem> Items { get; set; } = new();

    /// <summary>
    /// Extracted text content from AI processing
    /// </summary>
    [JsonPropertyName("extractedText")]
    public string? ExtractedText { get; set; }

    /// <summary>
    /// HTML content from AI processing
    /// </summary>
    [JsonPropertyName("htmlContent")]
    public string? HtmlContent { get; set; }

    /// <summary>
    /// AI analysis summary
    /// </summary>
    [JsonPropertyName("aiSummary")]
    public string? AiSummary { get; set; }

    /// <summary>
    /// Associated travel ID (optional)
    /// </summary>
    [JsonPropertyName("travelId")]
    public string? TravelId { get; set; }

    /// <summary>
    /// Associated itinerary ID (optional)
    /// </summary>
    [JsonPropertyName("itineraryId")]
    public string? ItineraryId { get; set; }

    /// <summary>
    /// Associated activity ID (optional)
    /// </summary>
    [JsonPropertyName("activityId")]
    public string? ActivityId { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>
    /// MIME type
    /// </summary>
    [JsonPropertyName("mimeType")]
    public string MimeType { get; set; } = string.Empty;

    /// <summary>
    /// SAS URL for document access
    /// </summary>
    [JsonPropertyName("documentUrl")]
    public string? DocumentUrl { get; set; }

    /// <summary>
    /// Document creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Twin ID (partition key)
    /// </summary>
    [JsonPropertyName("twinId")]
    [Required(ErrorMessage = "Twin ID is required")]
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Document type for Cosmos DB
    /// </summary>
    [JsonPropertyName("docType")]
    public string DocType { get; set; } = "travelDocument";
}

/// <summary>
/// Individual item within a travel document
/// </summary>
public class TravelDocumentItem
{
    /// <summary>
    /// Item description
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Quantity
    /// </summary>
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; } = 1;

    /// <summary>
    /// Unit price
    /// </summary>
    [JsonPropertyName("unitPrice")]
    public decimal? UnitPrice { get; set; }

    /// <summary>
    /// Total amount for this item
    /// </summary>
    [JsonPropertyName("totalAmount")]
    public decimal? TotalAmount { get; set; }

    /// <summary>
    /// Item category (Food, Transportation, Entertainment, etc.)
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }
}

/// <summary>
/// Travel document type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TravelDocumentType
{
    [JsonPropertyName("receipt")]
    Receipt,

    [JsonPropertyName("invoice")]
    Invoice,

    [JsonPropertyName("ticket")]
    Ticket,

    [JsonPropertyName("booking_confirmation")]
    BookingConfirmation,

    [JsonPropertyName("voucher")]
    Voucher,

    [JsonPropertyName("contract")]
    Contract,

    [JsonPropertyName("other")]
    Other
}

/// <summary>
/// Establishment type enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstablishmentType
{
    [JsonPropertyName("restaurant")]
    Restaurant,

    [JsonPropertyName("museum")]
    Museum,

    [JsonPropertyName("hotel")]
    Hotel,

    [JsonPropertyName("transportation")]
    Transportation,

    [JsonPropertyName("entertainment")]
    Entertainment,

    [JsonPropertyName("shopping")]
    Shopping,

    [JsonPropertyName("gas_station")]
    GasStation,

    [JsonPropertyName("pharmacy")]
    Pharmacy,

    [JsonPropertyName("supermarket")]
    Supermarket,

    [JsonPropertyName("tour_operator")]
    TourOperator,

    [JsonPropertyName("airline")]
    Airline,

    [JsonPropertyName("car_rental")]
    CarRental,

    [JsonPropertyName("other")]
    Other
}

// ========================================================================================
// TRAVEL DOCUMENT REQUEST/RESPONSE MODELS
// ========================================================================================

/// <summary>
/// Request model for uploading travel documents
/// </summary>
public class UploadTravelDocumentRequest
{
    /// <summary>
    /// File name with extension
    /// </summary>
    [Required(ErrorMessage = "File name is required")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Base64 encoded file content
    /// </summary>
    [Required(ErrorMessage = "File content is required")]
    public string FileContent { get; set; } = string.Empty;

    /// <summary>
    /// Document title/name (optional, will use filename if not provided)
    /// </summary>
    public string? Titulo { get; set; }

    /// <summary>
    /// Document description (optional)
    /// </summary>
    public string? Descripcion { get; set; }

    /// <summary>
    /// Document type (Receipt, Invoice, Ticket, etc.)
    /// </summary>
    public TravelDocumentType DocumentType { get; set; } = TravelDocumentType.Receipt;

    /// <summary>
    /// Type of establishment (Museum, Restaurant, Hotel, etc.)
    /// </summary>
    public EstablishmentType EstablishmentType { get; set; } = EstablishmentType.Restaurant;

    /// <summary>
    /// Associated travel ID (optional)
    /// </summary>
    public string? TravelId { get; set; }

    /// <summary>
    /// Associated itinerary ID (optional)
    /// </summary>
    public string? ItineraryId { get; set; }

    /// <summary>
    /// Associated activity ID (optional)
    /// </summary>
    public string? ActivityId { get; set; }

    /// <summary>
    /// Override file path (optional, defaults to travel-documents/)
    /// </summary>
    public string? FilePath { get; set; }
}

/// <summary>
/// Response model for travel document upload
/// </summary>
public class UploadTravelDocumentResponse
{
    /// <summary>
    /// Whether the upload was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Success message for UI display
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Total processing time in seconds
    /// </summary>
    public double ProcessingTimeSeconds { get; set; }

    /// <summary>
    /// Created travel document
    /// </summary>
    public TravelDocument? Document { get; set; }

    /// <summary>
    /// AI processing results
    /// </summary>
    public TravelDocumentAiResult? AiResults { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// AI processing results for travel documents
/// </summary>
public class TravelDocumentAiResult
{
    /// <summary>
    /// Whether AI processing was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if AI processing failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Extracted vendor/business name
    /// </summary>
    public string? VendorName { get; set; }

    /// <summary>
    /// Extracted business address
    /// </summary>
    public string? VendorAddress { get; set; }

    /// <summary>
    /// Extracted document date
    /// </summary>
    public DateTime? DocumentDate { get; set; }

    /// <summary>
    /// Extracted total amount
    /// </summary>
    public decimal? TotalAmount { get; set; }

    /// <summary>
    /// Extracted currency
    /// </summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Extracted tax amount
    /// </summary>
    public decimal? TaxAmount { get; set; }

    /// <summary>
    /// Extracted items/line items
    /// </summary>
    public List<TravelDocumentItem> Items { get; set; } = new();

    /// <summary>
    /// AI summary in Spanish
    /// </summary>
    public string? AiSummary { get; set; }

    /// <summary>
    /// Full extracted text
    /// </summary>
    public string? ExtractedText { get; set; }

    /// <summary>
    /// HTML formatted content
    /// </summary>
    public string? HtmlContent { get; set; }

    /// <summary>
    /// Raw AI response
    /// </summary>
    public string? RawResponse { get; set; }

    // ===== NEW PROPERTIES FOR TRAVEL-SPECIFIC AI PROCESSING =====

    /// <summary>
    /// Type of document identified by AI
    /// </summary>
    public string? DocumentType { get; set; }

    /// <summary>
    /// Establishment name identified by AI
    /// </summary>
    public string? EstablishmentName { get; set; }

    /// <summary>
    /// Travel expense category (Alimentación, Transporte, Alojamiento, etc.)
    /// </summary>
    public string? TravelCategory { get; set; }

    /// <summary>
    /// Financial information extracted by AI
    /// </summary>
    public Dictionary<string, object>? Financial { get; set; }

    /// <summary>
    /// Location information extracted by AI
    /// </summary>
    public Dictionary<string, object>? Location { get; set; }

    /// <summary>
    /// Travel-specific insights from AI
    /// </summary>
    public Dictionary<string, object>? TravelInsights { get; set; }
}

/// <summary>
/// Response model for getting travel documents
/// </summary>
public class GetTravelDocumentsResponse
{
    /// <summary>
    /// Operation success status
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// List of travel documents
    /// </summary>
    public List<TravelDocument> Documents { get; set; } = new();

    /// <summary>
    /// Total number of documents found
    /// </summary>
    public int TotalDocuments { get; set; }

    /// <summary>
    /// Twin ID
    /// </summary>
    public string TwinId { get; set; } = string.Empty;

    /// <summary>
    /// Associated travel ID (if filtered)
    /// </summary>
    public string? TravelId { get; set; }

    /// <summary>
    /// Request processing timestamp
    /// </summary>
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}