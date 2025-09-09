using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Data class for Home/Housing information with JSON property mappings to match UI naming
/// Container: TwinHomes, PartitionKey: TwinID
/// </summary>
public class HomeData
{
    // ===== INFORMACIÓN BÁSICA =====
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("twinId")]
    public string TwinID { get; set; } = string.Empty;
    
    [JsonProperty("tipo")]
    public string Tipo { get; set; } = string.Empty; // actual, pasado, mudanza
    
    [JsonProperty("direccion")]
    public string Direccion { get; set; } = string.Empty;
    
    [JsonProperty("ciudad")]
    public string Ciudad { get; set; } = string.Empty;
    
    [JsonProperty("estado")]
    public string Estado { get; set; } = string.Empty;
    
    [JsonProperty("codigoPostal")]
    public string CodigoPostal { get; set; } = string.Empty;
    
    [JsonProperty("fechaInicio")]
    public string FechaInicio { get; set; } = string.Empty; // YYYY-MM-DD
    
    [JsonProperty("fechaFin")]
    public string? FechaFin { get; set; } // YYYY-MM-DD | null
    
    [JsonProperty("esPrincipal")]
    public bool EsPrincipal { get; set; } = false;

    // ===== INFORMACIÓN DE LA PROPIEDAD =====
    [JsonProperty("tipoPropiedad")]
    public string TipoPropiedad { get; set; } = string.Empty; // casa, apartamento, condominio, townhouse, otro
    
    [JsonProperty("areaTotal")]
    public double AreaTotal { get; set; } = 0; // pies cuadrados
    
    [JsonProperty("habitaciones")]
    public int Habitaciones { get; set; } = 0;
    
    [JsonProperty("banos")]
    public int Banos { get; set; } = 0;
    
    [JsonProperty("medioBanos")]
    public int MedioBanos { get; set; } = 0;

    // ===== INFORMACIÓN DE CONSTRUCCIÓN =====
    [JsonProperty("anoConstruction")]
    public int AnoConstruction { get; set; } = 0;
    
    [JsonProperty("tipoFundacion")]
    public string TipoFundacion { get; set; } = string.Empty;
    
    [JsonProperty("materialConstruction")]
    public string MaterialConstruction { get; set; } = string.Empty;

    // ===== SISTEMAS Y CARACTERÍSTICAS =====
    [JsonProperty("calefaccion")]
    public string Calefaccion { get; set; } = string.Empty;
    
    [JsonProperty("aireAcondicionado")]
    public string AireAcondicionado { get; set; } = string.Empty;

    // ===== ESTACIONAMIENTO =====
    [JsonProperty("tipoEstacionamiento")]
    public string TipoEstacionamiento { get; set; } = string.Empty;
    
    [JsonProperty("espaciosEstacionamiento")]
    public int EspaciosEstacionamiento { get; set; } = 0;

    // ===== INFORMACIÓN DE TERRENO =====
    [JsonProperty("areaTerreno")]
    public double? AreaTerreno { get; set; }
    
    [JsonProperty("caracteristicasTerreno")]
    public List<string> CaracteristicasTerreno { get; set; } = new();

    // ===== INFORMACIÓN FINANCIERA =====
    [JsonProperty("valorEstimado")]
    public double? ValorEstimado { get; set; }
    
    [JsonProperty("impuestosPrediales")]
    public double? ImpuestosPrediales { get; set; }
    
    [JsonProperty("hoaFee")]
    public double? HoaFee { get; set; }
    
    [JsonProperty("tieneHOA")]
    public bool TieneHOA { get; set; } = false;

    // ===== UBICACIÓN Y VECINDARIO =====
    [JsonProperty("vecindario")]
    public string Vecindario { get; set; } = string.Empty;
    
    [JsonProperty("walkScore")]
    public int? WalkScore { get; set; }
    
    [JsonProperty("bikeScore")]
    public int? BikeScore { get; set; }

    // ===== INFORMACIÓN ADICIONAL =====
    [JsonProperty("descripcion")]
    public string Descripcion { get; set; } = string.Empty;
    
    [JsonProperty("razonMudanza")]
    public string? RazonMudanza { get; set; }
    
    [JsonProperty("aspectosPositivos")]
    public List<string> AspectosPositivos { get; set; } = new();
    
    [JsonProperty("aspectosNegativos")]
    public List<string> AspectosNegativos { get; set; } = new();
    
    [JsonProperty("fotos")]
    public List<string> Fotos { get; set; } = new();

    // ===== METADATOS =====
    public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
    public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "home";

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["TwinID"] = TwinID,
            ["tipo"] = Tipo,
            ["direccion"] = Direccion,
            ["ciudad"] = Ciudad,
            ["estado"] = Estado,
            ["codigoPostal"] = CodigoPostal,
            ["fechaInicio"] = FechaInicio,
            ["fechaFin"] = FechaFin,
            ["esPrincipal"] = EsPrincipal,
            ["tipoPropiedad"] = TipoPropiedad,
            ["areaTotal"] = AreaTotal,
            ["habitaciones"] = Habitaciones,
            ["banos"] = Banos,
            ["medioBanos"] = MedioBanos,
            ["anoConstruction"] = AnoConstruction,
            ["tipoFundacion"] = TipoFundacion,
            ["materialConstruction"] = MaterialConstruction,
            ["calefaccion"] = Calefaccion,
            ["aireAcondicionado"] = AireAcondicionado,
            ["tipoEstacionamiento"] = TipoEstacionamiento,
            ["espaciosEstacionamiento"] = EspaciosEstacionamiento,
            ["areaTerreno"] = AreaTerreno,
            ["caracteristicasTerreno"] = CaracteristicasTerreno,
            ["valorEstimado"] = ValorEstimado,
            ["impuestosPrediales"] = ImpuestosPrediales,
            ["hoaFee"] = HoaFee,
            ["tieneHOA"] = TieneHOA,
            ["vecindario"] = Vecindario,
            ["walkScore"] = WalkScore,
            ["bikeScore"] = BikeScore,
            ["descripcion"] = Descripcion,
            ["razonMudanza"] = RazonMudanza,
            ["aspectosPositivos"] = AspectosPositivos,
            ["aspectosNegativos"] = AspectosNegativos,
            ["fotos"] = Fotos,
            ["fechaCreacion"] = FechaCreacion.ToString("O"),
            ["fechaActualizacion"] = FechaActualizacion.ToString("O"),
            ["type"] = Type
        };
    }

    public static HomeData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
            {
                Console.WriteLine($"?? HomeData.FromDict: Key '{key}' not found or null, using default: {defaultValue}");
                return defaultValue;
            }

            try
            {
                // Tipo directo
                if (value is T directValue)
                {
                    Console.WriteLine($"? HomeData.FromDict: Key '{key}' = {directValue} (direct type)");
                    return directValue;
                }

                // JsonElement (System.Text.Json)
                if (value is JsonElement jsonElement)
                {
                    var result = ParseJsonElement<T>(jsonElement, key, defaultValue);
                    Console.WriteLine($"? HomeData.FromDict: Key '{key}' = {result} (from JsonElement)");
                    return result;
                }

                // JToken (Newtonsoft.Json) - común en Cosmos DB responses
                if (value is JToken jToken)
                {
                    var result = ParseJToken<T>(jToken, key, defaultValue);
                    Console.WriteLine($"? HomeData.FromDict: Key '{key}' = {result} (from JToken)");
                    return result;
                }

                // Conversión directa
                var converted = (T)Convert.ChangeType(value, typeof(T));
                Console.WriteLine($"? HomeData.FromDict: Key '{key}' = {converted} (converted from {value.GetType().Name})");
                return converted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"? HomeData.FromDict: Error parsing key '{key}' (value: {value}, type: {value?.GetType().Name}): {ex.Message}");
                return defaultValue;
            }
        }

        List<string> GetStringList(string key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return new List<string>();

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();
            
            if (value is JToken jToken && jToken.Type == JTokenType.Array)
                return jToken.Children().Select(item => item.ToString()).ToList();
            
            if (value is IEnumerable<object> enumerable)
                return enumerable.Select(item => item?.ToString() ?? string.Empty).ToList();

            return new List<string>();
        }

        return new HomeData
        {
            Id = GetValue("id", ""),
            TwinID = GetValue<string>("TwinID"),
            Tipo = GetValue<string>("tipo"),
            Direccion = GetValue<string>("direccion"),
            Ciudad = GetValue<string>("ciudad"),
            Estado = GetValue<string>("estado"),
            CodigoPostal = GetValue<string>("codigoPostal"),
            FechaInicio = GetValue<string>("fechaInicio"),
            FechaFin = GetValue<string?>("fechaFin"),
            EsPrincipal = GetValue("esPrincipal", false),
            TipoPropiedad = GetValue<string>("tipoPropiedad"),
            AreaTotal = GetValue("areaTotal", 0.0),
            Habitaciones = GetValue("habitaciones", 0),
            Banos = GetValue("banos", 0),
            MedioBanos = GetValue("medioBanos", 0),
            AnoConstruction = GetValue("anoConstruction", 0),
            TipoFundacion = GetValue<string>("tipoFundacion"),
            MaterialConstruction = GetValue<string>("materialConstruction"),
            Calefaccion = GetValue<string>("calefaccion"),
            AireAcondicionado = GetValue<string>("aireAcondicionado"),
            TipoEstacionamiento = GetValue<string>("tipoEstacionamiento"),
            EspaciosEstacionamiento = GetValue("espaciosEstacionamiento", 0),
            AreaTerreno = GetValue<double?>("areaTerreno"),
            CaracteristicasTerreno = GetStringList("caracteristicasTerreno"),
            ValorEstimado = GetValue<double?>("valorEstimado"),
            ImpuestosPrediales = GetValue<double?>("impuestosPrediales"),
            HoaFee = GetValue<double?>("hoaFee"),
            TieneHOA = GetValue("tieneHOA", false),
            Vecindario = GetValue<string>("vecindario"),
            WalkScore = GetValue<int?>("walkScore"),
            BikeScore = GetValue<int?>("bikeScore"),
            Descripcion = GetValue<string>("descripcion"),
            RazonMudanza = GetValue<string?>("razonMudanza"),
            AspectosPositivos = GetStringList("aspectosPositivos"),
            AspectosNegativos = GetStringList("aspectosNegativos"),
            Fotos = GetStringList("fotos"),
            FechaCreacion = GetValue("fechaCreacion", DateTime.UtcNow),
            FechaActualizacion = GetValue("fechaActualizacion", DateTime.UtcNow),
            Type = GetValue("type", "home")
        };
    }

    private static T ParseJsonElement<T>(JsonElement jsonElement, string key, T defaultValue)
    {
        var type = typeof(T);
        
        if (jsonElement.ValueKind == JsonValueKind.Null)
            return defaultValue;

        if (type == typeof(string))
            return (T)(object)(jsonElement.GetString() ?? string.Empty);
        
        if (type == typeof(int) || type == typeof(int?))
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return (T)(object)jsonElement.GetInt32();
        }

        if (type == typeof(double) || type == typeof(double?))
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return (T)(object)jsonElement.GetDouble();
        }

        if (type == typeof(bool))
        {
            if (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
                return (T)(object)jsonElement.GetBoolean();
        }

        if (type == typeof(DateTime))
        {
            if (jsonElement.ValueKind == JsonValueKind.String && DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                return (T)(object)dateTime;
        }

        return defaultValue;
    }

    private static T ParseJToken<T>(JToken jToken, string key, T defaultValue)
    {
        var type = typeof(T);
        
        if (jToken.Type == JTokenType.Null || jToken.Type == JTokenType.Undefined)
            return defaultValue;

        try
        {
            if (type == typeof(string))
                return (T)(object)(jToken.ToString() ?? string.Empty);
            
            if (type == typeof(int) || type == typeof(int?))
                return (T)(object)jToken.ToObject<int>();
            
            if (type == typeof(double) || type == typeof(double?))
                return (T)(object)jToken.ToObject<double>();
            
            if (type == typeof(bool))
                return (T)(object)jToken.ToObject<bool>();
            
            if (type == typeof(DateTime))
                return (T)(object)jToken.ToObject<DateTime>();

            return jToken.ToObject<T>() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}

/// <summary>
/// Query parameters for filtering homes
/// </summary>
public class HomeQuery
{
    public string? Tipo { get; set; } // actual, pasado, mudanza
    public string? TipoPropiedad { get; set; } // casa, apartamento, etc.
    public string? Ciudad { get; set; }
    public string? Estado { get; set; }
    public bool? EsPrincipal { get; set; }
    public int? HabitacionesMin { get; set; }
    public int? HabitacionesMax { get; set; }
    public double? AreaMin { get; set; }
    public double? AreaMax { get; set; }
    public string? SortBy { get; set; } = "fechaCreacion"; // fechaCreacion, direccion, areaTotal
    public string? SortDirection { get; set; } = "DESC"; // ASC, DESC
}

/// <summary>
/// Service class for managing Home/Housing data in Cosmos DB
/// Container: TwinHomes, PartitionKey: TwinID
/// </summary>
public class HomesCosmosDbService
{
    private readonly ILogger<HomesCosmosDbService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly Container _homesContainer;

    public HomesCosmosDbService(ILogger<HomesCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions)
    {
        _logger = logger;
        var cosmosSettings = cosmosOptions.Value;

        _logger.LogInformation("?? Initializing Homes Cosmos DB Service");
        _logger.LogInformation($"   • Endpoint: {cosmosSettings.Endpoint}");
        _logger.LogInformation($"   • Database: {cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   • Container: TwinHomes");

        if (string.IsNullOrEmpty(cosmosSettings.Key))
        {
            _logger.LogError("? COSMOS_KEY is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration");
        }

        if (string.IsNullOrEmpty(cosmosSettings.Endpoint))
        {
            _logger.LogError("? COSMOS_ENDPOINT is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration");
        }

        try
        {
            _client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
            _database = _client.GetDatabase(cosmosSettings.DatabaseName);
            _homesContainer = _database.GetContainer("TwinHomes");
            
            _logger.LogInformation("? Homes Cosmos DB Service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to initialize Homes Cosmos DB client");
            throw;
        }
    }

    /// <summary>
    /// Obtener todos los lugares de vivienda de un twin
    /// </summary>
    public async Task<List<HomeData>> GetLugaresByTwinIdAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting all homes for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var homes = new List<HomeData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var home = HomeData.FromDict(item);
                        homes.Add(home);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to HomeData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} homes for Twin ID: {TwinId}", homes.Count, twinId);
            return homes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get homes for Twin ID: {TwinId}", twinId);
            return new List<HomeData>();
        }
    }

    /// <summary>
    /// Filtrar lugares por tipo (actual/pasado/mudanza)
    /// </summary>
    public async Task<List<HomeData>> GetLugaresByTipoAsync(string twinId, string tipo)
    {
        try
        {
            _logger.LogInformation("?? Getting homes by type '{Tipo}' for Twin ID: {TwinId}", tipo, twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.tipo = @tipo ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId)
                .WithParameter("@tipo", tipo);

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var homes = new List<HomeData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var home = HomeData.FromDict(item);
                        homes.Add(home);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to HomeData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} homes of type '{Tipo}' for Twin ID: {TwinId}", homes.Count, tipo, twinId);
            return homes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get homes by type '{Tipo}' for Twin ID: {TwinId}", tipo, twinId);
            return new List<HomeData>();
        }
    }

    /// <summary>
    /// Crear nuevo lugar de vivienda
    /// </summary>
    public async Task<bool> CreateLugarAsync(HomeData homeData)
    {
        try
        {
            _logger.LogInformation("?? Creating new home: {Direccion} ({TipoPropiedad}) for Twin: {TwinID}",
                homeData.Direccion, homeData.TipoPropiedad, homeData.TwinID);

            // Generar ID si no se proporciona
            if (string.IsNullOrEmpty(homeData.Id))
            {
                homeData.Id = Guid.NewGuid().ToString();
            }

            // Si es principal, desmarcar otros como principales
            if (homeData.EsPrincipal)
            {
                await DesmarcarOtrosComoPrincipalAsync(homeData.TwinID, homeData.Id);
            }

            var homeDict = homeData.ToDict();
            await _homesContainer.CreateItemAsync(homeDict, new PartitionKey(homeData.TwinID));

            _logger.LogInformation("? Home created successfully: {Direccion} for Twin: {TwinID}",
                homeData.Direccion, homeData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to create home: {Direccion} for Twin: {TwinID}",
                homeData.Direccion, homeData.TwinID);
            return false;
        }
    }

    /// <summary>
    /// Actualizar lugar existente
    /// </summary>
    public async Task<bool> UpdateLugarAsync(HomeData homeData)
    {
        try
        {
            _logger.LogInformation("?? Updating home: {Id} for Twin: {TwinID}", homeData.Id, homeData.TwinID);

            // Si es principal, desmarcar otros como principales
            if (homeData.EsPrincipal)
            {
                await DesmarcarOtrosComoPrincipalAsync(homeData.TwinID, homeData.Id);
            }

            homeData.FechaActualizacion = DateTime.UtcNow;
            var homeDict = homeData.ToDict();

            await _homesContainer.UpsertItemAsync(homeDict, new PartitionKey(homeData.TwinID));

            _logger.LogInformation("? Home updated successfully: {Direccion} for Twin: {TwinID}",
                homeData.Direccion, homeData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update home: {Id} for Twin: {TwinID}",
                homeData.Id, homeData.TwinID);
            return false;
        }
    }

    /// <summary>
    /// Eliminar lugar
    /// </summary>
    public async Task<bool> DeleteLugarAsync(string homeId, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Deleting home: {HomeId} for Twin: {TwinId}", homeId, twinId);

            await _homesContainer.DeleteItemAsync<Dictionary<string, object?>>(
                homeId,
                new PartitionKey(twinId)
            );

            _logger.LogInformation("? Home deleted successfully: {HomeId} for Twin: {TwinId}", homeId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to delete home: {HomeId} for Twin: {TwinId}", homeId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Marcar vivienda como principal
    /// </summary>
    public async Task<bool> MarcarComoPrincipalAsync(string homeId, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Marking home as principal: {HomeId} for Twin: {TwinId}", homeId, twinId);

            // Primero obtener la vivienda
            var response = await _homesContainer.ReadItemAsync<Dictionary<string, object?>>(
                homeId,
                new PartitionKey(twinId)
            );

            var homeData = HomeData.FromDict(response.Resource);

            // Desmarcar otros como principales
            await DesmarcarOtrosComoPrincipalAsync(twinId, homeId);

            // Marcar esta como principal
            homeData.EsPrincipal = true;
            homeData.FechaActualizacion = DateTime.UtcNow;

            var homeDict = homeData.ToDict();
            await _homesContainer.UpsertItemAsync(homeDict, new PartitionKey(twinId));

            _logger.LogInformation("? Home marked as principal: {Direccion} for Twin: {TwinId}",
                homeData.Direccion, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to mark home as principal: {HomeId} for Twin: {TwinId}", homeId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Obtener lugar específico por ID
    /// </summary>
    public async Task<HomeData?> GetLugarByIdAsync(string homeId, string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting home by ID: {HomeId} for Twin: {TwinId}", homeId, twinId);

            var response = await _homesContainer.ReadItemAsync<Dictionary<string, object?>>(
                homeId,
                new PartitionKey(twinId)
            );

            var home = HomeData.FromDict(response.Resource);
            _logger.LogInformation("? Home retrieved successfully: {Direccion}", home.Direccion);
            return home;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get home by ID {HomeId} for Twin: {TwinId}", homeId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Obtener vivienda principal del twin
    /// </summary>
    public async Task<HomeData?> GetViviendaPrincipalAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("?? Getting principal home for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.esPrincipal = true")
                .WithParameter("@twinId", twinId);

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                var item = response.FirstOrDefault();
                if (item != null)
                {
                    var home = HomeData.FromDict(item);
                    _logger.LogInformation("? Principal home found: {Direccion} for Twin: {TwinId}",
                        home.Direccion, twinId);
                    return home;
                }
            }

            _logger.LogInformation("?? No principal home found for Twin ID: {TwinId}", twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get principal home for Twin ID: {TwinId}", twinId);
            return null;
        }
    }

    /// <summary>
    /// Filtrar lugares con query avanzada
    /// </summary>
    public async Task<List<HomeData>> GetFilteredLugaresAsync(string twinId, HomeQuery query)
    {
        try
        {
            _logger.LogInformation("?? Getting filtered homes for Twin ID: {TwinId}", twinId);

            // Build dynamic SQL query based on filters
            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (!string.IsNullOrEmpty(query.Tipo))
            {
                conditions.Add("c.tipo = @tipo");
                parameters["@tipo"] = query.Tipo;
            }

            if (!string.IsNullOrEmpty(query.TipoPropiedad))
            {
                conditions.Add("c.tipoPropiedad = @tipoPropiedad");
                parameters["@tipoPropiedad"] = query.TipoPropiedad;
            }

            if (!string.IsNullOrEmpty(query.Ciudad))
            {
                conditions.Add("CONTAINS(LOWER(c.ciudad), LOWER(@ciudad))");
                parameters["@ciudad"] = query.Ciudad;
            }

            if (!string.IsNullOrEmpty(query.Estado))
            {
                conditions.Add("CONTAINS(LOWER(c.estado), LOWER(@estado))");
                parameters["@estado"] = query.Estado;
            }

            if (query.EsPrincipal.HasValue)
            {
                conditions.Add("c.esPrincipal = @esPrincipal");
                parameters["@esPrincipal"] = query.EsPrincipal.Value;
            }

            if (query.HabitacionesMin.HasValue)
            {
                conditions.Add("c.habitaciones >= @habitacionesMin");
                parameters["@habitacionesMin"] = query.HabitacionesMin.Value;
            }

            if (query.HabitacionesMax.HasValue)
            {
                conditions.Add("c.habitaciones <= @habitacionesMax");
                parameters["@habitacionesMax"] = query.HabitacionesMax.Value;
            }

            if (query.AreaMin.HasValue)
            {
                conditions.Add("c.areaTotal >= @areaMin");
                parameters["@areaMin"] = query.AreaMin.Value;
            }

            if (query.AreaMax.HasValue)
            {
                conditions.Add("c.areaTotal <= @areaMax");
                parameters["@areaMax"] = query.AreaMax.Value;
            }

            // Build ORDER BY clause
            var orderBy = query.SortBy?.ToLowerInvariant() switch
            {
                "direccion" => "c.direccion",
                "areatotal" => "c.areaTotal",
                "fechainicio" => "c.fechaInicio",
                "fechacreacion" => "c.fechaCreacion",
                _ => "c.fechaCreacion"
            };

            var orderDirection = query.SortDirection?.ToUpperInvariant() == "ASC" ? "ASC" : "DESC";

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY {orderBy} {orderDirection}";

            var cosmosQuery = new QueryDefinition(sql);
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var homes = new List<HomeData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var home = HomeData.FromDict(item);
                        homes.Add(home);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to HomeData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} filtered homes for Twin ID: {TwinId}", homes.Count, twinId);
            return homes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get filtered homes for Twin ID: {TwinId}", twinId);
            return new List<HomeData>();
        }
    }

    /// <summary>
    /// Helper method to unmark other homes as principal
    /// </summary>
    private async Task DesmarcarOtrosComoPrincipalAsync(string twinId, string excludeHomeId)
    {
        try
        {
            _logger.LogInformation("?? Unmarking other homes as principal for Twin: {TwinId}, excluding: {ExcludeHomeId}",
                twinId, excludeHomeId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.esPrincipal = true AND c.id != @excludeId")
                .WithParameter("@twinId", twinId)
                .WithParameter("@excludeId", excludeHomeId);

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var homesToUpdate = new List<HomeData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var home = HomeData.FromDict(item);
                        home.EsPrincipal = false;
                        home.FechaActualizacion = DateTime.UtcNow;
                        homesToUpdate.Add(home);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to HomeData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            // Update each home to unmark as principal
            foreach (var home in homesToUpdate)
            {
                var homeDict = home.ToDict();
                await _homesContainer.UpsertItemAsync(homeDict, new PartitionKey(home.TwinID));
                _logger.LogInformation("?? Unmarked as principal: {Direccion}", home.Direccion);
            }

            _logger.LogInformation("? Unmarked {Count} other homes as principal for Twin: {TwinId}",
                homesToUpdate.Count, twinId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to unmark other homes as principal for Twin: {TwinId}", twinId);
        }
    }
}