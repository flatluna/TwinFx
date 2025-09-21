using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using TwinFx.Models;

namespace TwinFx.Services;

/// <summary>
/// Data class for Car information with JSON property mappings
/// Container: TwinCars, PartitionKey: TwinID
/// </summary>
public class CarData
{
    // ===== INFORMACIÓN BÁSICA =====

    public CarsSearchResultItem CarAnalytics { get; set; } = new CarsSearchResultItem();
    public string Id { get; set; } = string.Empty;
    
    [JsonProperty("twinId")]
    public string TwinID { get; set; } = string.Empty;
    
    [JsonProperty("stockNumber")]
    public string? StockNumber { get; set; }

    [JsonProperty("make")]
    public string Make { get; set; } = string.Empty;

    [JsonProperty("model")]
    public string Model { get; set; } = string.Empty;

    [JsonProperty("year")]
    public int Year { get; set; }

    [JsonProperty("trim")]
    public string? Trim { get; set; }

    [JsonProperty("subModel")]
    public string? SubModel { get; set; }

    [JsonProperty("bodyStyle")]
    public string? BodyStyle { get; set; }

    [JsonProperty("doors")]
    public int? Doors { get; set; }

    [JsonProperty("licensePlate")]
    public string LicensePlate { get; set; } = string.Empty;

    [JsonProperty("plateState")]
    public string? PlateState { get; set; }

    [JsonProperty("vin")]
    public string? Vin { get; set; }

    // ===== ESPECIFICACIONES TÉCNICAS =====
    [JsonProperty("transmission")]
    public string? Transmission { get; set; }

    [JsonProperty("drivetrain")]
    public string? Drivetrain { get; set; }

    [JsonProperty("fuelType")]
    public string? FuelType { get; set; }

    [JsonProperty("engineDescription")]
    public string? EngineDescription { get; set; }

    [JsonProperty("cylinders")]
    public int? Cylinders { get; set; }

    [JsonProperty("engineDisplacementLiters")]
    public decimal? EngineDisplacementLiters { get; set; }

    [JsonProperty("mileage")]
    public long? Mileage { get; set; }

    [JsonProperty("mileageUnit")]
    public string? MileageUnit { get; set; }

    [JsonProperty("odometerStatus")]
    public string? OdometerStatus { get; set; }

    // ===== COLORES Y APARIENCIA =====
    [JsonProperty("exteriorColor")]
    public string? ExteriorColor { get; set; }

    [JsonProperty("interiorColor")]
    public string? InteriorColor { get; set; }

    [JsonProperty("upholstery")]
    public string? Upholstery { get; set; }

    // ===== ESTADO Y CONDICIÓN =====
    [JsonProperty("condition")]
    public string? Condition { get; set; }

    [JsonProperty("stockStatus")]
    public string? StockStatus { get; set; }

    [JsonProperty("hasOpenRecalls")]
    public bool HasOpenRecalls { get; set; }

    [JsonProperty("hasAccidentHistory")]
    public bool HasAccidentHistory { get; set; }

    [JsonProperty("isCertifiedPreOwned")]
    public bool IsCertifiedPreOwned { get; set; }

    // ===== FECHAS Y ADQUISICIÓN =====
    [JsonProperty("dateAcquired")]
    public DateTime? DateAcquired { get; set; }

    [JsonProperty("dateListed")]
    public DateTime? DateListed { get; set; }

    [JsonProperty("acquisitionSource")]
    public string? AcquisitionSource { get; set; }

    // ===== UBICACIÓN =====
    [JsonProperty("addressComplete")]
    public string? AddressComplete { get; set; }

    [JsonProperty("city")]
    public string? City { get; set; }

    [JsonProperty("state")]
    public string? State { get; set; }

    [JsonProperty("postalCode")]
    public string? PostalCode { get; set; }

    [JsonProperty("country")]
    public string? Country { get; set; }

    [JsonProperty("latitude")]
    public decimal? Latitude { get; set; }

    [JsonProperty("longitude")]
    public decimal? Longitude { get; set; }

    [JsonProperty("parkingLocation")]
    public string? ParkingLocation { get; set; }

    // ===== INFORMACIÓN FINANCIERA =====
    [JsonProperty("originalListPrice")]
    public decimal? OriginalListPrice { get; set; } // Precio de Lista Original

    [JsonProperty("listPrice")]
    public decimal? ListPrice { get; set; }

    [JsonProperty("currentPrice")]
    public decimal? CurrentPrice { get; set; }

    [JsonProperty("actualPaidPrice")]
    public decimal? ActualPaidPrice { get; set; } // Precio Actual/Pagado

    [JsonProperty("estimatedTax")]
    public decimal? EstimatedTax { get; set; }

    [JsonProperty("estimatedRegistrationFee")]
    public decimal? EstimatedRegistrationFee { get; set; }

    [JsonProperty("dealerProcessingFee")]
    public decimal? DealerProcessingFee { get; set; }

    // ===== FINANCIAMIENTO =====
    [JsonProperty("monthlyPayment")]
    public decimal? MonthlyPayment { get; set; }

    [JsonProperty("apr")]
    public decimal? Apr { get; set; }

    [JsonProperty("termMonths")]
    public int? TermMonths { get; set; }

    [JsonProperty("downPayment")]
    public decimal? DownPayment { get; set; }

    // ===== CARACTERÍSTICAS =====
    [JsonProperty("standardFeatures")]
    public List<string> StandardFeatures { get; set; } = new List<string>();

    [JsonProperty("optionalFeatures")]
    public List<string> OptionalFeatures { get; set; } = new List<string>();

    [JsonProperty("safetyFeatures")]
    public List<string> SafetyFeatures { get; set; } = new List<string>();

    // ===== TÍTULO =====
    [JsonProperty("titleBrand")]
    public string? TitleBrand { get; set; }

    [JsonProperty("hasLien")]
    public bool HasLien { get; set; }

    [JsonProperty("lienHolder")]
    public string? LienHolder { get; set; }

    [JsonProperty("lienAmount")]
    public decimal? LienAmount { get; set; }

    [JsonProperty("titleState")]
    public string? TitleState { get; set; }

    // ===== GARANTÍA =====
    [JsonProperty("warrantyType")]
    public string? WarrantyType { get; set; }

    [JsonProperty("warrantyStart")]
    public DateTime? WarrantyStart { get; set; }

    [JsonProperty("warrantyEnd")]
    public DateTime? WarrantyEnd { get; set; }

    [JsonProperty("warrantyProvider")]
    public string? WarrantyProvider { get; set; }

    // ===== MULTIMEDIA =====
    [JsonProperty("photos")]
    public List<string> Photos { get; set; } = new List<string>();

    [JsonProperty("videoUrl")]
    public string? VideoUrl { get; set; }

    // ===== NOTAS =====
    [JsonProperty("internalNotes")]
    public string? InternalNotes { get; set; }

    [JsonProperty("description")]
    public string? Description { get; set; }

    // ===== ESTADO DE PROPIEDAD =====
    [JsonProperty("estado")]
    public string? Estado { get; set; } // Propio, Financiado, Arrendado, Vendido

    // ===== METADATOS =====
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonProperty("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonProperty("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonProperty("updatedBy")]
    public string? UpdatedBy { get; set; }

    public string Type { get; set; } = "car";

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["TwinID"] = TwinID,
            ["stockNumber"] = StockNumber,
            ["make"] = Make,
            ["model"] = Model,
            ["year"] = Year,
            ["trim"] = Trim,
            ["subModel"] = SubModel,
            ["bodyStyle"] = BodyStyle,
            ["doors"] = Doors,
            ["licensePlate"] = LicensePlate,
            ["plateState"] = PlateState,
            ["vin"] = Vin,
            ["transmission"] = Transmission,
            ["drivetrain"] = Drivetrain,
            ["fuelType"] = FuelType,
            ["engineDescription"] = EngineDescription,
            ["cylinders"] = Cylinders,
            ["engineDisplacementLiters"] = EngineDisplacementLiters,
            ["mileage"] = Mileage,
            ["mileageUnit"] = MileageUnit,
            ["odometerStatus"] = OdometerStatus,
            ["exteriorColor"] = ExteriorColor,
            ["interiorColor"] = InteriorColor,
            ["upholstery"] = Upholstery,
            ["condition"] = Condition,
            ["stockStatus"] = StockStatus,
            ["hasOpenRecalls"] = HasOpenRecalls,
            ["hasAccidentHistory"] = HasAccidentHistory,
            ["isCertifiedPreOwned"] = IsCertifiedPreOwned,
            ["dateAcquired"] = DateAcquired?.ToString("O"),
            ["dateListed"] = DateListed?.ToString("O"),
            ["acquisitionSource"] = AcquisitionSource,
            ["addressComplete"] = AddressComplete,
            ["city"] = City,
            ["state"] = State,
            ["postalCode"] = PostalCode,
            ["country"] = Country,
            ["latitude"] = Latitude,
            ["longitude"] = Longitude,
            ["parkingLocation"] = ParkingLocation,
            ["originalListPrice"] = OriginalListPrice,
            ["listPrice"] = ListPrice,
            ["currentPrice"] = CurrentPrice,
            ["actualPaidPrice"] = ActualPaidPrice,
            ["estimatedTax"] = EstimatedTax,
            ["estimatedRegistrationFee"] = EstimatedRegistrationFee,
            ["dealerProcessingFee"] = DealerProcessingFee,
            ["monthlyPayment"] = MonthlyPayment,
            ["apr"] = Apr,
            ["termMonths"] = TermMonths,
            ["downPayment"] = DownPayment,
            ["standardFeatures"] = StandardFeatures,
            ["optionalFeatures"] = OptionalFeatures,
            ["safetyFeatures"] = SafetyFeatures,
            ["titleBrand"] = TitleBrand,
            ["hasLien"] = HasLien,
            ["lienHolder"] = LienHolder,
            ["lienAmount"] = LienAmount,
            ["titleState"] = TitleState,
            ["warrantyType"] = WarrantyType,
            ["warrantyStart"] = WarrantyStart?.ToString("O"),
            ["warrantyEnd"] = WarrantyEnd?.ToString("O"),
            ["warrantyProvider"] = WarrantyProvider,
            ["photos"] = Photos,
            ["videoUrl"] = VideoUrl,
            ["internalNotes"] = InternalNotes,
            ["description"] = Description,
            ["estado"] = Estado,
            ["createdAt"] = CreatedAt.ToString("O"),
            ["createdBy"] = CreatedBy,
            ["updatedAt"] = UpdatedAt?.ToString("O"),
            ["updatedBy"] = UpdatedBy,
            ["type"] = Type
        };
    }

    public static CarData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                // Tipo directo
                if (value is T directValue)
                {
                    return directValue;
                }

                // JsonElement (System.Text.Json)
                if (value is JsonElement jsonElement)
                {
                    return ParseJsonElement<T>(jsonElement, key, defaultValue);
                }

                // JToken (Newtonsoft.Json) - común en Cosmos DB responses
                if (value is JToken jToken)
                {
                    return ParseJToken<T>(jToken, key, defaultValue);
                }

                // Conversión directa
                var converted = (T)Convert.ChangeType(value, typeof(T));
                return converted;
            }
            catch
            {
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

        DateTime? GetDateTime(string key)
        {
            var value = GetValue<string>(key);
            if (string.IsNullOrEmpty(value)) return null;
            
            return DateTime.TryParse(value, out var result) ? result : null;
        }

        return new CarData
        {
            Id = GetValue("id", ""),
            TwinID = GetValue<string>("TwinID"),
            StockNumber = GetValue<string?>("stockNumber"),
            Make = GetValue<string>("make"),
            Model = GetValue<string>("model"),
            Year = GetValue("year", 0),
            Trim = GetValue<string?>("trim"),
            SubModel = GetValue<string?>("subModel"),
            BodyStyle = GetValue<string?>("bodyStyle"),
            Doors = GetValue<int?>("doors"),
            LicensePlate = GetValue<string>("licensePlate"),
            PlateState = GetValue<string?>("plateState"),
            Vin = GetValue<string?>("vin"),
            Transmission = GetValue<string?>("transmission"),
            Drivetrain = GetValue<string?>("drivetrain"),
            FuelType = GetValue<string?>("fuelType"),
            EngineDescription = GetValue<string?>("engineDescription"),
            Cylinders = GetValue<int?>("cylinders"),
            EngineDisplacementLiters = GetValue<decimal?>("engineDisplacementLiters"),
            Mileage = GetValue<long?>("mileage"),
            MileageUnit = GetValue<string?>("mileageUnit"),
            OdometerStatus = GetValue<string?>("odometerStatus"),
            ExteriorColor = GetValue<string?>("exteriorColor"),
            InteriorColor = GetValue<string?>("interiorColor"),
            Upholstery = GetValue<string?>("upholstery"),
            Condition = GetValue<string?>("condition"),
            StockStatus = GetValue<string?>("stockStatus"),
            HasOpenRecalls = GetValue("hasOpenRecalls", false),
            HasAccidentHistory = GetValue("hasAccidentHistory", false),
            IsCertifiedPreOwned = GetValue("isCertifiedPreOwned", false),
            DateAcquired = GetDateTime("dateAcquired"),
            DateListed = GetDateTime("dateListed"),
            AcquisitionSource = GetValue<string?>("acquisitionSource"),
            AddressComplete = GetValue<string?>("addressComplete"),
            City = GetValue<string?>("city"),
            State = GetValue<string?>("state"),
            PostalCode = GetValue<string?>("postalCode"),
            Country = GetValue<string?>("country"),
            Latitude = GetValue<decimal?>("latitude"),
            Longitude = GetValue<decimal?>("longitude"),
            ParkingLocation = GetValue<string?>("parkingLocation"),
            OriginalListPrice = GetValue<decimal?>("originalListPrice"),
            ListPrice = GetValue<decimal?>("listPrice"),
            CurrentPrice = GetValue<decimal?>("currentPrice"),
            ActualPaidPrice = GetValue<decimal?>("actualPaidPrice"),
            EstimatedTax = GetValue<decimal?>("estimatedTax"),
            EstimatedRegistrationFee = GetValue<decimal?>("estimatedRegistrationFee"),
            DealerProcessingFee = GetValue<decimal?>("dealerProcessingFee"),
            MonthlyPayment = GetValue<decimal?>("monthlyPayment"),
            Apr = GetValue<decimal?>("apr"),
            TermMonths = GetValue<int?>("termMonths"),
            DownPayment = GetValue<decimal?>("downPayment"),
            StandardFeatures = GetStringList("standardFeatures"),
            OptionalFeatures = GetStringList("optionalFeatures"),
            SafetyFeatures = GetStringList("safetyFeatures"),
            TitleBrand = GetValue<string?>("titleBrand"),
            HasLien = GetValue("hasLien", false),
            LienHolder = GetValue<string?>("lienHolder"),
            LienAmount = GetValue<decimal?>("lienAmount"),
            TitleState = GetValue<string?>("titleState"),
            WarrantyType = GetValue<string?>("warrantyType"),
            WarrantyStart = GetDateTime("warrantyStart"),
            WarrantyEnd = GetDateTime("warrantyEnd"),
            WarrantyProvider = GetValue<string?>("warrantyProvider"),
            Photos = GetStringList("photos"),
            VideoUrl = GetValue<string?>("videoUrl"),
            InternalNotes = GetValue<string?>("internalNotes"),
            Description = GetValue<string?>("description"),
            Estado = GetValue<string?>("estado"),
            CreatedAt = GetValue("createdAt", DateTime.UtcNow),
            CreatedBy = GetValue<string?>("createdBy"),
            UpdatedAt = GetDateTime("updatedAt"),
            UpdatedBy = GetValue<string?>("updatedBy"),
            Type = GetValue("type", "car")
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

        if (type == typeof(long) || type == typeof(long?))
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return (T)(object)jsonElement.GetInt64();
        }

        if (type == typeof(decimal) || type == typeof(decimal?))
        {
            if (jsonElement.ValueKind == JsonValueKind.Number)
                return (T)(object)jsonElement.GetDecimal();
        }

        if (type == typeof(bool))
        {
            if (jsonElement.ValueKind == JsonValueKind.True || jsonElement.ValueKind == JsonValueKind.False)
                return (T)(object)jsonElement.GetBoolean();
        }

        if (type == typeof(DateTime) || type == typeof(DateTime?))
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

            if (type == typeof(long) || type == typeof(long?))
                return (T)(object)jToken.ToObject<long>();
            
            if (type == typeof(decimal) || type == typeof(decimal?))
                return (T)(object)jToken.ToObject<decimal>();
            
            if (type == typeof(bool))
                return (T)(object)jToken.ToObject<bool>();
            
            if (type == typeof(DateTime) || type == typeof(DateTime?))
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
/// Query parameters for filtering cars
/// </summary>
public class CarQuery
{
    public string? Make { get; set; }
    public string? Model { get; set; }
    public int? YearMin { get; set; }
    public int? YearMax { get; set; }
    public string? BodyStyle { get; set; }
    public string? FuelType { get; set; }
    public string? Condition { get; set; }
    public string? Estado { get; set; } // Propio, Financiado, etc.
    public decimal? PriceMin { get; set; }
    public decimal? PriceMax { get; set; }
    public long? MileageMax { get; set; }
    public string? SortBy { get; set; } = "createdAt"; // createdAt, make, year, currentPrice
    public string? SortDirection { get; set; } = "DESC"; // ASC, DESC
}

/// <summary>
/// Service class for managing Car data in Cosmos DB
/// Container: TwinCars, PartitionKey: TwinID
/// </summary>
public class CarsCosmosDbService
{
    private readonly ILogger<CarsCosmosDbService> _logger;
    private readonly IConfiguration _configuration;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly Container _carsContainer;
    private Kernel? _kernel;

    public CarsCosmosDbService(ILogger<CarsCosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        var cosmosSettings = cosmosOptions.Value;

        _logger.LogInformation("🚗 Initializing Cars Cosmos DB Service");
        _logger.LogInformation($"   • Endpoint: {cosmosSettings.Endpoint}");
        _logger.LogInformation($"   • Database: {cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   • Container: TwinCars");

        if (string.IsNullOrEmpty(cosmosSettings.Key))
        {
            _logger.LogError("❌ COSMOS_KEY is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration");
        }

        if (string.IsNullOrEmpty(cosmosSettings.Endpoint))
        {
            _logger.LogError("❌ COSMOS_ENDPOINT is required but not found in configuration");
            throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration");
        }

        try
        {
            _client = new CosmosClient(cosmosSettings.Endpoint, cosmosSettings.Key);
            _database = _client.GetDatabase(cosmosSettings.DatabaseName);
            _carsContainer = _database.GetContainer("TwinCars");
            
            _logger.LogInformation("✅ Cars Cosmos DB Service initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Cars Cosmos DB client");
            throw;
        }
    }

    /// <summary>
    /// Obtener todos los vehículos de un twin
    /// </summary>
    public async Task<List<CarData>> GetCarsByTwinIdAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("🚗 Getting all cars for Twin ID: {TwinId}", twinId);

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = _carsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var cars = new List<CarData>();

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
                        var car = CarData.FromDict(item);
                        
                        // 📸 NUEVO: Obtener fotos SAS del directorio cars/{carId}/photos
                        try
                        {
                            _logger.LogDebug("📸 Getting photos for CarId: {CarId}", car.Id);
                            
                            var photosDirectoryPath = $"cars/{car.Id}/photos";
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
                            
                            // Actualizar la lista de fotos del CarData con las SAS URLs
                            car.Photos = photoSasUrls;
                            
                            _logger.LogDebug("✅ Found {PhotoCount} photos for CarId: {CarId}", photoSasUrls.Count, car.Id);
                        }
                        catch (Exception photosEx)
                        {
                            _logger.LogWarning(photosEx, "⚠️ Error loading photos for CarId: {CarId}", car.Id);
                            // No fallar la operación principal, solo logear el warning
                            car.Photos = new List<string>(); // Asegurar que la lista esté inicializada
                        }
                        
                        cars.Add(car);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to CarData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} cars for Twin ID: {TwinId} ({PhotoCount} with photos)", 
                cars.Count, twinId, cars.Count(c => c.Photos.Any()));
            return cars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get cars for Twin ID: {TwinId}", twinId);
            return new List<CarData>();
        }
    }

    /// <summary>
    /// Crear nuevo vehículo
    /// </summary>
    public async Task<bool> CreateCarAsync(CarData carData)
    {
        try
        {
            _logger.LogInformation("🚗 Creating new car: {Make} {Model} {Year} for Twin: {TwinID}",
                carData.Make, carData.Model, carData.Year, carData.TwinID);

            // Generar ID si no se proporciona
            if (string.IsNullOrEmpty(carData.Id))
            {
                carData.Id = Guid.NewGuid().ToString();
            }

            var carDict = carData.ToDict();
            await _carsContainer.CreateItemAsync(carDict, new PartitionKey(carData.TwinID));

            _logger.LogInformation("✅ Car created successfully: {Make} {Model} {Year} for Twin: {TwinID}",
                carData.Make, carData.Model, carData.Year, carData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create car: {Make} {Model} for Twin: {TwinID}",
                carData.Make, carData.Model, carData.TwinID);
            return false;
        }
    }

    /// <summary>
    /// Actualizar vehículo existente
    /// </summary>
    public async Task<bool> UpdateCarAsync(CarData carData)
    {
        try
        {
            _logger.LogInformation("🔄 Updating car: {Id} for Twin: {TwinID}", carData.Id, carData.TwinID);

            carData.UpdatedAt = DateTime.UtcNow;
            var carDict = carData.ToDict();

            await _carsContainer.UpsertItemAsync(carDict, new PartitionKey(carData.TwinID));

            _logger.LogInformation("✅ Car updated successfully: {Make} {Model} {Year} for Twin: {TwinID}",
                carData.Make, carData.Model, carData.Year, carData.TwinID);
            // PASO 4: Generar respuesta inteligente con recomendaciones
          
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update car: {Id} for Twin: {TwinID}",
                carData.Id, carData.TwinID);
            return false;
        }
    }

    /// <summary>
    /// Eliminar vehículo
    /// </summary>
    public async Task<bool> DeleteCarAsync(string carId, string twinId)
    {
        try
        {
            _logger.LogInformation("🗑️ Deleting car: {CarId} for Twin: {TwinId}", carId, twinId);

            await _carsContainer.DeleteItemAsync<Dictionary<string, object?>>(
                carId,
                new PartitionKey(twinId)
            );

            _logger.LogInformation("✅ Car deleted successfully: {CarId} for Twin: {TwinId}", carId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete car: {CarId} for Twin: {TwinId}", carId, twinId);
            return false;
        }
    }

    /// <summary>
    /// Obtener vehículo específico por ID
    /// </summary>
    public async Task<CarData?> GetCarByIdAsync(string carId, string twinId)
    {
        try
        {
            _logger.LogInformation("🔍 Getting car by ID: {CarId} for Twin: {TwinId}", carId, twinId);

            // Definir la consulta para buscar el coche por ID y TwinID  
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @carId AND c.TwinID = @twinId")
                .WithParameter("@carId", carId)
                .WithParameter("@twinId", twinId);

            var iterator = _carsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            CarData? car = null;

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
                        // Usar Newtonsoft.Json para deserializar el objeto  
                        var json = JsonConvert.SerializeObject(item);
                        car = JsonConvert.DeserializeObject<CarData>(json);

                        if (car != null)
                        {
                            // Logear los datos del coche  
                            _logger.LogInformation("✅ Car retrieved successfully: {Make} {Model} {Year}", car.Make, car.Model, car.Year);

                            // Logear los precios  
                            _logger.LogInformation("💰 DEBUG - Parsed financial data: OriginalListPrice={OriginalListPrice}, ListPrice={ListPrice}, CurrentPrice={CurrentPrice}, ActualPaidPrice={ActualPaidPrice}, MonthlyPayment={MonthlyPayment}",
                                car.OriginalListPrice, car.ListPrice, car.CurrentPrice, car.ActualPaidPrice, car.MonthlyPayment);

                            // 📸 Obtener fotos SAS del directorio cars/{carId}/photos
                            try
                            {
                                var photosDirectoryPath = $"cars/{car.Id}/photos";
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

                                // Actualizar la lista de fotos del CarData con las SAS URLs
                                car.Photos = photoSasUrls;

                                _logger.LogDebug("✅ Found {PhotoCount} photos for CarId: {CarId}", photoSasUrls.Count, car.Id);
                            }
                            catch (Exception photosEx)
                            {
                                _logger.LogWarning(photosEx, "⚠️ Error loading photos for CarId: {CarId}", car.Id);
                                // No fallar la operación principal, solo logear el warning
                                car.Photos = new List<string>(); // Asegurar que la lista esté inicializada
                            }

                            // 🔍 Buscar análisis AI usando CarsSearchIndex
                            try
                            {
                                _logger.LogDebug("🔍 Searching for AI analysis for CarId: {CarId}", car.Id);

                                // Crear instancia del CarsSearchIndex para buscar análisis AI
                                var carsSearchLogger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<CarsSearchIndex>();

                                // Obtener configuración desde el entorno
                                var config = new ConfigurationBuilder()
                                    .AddEnvironmentVariables()
                                    .Build();

                                var carsSearchIndex = new CarsSearchIndex(carsSearchLogger, config);

                                if (carsSearchIndex.IsAvailable)
                                {
                                   var CarAnalycis =await  carsSearchIndex.GetCarByIdAsync(car.Id, twinId);

                                    car.CarAnalytics = CarAnalycis.CarSearchResults;
                                }
                                else
                                {
                                    _logger.LogDebug("⚠️ CarsSearchIndex not available for AI analysis");
                                }
                            }
                            catch (Exception aiEx)
                            {
                                _logger.LogWarning(aiEx, "⚠️ Error searching for AI analysis for CarId: {CarId}", car.Id);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to CarData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            return car;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get car by ID {CarId} for Twin: {TwinId}", carId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Filtrar vehículos con consulta avanzada
    /// </summary>
    public async Task<List<CarData>> GetFilteredCarsAsync(string twinId, CarQuery query)
    {
        try
        {
            _logger.LogInformation("🔍 Getting filtered cars for Twin ID: {TwinId}", twinId);

            // Construir consulta SQL dinámica basada en filtros
            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (!string.IsNullOrEmpty(query.Make))
            {
                conditions.Add("CONTAINS(LOWER(c.make), LOWER(@make))");
                parameters["@make"] = query.Make;
            }

            if (!string.IsNullOrEmpty(query.Model))
            {
                conditions.Add("CONTAINS(LOWER(c.model), LOWER(@model))");
                parameters["@model"] = query.Model;
            }

            if (query.YearMin.HasValue)
            {
                conditions.Add("c.year >= @yearMin");
                parameters["@yearMin"] = query.YearMin.Value;
            }

            if (query.YearMax.HasValue)
            {
                conditions.Add("c.year <= @yearMax");
                parameters["@yearMax"] = query.YearMax.Value;
            }

            if (!string.IsNullOrEmpty(query.BodyStyle))
            {
                conditions.Add("c.bodyStyle = @bodyStyle");
                parameters["@bodyStyle"] = query.BodyStyle;
            }

            if (!string.IsNullOrEmpty(query.FuelType))
            {
                conditions.Add("c.fuelType = @fuelType");
                parameters["@fuelType"] = query.FuelType;
            }

            if (!string.IsNullOrEmpty(query.Condition))
            {
                conditions.Add("c.condition = @condition");
                parameters["@condition"] = query.Condition;
            }

            if (!string.IsNullOrEmpty(query.Estado))
            {
                conditions.Add("c.estado = @estado");
                parameters["@estado"] = query.Estado;
            }

            if (query.PriceMin.HasValue)
            {
                conditions.Add("c.currentPrice >= @priceMin");
                parameters["@priceMin"] = query.PriceMin.Value;
            }

            if (query.PriceMax.HasValue)
            {
                conditions.Add("c.currentPrice <= @priceMax");
                parameters["@priceMax"] = query.PriceMax.Value;
            }

            if (query.MileageMax.HasValue)
            {
                conditions.Add("c.mileage <= @mileageMax");
                parameters["@mileageMax"] = query.MileageMax.Value;
            }

            // Construir cláusula ORDER BY
            var orderBy = query.SortBy?.ToLowerInvariant() switch
            {
                "make" => "c.make",
                "year" => "c.year",
                "currentprice" => "c.currentPrice",
                "mileage" => "c.mileage",
                "createdat" => "c.createdAt",
                _ => "c.createdAt"
            };

            var orderDirection = query.SortDirection?.ToUpperInvariant() == "ASC" ? "ASC" : "DESC";

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY {orderBy} {orderDirection}";

            var cosmosQuery = new QueryDefinition(sql);
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = _carsContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var cars = new List<CarData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var car = CarData.FromDict(item);
                        cars.Add(car);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to CarData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("✅ Found {Count} filtered cars for Twin ID: {TwinId}", cars.Count, twinId);
            return cars;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get filtered cars for Twin ID: {TwinId}", twinId);
            return new List<CarData>();
        }
    }

    /// <summary>
    /// Obtener estadísticas de vehículos
    /// </summary>
    public async Task<CarStatistics> GetCarStatisticsAsync(string twinId)
    {
        try
        {
            _logger.LogInformation("📊 Getting car statistics for Twin ID: {TwinId}", twinId);

            var cars = await GetCarsByTwinIdAsync(twinId);

            var stats = new CarStatistics
            {
                TotalCars = cars.Count,
                TotalValue = cars.Where(c => c.CurrentPrice.HasValue).Sum(c => c.CurrentPrice.Value),
                AverageYear = cars.Where(c => c.Year > 0).Average(c => c.Year),
                AverageMileage = cars.Where(c => c.Mileage.HasValue).Average(c => c.Mileage.Value),
                MakeDistribution = cars.GroupBy(c => c.Make)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ConditionDistribution = cars.Where(c => !string.IsNullOrEmpty(c.Condition))
                    .GroupBy(c => c.Condition!)
                    .ToDictionary(g => g.Key, g => g.Count()),
                EstadoDistribution = cars.Where(c => !string.IsNullOrEmpty(c.Estado))
                    .GroupBy(c => c.Estado!)
                    .ToDictionary(g => g.Key, g => g.Count())
            };

            _logger.LogInformation("✅ Car statistics calculated for Twin ID: {TwinId}", twinId);
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get car statistics for Twin ID: {TwinId}", twinId);
            return new CarStatistics();
        }
    }
}

/// <summary>
/// Estadísticas de vehículos
/// </summary>
public class CarStatistics
{
    public int TotalCars { get; set; }
    public decimal TotalValue { get; set; }
    public double AverageYear { get; set; }
    public double AverageMileage { get; set; }
    public Dictionary<string, int> MakeDistribution { get; set; } = new();
    public Dictionary<string, int> ConditionDistribution { get; set; } = new();
    public Dictionary<string, int> EstadoDistribution { get; set; } = new();
}