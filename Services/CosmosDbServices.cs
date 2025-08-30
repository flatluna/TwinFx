using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using TwinFx.Models;

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
        }

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
/// Data class for education information.
/// </summary>
public class EducationData
{
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("twinId")]
    public string TwinID { get; set; } = string.Empty;
    
    [JsonPropertyName("countryId")] 
    public string CountryID { get; set; } = string.Empty;
    
    [JsonPropertyName("institution")]
    public string Institution { get; set; } = string.Empty;
    
    [JsonPropertyName("education_type")]
    public string EducationType { get; set; } = string.Empty;
    
    [JsonPropertyName("degree_obtained")]
    public string DegreeObtained { get; set; } = string.Empty;
    
    [JsonPropertyName("field_of_study")]
    public string FieldOfStudy { get; set; } = string.Empty;
    
    [JsonPropertyName("start_date")]
    public string StartDate { get; set; } = string.Empty;
    
    [JsonPropertyName("end_date")]
    public string EndDate { get; set; } = string.Empty;
    
    [JsonPropertyName("in_progress")]
    public bool InProgress { get; set; } = false;
    
    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
    
    [JsonPropertyName("achievements")]
    public string Achievements { get; set; } = string.Empty;
    
    [JsonPropertyName("gpa")]
    public string Gpa { get; set; } = string.Empty;
    
    [JsonPropertyName("credits")]
    public int Credits { get; set; } = 0;
    
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public string Type { get; set; } = "education";

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

    public CosmosDbTwinProfileService(ILogger<CosmosDbTwinProfileService> logger, IConfiguration configuration)
    {
        _logger = logger;

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
        
        _logger.LogInformation("? Cosmos DB Twin Profile Service initialized successfully");
    }

    // Education methods
    public async Task<bool> CreateEducationAsync(EducationData educationData)
    {
        try
        {
            var educationContainer = _database.GetContainer("TwinEducation");
            var educationDict = educationData.ToDict();
            await educationContainer.CreateItemAsync(educationDict, new PartitionKey(educationData.CountryID));
            
            _logger.LogInformation("?? Education record created successfully: {Institution} {EducationType} for Twin: {TwinID}", 
                educationData.Institution, educationData.EducationType, educationData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to create education record: {Institution} {EducationType} for Twin: {TwinID}", 
                educationData.Institution, educationData.EducationType, educationData.TwinID);
            return false;
        }
    }

    public async Task<List<EducationData>> GetEducationsByTwinIdAsync(string twinId)
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
                        var education = EducationData.FromDict(item);
                        educationRecords.Add(education);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to EducationData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} education records for Twin ID: {TwinId}", educationRecords.Count, twinId);
            return educationRecords;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get education records for Twin ID: {TwinId}", twinId);
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
            _logger.LogInformation("?? Education record retrieved successfully: {Institution} {EducationType}", education.Institution, education.EducationType);
            return education;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get education record by ID {EducationId} for CountryID: {CountryId}", educationId, countryId);
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
            
            _logger.LogInformation("?? Education record updated successfully: {Institution} {EducationType} for Twin: {TwinID}", 
                educationData.Institution, educationData.EducationType, educationData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update education record: {Id} for Twin: {TwinID}", 
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
            
            _logger.LogInformation("?? Education record deleted successfully: {EducationId} for CountryID: {CountryId}", educationId, countryId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to delete education record: {EducationId} for CountryID: {CountryId}", educationId, countryId);
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
            _logger.LogError(ex, "? Failed to get Twin profile by ID cross-partition {TwinId}", twinId);
            return null;
        }
    }

    public async Task<bool> UpdateProfileAsync(TwinProfileData profile)
    {
        _logger.LogWarning("?? UpdateProfileAsync: Method not fully implemented");
        await Task.CompletedTask;
        return false;
    }

    public async Task<bool> CreateProfileAsync(TwinProfileData profile)
    {
        _logger.LogWarning("?? CreateProfileAsync: Method not fully implemented");
        await Task.CompletedTask;
        return false;
    }

    public async Task<List<TwinProfileData>> SearchProfilesAsync(string searchTerm, string? countryId = null)
    {
        _logger.LogWarning("?? SearchProfilesAsync: Method not fully implemented");
        await Task.CompletedTask;
        return new List<TwinProfileData>();
    }

    public async Task<List<Dictionary<string, object?>>> LoadConversationHistoryAsync(string twinId, int limit = 10)
    {
        _logger.LogWarning("?? LoadConversationHistoryAsync: Method not fully implemented");
        await Task.CompletedTask;
        return new List<Dictionary<string, object?>>();
    }

    public async Task<Dictionary<string, object>> CountConversationMessagesAsync(string twinId)
    {
        _logger.LogWarning("?? CountConversationMessagesAsync: Method not fully implemented");
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
                        _logger.LogWarning(ex, "?? Error converting document to ContactData: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} contacts for Twin ID: {TwinId}", contacts.Count, twinId);
            return contacts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get contacts for Twin ID: {TwinId}", twinId);
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
            _logger.LogInformation("?? Contact retrieved successfully: {Nombre} {Apellido}", contact.Nombre, contact.Apellido);
            return contact;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get contact by ID {ContactId} for Twin: {TwinId}", contactId, twinId);
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
            
            _logger.LogInformation("?? Contact created successfully: {Nombre} {Apellido} for Twin: {TwinID}", 
                contactData.Nombre, contactData.Apellido, contactData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to create contact: {Nombre} {Apellido} for Twin: {TwinID}", 
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
            
            _logger.LogInformation("?? Contact updated successfully: {Nombre} {Apellido} for Twin: {TwinID}", 
                contactData.Nombre, contactData.Apellido, contactData.TwinID);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to update contact: {Id} for Twin: {TwinID}", 
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
            
            _logger.LogInformation("?? Contact deleted successfully: {ContactId} for Twin: {TwinId}", contactId, twinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to delete contact: {ContactId} for Twin: {TwinId}", contactId, twinId);
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
            
            await picturesContainer.CreateItemAsync(photoDict, new PartitionKey(photoDocument.TwinId));
            
            _logger.LogInformation("?? Photo document saved successfully: {PhotoId} for Twin: {TwinId}", 
                photoDocument.PhotoId, photoDocument.TwinId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to save photo document: {PhotoId} for Twin: {TwinId}", 
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
                        _logger.LogWarning(ex, "?? Error converting document to PhotoDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} photo documents for Twin ID: {TwinId}", photoDocuments.Count, twinId);
            return photoDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get photo documents for Twin ID: {TwinId}", twinId);
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
                        _logger.LogWarning(ex, "?? Error converting document to PhotoDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} filtered photo documents for Twin ID: {TwinId}", photoDocuments.Count, twinId);
            return photoDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get filtered photo documents for Twin ID: {TwinId}", twinId);
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
                        
                        _logger.LogInformation("?? Parsed InvoiceDocument: {Id} with {LineItemsCount} line items", 
                            invoiceDocument.Id, invoiceDocument.InvoiceData.LineItems.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to InvoiceDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} invoice documents for Twin ID: {TwinId}", invoiceDocuments.Count, twinId);
            return invoiceDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get invoice documents for Twin ID: {TwinId}", twinId);
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
                _logger.LogInformation("?? Using JOIN syntax for LineItems query: {Query}", fullQuery);
            }
            else
            {
                // Standard query without JOIN
                fullQuery = $"SELECT * FROM c WHERE {sqlQuery} ORDER BY c.createdAt DESC";
                _logger.LogInformation("?? Using standard query: {Query}", fullQuery);
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
                            _logger.LogInformation("?? JOIN query detected: extracting document from 'c' key");
                            
                            // Extract the actual document from the "c" wrapper
                            var cValue = item["c"];
                            documentData = ConvertObjectToDictionary(cValue);
                            
                            if (documentData.Any())
                            {
                                _logger.LogInformation("? Successfully extracted document from JOIN result with {Count} keys", documentData.Count);
                            }
                            else
                            {
                                _logger.LogWarning("?? Failed to extract document from JOIN result - empty dictionary");
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
                        
                        _logger.LogInformation("?? Parsed InvoiceDocument: {Id} with {LineItemsCount} line items", 
                            invoiceDocument.Id, invoiceDocument.InvoiceData.LineItems.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "?? Error converting document to InvoiceDocument: {Id}", item.GetValueOrDefault("id"));
                    }
                }
            }

            _logger.LogInformation("?? Found {Count} invoice documents for Twin ID: {TwinId}", invoiceDocuments.Count, twinId);
            return invoiceDocuments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Failed to get invoice documents for Twin ID: {TwinId}", twinId);
            return new List<InvoiceDocument>();
        }
    }

    public async Task<bool> SaveInvoiceAnalysisAsync(InvoiceAnalysisResult invoiceResult, string containerName, string fileName)
    {
        _logger.LogWarning("?? SaveInvoiceAnalysisAsync: Method not fully implemented");
        await Task.CompletedTask;
        return false;
    }

    // Helper methods
    private static Dictionary<string, object?> ConvertObjectToDictionary(object? obj)
    {
        if (obj == null)
            return new Dictionary<string, object?>();

        if (obj is Dictionary<string, object?> dict)
            return dict;

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
            var serialized = JsonSerializer.Serialize(obj);
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, object?>>(serialized);
            return deserialized ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
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
            JsonValueKind.Array => element.EnumerateArray().Select(JsonElementToObject).ToArray(),
            _ => element.ToString()
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