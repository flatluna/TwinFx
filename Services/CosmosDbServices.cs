using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;
using TwinFx.Agents;
using TwinFx.Models;
using TwinAgentsLibrary.Models;
using ContactData = TwinAgentsLibrary.Models.ContactData;

namespace TwinFx.Services;

/// <summary>
/// Data class for Twin profile information.
/// </summary>
 
/// <summary>
/// Data class for family information.
/// </summary> 
    /// Crear instancia desde Dictionary de Cosmos DB
    /// </summary>
   
 

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
        ;

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

                // Handle direct numeric values (when they come as raw numbers)
                var targetType = typeof(T);
                if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                {
                    if (decimal.TryParse(value.ToString(), out var decimalValue))
                        return (T)(object)decimalValue;
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
public class CosmosDbService
{
    private readonly ILogger<CosmosDbService> _logger;
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly CosmosDbSettings _cosmosSettings;
    private readonly AzureStorageSettings _storageSettings;

    public CosmosDbService(ILogger<CosmosDbService> logger, IOptions<CosmosDbSettings> cosmosOptions, IOptions<AzureStorageSettings> storageOptions = null)
    {
        _logger = logger;
        _cosmosSettings = cosmosOptions.Value;
        _storageSettings = storageOptions?.Value ?? new AzureStorageSettings();

        // Log configuration debug info usando configuraciones fuertemente tipadas
        _logger.LogInformation("🔧 Cosmos DB Configuration (Strongly Typed):");
        _logger.LogInformation($"   • Endpoint: {_cosmosSettings.Endpoint}");
        _logger.LogInformation($"   • Database Name: {_cosmosSettings.DatabaseName}");
        _logger.LogInformation($"   • Key Length: {_cosmosSettings.Key?.Length ?? 0} characters");
        _logger.LogInformation($"   • Key Present: {!string.IsNullOrEmpty(_cosmosSettings.Key)}");

        if (string.IsNullOrEmpty(_cosmosSettings.Key))
        {
            _logger.LogError("❌ COSMOS_KEY is required but not found in configuration.");
            _logger.LogError("💡 Check that CosmosDbSettings is properly configured in settings.json");
            throw new InvalidOperationException("COSMOS_KEY is required but not found in configuration.");
        }

        if (string.IsNullOrEmpty(_cosmosSettings.Endpoint))
        {
            _logger.LogError("❌ COSMOS_ENDPOINT is required but not found in configuration.");
            throw new InvalidOperationException("COSMOS_ENDPOINT is required but not found in configuration.");
        }
        
        try
        {
            _client = new CosmosClient(_cosmosSettings.Endpoint, _cosmosSettings.Key);
            _database = _client.GetDatabase(_cosmosSettings.DatabaseName);
            
            _logger.LogInformation("✅ Cosmos DB Twin Profile Service initialized successfully");
            _logger.LogInformation($"   • Endpoint: {_cosmosSettings.Endpoint}");
            _logger.LogInformation($"   • Database: {_cosmosSettings.DatabaseName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to initialize Cosmos DB client");
            throw;
        }
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
                educationRecords.AddRange(page.Resource);
            }

            // Generate SAS URLs for each education record
            foreach (var education in educationRecords)
            {
                try
                {
                    foreach (var docu in education.Documents)
                    {
                        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
                        var storageOptions = Microsoft.Extensions.Options.Options.Create(_storageSettings);
                        var dataLakeFactory = new DataLakeClientFactory(loggerFactory, storageOptions);
                        var dataLakeClient = dataLakeFactory.CreateClient(education.TwinID);

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
                        var contact = TwinAgentsLibrary.Models.ContactData.FromDict(item);
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
                ["country"] = photoDocument.Country,
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
                            invoiceDocument.id, invoiceDocument.InvoiceData.LineItems.Count);
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

            if (sqlQuery.Contains("li.Description") || sqlQuery.Contains("li.Amount") || sqlQuery.Contains("li.Quantity"))
            {
                fullQuery = $"SELECT DISTINCT c FROM c JOIN li IN c.invoiceData.LineItems WHERE {sqlQuery} ORDER BY c.createdAt DESC";
                isJoinQuery = true;
                _logger.LogInformation("🔍 Using JOIN syntax for LineItems query: {Query}", fullQuery);
            }
            else
            {
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

                        if (isJoinQuery && item.ContainsKey("c"))
                        {
                            _logger.LogInformation("🔗 JOIN query detected: extracting document from 'c' key");

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
                            documentData = item;
                        }

                        var invoiceDocument = InvoiceDocument.FromCosmosDocument(documentData);
                        invoiceDocuments.Add(invoiceDocument);

                        _logger.LogInformation("📄 Parsed InvoiceDocument: {Id} with {LineItemsCount} line items",
                            invoiceDocument.id, invoiceDocument.InvoiceData.LineItems.Count);
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

    // Food methods - AGREGADOS
    public async Task<bool> CreateFoodAsync(FoodData foodData)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            var foodDict = foodData.ToDict();
            await foodContainer.CreateItemAsync(foodDict, new PartitionKey(foodData.TwinID));

            _logger.LogInformation("🥗 Food created successfully: {NombreAlimento} ({Categoria}) for Twin: {TwinID}",
                foodData.NombreAlimento, foodData.Categoria, foodData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create food: {NombreAlimento} ({Categoria}) for Twin: {TwinID}",
                foodData.NombreAlimento, foodData.Categoria, foodData.TwinID);
            return false;
        }
    }

    public async Task<List<FoodData>> GetFoodsByTwinIdAsync(string twinId)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            var query = new QueryDefinition("SELECT * FROM c WHERE c.TwinID = @twinId ORDER BY c.fechaCreacion DESC")
                .WithParameter("@twinId", twinId);

            var iterator = foodContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var foods = new List<FoodData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var food = FoodData.FromDict(item);
                        foods.Add(food);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to FoodData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("🥗 Found {Count} foods for Twin ID: {TwinId}", foods.Count, twinId);
            return foods;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get foods for Twin ID: {TwinId}", twinId);
            return new List<FoodData>();
        }
    }

    public async Task<FoodData?> GetFoodByIdAsync(string foodId, string twinId)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            var response = await foodContainer.ReadItemAsync<Dictionary<string, object?>>(
                foodId,
                new PartitionKey(twinId)
            );

            var food = FoodData.FromDict(response.Resource);
            _logger.LogInformation("🥗 Food retrieved successfully: {NombreAlimento} ({Categoria})",
                food.NombreAlimento, food.Categoria);
            return food;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get food by ID {FoodId} for Twin: {TwinId}", foodId, twinId);
            return null;
        }
    }

    public async Task<bool> UpdateFoodAsync(FoodData foodData)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            var foodDict = foodData.ToDict();
            foodDict["fechaActualizacion"] = DateTime.UtcNow.ToString("O");

            await foodContainer.UpsertItemAsync(foodDict, new PartitionKey(foodData.TwinID));

            _logger.LogInformation("🥗 Food updated successfully: {NombreAlimento} ({Categoria}) for Twin: {TwinID}",
                foodData.NombreAlimento, foodData.Categoria, foodData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to update food: {Id} for Twin: {TwinID}",
                foodData.Id, foodData.TwinID);
            return false;
        }
    }

    public async Task<bool> DeleteFoodAsync(string foodId, string twinId)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            await foodContainer.DeleteItemAsync<Dictionary<string, object?>>(
                foodId,
                new PartitionKey(twinId)
            );

            _logger.LogInformation("🥗 Food deleted successfully: {FoodId} for Twin: {TwinId}", foodId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to delete food: {FoodId} for Twin: {TwinId}", foodId, twinId);
            return false;
        }
    }

    public async Task<List<FoodData>> GetFilteredFoodsAsync(string twinId, FoodQuery query)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            // Build dynamic SQL query based on filters
            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (!string.IsNullOrEmpty(query.Categoria))
            {
                conditions.Add("CONTAINS(LOWER(c.categoria), LOWER(@categoria))");
                parameters["@categoria"] = query.Categoria;
            }

            if (!string.IsNullOrEmpty(query.NombreContiene))
            {
                conditions.Add("CONTAINS(LOWER(c.nombreAlimento), LOWER(@nombre))");
                parameters["@nombre"] = query.NombreContiene;
            }

            if (query.CaloriasMin.HasValue)
            {
                conditions.Add("c.caloriasPor100g >= @caloriasMin");
                parameters["@caloriasMin"] = query.CaloriasMin.Value;
            }

            if (query.CaloriasMax.HasValue)
            {
                conditions.Add("c.caloriasPor100g <= @caloriasMax");
                parameters["@caloriasMax"] = query.CaloriasMax.Value;
            }

            // Build ORDER BY clause
            var orderBy = query.OrderBy?.ToLowerInvariant() switch
            {
                "categoria" => "c.categoria",
                "calorias" or "calorias100g" or "caloriaspor100g" => "c.caloriasPor100g",
                "fechacreacion" => "c.fechaCreacion",
                _ => "c.nombreAlimento"
            };

            var orderDirection = query.OrderDirection?.ToUpperInvariant() == "DESC" ? "DESC" : "ASC";

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY {orderBy} {orderDirection}";

            // Add pagination with OFFSET/LIMIT if supported
            if (query.Page > 1 || query.PageSize < 1000)
            {
                var offset = (query.Page - 1) * query.PageSize;
                sql += $" OFFSET {offset} LIMIT {query.PageSize}";
            }

            var cosmosQuery = new QueryDefinition(sql);
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = foodContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var foods = new List<FoodData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var food = FoodData.FromDict(item);
                        foods.Add(food);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to FoodData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("🥗 Found {Count} filtered foods for Twin ID: {TwinId}", foods.Count, twinId);
            return foods;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get filtered foods for Twin ID: {TwinId}", twinId);
            return new List<FoodData>();
        }
    }

    public async Task<FoodStats> GetFoodStatsAsync(string twinId)
    {
        try
        {
            var foods = await GetFoodsByTwinIdAsync(twinId);

            if (!foods.Any())
            {
                return new FoodStats
                {
                    TotalAlimentos = 0,
                    AlimentosPorCategoria = new Dictionary<string, int>(),
                    CategoriaConMasAlimentos = "",
                    AlimentoConMasCalorias = "",
                    MaxCalorias = 0
                };
            }

            var stats = new FoodStats
            {
                TotalAlimentos = foods.Count,
                PromedioCaloriasPor100g = foods.Average(f => f.CaloriasPor100g),
                TotalProteinas = foods.Sum(f => f.Proteinas ?? 0),
                TotalCarbohidratos = foods.Sum(f => f.Carbohidratos ?? 0),
                TotalGrasas = foods.Sum(f => f.Grasas ?? 0),
                TotalFibra = foods.Sum(f => f.Fibra ?? 0),
                AlimentosPorCategoria = foods.GroupBy(f => f.Categoria)
                                            .ToDictionary(g => g.Key, g => g.Count()),
                CategoriaConMasAlimentos = foods.GroupBy(f => f.Categoria)
                                               .OrderByDescending(g => g.Count())
                                               .FirstOrDefault()?.Key ?? "",
                AlimentoConMasCalorias = foods.OrderByDescending(f => f.CaloriasPor100g)
                                              .FirstOrDefault()?.NombreAlimento ?? "",
                MaxCalorias = foods.Max(f => f.CaloriasPor100g)
            };

            _logger.LogInformation("📊 Generated food statistics for Twin ID: {TwinId} - {TotalAlimentos} foods analyzed",
                twinId, stats.TotalAlimentos);
            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get food statistics for Twin ID: {TwinId}", twinId);
            return new FoodStats
            {
                TotalAlimentos = 0,
                AlimentosPorCategoria = new Dictionary<string, int>(),
                CategoriaConMasAlimentos = "",
                AlimentoConMasCalorias = "",
                MaxCalorias = 0
            };
        }
    }

    public async Task<List<FoodData>> SearchFoodsByNameAsync(string twinId, string searchTerm, int limit = 20)
    {
        try
        {
            var foodContainer = _database.GetContainer("TwinAlimentos");

            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.TwinID = @twinId AND CONTAINS(LOWER(c.nombreAlimento), LOWER(@searchTerm)) ORDER BY c.nombreAlimento")
                .WithParameter("@twinId", twinId)
                .WithParameter("@searchTerm", searchTerm);

            var iterator = foodContainer.GetItemQueryIterator<Dictionary<string, object?>>(
                query,
                requestOptions: new QueryRequestOptions { MaxItemCount = limit }
            );

            var foods = new List<FoodData>();
            var itemsProcessed = 0;

            while (iterator.HasMoreResults && itemsProcessed < limit)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    if (itemsProcessed >= limit) break;

                    try
                    {
                        var food = FoodData.FromDict(item);
                        foods.Add(food);
                        itemsProcessed++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to FoodData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("🔍 Found {Count} foods matching '{SearchTerm}' for Twin ID: {TwinId}",
                foods.Count, searchTerm, twinId);
            return foods;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to search foods for Twin ID: {TwinId}", twinId);
            return new List<FoodData>();
        }
    }

    // Job Opportunity methods - AGREGADOS UNO POR UNO
    public async Task<bool> CreateJobOpportunityAsync(TwinFx.Models.JobOpportunityData jobOpportunity)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var jobDict = jobOpportunity.ToDict();
            await jobContainer.CreateItemAsync(jobDict, new PartitionKey(jobOpportunity.TwinID));

            _logger.LogInformation("💼 Job opportunity created successfully: {Puesto} at {Empresa} for Twin: {TwinID}",
                jobOpportunity.Puesto, jobOpportunity.Empresa, jobOpportunity.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create job opportunity: {Puesto} at {Empresa} for Twin: {TwinID}",
                jobOpportunity.Puesto, jobOpportunity.Empresa, jobOpportunity.TwinID);
            return false;
        }
    }

    public async Task<List<TwinFx.Models.JobOpportunityData>> GetJobOpportunitiesByTwinIdAsync(string twinId, TwinFx.Models.JobOpportunityQuery query)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            // Build dynamic SQL query based on filters
            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (query.Estado.HasValue)
            {
                conditions.Add("c.estado = @estado");
                parameters["@estado"] = query.Estado.Value.ToString();
            }

            if (!string.IsNullOrEmpty(query.Empresa))
            {
                conditions.Add("CONTAINS(LOWER(c.empresa), LOWER(@empresa))");
                parameters["@empresa"] = query.Empresa;
            }

            if (!string.IsNullOrEmpty(query.Puesto))
            {
                conditions.Add("CONTAINS(LOWER(c.puesto), LOWER(@puesto))");
                parameters["@puesto"] = query.Puesto;
            }

            if (!string.IsNullOrEmpty(query.Ubicacion))
            {
                conditions.Add("CONTAINS(LOWER(c.ubicacion), LOWER(@ubicacion))");
                parameters["@ubicacion"] = query.Ubicacion;
            }

            if (query.FechaDesde.HasValue)
            {
                conditions.Add("c.fechaAplicacion >= @fechaDesde");
                parameters["@fechaDesde"] = query.FechaDesde.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            if (query.FechaHasta.HasValue)
            {
                conditions.Add("c.fechaAplicacion <= @fechaHasta");
                parameters["@fechaHasta"] = query.FechaHasta.Value.ToString("yyyy-MM-ddTHH:mm:ss");
            }

            // Build ORDER BY clause
            var orderBy = query.SortBy?.ToLowerInvariant() switch
            {
                "empresa" => "c.empresa",
                "puesto" => "c.puesto",
                "fechaaplicacion" => "c.fechaAplicacion",
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

            var iterator = jobContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var jobOpportunities = new List<TwinFx.Models.JobOpportunityData>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var jobOpportunity = TwinFx.Models.JobOpportunityData.FromDict(item);
                        jobOpportunities.Add(jobOpportunity);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to JobOpportunityData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("💼 Found {Count} job opportunities for Twin ID: {TwinId}", jobOpportunities.Count, twinId);
            return jobOpportunities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get job opportunities for Twin ID: {TwinId}", twinId);
            return new List<TwinFx.Models.JobOpportunityData>();
        }
    }

    public async Task<TwinFx.Models.JobOpportunityData?> GetJobOpportunityByIdAsync(string jobId, string twinId)
    {
        try
        {
            var jobContainer = _database.GetContainer("TwinJobOpportunities");

            var response = await jobContainer.ReadItemAsync<Dictionary<string, object?>>(
                jobId,
                new PartitionKey(twinId)
            );

            var jobOpportunity = TwinFx.Models.JobOpportunityData.FromDict(response.Resource);
            _logger.LogInformation("💼 Job opportunity retrieved successfully: {Puesto} at {Empresa}",
                jobOpportunity.Puesto, jobOpportunity.Empresa);
            return jobOpportunity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get job opportunity by ID {JobId} for Twin: {TwinId}", jobId, twinId);
            return null;
        }
    }

    /// <summary>
    /// Save education document analysis to TwinEducation container by updating existing education record
    /// </summary>
    /// <param name="educationResult">Education analysis result from Document Intelligence</param>
    /// <param name="educationId">Education record ID to update</param>
    /// <param name="containerName">Container name (for reference)</param>
    /// <param name="fileName">File name (for reference)</param>
    /// <returns>True if saved successfully</returns>
    public async Task<bool> SaveEducationAnalysisAsync(DocumentExtractionResult educationResult, string educationId, string containerName, string fileName, string filePath)
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
                ["processedAt"] = DateTime.UtcNow.ToString("O"),
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

    /// <summary>
    /// Helper method to convert various object types to Dictionary for robust parsing
    /// </summary>
    private static Dictionary<string, object?> ConvertObjectToDictionary(object? obj)
    {
        if (obj == null)
            return new Dictionary<string, object?>();

        if (obj is Dictionary<string, object?> dict)
            return dict;

        if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var property in jsonElement.EnumerateObject())
            {
                dictionary[property.Name] = JsonElementToObject(property.Value);
            }
            return dictionary;
        }

        // Handle Newtonsoft.Json JObject
        if (obj is Newtonsoft.Json.Linq.JObject jObject)
        {
            var dictionary = new Dictionary<string, object?>();
            foreach (var property in jObject.Properties())
            {
                dictionary[property.Name] = JTokenToObject(property.Value);
            }
            return dictionary;
        }

        // Try to convert using System.Text.Json serialization as fallback
        try
        {
            var serialized = JsonSerializer.Serialize(obj);
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized);
            return deserialized ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    /// <summary>
    /// Helper method to convert JsonElement to appropriate object type
    /// </summary>
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
    /// Helper method to convert Newtonsoft.Json JToken to appropriate object type
    /// </summary>
    private static object? JTokenToObject(Newtonsoft.Json.Linq.JToken token)
    {
        return token.Type switch
        {
            Newtonsoft.Json.Linq.JTokenType.String => token.Value<string>(),
            Newtonsoft.Json.Linq.JTokenType.Integer => token.Value<long>(),
            Newtonsoft.Json.Linq.JTokenType.Float => token.Value<decimal>(),
            Newtonsoft.Json.Linq.JTokenType.Boolean => token.Value<bool>(),
            Newtonsoft.Json.Linq.JTokenType.Null => null,
            Newtonsoft.Json.Linq.JTokenType.Object => ConvertObjectToDictionary(token),
            Newtonsoft.Json.Linq.JTokenType.Array => token.Children().Select(JTokenToObject).ToList(),
            _ => token.ToString()
        };
    }

    /// <summary>
    /// Helper method to convert various object types to List for robust parsing
    /// </summary>
    private static List<object> ConvertToObjectList(object? obj)
    {
        if (obj == null)
            return new List<object>();

        if (obj is List<object> list)
            return list;

        if (obj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Array)
        {
            return jsonElement.EnumerateArray().Select(JsonElementToObject).Where(x => x != null).Cast<object>().ToList();
        }

        if (obj is Newtonsoft.Json.Linq.JArray jArray)
        {
            return jArray.Children().Select(JTokenToObject).Where(x => x != null).Cast<object>().ToList();
        }

        if (obj is IEnumerable<object> enumerable)
            return enumerable.ToList();

        // Try to convert using System.Text.Json serialization as fallback
        try
        {
            var serialized = JsonSerializer.Serialize(obj);
            var deserialized = JsonSerializer.Deserialize<List<object>>(serialized);
            return deserialized ?? new List<object>();
        }
        catch
        {
            return new List<object>();
        }
    }

    // Travel Document methods
    public async Task<bool> SaveTravelDocumentAsync(TravelDocument travelDocument)
    {
        try
        {
            var travelDocumentsContainer = _database.GetContainer("TwinTravelDocuments");

            var documentDict = new Dictionary<string, object?>
            {
                ["id"] = travelDocument.Id,
                ["titulo"] = travelDocument.Titulo,
                ["descripcion"] = travelDocument.Descripcion,
                ["fileName"] = travelDocument.FileName,
                ["filePath"] = travelDocument.FilePath,
                ["documentType"] = travelDocument.DocumentType.ToString(),
                ["establishmentType"] = travelDocument.EstablishmentType.ToString(),
                ["vendorName"] = travelDocument.VendorName,
                ["vendorAddress"] = travelDocument.VendorAddress,
                ["documentDate"] = travelDocument.DocumentDate?.ToString("O"),
                ["totalAmount"] = travelDocument.TotalAmount,
                ["currency"] = travelDocument.Currency,
                ["taxAmount"] = travelDocument.TaxAmount,
                ["items"] = travelDocument.Items.Select(item => new Dictionary<string, object?>
                {
                    ["description"] = item.Description,
                    ["quantity"] = item.Quantity,
                    ["unitPrice"] = item.UnitPrice,
                    ["totalAmount"] = item.TotalAmount,
                    ["category"] = item.Category
                }).ToList(),
                ["extractedText"] = travelDocument.ExtractedText,
                ["htmlContent"] = travelDocument.HtmlContent,
                ["aiSummary"] = travelDocument.AiSummary,
                ["travelId"] = travelDocument.TravelId,
                ["itineraryId"] = travelDocument.ItineraryId,
                ["activityId"] = travelDocument.ActivityId,
                ["fileSize"] = travelDocument.FileSize,
                ["mimeType"] = travelDocument.MimeType,
                ["documentUrl"] = travelDocument.DocumentUrl,
                ["createdAt"] = travelDocument.CreatedAt.ToString("O"),
                ["updatedAt"] = travelDocument.UpdatedAt.ToString("O"),
                ["TwinID"] = travelDocument.TwinId,
                ["docType"] = travelDocument.DocType
            };

            await travelDocumentsContainer.UpsertItemAsync(documentDict, new PartitionKey(travelDocument.TwinId));

            _logger.LogInformation("📄 Travel document saved successfully: {Titulo} ({EstablishmentType}) for Twin: {TwinId}",
                travelDocument.Titulo, travelDocument.EstablishmentType, travelDocument.TwinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to save travel document: {Titulo} for Twin: {TwinId}",
                travelDocument.Titulo, travelDocument.TwinId);
            return false;
        }
    }

    public async Task<List<TravelDocument>> GetTravelDocumentsAsync(string twinId, string? travelId = null, string? documentType = null, string? establishmentType = null)
    {
        try
        {
            var travelDocumentsContainer = _database.GetContainer("TwinTravelDocuments");

            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (!string.IsNullOrEmpty(travelId))
            {
                conditions.Add("c.travelId = @travelId");
                parameters["@travelId"] = travelId;
            }

            if (!string.IsNullOrEmpty(documentType))
            {
                conditions.Add("c.documentType = @documentType");
                parameters["@documentType"] = documentType;
            }

            if (!string.IsNullOrEmpty(establishmentType))
            {
                conditions.Add("c.establishmentType = @establishmentType");
                parameters["@establishmentType"] = establishmentType;
            }

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.createdAt DESC";
            var cosmosQuery = new QueryDefinition(sql);
            
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = travelDocumentsContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var documents = new List<TravelDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var document = TravelDocumentFromDict(item);
                        documents.Add(document);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to TravelDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("📄 Found {Count} travel documents for Twin ID: {TwinId}", documents.Count, twinId);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get travel documents for Twin ID: {TwinId}", twinId);
            return new List<TravelDocument>();
        }
    }

    /// <summary>
    /// Get travel documents by activity ID
    /// </summary>
    /// <param name="twinId">Twin ID (partition key)</param>
    /// <param name="activityId">Activity ID to filter by</param>
    /// <returns>List of travel documents for the specified activity</returns>
    public async Task<List<TravelDocument>> GetTravelDocumentsByActivityIdAsync(string twinId, string activityId)
    {
        try
        {
            var travelDocumentsContainer = _database.GetContainer("TwinTravelDocuments");

            var sql = "SELECT * FROM c WHERE c.TwinID = @twinId AND c.activityId = @activityId ORDER BY c.createdAt DESC";
            var query = new QueryDefinition(sql)
                .WithParameter("@twinId", twinId)
                .WithParameter("@activityId", activityId);

            var iterator = travelDocumentsContainer.GetItemQueryIterator<Dictionary<string, object?>>(query);
            var documents = new List<TravelDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var document = TravelDocumentFromDict(item);
                        documents.Add(document);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to TravelDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("📄 Found {Count} travel documents for Activity ID: {ActivityId}, Twin ID: {TwinId}", 
                documents.Count, activityId, twinId);
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get travel documents for Activity ID: {ActivityId}, Twin ID: {TwinId}", 
                activityId, twinId);
            return new List<TravelDocument>();
        }
    }

    /// <summary>
    /// Get travel documents by multiple criteria with more flexible filtering
    /// </summary>
    /// <param name="twinId">Twin ID (partition key)</param>
    /// <param name="travelId">Optional travel ID</param>
    /// <param name="itineraryId">Optional itinerary ID</param>
    /// <param name="activityId">Optional activity ID</param>
    /// <param name="documentType">Optional document type</param>
    /// <param name="establishmentType">Optional establishment type</param>
    /// <returns>List of travel documents matching the criteria</returns>
    public async Task<List<TravelDocument>> GetTravelDocumentsByCriteriaAsync(
        string twinId, 
        string? travelId = null, 
        string? itineraryId = null, 
        string? activityId = null, 
        string? documentType = null, 
        string? establishmentType = null)
    {
        try
        {
            var travelDocumentsContainer = _database.GetContainer("TwinTravelDocuments");

            var conditions = new List<string> { "c.TwinID = @twinId" };
            var parameters = new Dictionary<string, object> { ["@twinId"] = twinId };

            if (!string.IsNullOrEmpty(travelId))
            {
                conditions.Add("c.travelId = @travelId");
                parameters["@travelId"] = travelId;
            }

            if (!string.IsNullOrEmpty(itineraryId))
            {
                conditions.Add("c.itineraryId = @itineraryId");
                parameters["@itineraryId"] = itineraryId;
            }

            if (!string.IsNullOrEmpty(activityId))
            {
                conditions.Add("c.activityId = @activityId");
                parameters["@activityId"] = activityId;
            }

            if (!string.IsNullOrEmpty(documentType))
            {
                conditions.Add("c.documentType = @documentType");
                parameters["@documentType"] = documentType;
            }

            if (!string.IsNullOrEmpty(establishmentType))
            {
                conditions.Add("c.establishmentType = @establishmentType");
                parameters["@establishmentType"] = establishmentType;
            }

            var sql = $"SELECT * FROM c WHERE {string.Join(" AND ", conditions)} ORDER BY c.createdAt DESC";
            var cosmosQuery = new QueryDefinition(sql);
            
            foreach (var param in parameters)
            {
                cosmosQuery = cosmosQuery.WithParameter($"@{param.Key.TrimStart('@')}", param.Value);
            }

            var iterator = travelDocumentsContainer.GetItemQueryIterator<Dictionary<string, object?>>(cosmosQuery);
            var documents = new List<TravelDocument>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                foreach (var item in response)
                {
                    try
                    {
                        var document = TravelDocumentFromDict(item);
                        documents.Add(document);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "⚠️ Error converting document to TravelDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            var filterSummary = new List<string>();
            if (!string.IsNullOrEmpty(travelId)) filterSummary.Add($"TravelId: {travelId}");
            if (!string.IsNullOrEmpty(itineraryId)) filterSummary.Add($"ItineraryId: {itineraryId}");
            if (!string.IsNullOrEmpty(activityId)) filterSummary.Add($"ActivityId: {activityId}");
            if (!string.IsNullOrEmpty(documentType)) filterSummary.Add($"DocumentType: {documentType}");
            if (!string.IsNullOrEmpty(establishmentType)) filterSummary.Add($"EstablishmentType: {establishmentType}");

            _logger.LogInformation("📄 Found {Count} travel documents for Twin ID: {TwinId} with filters: [{Filters}]", 
                documents.Count, twinId, string.Join(", ", filterSummary));
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get travel documents by criteria for Twin ID: {TwinId}", twinId);
            return new List<TravelDocument>();
        }
    }

    private static TravelDocument TravelDocumentFromDict(Dictionary<string, object?> data)
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
                    if (type == typeof(long))
                        return (T)(object)jsonElement.GetInt64();
                    if (type == typeof(decimal) || type == typeof(decimal?))
                    {
                        // Try multiple approaches to get decimal value
                        if (jsonElement.TryGetDecimal(out var decValue))
                            return (T)(object)decValue;
                        if (jsonElement.TryGetDouble(out var doubleValue))
                            return (T)(object)(decimal)doubleValue;
                        if (jsonElement.TryGetInt64(out var longValue))
                            return (T)(object)(decimal)longValue;
                        if (jsonElement.TryGetInt32(out var intValue))
                            return (T)(object)(decimal)intValue;
                        
                        // Try parsing string value
                        var stringValue = jsonElement.GetString();
                        if (!string.IsNullOrEmpty(stringValue) && decimal.TryParse(stringValue, out var parsedDecimal))
                            return (T)(object)parsedDecimal;
                            
                        return defaultValue;
                    }
                    if (type == typeof(DateTime) || type == typeof(DateTime?))
                    {
                        if (DateTime.TryParse(jsonElement.GetString(), out var dateTime))
                            return (T)(object)dateTime;
                        return defaultValue;
                    }
                }

                // Handle direct numeric values (when they come as raw numbers)
                var targetType = typeof(T);
                if (targetType == typeof(decimal) || targetType == typeof(decimal?))
                {
                    if (decimal.TryParse(value.ToString(), out var decimalValue))
                        return (T)(object)decimalValue;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        };

        // Parse items
        var items = new List<TravelDocumentItem>();
        if (data.TryGetValue("items", out var itemsValue) && itemsValue != null)
        {
            try
            {
                if (itemsValue is JsonElement itemsElement && itemsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemElement in itemsElement.EnumerateArray())
                    {
                        var item = new TravelDocumentItem
                        {
                            Description = itemElement.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                            Quantity = itemElement.TryGetProperty("quantity", out var qty) ? qty.GetDecimal() : 1,
                            UnitPrice = itemElement.TryGetProperty("unitPrice", out var price) ? price.GetDecimal() : null,
                            TotalAmount = itemElement.TryGetProperty("totalAmount", out var total) ? total.GetDecimal() : null,
                            Category = itemElement.TryGetProperty("category", out var cat) ? cat.GetString() : null
                        };
                        items.Add(item);
                    }
                }
            }
            catch (Exception)
            {
                // Ignore parsing errors for items
            }
        }

        return new TravelDocument
        {
            Id = GetValue<string>("id"),
            Titulo = GetValue<string>("titulo"),
            Descripcion = GetValue<string>("descripcion"),
            FileName = GetValue<string>("fileName"),
            FilePath = GetValue<string>("filePath"),
            DocumentType = Enum.TryParse<TravelDocumentType>(GetValue<string>("documentType"), out var docType) ? docType : TravelDocumentType.Receipt,
            EstablishmentType = Enum.TryParse<EstablishmentType>(GetValue<string>("establishmentType"), out var estType) ? estType : EstablishmentType.Restaurant,
            VendorName = GetValue<string>("vendorName"),
            VendorAddress = GetValue<string>("vendorAddress"),
            DocumentDate = GetValue<DateTime?>("documentDate"),
            TotalAmount = GetValue<decimal?>("totalAmount"),
            Currency = GetValue<string>("currency"),
            TaxAmount = GetValue<decimal?>("taxAmount"),
            Items = items,
            ExtractedText = GetValue<string>("extractedText"),
            HtmlContent = GetValue<string>("htmlContent"),
            AiSummary = GetValue<string>("aiSummary"),
            TravelId = GetValue<string>("travelId"),
            ItineraryId = GetValue<string>("itineraryId"),
            ActivityId = GetValue<string>("activityId"),
            FileSize = GetValue<long>("fileSize"),
            MimeType = GetValue<string>("mimeType"),
            DocumentUrl = GetValue<string>("documentUrl"),
            CreatedAt = GetValue<DateTime>("createdAt"),
            UpdatedAt = GetValue<DateTime>("updatedAt"),
            TwinId = GetValue<string>("TwinID"), // Note: Changed from "twinId" to "TwinID" to match Cosmos DB field
            DocType = GetValue("docType", "travelDocument")
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

        List<string> GetStringList(string key)
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
            SubscriptionId = GetValue<string>("subscriptionID"),
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
            CountryID = GetValue<string>("CountryID"),
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