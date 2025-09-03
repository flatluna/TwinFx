using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Agents;
using TwinFx.Models;
using EducationItem = TwinFx.Agents.EducationItem;
using Newtonsoft.Json.Linq;

namespace TwinFx.Services;

/// <summary>
/// Data class for Twin profile information.
/// </summary>
public class TwinProfileData
{
    public string TwinId { get; set; } = string.Empty;
    public string TwinName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string DateOfBirth { get; set; } = string.Empty;
    public string BirthCountry { get; set; } = string.Empty;
    public string BirthCity { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string Occupation { get; set; } = string.Empty;
    public List<string> Interests { get; set; } = new();
    public List<string> Languages { get; set; } = new();
    public string CountryId { get; set; } = string.Empty;
    public string? ProfilePhoto { get; set; }
    public string? MiddleName { get; set; }
    public string? Nickname { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? MaritalStatus { get; set; }
    public string? PersonalBio { get; set; }
    public string? EmergencyContact { get; set; }
    public string? EmergencyPhone { get; set; }
    public string? BloodType { get; set; }
    public string? Height { get; set; }
    public string? Weight { get; set; }
    public string? DocumentType { get; set; }
    public string? DocumentNumber { get; set; }
    public string? PassportNumber { get; set; }
    public string? SocialSecurityNumber { get; set; }
    public string? Website { get; set; }
    public string? LinkedIn { get; set; }
    public string? Facebook { get; set; }
    public string? Instagram { get; set; }
    public string? Twitter { get; set; }
    public string? Company { get; set; }
    public string? FamilyRelation { get; set; }
    public string? AccountManagement { get; set; }
    public string? PrivacyLevel { get; set; }
    public List<string> AuthorizedEmails { get; set; } = new();
    public string? OwnerEmail { get; set; }
}

/// <summary>
/// Data class for family information.
/// </summary>
public class FamilyData
{
    public string Id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string Parentesco { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string FechaNacimiento { get; set; } = string.Empty; // mm/dd/yyyy
    public string NumeroCelular { get; set; } = string.Empty; // (XXX) XXX-XXXX o +1234567890
    public string Email { get; set; } = string.Empty; // ejemplo@email.com
    public string UrlFoto { get; set; } = string.Empty; // https://ejemplo.com/foto.jpg
    public string Notas { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "family";

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["TwinID"] = TwinID,
            ["parentesco"] = Parentesco,
            ["nombre"] = Nombre,
            ["apellido"] = Apellido,
            ["fecha_nacimiento"] = FechaNacimiento,
            ["numero_celular"] = NumeroCelular,
            ["email"] = Email,
            ["url_foto"] = UrlFoto,
            ["notas"] = Notas,
            ["createdDate"] = CreatedDate.ToString("O"),
            ["type"] = Type
        };
    }

    public static FamilyData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(DateTime))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        };

        return new FamilyData
        {
            Id = GetValue("id", ""),
            TwinID = GetValue<string>("TwinID"),
            Parentesco = GetValue<string>("parentesco"),
            Nombre = GetValue<string>("nombre"),
            Apellido = GetValue<string>("apellido"),
            FechaNacimiento = GetValue<string>("fecha_nacimiento"),
            NumeroCelular = GetValue<string>("numero_celular"),
            Email = GetValue<string>("email"),
            UrlFoto = GetValue<string>("url_foto"),
            Notas = GetValue<string>("notas"),
            CreatedDate = GetValue("createdDate", DateTime.UtcNow),
            Type = GetValue("type", "family")
        };
    }
}

/// <summary>
/// Data class for contact information.
/// </summary>
public class ContactData
{
    public string Id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Apellido { get; set; } = string.Empty;
    public string Relacion { get; set; } = string.Empty;
    public string Apodo { get; set; } = string.Empty;
    public string TelefonoMovil { get; set; } = string.Empty;
    public string TelefonoTrabajo { get; set; } = string.Empty;
    public string TelefonoCasa { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Direccion { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Cargo { get; set; } = string.Empty;
    public string Cumpleanos { get; set; } = string.Empty;
    public string Notas { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "contact";

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["TwinID"] = TwinID,
            ["nombre"] = Nombre,
            ["apellido"] = Apellido,
            ["relacion"] = Relacion,
            ["apodo"] = Apodo,
            ["telefono_movil"] = TelefonoMovil,
            ["telefono_trabajo"] = TelefonoTrabajo,
            ["telefono_casa"] = TelefonoCasa,
            ["email"] = Email,
            ["direccion"] = Direccion,
            ["empresa"] = Empresa,
            ["cargo"] = Cargo,
            ["cumpleanos"] = Cumpleanos,
            ["notas"] = Notas,
            ["createdDate"] = CreatedDate.ToString("O"),
            ["type"] = Type
        };
    }

    public static ContactData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(DateTime))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        };

        return new ContactData
        {
            Id = GetValue("id", ""),
            TwinID = GetValue<string>("TwinID"),
            Nombre = GetValue<string>("nombre"),
            Apellido = GetValue<string>("apellido"),
            Relacion = GetValue<string>("relacion"),
            Apodo = GetValue<string>("apodo"),
            TelefonoMovil = GetValue<string>("telefono_movil"),
            TelefonoTrabajo = GetValue<string>("telefono_trabajo"),
            TelefonoCasa = GetValue<string>("telefono_casa"),
            Email = GetValue<string>("email"),
            Direccion = GetValue<string>("direccion"),
            Empresa = GetValue<string>("empresa"),
            Cargo = GetValue<string>("cargo"),
            Cumpleanos = GetValue<string>("cumpleanos"),
            Notas = GetValue<string>("notas"),
            CreatedDate = GetValue("createdDate", DateTime.UtcNow),
            Type = GetValue("type", "contact")
        };
    }
}

/// <summary>
/// Custom JsonConverter for EducationData that handles multiple property name formats
/// </summary>
public class EducationDataJsonConverter : JsonConverter<EducationData>
{
    public override EducationData? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        var educationData = new EducationData();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            string? propertyName = reader.GetString();
            reader.Read();

            switch (propertyName?.ToLowerInvariant())
            {
                case "id":
                    educationData.Id = reader.GetString() ?? "";
                    break;
                case "twinid":
                case "twin_id":
                case "twinId":
                    educationData.TwinID = reader.GetString() ?? "";
                    break;
                case "countryid":
                case "country_id":
                case "countryId":
                    educationData.CountryID = reader.GetString() ?? "";
                    break;
                case "institution":
                case "institucion":
                    educationData.Institution = reader.GetString() ?? "";
                    break;
                case "education_type":
                case "educationtype":
                case "tipoeducacion":
                    educationData.EducationType = reader.GetString() ?? "";
                    break;
                case "degree_obtained":
                case "degreeobtained":
                case "tituloobtenido":
                    educationData.DegreeObtained = reader.GetString() ?? "";
                    break;
                case "field_of_study":
                case "fieldofstudy":
                case "campoestudio":
                    educationData.FieldOfStudy = reader.GetString() ?? "";
                    break;
                case "start_date":
                case "startdate":
                case "fechainicio":
                    educationData.StartDate = reader.GetString() ?? "";
                    break;
                case "end_date":
                case "enddate":
                case "fechafin":
                    educationData.EndDate = reader.GetString() ?? "";
                    break;
                case "in_progress":
                case "inprogress":
                case "enprogreso":
                    educationData.InProgress = reader.TokenType == JsonTokenType.True;
                    break;
                case "country":
                case "pais":
                    educationData.Country = reader.GetString() ?? "";
                    break;
                case "description":
                case "descripcion":
                    educationData.Description = reader.GetString() ?? "";
                    break;
                case "achievements":
                case "logrosdestacados":
                    educationData.Achievements = reader.GetString() ?? "";
                    break;
                case "gpa":
                case "promedio":
                    educationData.Gpa = reader.GetString() ?? "";
                    break;
                case "credits":
                case "creditos":
                    educationData.Credits = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : 0;
                    break;
                case "createddate":
                case "created_date":
                    if (reader.TokenType == JsonTokenType.String && DateTime.TryParse(reader.GetString(), out var date))
                    {
                        educationData.CreatedDate = date;
                    }
                    break;
                case "type":
                case "tipo":
                    educationData.Type = reader.GetString() ?? "education";
                    break;
                default:
                    // Skip unknown properties
                    reader.Skip();
                    break;
            }
        }

        return educationData;
    }

    public override void Write(Utf8JsonWriter writer, EducationData value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        
        writer.WriteString("id", value.Id);
        writer.WriteString("twinId", value.TwinID);
        writer.WriteString("countryId", value.CountryID);
        
        writer.WriteString("institution", value.Institution);
        writer.WriteString("education_type", value.EducationType);
        writer.WriteString("degree_obtained", value.DegreeObtained);
        writer.WriteString("field_of_study", value.FieldOfStudy);
        writer.WriteString("start_date", value.StartDate);
        writer.WriteString("end_date", value.EndDate);
        writer.WriteBoolean("in_progress", value.InProgress);
        writer.WriteString("country", value.Country);
        writer.WriteString("description", value.Description);
        writer.WriteString("achievements", value.Achievements);
        writer.WriteString("gpa", value.Gpa);
        writer.WriteNumber("credits", value.Credits);
        writer.WriteString("createdDate", value.CreatedDate.ToString("O"));
        writer.WriteString("type", value.Type);
        
        writer.WriteEndObject();
    }
}

/// <summary>
/// Data class for education information with custom JsonConverter
/// </summary>
[JsonConverter(typeof(EducationDataJsonConverter))]
public class EducationData
{
    public string Id { get; set; } = string.Empty;
    public string TwinID { get; set; } = string.Empty;
    public string CountryID { get; set; } = string.Empty;

    
    public string Institution { get; set; } = string.Empty;
    public string EducationType { get; set; } = string.Empty;
    public string DegreeObtained { get; set; } = string.Empty;
    public string FieldOfStudy { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public bool InProgress { get; set; } = false;
    public string Country { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Achievements { get; set; } = string.Empty;
    public string Gpa { get; set; } = string.Empty;
    public int Credits { get; set; } = 0;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "education";
    public List<EducationDocument> Documents { get; set; } = new();

    public Dictionary<string, object?> ToDict()
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Id,
            ["TwinID"] = TwinID,
            ["CountryID"] = CountryID,
            ["institution"] = Institution,
            ["education_type"] = EducationType,
            ["degree_obtained"] = DegreeObtained,
            ["field_of_study"] = FieldOfStudy,
            ["start_date"] = StartDate,
            ["end_date"] = EndDate,
            ["in_progress"] = InProgress,
            ["country"] = Country,
            ["description"] = Description,
            ["achievements"] = Achievements,
            ["gpa"] = Gpa,
            ["credits"] = Credits,
            ["createdDate"] = CreatedDate.ToString("O"),
            ["type"] = Type
        };
    }

    public static EducationData FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(int))
                        return (T)(object)jsonElement.GetInt32();
                    if (type == typeof(bool))
                        return (T)(object)jsonElement.GetBoolean();
                    if (type == typeof(DateTime))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return new EducationData
        {
            Id = GetValue("id", ""),
            TwinID = GetValue<string>("TwinID"),
            CountryID = GetValue<string>("CountryID"),
            Institution = GetValue<string>("institution"),
            EducationType = GetValue<string>("education_type"),
            DegreeObtained = GetValue<string>("degree_obtained"),
            FieldOfStudy = GetValue<string>("field_of_study"),
            StartDate = GetValue<string>("start_date"),
            EndDate = GetValue<string>("end_date"),
            InProgress = GetValue("in_progress", false),
            Country = GetValue<string>("country"),
            Description = GetValue<string>("description"),
            Achievements = GetValue<string>("achievements"),
            Gpa = GetValue<string>("gpa"),
            Credits = GetValue("credits", 0),
            CreatedDate = GetValue("createdDate", DateTime.UtcNow),
            Type = GetValue("type", "education")
        };
    }
}

/// <summary>
/// Service class for managing Twin profiles in Cosmos DB.
/// </summary>
public class CosmosDbTwinProfileService
{
    private readonly ILogger<CosmosDbTwinProfileService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly IConfiguration _configuration;

    public CosmosDbTwinProfileService(ILogger<CosmosDbTwinProfileService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;

        var accountName = configuration.GetValue<string>("Values:COSMOS_ACCOUNT_NAME") ?? 
                         configuration.GetValue<string>("COSMOS_ACCOUNT_NAME") ?? "flatbitdb";
        
        var databaseName = configuration.GetValue<string>("Values:COSMOS_DATABASE_NAME") ?? 
                          configuration.GetValue<string>("COSMOS_DATABASE_NAME") ?? "TwinHumanDB";

        var endpoint = $"https://{accountName}.documents.azure.com:443/";
        var key = configuration.GetValue<string>("Values:COSMOS_KEY") ?? 
                 configuration.GetValue<string>("COSMOS_KEY");
        
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
        }

        _client = new CosmosClient(endpoint, key);
        _database = _client.GetDatabase(databaseName);
        
        _logger.LogInformation("✅ Cosmos DB Twin Profile Service initialized successfully");
    }

    // Family methods
    public async Task<List<FamilyData>> GetFamilyByTwinIdAsync(string twinId)
    {
        try
        {
            var familyContainer = _database.GetContainer("TwinFamily");
            
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdDate DESC")
                .WithParameter("@twinId", twinId);

            var iterator = familyContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var familyMembers = new List<FamilyData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var family = FamilyData.FromDict(item);
                        familyMembers.Add(family);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to FamilyData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("👨‍👩‍👧‍👦 Found {Count} family members for Twin ID: {TwinId}", familyMembers.Count, twinId);
            return familyMembers;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get family members for Twin ID: {TwinId}", twinId);
            return new List<FamilyData>();
        }
    }

    public async Task<FamilyData?> GetFamilyByIdAsync(string familyId, string twinId)
    {
        try
        {
            var familyContainer = _database.GetContainer("TwinFamily");
            
            var response = await familyContainer.ReadItemAsync<Dictionary<string, object?>>(
                familyId,
                new PartitionKey(twinId)
            );
            
            var family = FamilyData.FromDict(response.Resource);
            _logger.LogInformation("👤 Family member retrieved successfully: {Nombre} {Apellido} ({Parentesco})", 
                family.Nombre, family.Apellido, family.Parentesco);
            return family;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get family member by ID {FamilyId} for Twin: {TwinId}", familyId, twinId);
            return null;
        }
    }

    public async Task<bool> CreateFamilyAsync(FamilyData familyData)
    {
        try
        {
            var familyContainer = _database.GetContainer("TwinFamily");
            
            var familyDict = familyData.ToDict();
            await familyContainer.CreateItemAsync(familyDict, new PartitionKey(familyData.TwinID));
            
            _logger.LogInformation("👨‍👩‍👧‍👦 Family member created successfully: {Nombre} {Apellido} ({Parentesco}) for Twin: {TwinID}", 
                familyData.Nombre, familyData.Apellido, familyData.Parentesco, familyData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create family member: {Nombre} {Apellido} ({Parentesco}) for Twin: {TwinID}", 
                familyData.Nombre, familyData.Apellido, familyData.Parentesco, familyData.TwinID);
            return false;
        }
    }

    public async Task<bool> UpdateFamilyAsync(FamilyData familyData)
    {
        try
        {
            var familyContainer = _database.GetContainer("TwinFamily");
            
            var familyDict = familyData.ToDict();
            familyDict["updatedAt"] = DateTime.UtcNow.ToString("O");
            
            await familyContainer.UpsertItemAsync(familyDict, new PartitionKey(familyData.TwinID));
            
            _logger.LogInformation("👨‍👩‍👧‍👦 Family member updated successfully: {Nombre} {Apellido} ({Parentesco}) for Twin: {TwinID}", 
                familyData.Nombre, familyData.Apellido, familyData.Parentesco, familyData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update family member: {Id} for Twin: {TwinID}", 
                familyData.Id, familyData.TwinID);
            return false;
        }
    }

    public async Task<bool> DeleteFamilyAsync(string familyId, string twinId)
    {
        try
        {
            var familyContainer = _database.GetContainer("TwinFamily");
            
            await familyContainer.DeleteItemAsync<Dictionary<string, object?>>(
                familyId,
                new PartitionKey(twinId)
            );
            
            _logger.LogInformation("👨‍👩‍👧‍👦 Family member deleted successfully: {FamilyId} for Twin: {TwinId}", familyId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete family member: {FamilyId} for Twin: {TwinId}", familyId, twinId);
            return false;
        }
    }

    // Education methods
    public async Task<bool> CreateEducationAsync(EducationData educationData)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            var educationDict = educationData.ToDict();
            await educationContainer.CreateItemAsync(educationDict, new PartitionKey(educationData.CountryID));
            
            _logger.LogInformation("🎓 Education record created successfully: {Institution} {EducationType} for Twin: {TwinID}", 
                educationData.Institution, educationData.EducationType, educationData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create education record: {Institution} {EducationType} for Twin: {TwinID}", 
                educationData.Institution, educationData.EducationType, educationData.TwinID);
            return false;
        }
    }
    public async Task<List<EducationData>> GetEducationsByTwinIdAsync(string twinId)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            var educationRecords = new List<EducationData>();
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.start_date DESC")
                 .WithParameter("@twinId", twinId);
            using FeedIterator<EducationData> iterator = educationContainer.GetItemQueryIterator<EducationData>(query);

            while (iterator.HasMoreResults)
            {
                FeedResponse<EducationData> page = await iterator.ReadNextAsync();
                educationRecords.AddRange(page.Resource); // FeedResponse<T> es IEnumerable<T>  
            }

            // Generate SAS URLs for each education record
            foreach (var education in educationRecords)
            {
                try
                {
                    foreach(var docu in education.Documents)
                    {
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var dataLakeFactory = new DataLakeClientFactory(loggerFactory, _configuration);
                        var dataLakeClient = dataLakeFactory.CreateClient(education.TwinID);

                        // Generate SAS URL for education documents - using a generic path
                        // You can customize this path based on your file naming convention
                        var educationFilePath = docu.FilePath + "/" + docu.FileName;
                         var sasUrl = await dataLakeClient.GenerateSasUrlAsync(educationFilePath, TimeSpan.FromHours(24));

                        docu.SASURL = sasUrl ?? string.Empty;

                        if (!string.IsNullOrEmpty(sasUrl))
                        {
                            _logger.LogInformation("🔗 Generated SAS URL for education record: {EducationId}", education.Id);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Could not generate SAS URL for education record: {EducationId}", education.Id);
                        }
                    }
                    // Create DataLake client for this twin
                    
                }
                catch (Exception sasEx)
                {
                    _logger.LogWarning(sasEx, "⚠️ Error generating SAS URL for education record: {EducationId}", education.Id);
                    
                }
            }

            _logger.LogInformation("🎓 Found {Count} education records for Twin ID: {TwinId}", educationRecords.Count, twinId);
            return educationRecords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get education records for Twin ID: {TwinId}", twinId);
            return new List<EducationData>();
        }
    }

    public async Task<List<EducationData>> GetEducationsByTwinIdAsyncB(string twinId)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.start_date DESC")
                .WithParameter("@twinId", twinId);

            var iterator = educationContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var educationRecords = new List<EducationData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        // Convert the base education data
                        var education = EducationData.FromDict(item);
                        
                        // Handle the documents array if present - just add info to description
                        if (item.TryGetValue("documents", out var documentsValue) && documentsValue != null)
                        {
                            try
                            {
                                var documentsCount = GetArrayCount(documentsValue);
                                
                                if (documentsCount > 0)
                                {
                                    _logger.LogInformation("📄 Found {Count} documents for education record: {Id}", 
                                        documentsCount, education.Id);
                                    
                                    // Update description to include document info if documents exist
                                    if (!string.IsNullOrEmpty(education.Description))
                                    {
                                        education.Description += $" [📄 {documentsCount} document(s) attached]";
                                    }
                                    else
                                    {
                                        education.Description = $"📄 {documentsCount} document(s) attached";
                                    }
                                }
                            }
                            catch (Exception docEx)
                            {
                                _logger.LogWarning(docEx, "⚠️ Error parsing documents array for education {Id}", education.Id);
                            }
                        }
                        
                        // Handle other dynamic arrays that might be added in the future
                        HandleDynamicArrays(item, education);
                        
                        educationRecords.Add(education);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to EducationData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("🎓 Found {Count} education records for Twin ID: {TwinId}", educationRecords.Count, twinId);
            return educationRecords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get education records for Twin ID: {TwinId}", twinId);
            return new List<EducationData>();
        }
    }

    public async Task<EducationData?> GetEducationByIdAsync(string educationId, string countryId)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            
            var response = await educationContainer.ReadItemAsync<Dictionary<string, object?>>(
                educationId,
                new PartitionKey(countryId)
            );
            
            var education = EducationData.FromDict(response.Resource);
            _logger.LogInformation("🎓 Education record retrieved successfully: {Institution} {EducationType}", education.Institution, education.EducationType);
            return education;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get education record by ID {EducationId} for CountryID: {CountryId}", educationId, countryId);
            return null;
        }
    }

    public async Task<bool> UpdateEducationAsync(EducationData educationData)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            
            var educationDict = educationData.ToDict();
            educationDict["updatedAt"] = DateTime.UtcNow.ToString("O");
            
            await educationContainer.UpsertItemAsync(educationDict, new PartitionKey(educationData.CountryID));
            
            _logger.LogInformation("🎓 Education record updated successfully: {Institution} {EducationType} for Twin: {TwinID}", 
                educationData.Institution, educationData.EducationType, educationData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update education record: {Id} for Twin: {TwinID}", 
                educationData.Id, educationData.TwinID);
            return false;
        }
    }

    public async Task<bool> DeleteEducationAsync(string educationId, string countryId)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            
            await educationContainer.DeleteItemAsync<Dictionary<string, object?>>(
                educationId,
                new PartitionKey(countryId)
            );
            
            _logger.LogInformation("🎓 Education record deleted successfully: {EducationId} for CountryID: {CountryId}", educationId, countryId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete education record: {EducationId} for CountryID: {CountryId}", educationId, countryId);
            return false;
        }
    }

    // Profile methods
    public async Task<TwinProfileData?> GetProfileByIdCrossPartitionAsync(string twinId)
    {
        try
        {
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @twinId")
                .WithParameter("@twinId", twinId);

            var iterator = _database.GetContainer("TwinProfiles").GetItemQueryIterator<Dictionary<string, object?>>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = 1 }
            );

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                var item = response.FirstOrDefault();
                if (item != null)
                {
                    return TwinProfileDataHelper.FromDict(item);
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get Twin profile by ID cross-partition {TwinId}", twinId);
            return null;
        }
    }

    public async Task<bool> UpdateProfileAsync(TwinProfileData profile)
    {
        _logger.LogWarning("⚠️ UpdateProfileAsync: Method not fully implemented");
        await Task.CompletedTask;
        return false;
    }

    public async Task<bool> CreateProfileAsync(TwinProfileData profile)
    {
        _logger.LogWarning("⚠️ CreateProfileAsync: Method not fully implemented");
        await Task.CompletedTask;
        return false;
    }

    public async Task<List<TwinProfileData>> SearchProfilesAsync(string searchTerm, string? countryId = null)
    {
        _logger.LogWarning("⚠️ SearchProfilesAsync: Method not fully implemented");
        await Task.CompletedTask;
        return new List<TwinProfileData>();
    }

    public async Task<List<Dictionary<string, object?>>> LoadConversationHistoryAsync(string twinId, int limit = 10)
    {
        _logger.LogWarning("⚠️ LoadConversationHistoryAsync: Method not fully implemented");
        await Task.CompletedTask;
        return new List<Dictionary<string, object?>>();
    }

    public async Task<Dictionary<string, object>> CountConversationMessagesAsync(string twinId)
    {
        _logger.LogWarning("⚠️ CountConversationMessagesAsync: Method not fully implemented");
        await Task.CompletedTask;
        return new Dictionary<string, object>();
    }

    // Contact methods
    public async Task<List<ContactData>> GetContactsByTwinIdAsync(string twinId)
    {
        try
        {
            var contactsContainer = _database.GetContainer("TwinContacts");
            
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdDate DESC")
                .WithParameter("@twinId", twinId);

            var iterator = contactsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var contacts = new List<ContactData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var contact = ContactData.FromDict(item);
                        contacts.Add(contact);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to ContactData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("👥 Found {Count} contacts for Twin ID: {TwinId}", contacts.Count, twinId);
            return contacts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get contacts for Twin ID: {TwinId}", twinId);
            return new List<ContactData>();
        }
    }

    public async Task<ContactData?> GetContactByIdAsync(string contactId, string twinId)
    {
        try
        {
            var contactsContainer = _database.GetContainer("TwinContacts");
            
            var response = await contactsContainer.ReadItemAsync<Dictionary<string, object?>>(
                contactId,
                new PartitionKey(twinId)
            );
            
            var contact = ContactData.FromDict(response.Resource);
            _logger.LogInformation("👤 Contact retrieved successfully: {Nombre} {Apellido}", contact.Nombre, contact.Apellido);
            return contact;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get contact by ID {ContactId} for Twin: {TwinId}", contactId, twinId);
            return null;
        }
    }

    public async Task<bool> CreateContactAsync(ContactData contactData)
    {
        try
        {
            var contactsContainer = _database.GetContainer("TwinContacts");
            
            var contactDict = contactData.ToDict();
            await contactsContainer.CreateItemAsync(contactDict, new PartitionKey(contactData.TwinID));
            
            _logger.LogInformation("👤 Contact created successfully: {Nombre} {Apellido} for Twin: {TwinID}", 
                contactData.Nombre, contactData.Apellido, contactData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create contact: {Nombre} {Apellido} for Twin: {TwinID}", 
                contactData.Nombre, contactData.Apellido, contactData.TwinID);
            return false;
        }
    }

    public async Task<bool> UpdateContactAsync(ContactData contactData)
    {
        try
        {
            var contactsContainer = _database.GetContainer("TwinContacts");
            
            var contactDict = contactData.ToDict();
            contactDict["updatedAt"] = DateTime.UtcNow.ToString("O");
            
            await contactsContainer.UpsertItemAsync(contactDict, new PartitionKey(contactData.TwinID));
            
            _logger.LogInformation("👤 Contact updated successfully: {Nombre} {Apellido} for Twin: {TwinID}", 
                contactData.Nombre, contactData.Apellido, contactData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update contact: {Id} for Twin: {TwinID}", 
                contactData.Id, contactData.TwinID);
            return false;
        }
    }

    public async Task<bool> DeleteContactAsync(string contactId, string twinId)
    {
        try
        {
            var contactsContainer = _database.GetContainer("TwinContacts");
            
            await contactsContainer.DeleteItemAsync<Dictionary<string, object?>>(
                contactId,
                new PartitionKey(twinId)
            );
            
            _logger.LogInformation("👤 Contact deleted successfully: {ContactId} for Twin: {TwinId}", contactId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete contact: {ContactId} for Twin: {TwinId}", contactId, twinId);
            return false;
        }
    }

    // Photo methods
    public async Task<bool> SavePhotoDocumentAsync(PhotoDocument photoDocument)
    {
        try
        {
            var picturesContainer = _database.GetContainer("TwinPictures");
            
            var photoDict = new Dictionary<string, object?>
            {
                ["id"] = photoDocument.Id,
                ["TwinID"] = photoDocument.TwinId,
                ["photoId"] = photoDocument.PhotoId,
                ["country"] = photoDocument.Country ,
                ["description"] = photoDocument.Description,
                ["dateTaken"] = photoDocument.DateTaken,
                ["location"] = photoDocument.Location,
                ["peopleInPhoto"] = photoDocument.PeopleInPhoto,
                ["category"] = photoDocument.Category,
                ["tags"] = photoDocument.Tags,
                ["filePath"] = photoDocument.FilePath,
                ["fileName"] = photoDocument.FileName,
                ["fileSize"] = photoDocument.FileSize,
                ["mimeType"] = photoDocument.MimeType,
                ["uploadDate"] = photoDocument.UploadDate.ToString("O"),
                ["createdAt"] = photoDocument.CreatedAt.ToString("O"),
                ["processedAt"] = photoDocument.ProcessedAt.ToString("O")
            };
            
            // ✅ FIXED: Use UpsertItemAsync instead of CreateItemAsync for UPDATE operations
            await picturesContainer.UpsertItemAsync(photoDict, new PartitionKey(photoDocument.TwinId));
            
            _logger.LogInformation("📸 Photo document saved/updated successfully: {PhotoId} for Twin: {TwinId}", 
                photoDocument.PhotoId, photoDocument.TwinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save/update photo document: {PhotoId} for Twin: {TwinId}", 
                photoDocument.PhotoId, photoDocument.TwinId);
            return false;
        }
    }

    public async Task<List<PhotoDocument>> GetPhotoDocumentsByTwinIdAsync(string twinId)
    {
        try
        {
            var picturesContainer = _database.GetContainer("TwinPictures");
            
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = picturesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var photoDocuments = new List<PhotoDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var photoDocument = PhotoDocument.FromDict(item);
                        photoDocuments.Add(photoDocument);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to PhotoDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("📸 Found {Count} photo documents for Twin ID: {TwinId}", photoDocuments.Count, twinId);
            return photoDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get photo documents for Twin ID: {TwinId}", twinId);
            return new List<PhotoDocument>();
        }
    }

    public async Task<List<PhotoDocument>> GetFilteredPhotoDocumentsAsync(string twinId, string? category, string sqlFilter)
    {
        try
        {
            var picturesContainer = _database.GetContainer("TwinPictures");
            
            var query = new QueryDefinition($"SELECT * FROM c WHERE {sqlFilter} ORDER BY c.uploadDate DESC");

            var iterator = picturesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var photoDocuments = new List<PhotoDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var photoDocument = PhotoDocument.FromDict(item);
                        photoDocuments.Add(photoDocument);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to PhotoDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("📸 Found {Count} filtered photo documents for Twin ID: {TwinId}", photoDocuments.Count, twinId);
            return photoDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get filtered photo documents for Twin ID: {TwinId}", twinId);
            return new List<PhotoDocument>();
        }
    }

    // Invoice methods
    public async Task<List<InvoiceDocument>> GetInvoiceDocumentsByTwinIdAsync(string twinId)
    {
        try
        {
            var invoicesContainer = _database.GetContainer("TwinInvoices");
            
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = invoicesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var invoiceDocuments = new List<InvoiceDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var invoiceDocument = InvoiceDocument.FromCosmosDocument(item);
                        invoiceDocuments.Add(invoiceDocument);
                        
                        _logger.LogInformation("📄 Parsed InvoiceDocument: {Id} with {LineItemsCount} line items", 
                            invoiceDocument.Id, invoiceDocument.InvoiceData.LineItems.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to InvoiceDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("📄 Found {Count} invoice documents for Twin ID: {TwinId}", invoiceDocuments.Count, twinId);
            return invoiceDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get invoice documents for Twin ID: {TwinId}", twinId);
            return new List<InvoiceDocument>();
        }
    }

    public async Task<List<InvoiceDocument>> GetFilteredInvoiceDocumentsAsync(string twinId, string sqlQuery)
    {
        try
        {
            var invoicesContainer = _database.GetContainer("TwinInvoices");
            
            string fullQuery;
            bool isJoinQuery = false;
            
            // Check if WHERE clause references li.Description or li.Amount (requires JOIN)
            if (sqlQuery.Contains("li.Description") || sqlQuery.Contains("li.Amount") || sqlQuery.Contains("li.Quantity"))
            {
                // Use JOIN syntax for LineItems
                fullQuery = $"SELECT DISTINCT c FROM c JOIN li IN c.invoiceData.LineItems WHERE {sqlQuery} ORDER BY c.createdAt DESC";
                isJoinQuery = true;
                _logger.LogInformation("🔍 Using JOIN syntax for LineItems query: {Query}", fullQuery);
            }
            else
            {
                // Standard query without JOIN
                fullQuery = $"SELECT * FROM c WHERE {sqlQuery} ORDER BY c.createdAt DESC";
                _logger.LogInformation("📋 Using standard query: {Query}", fullQuery);
            }

            var query = new QueryDefinition(fullQuery);

            var iterator = invoicesContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var invoiceDocuments = new List<InvoiceDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        Dictionary<string, object?> documentData;
                        
                        // Handle JOIN query results where document is nested under "c"
                        if (isJoinQuery && item.ContainsKey("c"))
                        {
                            _logger.LogInformation("🔗 JOIN query detected: extracting document from 'c' key");
                            
                            // Extract the actual document from the "c" wrapper
                            var cValue = item["c"];
                            documentData = ConvertObjectToDictionary(cValue);
                            
                            if (documentData.Any())
                            {
                                _logger.LogInformation("✅ Successfully extracted document from JOIN result with {Count} keys", documentData.Count);
                            }
                            else
                            {
                                _logger.LogWarning("⚠️ Failed to extract document from JOIN result - empty dictionary");
                                continue;
                            }
                        }
                        else
                        {
                            // Standard query result - use item directly
                            documentData = item;
                        }

                        var invoiceDocument = InvoiceDocument.FromCosmosDocument(documentData);
                        invoiceDocuments.Add(invoiceDocument);
                        
                        _logger.LogInformation("📄 Parsed InvoiceDocument: {Id} with {LineItemsCount} line items", 
                            invoiceDocument.Id, invoiceDocument.InvoiceData.LineItems.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to InvoiceDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("📄 Found {Count} invoice documents for Twin ID: {TwinId}", invoiceDocuments.Count, twinId);
            return invoiceDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get invoice documents for Twin ID: {TwinId}", twinId);
            return new List<InvoiceDocument>();
        }
    }

    public async Task<bool> SaveInvoiceAnalysisAsync(InvoiceAnalysisResult invoiceResult, string containerName, string fileName)
    {
        _logger.LogWarning("⚠️ SaveInvoiceAnalysisAsync: Method not fully implemented");
        await Task.CompletedTask;
        return false;
    }

    /// <summary>
    /// Save education document analysis to TwinEducation container by updating existing education record
    /// </summary>
    /// <param name="educationResult">Education analysis result from Document Intelligence</param>
    /// <param name="educationId">Education record ID to update</param>
    /// <param name="containerName">Container name (for reference)</param>
    /// <param name="fileName">File name (for reference)</param>
    /// <returns>True if saved successfully</returns>
    public async Task<bool> SaveEducationAnalysisAsync(DocumentExtractionResult  educationResult, string educationId, string containerName, string fileName,
        string filePath)
    {
        try
        {
            _logger.LogInformation("📚 Saving education document analysis to Cosmos DB...");
            _logger.LogInformation($"   • Education ID: {educationId}");
            _logger.LogInformation($"   • Container: {containerName}");
            _logger.LogInformation($"   • File: {fileName}");

            var educationContainer = _database.GetContainer("TwinEducation");

            // First, get all education records to find the one with matching ID
            var query = new QueryDefinition("SELECT * FROM c WHERE c.id = @educationId")
                .WithParameter("@educationId", educationId);

            var iterator = educationContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            Dictionary<string, object?>? existingEducation = null;

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                existingEducation = response.FirstOrDefault();
                if (existingEducation != null) break;
            }

            if (existingEducation == null)
            {
                _logger.LogError($"❌ Education record not found with ID: {educationId}");
                return false;
            }

            _logger.LogInformation($"✅ Found education record: {existingEducation.GetValueOrDefault("institution")}");

            // Create the document analysis object to add to the documents array
            var documentAnalysis = new Dictionary<string, object?>
            {
                ["documentId"] = Guid.NewGuid().ToString(),
                ["fileName"] = fileName,
                ["filePath"] = filePath,
                ["containerName"] = containerName,
                ["processedAt"] =  DateTime.UtcNow.ToString("O"),
                ["success"] = educationResult.Success,
                ["errorMessage"] = educationResult.ErrorMessage,
                ["textContent"] = educationResult.Metadata.DocumentTextContent,
                ["htmlContent"] = educationResult.Metadata.DocumentHTMLContent,
                ["documentType"] = educationResult.Metadata.DocumentType, 
            };

            // Get or create the documents array
            var documentsValue = existingEducation.GetValueOrDefault("documents");
            var documentsArray = ConvertToObjectList(documentsValue);
            
            // Add the new document analysis
            documentsArray.Add(documentAnalysis);
            
            // Update the education record with the new documents array
            existingEducation["documents"] = documentsArray;
            existingEducation["updatedAt"] = DateTime.UtcNow.ToString("O");
            existingEducation["lastDocumentProcessed"] = DateTime.UtcNow.ToString("O");

            // Get CountryID for partition key
            var countryId = existingEducation.GetValueOrDefault("CountryID")?.ToString() ?? "US";

            // Update the document in Cosmos DB
            await educationContainer.UpsertItemAsync(existingEducation, new PartitionKey(countryId));

            
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save education document analysis to Cosmos DB");
            return false;
        }
    }

    // Work document methods
    public async Task<bool> SaveWorkDocumentAsync(Dictionary<string, object?> workDocument)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");
            
            // Extract TwinID for partition key
            var twinId = workDocument.GetValueOrDefault("TwinID")?.ToString() ?? throw new ArgumentException("TwinID is required");
            
            // Use UpsertItemAsync to handle both create and update scenarios
            await workContainer.UpsertItemAsync(workDocument, new PartitionKey(twinId));
            
            _logger.LogInformation("💼 Work document saved/updated successfully: {DocumentType} for Twin: {TwinId}", 
                workDocument.GetValueOrDefault("documentType"), twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save/update work document for Twin: {TwinId}", 
                workDocument.GetValueOrDefault("TwinID"));
            return false;
        }
    }

    public async Task<List<Dictionary<string, object?>>> GetWorkDocumentsByTwinId2Async(string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");
            
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = workContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var workDocuments = new List<Dictionary<string, object?>>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                workDocuments.AddRange(response.Resource);
            }

            _logger.LogInformation("💼 Found {Count} work documents for Twin ID: {TwinId}", workDocuments.Count, twinId);
            return workDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get work documents for Twin ID: {TwinId}", twinId);
            return new List<Dictionary<string, object?>>();
        }
    }
    public async Task<List<ResumeStructuredData>> GetWorkDocumentsByTwinIdAsync(string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");
            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.createdAt DESC")
                .WithParameter("@twinId", twinId);

            var iterator = workContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var workDocuments = new List<ResumeStructuredData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var document in response)
                {
                    try
                    {
                        ResumeStructuredData structuredData;

                        // Try to find resumeData in the document
                        if (document.TryGetValue("resumeData", out var resumeDataValue) && resumeDataValue != null)
                        {
                            var resumeDict = ConvertObjectToDictionary(resumeDataValue);
                            structuredData = MapToResumeStructuredData(resumeDict);
                        }
                        else
                        {
                            // Fallback: if the document itself contains resume fields, attempt to convert entire document
                            var docDict = ConvertObjectToDictionary(document);
                            structuredData = MapToResumeStructuredData(docDict);
                        }

                        // Populate top-level metadata and aggregate fields from the document
                        // Helper local functions
                        static string? GetString(object? v)
                        {
                            if (v == null) return null;
                            if (v is string s) return s;
                            if (v is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString();
                            if (v is JsonElement je2 && (je2.ValueKind == JsonValueKind.Number || je2.ValueKind == JsonValueKind.True || je2.ValueKind == JsonValueKind.False)) return je2.ToString();
                            if (v is JValue jv) return jv.Value?.ToString();
                            if (v is JToken jt) return jt.ToString();
                            return v.ToString();
                        }

                        static DateTime? GetDateTime(object? v)
                        {
                            try
                            {
                                var s = GetString(v);
                                if (string.IsNullOrEmpty(s)) return null;
                                if (DateTime.TryParse(s, out var dt)) return dt;
                            }
                            catch { }
                            return null;
                        }

                        static int GetInt(object? v)
                        {
                            if (v == null) return 0;
                            if (v is int i) return i;
                            if (v is long l) return (int)l;
                            if (v is JsonElement je && je.ValueKind == JsonValueKind.Number)
                            {
                                if (je.TryGetInt32(out var iv)) return iv;
                                return (int)je.GetDouble();
                            }
                            if (int.TryParse(v.ToString(), out var parsed)) return parsed;
                            return 0;
                        }

                        static bool GetBool(object? v)
                        {
                            if (v == null) return false;
                            if (v is bool b) return b;
                            if (v is JsonElement je && (je.ValueKind == JsonValueKind.True || je.ValueKind == JsonValueKind.False)) return je.GetBoolean();
                            if (bool.TryParse(v.ToString(), out var parsed)) return parsed;
                            return false;
                        }

                        // Assign metadata from top-level document (if present)
                        structuredData.Id = GetString(document.GetValueOrDefault("id")) ?? structuredData.Id;
                        structuredData.TwinID = GetString(document.GetValueOrDefault("TwinID")) ?? structuredData.TwinID;
                        structuredData.DocumentType = GetString(document.GetValueOrDefault("documentType")) ?? structuredData.DocumentType;
                        structuredData.FileName = GetString(document.GetValueOrDefault("fileName")) ?? structuredData.FileName;
                        structuredData.FilePath = GetString(document.GetValueOrDefault("filePath")) ?? structuredData.FilePath;
                        structuredData.ContainerName = GetString(document.GetValueOrDefault("containerName")) ?? structuredData.ContainerName;
                        structuredData.SasUrl = GetString(document.GetValueOrDefault("sasUrl")) ?? structuredData.SasUrl;
                        structuredData.ProcessedAt = GetDateTime(document.GetValueOrDefault("processedAt")) ?? structuredData.ProcessedAt;
                        structuredData.CreatedAt = GetDateTime(document.GetValueOrDefault("createdAt")) ?? structuredData.CreatedAt;
                        structuredData.Success = GetBool(document.GetValueOrDefault("success"));

                        // Aggregate/summary fields: prefer explicit top-level fields, fallback to counts from nested resume
                        structuredData.TotalCertifications = GetInt(document.GetValueOrDefault("totalCertifications")) != 0
                            ? GetInt(document.GetValueOrDefault("totalCertifications"))
                            : (structuredData.Resume?.Certifications?.Count ?? 0);

                        structuredData.TotalProjects = GetInt(document.GetValueOrDefault("totalProjects")) != 0
                            ? GetInt(document.GetValueOrDefault("totalProjects"))
                            : (structuredData.Resume?.Projects?.Count ?? 0);

                        structuredData.TotalAwards = GetInt(document.GetValueOrDefault("totalAwards")) != 0
                            ? GetInt(document.GetValueOrDefault("totalAwards"))
                            : (structuredData.Resume?.Awards?.Count ?? 0);

                        structuredData.HasSalaryInfo = GetBool(document.GetValueOrDefault("hasSalaryInfo")) || (structuredData.Resume?.Salaries?.Any() ?? false);
                        structuredData.HasBenefitsInfo = GetBool(document.GetValueOrDefault("hasBenefitsInfo")) || (structuredData.Resume?.Benefits?.Any() ?? false);

                        structuredData.TotalAssociations = GetInt(document.GetValueOrDefault("totalAssociations")) != 0
                            ? GetInt(document.GetValueOrDefault("totalAssociations"))
                            : (structuredData.Resume?.ProfessionalAssociations?.Count ?? 0);

                        structuredData.Summary = GetString(document.GetValueOrDefault("summary")) ?? structuredData.Resume?.Summary ?? string.Empty;
                        structuredData.ExecutiveSummary = GetString(document.GetValueOrDefault("executiveSummary")) ?? structuredData.Resume?.ExecutiveSummary ?? string.Empty;

                        workDocuments.Add(structuredData);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error mapping work document to ResumeStructuredData for TwinId {TwinId}", twinId);
                    }
                }
            }

            _logger.LogInformation("💼 Found {Count} work documents for Twin ID: {TwinId}", workDocuments.Count, twinId);
            return workDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work documents for Twin ID: {TwinId}", twinId);
            throw; // or handle the exception as needed  
        }
    }

    // Your existing MapToResumeStructuredData method remains unchanged.  
    private ResumeStructuredData MapToResumeStructuredData(Dictionary<string, object?> resumeData)
    {
        var resumeStructuredData = new ResumeStructuredData();

        if (resumeData.TryGetValue("resume", out var resumeObj) && resumeObj is Dictionary<string, object?> resume)
        {
            resumeStructuredData.Resume.ExecutiveSummary = resume.GetValueOrDefault("executive_summary")?.ToString() ?? string.Empty;

            if (resume.TryGetValue("personal_information", out var personalInfoObj) && personalInfoObj is Dictionary<string, object?> personalInfo)
            {
                resumeStructuredData.Resume.PersonalInformation.FullName = personalInfo.GetValueOrDefault("full_name")?.ToString() ?? string.Empty;
                resumeStructuredData.Resume.PersonalInformation.Address = personalInfo.GetValueOrDefault("address")?.ToString() ?? string.Empty;
                resumeStructuredData.Resume.PersonalInformation.PhoneNumber = personalInfo.GetValueOrDefault("phone_number")?.ToString() ?? string.Empty;
                resumeStructuredData.Resume.PersonalInformation.Email = personalInfo.GetValueOrDefault("email")?.ToString() ?? string.Empty;
                resumeStructuredData.Resume.PersonalInformation.LinkedIn = personalInfo.GetValueOrDefault("linkedin")?.ToString() ?? string.Empty;
            }

            resumeStructuredData.Resume.Summary = resume.GetValueOrDefault("summary")?.ToString() ?? string.Empty;

            if (resume.TryGetValue("skills", out var skillsObj) && skillsObj is List<object> skills)
            {
                resumeStructuredData.Resume.Skills = skills.Select(skill => skill.ToString()).ToList();
            }

            if (resume.TryGetValue("education", out var educationObj) && educationObj is List<object> education)
            {
                resumeStructuredData.Resume.Education = education.Select(item =>
                {
                    var eduItem = item as Dictionary<string, object?>;
                    return new EducationItem
                    {
                        Degree = eduItem.GetValueOrDefault("degree")?.ToString() ?? string.Empty,
                        Institution = eduItem.GetValueOrDefault("institution")?.ToString() ?? string.Empty,
                        GraduationYear = Convert.ToInt32(eduItem.GetValueOrDefault("graduation_year") ?? 0),
                        Location = eduItem.GetValueOrDefault("location")?.ToString() ?? string.Empty
                    };
                }).ToList();
            }

            if (resume.TryGetValue("work_experience", out var workExperienceObj) && workExperienceObj is List<object> workExperience)
            {
                resumeStructuredData.Resume.WorkExperience = workExperience.Select(item =>
                {
                    var workItem = item as Dictionary<string, object?>;
                    return new WorkExperience
                    {
                        JobTitle = workItem.GetValueOrDefault("job_title")?.ToString() ?? string.Empty,
                        Company = workItem.GetValueOrDefault("company")?.ToString() ?? string.Empty,
                        Duration = workItem.GetValueOrDefault("duration")?.ToString() ?? string.Empty,
                        Responsibilities = workItem.GetValueOrDefault("responsibilities") is List<object> responsibilities
                            ? responsibilities.Select(r => r.ToString()).ToList() : new List<string>()
                    };
                }).ToList();
            }

            // Handle other fields like salaries, benefits, certifications, projects, awards, and professional associations here similarly.  
        }

        return resumeStructuredData;
    }

    public async Task<Dictionary<string, object?>?> GetWorkDocumentByIdAsync(string documentId, string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");
            
            var response = await workContainer.ReadItemAsync<Dictionary<string, object?>>(
                documentId,
                new PartitionKey(twinId)
            );
            
            var workDocument = response.Resource;
            _logger.LogInformation("💼 Work document retrieved successfully: {DocumentId} for Twin: {TwinId}", documentId, twinId);
            return workDocument;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get work document by ID {DocumentId} for Twin: {TwinId}", documentId, twinId);
            return null;
        }
    }

    public async Task<bool> DeleteWorkDocumentAsync(string documentId, string twinId)
    {
        try
        {
            var workContainer = _database.GetContainer("TwinWork");
            
            await workContainer.DeleteItemAsync<Dictionary<string, object?>>(
                documentId,
                new PartitionKey(twinId)
            );
            
            _logger.LogInformation("💼 Work document deleted successfully: {DocumentId} for Twin: {TwinId}", documentId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete work document: {DocumentId} for Twin: {TwinId}", documentId, twinId);
            return false;
        }
    }

    // Helper methods
    private static Dictionary<string, object?> ConvertObjectToDictionary(object? obj)
    {
        if (obj == null)
            return new Dictionary<string, object?>();

        if (obj is Dictionary<string, object?> dict)
            return dict;

        // Handle Newtonsoft JToken (JObject, JArray, JValue)
        if (obj is JToken jtoken)
        {
            var result = new Dictionary<string, object?>();
            if (jtoken is JObject jobj)
            {
                foreach (var prop in jobj.Properties())
                {
                    result[prop.Name] = JTokenToObject(prop.Value);
                }
            }
            else if (jtoken is JValue jval)
            {
                // Single value - return wrapper with value? We'll try to parse as empty dict
                return new Dictionary<string, object?> { ["value"] = jval.Value };
            }
            else if (jtoken is JArray jarr)
            {
                // Convert array into dictionary with index keys
                int i = 0;
                foreach (var item in jarr)
                {
                    result[i.ToString()] = JTokenToObject(item);
                    i++;
                }
            }

            return result;
        }
        
        if (obj is JsonElement jsonElement)
        {
            var dictionary = new Dictionary<string, object?>();

            foreach (var property in jsonElement.EnumerateObject())
            {
                dictionary[property.Name] = JsonElementToObject(property.Value);
            }

            return dictionary;
        }

        // Last resort: try to serialize and deserialize
        try
        {
            // Use Newtonsoft serializer to handle JObjects/JArrays and complex nested structures robustly
            var serialized = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
            var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object?>>(serialized, new Newtonsoft.Json.JsonSerializerSettings
            {
                DateParseHandling = Newtonsoft.Json.DateParseHandling.None
            });
            if (deserialized != null)
                return DeserializeNewtonsoftObjects(deserialized);
            return new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    // Recursively convert objects produced by Newtonsoft deserialization into Dictionary<string, object?> with nested dictionaries/lists
    private static Dictionary<string, object?> DeserializeNewtonsoftObjects(Dictionary<string, object?> input)
    {
        var output = new Dictionary<string, object?>();
        foreach (var kvp in input)
        {
            output[kvp.Key] = NormalizeNewtonsoftToken(kvp.Value);
        }
        return output;
    }

    private static object? NormalizeNewtonsoftToken(object? token)
    {
        if (token == null) return null;
        if (token is JObject jobj)
        {
            var dict = new Dictionary<string, object?>();
            foreach (var prop in jobj.Properties())
                dict[prop.Name] = JTokenToObject(prop.Value);
            return dict;
        }
        if (token is JArray jarr)
        {
            return jarr.Select(JTokenToObject).ToList();
        }
        if (token is JValue jval)
            return jval.Value;
        if (token is IEnumerable<object> list)
            return list.Select(NormalizeNewtonsoftToken).ToList();
        return token;
    }

    private static object? JTokenToObject(JToken token)
    {
        switch (token.Type)
        {
            case JTokenType.Object:
                var dict = new Dictionary<string, object?>();
                foreach (var prop in ((JObject)token).Properties())
                {
                    dict[prop.Name] = JTokenToObject(prop.Value);
                }
                return dict;
            case JTokenType.Array:
                return token.Select(JTokenToObject).ToList();
            case JTokenType.Integer:
                return token.Value<long>();
            case JTokenType.Float:
                return token.Value<double>();
            case JTokenType.String:
                return token.Value<string>();
            case JTokenType.Boolean:
                return token.Value<bool>();
            case JTokenType.Null:
                return null;
            case JTokenType.Date:
                return token.Value<DateTime>();
            default:
                return token.ToString();
        }
    }

    private static object? JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ConvertObjectToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToList(),
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Convert to object list handling various array formats from Cosmos DB
    /// </summary>
    private List<object> ConvertToObjectList(object? value)
    {
        if (value == null) return new List<object>();
        
        if (value is List<object> directList)
            return directList;
            
        if (value is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            return jsonElement.EnumerateArray().Select(JsonElementToObject).ToList();
        }
        
        if (value is IEnumerable<object> enumerable)
            return enumerable.ToList();
            
        if (value is System.Collections.IEnumerable nonGeneric)
            return nonGeneric.Cast<object>().ToList();
            
        return new List<object>();
    }

    /// <summary>
    /// Parse documents array from various formats (JsonElement, List, etc.)
    /// </summary>
    private List<Dictionary<string, object?>> ParseDocumentsArray(object documentsValue)
    {
        var documentsArray = new List<Dictionary<string, object?>>();

        try
        {
            if (documentsValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in jsonElement.EnumerateArray())
                {
                    var docDict = ConvertObjectToDictionary(element);
                    if (docDict.Any())
                    {
                        documentsArray.Add(docDict);
                    }
                }
            }
            else if (documentsValue is List<object> objectList)
            {
                foreach (var item in objectList)
                {
                    var docDict = ConvertObjectToDictionary(item);
                    if (docDict.Any())
                    {
                        documentsArray.Add(docDict);
                    }
                }
            }
            else if (documentsValue is IEnumerable<object> enumerable)
            {
                foreach (var item in enumerable)
                {
                    var docDict = ConvertObjectToDictionary(item);
                    if (docDict.Any())
                    {
                        documentsArray.Add(docDict);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error parsing documents array");
        }

        return documentsArray;
    }

    /// <summary>
    /// Get count of items in an array-like object
    /// </summary>
    private int GetArrayCount(object? arrayValue)
    {
        if (arrayValue == null) return 0;

        try
        {
            if (arrayValue is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
            {
                return jsonElement.GetArrayLength();
            }
            else if (arrayValue is System.Collections.ICollection collection)
            {
                return collection.Count;
            }
            else if (arrayValue is System.Collections.IEnumerable enumerable)
            {
                return enumerable.Cast<object>().Count();
            }
        }
        catch
        {
            // Silent fail for count operation
        }

        return 0;
    }

    /// <summary>
    /// Handle other dynamic arrays that might be added to education records in the future
    /// This provides a flexible way to handle new data structures without breaking existing code
    /// </summary>
    private void HandleDynamicArrays(Dictionary<string, object?> item, EducationData education)
    {
        try
        {
            // Handle certificates array if present
            if (item.TryGetValue("certificates", out var certificatesValue) && certificatesValue != null)
            {
                var certificatesCount = GetArrayCount(certificatesValue);
                if (certificatesCount > 0)
                {
                    _logger.LogInformation("🏆 Found {Count} certificates for education record: {Id}", 
                        certificatesCount, education.Id);
                }
            }

            // Handle transcripts array if present
            if (item.TryGetValue("transcripts", out var transcriptsValue) && transcriptsValue != null)
            {
                var transcriptsCount = GetArrayCount(transcriptsValue);
                if (transcriptsCount > 0)
                {
                    _logger.LogInformation("📊 Found {Count} transcripts for education record: {Id}", 
                        transcriptsCount, education.Id);
                }
            }

            // Handle courses array if present
            if (item.TryGetValue("courses", out var coursesValue) && coursesValue != null)
            {
                var coursesCount = GetArrayCount(coursesValue);
                if (coursesCount > 0)
                {
                    _logger.LogInformation("📚 Found {Count} courses for education record: {Id}", 
                        coursesCount, education.Id);
                }
            }

            // Handle any other arrays that might be added in the future
            var knownProperties = new HashSet<string>
            {
                "id", "TwinID", "CountryID", "institution", "education_type", "degree_obtained",
                "field_of_study", "start_date", "end_date", "in_progress", "country", "description",
                "achievements", "gpa", "credits", "createdDate", "type", "_rid", "_self", "_etag",
                "_attachments", "_ts", "updatedAt", "lastDocumentProcessed",
                "documents", "certificates", "transcripts", "courses" // Known arrays
            };

            foreach (var kvp in item)
            {
                if (!knownProperties.Contains(kvp.Key) && kvp.Value != null)
                {
                    // Check if this is an array-like property
                    if (IsArrayLikeProperty(kvp.Value))
                    {
                        var dynamicCount = GetArrayCount(kvp.Value);
                        if (dynamicCount > 0)
                        {
                            _logger.LogInformation("🔍 Found {Count} items in dynamic array '{PropertyName}' for education record: {Id}", 
                                dynamicCount, kvp.Key, education.Id);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Error handling dynamic arrays for education {Id}", education.Id);
        }
    }

    /// <summary>
    /// Check if a property value is array-like (JsonElement array, List, IEnumerable)
    /// </summary>
    private bool IsArrayLikeProperty(object value)
    {
        return value switch
        {
            JsonElement jsonElement => jsonElement.ValueKind == JsonValueKind.Array,
            System.Collections.ICollection => true,
            System.Collections.IEnumerable => true,
            _ => false
        };
    }
}

// Extension method for TwinProfileData.FromDict
public static class TwinProfileDataExtensions
{
    public static TwinProfileData FromDict(this TwinProfileData _, Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!) =>
            data.TryGetValue(key, out var value) && value != null 
                ? ConvertValue<T>(value, defaultValue) 
                : defaultValue;

        List<string> GetStringList(String key)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return new List<string>();

            if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
                return element.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToList();
            
            if (value is IEnumerable<object> enumerable)
                return enumerable.Select(item => item?.ToString() ?? string.Empty).ToList();

            return new List<string>();
        }

        static T ConvertValue<T>(object value, T defaultValue)
        {
            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(int))
                        return (T)(object)jsonElement.GetInt32();
                    if (type == typeof(bool))
                        return (T)(object)jsonElement.GetBoolean();
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return new TwinProfileData
        {
            TwinId = GetValue("twinId", GetValue("id", "")),
            TwinName = GetValue<string>("twinName"),
            FirstName = GetValue<string>("firstName"),
            LastName = GetValue<string>("lastName"),
            Email = GetValue<string>("email"),
            Phone = GetValue<string>("phone"),
            Address = GetValue<string>("address"),
            DateOfBirth = GetValue<string>("dateOfBirth"),
            BirthCountry = GetValue<string>("birthCountry"),
            BirthCity = GetValue<string>("birthCity"),
            Nationality = GetValue<string>("nationality"),
            Gender = GetValue<string>("gender"),
            Occupation = GetValue<string>("occupation"),
            Interests = GetStringList("interests"),
            Languages = GetStringList("languages"),
            CountryId = GetValue<string>("CountryID"),
            ProfilePhoto = GetValue<string?>("profilePhoto"),
            MiddleName = GetValue<string?>("middleName"),
            Nickname = GetValue<string?>("nickname"),
            City = GetValue<string?>("city"),
            State = GetValue<string?>("state"),
            Country = GetValue<string?>("country"),
            ZipCode = GetValue<string?>("zipCode"),
            MaritalStatus = GetValue<string?>("maritalStatus"),
            PersonalBio = GetValue<string?>("personalBio"),
            EmergencyContact = GetValue<string?>("emergencyContact"),
            EmergencyPhone = GetValue<string?>("emergencyPhone"),
            BloodType = GetValue<string?>("bloodType"),
            Height = GetValue<string?>("height"),
            Weight = GetValue<string?>("weight"),
            DocumentType = GetValue<string?>("documentType"),
            DocumentNumber = GetValue<string?>("documentNumber"),
            PassportNumber = GetValue<string?>("passportNumber"),
            SocialSecurityNumber = GetValue<string?>("socialSecurityNumber"),
            Website = GetValue<string?>("website"),
            LinkedIn = GetValue<string?>("linkedIn"),
            Facebook = GetValue<string?>("facebook"),
            Instagram = GetValue<string?>("instagram"),
            Twitter = GetValue<string?>("twitter"),
            Company = GetValue<string?>("company"),
            FamilyRelation = GetValue<string?>("familyRelation"),
            AccountManagement = GetValue<string?>("accountManagement"),
            PrivacyLevel = GetValue<string?>("privacyLevel"),
            AuthorizedEmails = GetStringList("authorizedEmails"),
            OwnerEmail = GetValue<string?>("ownerEmail")
        };
    }
}

public static class TwinProfileDataHelper
{
    public static TwinProfileData FromDict(Dictionary<string, object?> data)
    {
        return new TwinProfileData().FromDict(data);
    }
}

/// <summary>
/// Data class for education document information.
/// </summary>
public class EducationDocument
{
    public string DocumentId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; } = "";
    public string TextContent { get; set; } = string.Empty;
    public string HtmlContent { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;

    public string SASURL { get; set; } = string.Empty;

    public static EducationDocument FromDict(Dictionary<string, object?> data)
    {
        T GetValue<T>(string key, T defaultValue = default!)
        {
            if (!data.TryGetValue(key, out var value) || value == null)
                return defaultValue;

            try
            {
                if (value is T directValue)
                    return directValue;

                if (value is JsonElement jsonElement)
                {
                    var type = typeof(T);
                    if (type == typeof(string))
                        return (T)(object)(jsonElement.GetString() ?? string.Empty);
                    if (type == typeof(bool))
                        return (T)(object)jsonElement.GetBoolean();
                    if (type == typeof(DateTime))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        return new EducationDocument
        {
            DocumentId = GetValue<string>("documentId"),
            FileName = GetValue<string>("fileName"),
            FilePath = GetValue<string>("filePath"),
            ContainerName = GetValue<string>("containerName"),
            ProcessedAt = GetValue("processedAt", DateTime.UtcNow),
            Success = GetValue("success", false),
            ErrorMessage = GetValue<string?>("errorMessage"),
            TextContent = GetValue<string>("textContent"),
            HtmlContent = GetValue<string>("htmlContent"),
            DocumentType = GetValue<string>("documentType")
        };
    }
}