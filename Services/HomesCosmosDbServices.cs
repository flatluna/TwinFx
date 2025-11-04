using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
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

    // ===== ANÁLISIS AI DEL SEARCH INDEX =====
    /// <summary>
    /// Análisis AI de la propiedad obtenido del HomesSearchIndex (opcional)
    /// Contiene ExecutiveSummary, DetailedHtmlReport, SearchScore, etc.
    /// </summary>
    [JsonProperty("aiAnalysis")]
    public HomesSearchResultItem? AIAnalysis { get; set; }

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
            ["type"] = Type,
            ["aiAnalysis"] = AIAnalysis // Agregar análisis AI al diccionario
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
            Type = GetValue("type", "home"),
            AIAnalysis = null // El análisis AI se carga por separado en GetLugaresByTwinIdAsync
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
    private readonly IConfiguration _configuration;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly Container _homesContainer;
    private Kernel? _kernel;

    public HomesCosmosDbService(ILogger<HomesCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
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
            _logger.LogInformation("🏠 Getting all homes for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var homes = new List<HomeData>();

            // Setup DataLake client para obtener fotos
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var home = HomeData.FromDict(item);
                        
                        // 📸 NUEVO: Obtener fotos SAS del directorio homes/{homeId}/photos
                        try
                        {
                            _logger.LogDebug("📸 Getting photos for HomeId: {HomeId}", home.Id);
                            
                            var photosDirectoryPath = $"homes/{home.Id}/photos";
                            var photoFiles = await dataLakeClient.ListFilesAsync(photosDirectoryPath);
                            
                            // Filtrar solo archivos de imagen
                            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif" };
                            var imageFiles = photoFiles.Where(file => imageExtensions.Any(ext =>
                                file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToList();
                            
                            var photoSasUrls = new List<string>();
                            
                            foreach (var imageFile in imageFiles)
                            {
                                try
                                {
                                    // Generar SAS URL para cada foto (24 horas de validez)
                                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(imageFile.Name, TimeSpan.FromHours(24));
                                    if (!string.IsNullOrEmpty(sasUrl))
                                    {
                                        photoSasUrls.Add(sasUrl);
                                    }
                                }
                                catch (Exception sasEx)
                                {
                                    _logger.LogWarning(sasEx, "⚠️ Failed to generate SAS URL for photo: {PhotoPath}", imageFile.Name);
                                }
                            }
                            
                            // Actualizar la lista de fotos del HomeData con las SAS URLs
                            home.Fotos = photoSasUrls;
                            
                            _logger.LogDebug("✅ Found {PhotoCount} photos for HomeId: {HomeId}", photoSasUrls.Count, home.Id);
                        }
                        catch (Exception photosEx)
                        {
                            _logger.LogWarning(photosEx, "⚠️ Error loading photos for HomeId: {HomeId}", home.Id);
                            // No fallar la operación principal, solo logear el warning
                            home.Fotos = new List<string>(); // Asegurar que la lista esté inicializada
                        }
                        
                        // 🧠 EXISTENTE: Intentar obtener análisis AI del HomesSearchIndex
                        try
                        {
                            _logger.LogDebug("🔍 Searching for AI analysis for HomeId: {HomeId}", home.Id);
                            
                            // Crear instancia del HomesSearchIndex para buscar análisis AI
                            var homesSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesSearchIndex>();
                            
                            // Obtener configuración desde el entorno
                            var config = new ConfigurationBuilder()
                                .AddEnvironmentVariables()
                                .Build();
                            
                            var homesSearchIndex = new HomesSearchIndex(homesSearchLogger, config);

                            // Deserializar el JSON a un objeto Propiedad  
                           
                            if (homesSearchIndex.IsAvailable)
                            {
                                // Buscar análisis AI por HomeId y TwinId
                                var aiResult = await homesSearchIndex.GetHomeByIdAsync(home.Id, twinId);
                                
                                if (aiResult.Success && aiResult.Home != null)
                                {
                                    // Asignar el análisis AI a la propiedad
                                    home.AIAnalysis = aiResult.Home;
                                    _logger.LogDebug("✅ AI analysis found for HomeId: {HomeId}, Score: {Score}", 
                                        home.Id, aiResult.Home.SearchScore);
                                }
                                else
                                {
                                    _logger.LogDebug("📭 No AI analysis found for HomeId: {HomeId}", home.Id);
                                }
                            }
                            else
                            {
                                _logger.LogDebug("⚠️ HomesSearchIndex not available for AI analysis");
                            }
                        }
                        catch (Exception aiEx)
                        {
                            _logger.LogWarning(aiEx, "⚠️ Error loading AI analysis for HomeId: {HomeId}", home.Id);
                            // No fallar la operación principal, solo logear el warning
                        }
                        
                        homes.Add(home);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to HomeData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} homes for Twin ID: {TwinId} ({AnalysisCount} with AI analysis) ({PhotoCount} with photos)", 
                homes.Count, twinId, homes.Count(h => h.AIAnalysis != null), homes.Count(h => h.Fotos.Any()));
            return homes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get homes for Twin ID: {TwinId}", twinId);
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

            _logger.LogInformation("✅ Home updated successfully: {Direccion} for Twin: {TwinID}",
                homeData.Direccion, homeData.TwinID);

            // 🔄 NUEVO: Actualizar el índice de búsqueda después de actualizar en Cosmos DB
            _logger.LogInformation("📊 Updating home data in search index");
            try
            {
                var startTime = DateTime.UtcNow;
                
                // Crear instancia del HomesSearchIndex
                var homesSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesSearchIndex>();
                
                // Obtener configuración desde el entorno
                var config = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();
                
                var homesSearchIndex = new HomesSearchIndex(homesSearchLogger, config);
                
                if (homesSearchIndex.IsAvailable)
                {
                    // PASO 1: Generar análisis AI completo de la propiedad actualizada
                    var aiResponseJson = await GenerateUpdateHomeAnalysisAsync(homeData);
                    var processingTimeMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    // PASO 2: Indexar el análisis completo en HomesSearchIndex (no solo datos básicos)
                    var indexResult = await homesSearchIndex.IndexHomeAnalysisFromAIResponseAsync(
                        aiResponseJson, 
                        homeData.Id, 
                        homeData.TwinID, 
                        processingTimeMs);
                    
                    if (indexResult.Success)
                    {
                        _logger.LogInformation("✅ Home analysis updated successfully in search index: DocumentId={DocumentId}", indexResult.DocumentId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Failed to update home analysis in search index: {Error}", indexResult.Error);
                        // No fallamos toda la operación por esto, solo logueamos la advertencia
                    }
                }
                else
                {
                    _logger.LogDebug("⚠️ HomesSearchIndex not available for updating");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error updating home analysis in search index");
                // No fallamos toda la operación por esto
            }

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
    /// Filtrar lugares with advanced query
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

    /// <summary>
    /// Genera análisis AI completo para una propiedad actualizada
    /// </summary>
    private async Task<string> GenerateUpdateHomeAnalysisAsync(HomeData homeData)
    {
        try
        {
            string AspectosNegativos = "";
            string AspectosPositivos = "";
            foreach (var positivo in homeData.AspectosPositivos)
            {
                AspectosPositivos = AspectosPositivos + positivo +  " , ";
            }
            foreach (var negativo in homeData.AspectosNegativos)
            {
                AspectosNegativos = AspectosNegativos + negativo  + " , ";
            } 

            // Inicializar Semantic Kernel
            await InitializeKernelAsync();

            // Crear un prompt específico para análisis de actualización de propiedades
            var analysisPrompt = $@"
Eres un analista experto en gestión inmobiliaria y propiedades residenciales. Vas a analizar una propiedad que ha sido actualizada en el portafolio inmobiliario para generar un análisis comprensivo e insights útiles.
Importante: Siempre contesta, no inventes datos, pero tampoco me digas que no pudiste. Analiza lo que tienes disponible.

DATOS DE LA PROPIEDAD ACTUALIZADA:
==================================
📋 Información Básica:
• ID: {homeData.Id}
• Dirección: {homeData.Direccion}
• Ciudad: {homeData.Ciudad}
• Estado: {homeData.Estado}
• Código Postal: {homeData.CodigoPostal}
• Tipo: {homeData.Tipo}
• Tipo de Propiedad: {homeData.TipoPropiedad}
• Es Principal: {(homeData.EsPrincipal ? "Sí" : "No")}

🏠 Características de la Propiedad:
• Área Total: {homeData.AreaTotal} pies cuadrados
• Habitaciones: {homeData.Habitaciones}
• Baños: {homeData.Banos}
• Medio Baños: {homeData.MedioBanos}
• Año de Construcción: {homeData.AnoConstruction}
- Aspectos positivos : {AspectosPositivos}

- Aspectos negativos : {AspectosNegativos}

💰 Información Financiera:
• Valor Estimado: {(homeData.ValorEstimado.HasValue ? $"${homeData.ValorEstimado:N0}" : "No especificado")}
• Impuestos Prediales: {(homeData.ImpuestosPrediales.HasValue ? $"${homeData.ImpuestosPrediales:N0}" : "No especificado")}
• HOA Fee: {(homeData.HoaFee.HasValue ? $"${homeData.HoaFee:N0}" : "No aplica")}
• Tiene HOA: {(homeData.TieneHOA ? "Sí" : "No")}

🏡 Información Adicional:
• Vecindario: {homeData.Vecindario}
• Descripción: {homeData.Descripcion}
• Walk Score: {(homeData.WalkScore.HasValue ? homeData.WalkScore.ToString() : "No disponible")}
• Bike Score: {(homeData.BikeScore.HasValue ? homeData.BikeScore.ToString() : "No disponible")}

📅 Fechas:
• Fecha de Inicio: {homeData.FechaInicio}
• Fecha de Fin: {homeData.FechaFin ?? "En curso"}
• Última Actualización: {homeData.FechaActualizacion:yyyy-MM-dd HH:mm}

INSTRUCCIONES PARA EL ANÁLISIS:
===============================

Genera un análisis comprensivo que incluya EXACTAMENTE estos elementos en formato JSON:

1. **executiveSummary**: Un resumen ejecutivo conciso (2-3 párrafos) que incluya:
   - Resumen de las características actualizadas de la propiedad
   - Análisis del estado actual y potencial de la propiedad
   - Evaluación del valor y cambios recientes
   - Recomendaciones para gestión optimizada

2. **detailedHtmlReport**: Un reporte HTML detallado y visualmente atractivo que incluya:
   - Header con información de actualización
   - Importante: Comeinza siemre en colores la descripcion que dio el dueno de la casa que es esta {homeData.Descripcion}
   - Sección de características principales actualizadas
   - Análisis financiero y de mercado actualizado
   - Insights sobre ubicación y vecindario
   - Recomendaciones de gestión con íconos 
   - Aspectos positivos y negativos encontrados con fonts en colores
   - Estos son los aspectos positivos no los inventes toma estos : {AspectosPositivos}
    - Estos son los aspectos negativos no los inventes toma estos : {AspectosNegativos} podrian ser nada. 
   - Tabla con información detallada de la propiedad
   - Footer con información de última actualización
   - Al final del HTML explica el valor actual de la propiedad, beneficios para el portafolio, 
     y recomendaciones basadas en los datos actualizados.
   - Usa colores, grids, titulos, asegurate de poner los aspectos positivos y negativos en colores fonts mas grandes
   - iMportante en el encabezado usa colores ponlo todo en un div con lineas y usa cards
3. **detalleTexto**: Un reporte detallado en JSON que incluya:
   - Características completas actualizadas de la propiedad
   - Análisis financiero detallado actualizado
   - Información del vecindario y ubicación
   - Cambios y mejoras detectadas
   - Aspectos positivos y negativos encontrados
   - Usa JSON para crear cada campo que encuentres de la información de la propiedad

FORMATO DE RESPUESTA REQUERIDO:
===============================
{{
    ""executiveSummary"": ""Texto del sumario ejecutivo sobre la propiedad actualizada con todos los detalles"",
    ""detalleTexto"": ""Texto de todos los datos actualizados en formato JSON estructurado con cada campo y variable de la propiedad"",
    ""detailedHtmlReport"": ""<div style='font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto;'>...</div>"",
    ""metadata"": {{

        ""propertyValue"": {(homeData.ValorEstimado ?? 0)},
        ""confidenceLevel"": ""high"",
        ""insights"": [""insight1"", ""rec1""],
        ""recommendations"": [""rec1"", ""rec2""],
        ""marketAnalysis"": ""análisis del mercado inmobiliario actualizado"",
        ""propertyType"": ""{homeData.TipoPropiedad}"",
        ""neighborhood"": ""{homeData.Vecindario}"",
        ""isPrincipal"": {homeData.EsPrincipal.ToString().ToLower()},
        ""lastUpdated"": ""{homeData.FechaActualizacion:yyyy-MM-dd HH:mm}""
    }}
}}

IMPORTANTE:
- Responde SOLO con JSON válido
- Usa colores y estilos CSS inline en el HTML apropiados para bienes raíces
- Incluye emojis relevantes en el HTML (🏠🏡🏘️💰📊📍✅🔄)
- Genera insights inmobiliarios útiles basados en los datos actualizados
- Mantén el HTML responsive y profesional
- Analiza el potencial actual de inversión y gestión de la propiedad
- Todo el texto debe estar en español
- Enfócate en el valor actualizado que aporta al portafolio inmobiliario del Twin
- Incluye referencias a que la información ha sido actualizada recientemente";

            var chatCompletionService = _kernel!.GetRequiredService<IChatCompletionService>();
            var chatHistory = new ChatHistory();
            chatHistory.AddUserMessage(analysisPrompt);

            var executionSettings = new PromptExecutionSettings
            {
                ExtensionData = new Dictionary<string, object>
                {
                    ["max_tokens"] = 4000,  // Incrementado para análisis completo
                    ["temperature"] = 0.3   // Temperatura baja para análisis preciso
                }
            };

            var response = await chatCompletionService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                _kernel);

            var aiResponse = response.Content ?? "{}";
            
            _logger.LogInformation("✅ AI update home comprehensive analysis generated successfully");
            
            // Devolver la respuesta JSON completa para que pueda ser indexada
            return aiResponse.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error generating AI update home analysis");
            
            // Retornar análisis básico de fallback
            return GenerateBasicHomeAnalysisJson(homeData);
        }
    }

    /// <summary>
    /// Genera análisis básico de la propiedad en formato JSON
    /// </summary>
    private string GenerateBasicHomeAnalysisJson(HomeData homeData)
    {
        var propertyColor = homeData.TipoPropiedad?.ToLowerInvariant() switch
        {
            "casa" => "#2ecc71",
            "apartamento" => "#3498db", 
            "condominio" => "#e74c3c",
            "townhouse" => "#f39c12",
            _ => "#2ecc71"
        };

        var analysis = new
        {
            executiveSummary = $"Propiedad {homeData.TipoPropiedad} actualizada en {homeData.Ciudad}, {homeData.Estado}. " +
                              $"Con {homeData.AreaTotal} pies cuadrados, {homeData.Habitaciones} habitaciones y {homeData.Banos} baños. " +
                              $"{(homeData.ValorEstimado.HasValue ? $"Valorada en ${homeData.ValorEstimado:N0}. " : "")}" +
                              $"{(homeData.EsPrincipal ? "Marcada como vivienda principal del Twin. " : "")}" +
                              $"Información actualizada el {homeData.FechaActualizacion:yyyy-MM-dd}.",
            
            detalleTexto = System.Text.Json.JsonSerializer.Serialize(new
            {
                informacionBasica = new
                {
                    id = homeData.Id,
                    direccion = homeData.Direccion,
                    ciudad = homeData.Ciudad,
                    estado = homeData.Estado,
                    codigoPostal = homeData.CodigoPostal,
                    tipo = homeData.Tipo,
                    tipoPropiedad = homeData.TipoPropiedad,
                    esPrincipal = homeData.EsPrincipal
                },
                caracteristicas = new
                {
                    areaTotal = homeData.AreaTotal,
                    habitaciones = homeData.Habitaciones,
                    banos = homeData.Banos,
                    medioBanos = homeData.MedioBanos,
                    anoConstruction = homeData.AnoConstruction
                },
                informacionFinanciera = new
                {
                    valorEstimado = homeData.ValorEstimado,
                    impuestosPrediales = homeData.ImpuestosPrediales,
                    hoaFee = homeData.HoaFee,
                    tieneHOA = homeData.TieneHOA
                },
                ubicacion = new
                {
                    vecindario = homeData.Vecindario,
                    walkScore = homeData.WalkScore,
                    bikeScore = homeData.BikeScore
                },
                fechas = new
                {
                    fechaInicio = homeData.FechaInicio,
                    fechaFin = homeData.FechaFin,
                    fechaCreacion = homeData.FechaCreacion.ToString("O"),
                    fechaActualizacion = homeData.FechaActualizacion.ToString("O")
                }
            }),
            
            detailedHtmlReport = $@"
            <div style=""background: linear-gradient(135deg, {propertyColor} 0%, {propertyColor}dd 100%); padding: 20px; border-radius: 15px; color: white; font-family: 'Segoe UI', Arial, sans-serif;"">
                <h3 style=""color: #fff; margin: 0 0 15px 0;"">🔄 Propiedad Actualizada - {homeData.TipoPropiedad}</h3>
                
                <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                    <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">📍 Información de la Propiedad</h4>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Dirección:</strong> {homeData.Direccion}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Ciudad:</strong> {homeData.Ciudad}, {homeData.Estado} {homeData.CodigoPostal}</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Tipo:</strong> {homeData.TipoPropiedad} ({homeData.Tipo})</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Área:</strong> {homeData.AreaTotal} pies²</p>
                    <p style=""margin: 5px 0; line-height: 1.6;""><strong>Habitaciones:</strong> {homeData.Habitaciones} | <strong>Baños:</strong> {homeData.Banos}</p>
                </div>

                <div style=""background: rgba(255,255,255,0.1); padding: 15px; border-radius: 10px; margin: 10px 0;"">
                    <h4 style=""color: #e8f6f3; margin: 0 0 10px 0;"">💰 Información Financiera</h4>
                    <p style=""margin: 5px 0; line-height: 1.6;"">
                        <strong>Valor Estimado:</strong> {(homeData.ValorEstimado.HasValue ? $"${homeData.ValorEstimado:N0}" : "No especificado")}
                    </p>
                    {(homeData.ImpuestosPrediales.HasValue ? $"<p style='margin: 5px 0; line-height: 1.6;'><strong>Impuestos Anuales:</strong> ${homeData.ImpuestosPrediales:N0}</p>" : "")}
                    {(homeData.EsPrincipal ? "<p style='margin: 10px 0 0 0; color: #f1c40f;'><strong>🌟 Vivienda Principal</strong></p>" : "")}
                </div>
                
                <div style=""margin-top: 15px; font-size: 12px; opacity: 0.8; text-align: center;"">
                    🏠 ID: {homeData.Id} • 🔄 Actualizado: {homeData.FechaActualizacion:yyyy-MM-dd HH:mm}
                </div>
            </div>",
            
            metadata = new
            {
                propertyValue = homeData.ValorEstimado ?? 0,
                confidenceLevel = "medium",
                insights = new[] { 
                    "Información actualizada recientemente",
                    $"Propiedad tipo {homeData.TipoPropiedad}",
                    homeData.EsPrincipal ? "Vivienda principal del Twin" : "Propiedad secundaria"
                },
                recommendations = new[] {
                    "Revisar información de mercado actualizada",
                    "Considerar actualización de valor estimado",
                    "Verificar información de impuestos prediales"
                },
                marketAnalysis = $"Propiedad {homeData.TipoPropiedad} en {homeData.Ciudad}, {homeData.Estado} con características actualizadas",
                propertyType = homeData.TipoPropiedad,
                neighborhood = homeData.Vecindario,
                isPrincipal = homeData.EsPrincipal,
                lastUpdated = homeData.FechaActualizacion.ToString("yyyy-MM-dd HH:mm")
            }
        };

        return System.Text.Json.JsonSerializer.Serialize(analysis, new JsonSerializerOptions { WriteIndented = false });
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
            _logger.LogInformation("🔧 Initializing Semantic Kernel for HomesCosmosDbService");

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
                                "gpt4mini";

            // Agregar Azure OpenAI chat completion
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: deploymentName,
                endpoint: endpoint,
                apiKey: apiKey);

            // Construir el kernel
            _kernel = builder.Build();

            _logger.LogInformation("✅ Semantic Kernel initialized successfully for HomesCosmosDbService");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Semantic Kernel for HomesCosmosDbService");
            throw;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Obtener lugar específico por ID con análisis AI del search index
    /// </summary>
    public async Task<HomeData?> GetLugarByIdWithAIAnalysisAsync(string homeId, string twinId)
    {
        try
        {
            _logger.LogInformation("🔍 Getting home by ID with AI analysis: {HomeId} for Twin: {TwinId}", homeId, twinId);

            var response = await _homesContainer.ReadItemAsync<Dictionary<string, object?>>(
                homeId,
                new PartitionKey(twinId)
            );

            var home = HomeData.FromDict(response.Resource);
            
            // 🧠 NUEVO: Intentar obtener análisis AI del HomesSearchIndex
            try
            {
                _logger.LogDebug("🔍 Searching for AI analysis for HomeId: {HomeId}", home.Id);
                
                // Crear instancia del HomesSearchIndex para buscar análisis AI
                var homesSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesSearchIndex>();
                
                // Obtener configuración desde el entorno
                var config = new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();
                
                var homesSearchIndex = new HomesSearchIndex(homesSearchLogger, config);
                
                if (homesSearchIndex.IsAvailable)
                {
                    // Buscar análisis AI por HomeId y TwinId
                    var aiResult = await homesSearchIndex.GetHomeByIdAsync(home.Id, twinId);
                    
                    if (aiResult.Success && aiResult.Home != null)
                    {
                        // Asignar el análisis AI a la propiedad
                        home.AIAnalysis = aiResult.Home;
                        _logger.LogDebug("✅ AI analysis found for HomeId: {HomeId}, Score: {Score}", 
                            home.Id, aiResult.Home.SearchScore);
                    }
                    else
                    {
                        _logger.LogDebug("📭 No AI analysis found for HomeId: {HomeId}", home.Id);
                    }
                }
                else
                {
                    _logger.LogDebug("⚠️ HomesSearchIndex not available for AI analysis");
                }
            }
            catch (Exception aiEx)
            {
                _logger.LogWarning(aiEx, "⚠️ Error loading AI analysis for HomeId: {HomeId}", home.Id);
                // No fallar la operación principal, solo logear el warning
            }
            
            _logger.LogInformation("✅ Home retrieved successfully with AI analysis: {Direccion}", home.Direccion);
            return home;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("📭 Home not found: {HomeId} for Twin: {TwinId}", homeId, twinId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get home by ID {HomeId} for Twin: {TwinId}", homeId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Obtener registros de vivienda específicos por TwinId y HomeId
    /// Este método filtra por TwinId y HomeId para obtener solo un registro específico
    /// </summary>
    public async Task<List<HomeData>> GetLugaresByTwinIdHomeIdAsync(string twinId, string homeId)
    {
        try
        {
            _logger.LogInformation("🏠 Getting home by TwinId: {TwinId} and HomeId: {HomeId}", twinId, homeId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId AND c.id = @homeId")
                .WithParameter("@twinId", twinId)
                .WithParameter("@homeId", homeId);

            var iterator = _homesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var homes = new List<HomeData>();

            // Setup DataLake client para obtener fotos
            var dataLakeFactory = _configuration.CreateDataLakeFactory(LoggerFactory.Create(b => b.AddConsole()));
            var dataLakeClient = dataLakeFactory.CreateClient(twinId);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var home = HomeData.FromDict(item);
                        
                        // 📸 NUEVO: Obtener fotos SAS del directorio homes/{homeId}/photos
                        try
                        {
                            _logger.LogDebug("📸 Getting photos for HomeId: {HomeId}", home.Id);
                            
                            var photosDirectoryPath = $"homes/{home.Id}/photos";
                            var photoFiles = await dataLakeClient.ListFilesAsync(photosDirectoryPath);
                            
                            // Filtrar solo archivos de imagen
                            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".tiff", ".tif" };
                            var imageFiles = photoFiles.Where(file => imageExtensions.Any(ext =>
                                file.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToList();
                            
                            var photoSasUrls = new List<string>();
                            
                            foreach (var imageFile in imageFiles)
                            {
                                try
                                {
                                    // Generar SAS URL para cada foto (24 horas de validez)
                                    var sasUrl = await dataLakeClient.GenerateSasUrlAsync(imageFile.Name, TimeSpan.FromHours(24));
                                    if (!string.IsNullOrEmpty(sasUrl))
                                    {
                                        photoSasUrls.Add(sasUrl);
                                    }
                                }
                                catch (Exception sasEx)
                                {
                                    _logger.LogWarning(sasEx, "⚠️ Failed to generate SAS URL for photo: {PhotoPath}", imageFile.Name);
                                }
                            }
                            
                            // Actualizar la lista de fotos del HomeData con las SAS URLs
                            home.Fotos = photoSasUrls;
                            
                            _logger.LogDebug("✅ Found {PhotoCount} photos for HomeId: {HomeId}", photoSasUrls.Count, home.Id);
                        }
                        catch (Exception photosEx)
                        {
                            _logger.LogWarning(photosEx, "⚠️ Error loading photos for HomeId: {HomeId}", home.Id);
                            // No fallar la operación principal, solo logear el warning
                            home.Fotos = new List<string>(); // Asegurar que la lista esté inicializada
                        }
                        
                        // 🧠 EXISTENTE: Intentar obtener análisis AI del HomesSearchIndex
                        try
                        {
                            _logger.LogDebug("🔍 Searching for AI analysis for HomeId: {HomeId}", home.Id);
                            
                            // Crear instancia del HomesSearchIndex para buscar análisis AI
                            var homesSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<HomesSearchIndex>();
                            
                            // Obtener configuración desde el entorno
                            var config = new ConfigurationBuilder()
                                .AddEnvironmentVariables()
                                .Build();
                            
                            var homesSearchIndex = new HomesSearchIndex(homesSearchLogger, config);

                            if (homesSearchIndex.IsAvailable)
                            {
                                // Buscar análisis AI por HomeId y TwinId
                                var aiResult = await homesSearchIndex.GetHomeByIdAsync(home.Id, twinId);
                                
                                if (aiResult.Success && aiResult.Home != null)
                                {
                                    // Asignar el análisis AI a la propiedad
                                    home.AIAnalysis = aiResult.Home;
                                    _logger.LogDebug("✅ AI analysis found for HomeId: {HomeId}, Score: {Score}", 
                                        home.Id, aiResult.Home.SearchScore);
                                }
                                else
                                {
                                    _logger.LogDebug("📭 No AI analysis found for HomeId: {HomeId}", home.Id);
                                }
                            }
                            else
                            {
                                _logger.LogDebug("⚠️ HomesSearchIndex not available for AI analysis");
                            }
                        }
                        catch (Exception aiEx)
                        {
                            _logger.LogWarning(aiEx, "⚠️ Error loading AI analysis for HomeId: {HomeId}", home.Id);
                            // No fallar la operación principal, solo logear el warning
                        }
                        
                        homes.Add(home);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to HomeData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} home(s) for TwinId: {TwinId} and HomeId: {HomeId} ({AnalysisCount} with AI analysis) ({PhotoCount} with photos)", 
                homes.Count, twinId, homeId, homes.Count(h => h.AIAnalysis != null), homes.Count(h => h.Fotos.Any()));
            return homes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get home for TwinId: {TwinId} and HomeId: {HomeId}", twinId, homeId);
            return new List<HomeData>();
        }
    }
}