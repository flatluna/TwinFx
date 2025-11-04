using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using TwinFx.Models;
using TwinAgentsLibrary.Models;

namespace TwinFx.Services
{
    public class ProfileCosmosDB
    {

        private readonly ILogger<ProfileCosmosDB> _logger;
        private readonly CosmosClient _client;
        private readonly Database _database;
        private readonly CosmosDbSettings _cosmosSettings;
        private readonly AzureStorageSettings _storageSettings;
        public ProfileCosmosDB(ILogger<ProfileCosmosDB> logger, IOptions<CosmosDbSettings> cosmosOptions, IOptions<AzureStorageSettings> storageOptions = null)
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

        // Profile methods
        public async Task<TwinProfileData?> GetProfileById(string twinId)
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
                        return new TwinProfileData().FromDict(item);
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
        public async Task<TwinProfileData?> GetProfilesBySubscriptionIDAsync(string subscriptionId)
        {
            try
            {
                var query = new QueryDefinition("SELECT * FROM c WHERE c.subscriptionId = @subscriptionId")
                    .WithParameter("@subscriptionId", subscriptionId);

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
                        return new TwinProfileData().FromDict(item);
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get Twin profile by ID cross-partition {TwinId}", subscriptionId);
                return null;
            }
        }
        public async Task<bool> UpdateProfileAsync(TwinProfileData profile)
        {
            try
            {
                var profilesContainer = _database.GetContainer("TwinProfiles");

                // Validate required field
                if (string.IsNullOrEmpty(profile.TwinId))
                {
                    _logger.LogError("❌ Twin ID is required for profile update");
                    return false;
                }

                // Convert to dictionary for Cosmos DB - inline approach (same as CreateProfileAsync)
                var profileDict = new Dictionary<string, object?>
                {
                    ["id"] = profile.TwinId,
                    ["twinId"] = profile.TwinId,
                    ["subscriptionID"] = profile.SubscriptionId,
                    ["twinName"] = profile.TwinName,
                    ["firstName"] = profile.FirstName,
                    ["lastName"] = profile.LastName,
                    ["email"] = profile.Email,
                    ["phone"] = profile.Phone,
                    ["address"] = profile.Address,
                    ["dateOfBirth"] = profile.DateOfBirth,
                    ["birthCountry"] = profile.BirthCountry,
                    ["birthCity"] = profile.BirthCity,
                    ["nationality"] = profile.Nationality,
                    ["gender"] = profile.Gender,
                    ["occupation"] = profile.Occupation,
                    ["interests"] = profile.Interests,
                    ["languages"] = profile.Languages,
                    ["CountryID"] = profile.CountryID,
                    ["profilePhoto"] = profile.ProfilePhoto,
                    ["middleName"] = profile.MiddleName,
                    ["nickname"] = profile.Nickname,
                    ["city"] = profile.City,
                    ["state"] = profile.State,
                    ["country"] = profile.Country,
                    ["zipCode"] = profile.ZipCode,
                    ["maritalStatus"] = profile.MaritalStatus,
                    ["personalBio"] = profile.PersonalBio,
                    ["emergencyContact"] = profile.EmergencyContact,
                    ["emergencyPhone"] = profile.EmergencyPhone,
                    ["bloodType"] = profile.BloodType,
                    ["height"] = profile.Height,
                    ["weight"] = profile.Weight,
                    ["documentType"] = profile.DocumentType,
                    ["documentNumber"] = profile.DocumentNumber,
                    ["passportNumber"] = profile.PassportNumber,
                    ["socialSecurityNumber"] = profile.SocialSecurityNumber,
                    ["website"] = profile.Website,
                    ["linkedIn"] = profile.LinkedIn,
                    ["facebook"] = profile.Facebook,
                    ["instagram"] = profile.Instagram,
                    ["twitter"] = profile.Twitter,
                    ["company"] = profile.Company,
                    ["familyRelation"] = profile.FamilyRelation,
                    ["accountManagement"] = profile.AccountManagement,
                    ["privacyLevel"] = profile.PrivacyLevel,
                    ["authorizedEmails"] = profile.AuthorizedEmails,
                    ["ownerEmail"] = profile.OwnerEmail,
                    ["updatedAt"] = DateTime.UtcNow.ToString("O")
                };

                // Update the profile using UpsertItemAsync with TwinId as partition key
                await profilesContainer.UpsertItemAsync(profileDict, new PartitionKey(profile.CountryID));

                _logger.LogInformation("👤 Twin profile updated successfully: {FirstName} {LastName} for Twin: {TwinID}",
                    profile.FirstName, profile.LastName, profile.TwinId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to update Twin profile: {FirstName} {LastName} for Twin: {TwinID}",
                    profile.FirstName, profile.LastName, profile.TwinId);
                return false;
            }
        }

        public async Task<bool> CreateProfileAsync(TwinProfileData profile)
        {
            try
            {
                var profilesContainer = _database.GetContainer("TwinProfiles");

                // Generate ID if not provided
                if (string.IsNullOrEmpty(profile.TwinId))
                {
                    profile.TwinId = Guid.NewGuid().ToString();
                }

                // Set default values
                if (string.IsNullOrEmpty(profile.CountryID))
                    profile.CountryID = "US";

                if (string.IsNullOrEmpty(profile.TwinName))
                    profile.TwinName = $"Twin_{profile.TwinId}";

                // Convert to dictionary for Cosmos DB - inline approach
                var profileDict = new Dictionary<string, object?>
                {
                    ["id"] = profile.TwinId,
                    ["twinId"] = profile.TwinId,
                    ["subscriptionID"] = profile.SubscriptionId,
                    ["twinName"] = profile.TwinName,
                    ["firstName"] = profile.FirstName,
                    ["lastName"] = profile.LastName,
                    ["email"] = profile.Email,
                    ["phone"] = profile.Phone,
                    ["address"] = profile.Address,
                    ["dateOfBirth"] = profile.DateOfBirth,
                    ["birthCountry"] = profile.BirthCountry,
                    ["birthCity"] = profile.BirthCity,
                    ["nationality"] = profile.Nationality,
                    ["gender"] = profile.Gender,
                    ["occupation"] = profile.Occupation,
                    ["interests"] = profile.Interests,
                    ["languages"] = profile.Languages,
                    ["CountryID"] = profile.CountryID,
                    ["profilePhoto"] = profile.ProfilePhoto,
                    ["middleName"] = profile.MiddleName,
                    ["nickname"] = profile.Nickname,
                    ["city"] = profile.City,
                    ["state"] = profile.State,
                    ["country"] = profile.Country,
                    ["zipCode"] = profile.ZipCode,
                    ["maritalStatus"] = profile.MaritalStatus,
                    ["personalBio"] = profile.PersonalBio,
                    ["emergencyContact"] = profile.EmergencyContact,
                    ["emergencyPhone"] = profile.EmergencyPhone,
                    ["bloodType"] = profile.BloodType,
                    ["height"] = profile.Height,
                    ["weight"] = profile.Weight,
                    ["documentType"] = profile.DocumentType,
                    ["documentNumber"] = profile.DocumentNumber,
                    ["passportNumber"] = profile.PassportNumber,
                    ["socialSecurityNumber"] = profile.SocialSecurityNumber,
                    ["website"] = profile.Website,
                    ["linkedIn"] = profile.LinkedIn,
                    ["facebook"] = profile.Facebook,
                    ["instagram"] = profile.Instagram,
                    ["twitter"] = profile.Twitter,
                    ["company"] = profile.Company,
                    ["familyRelation"] = profile.FamilyRelation,
                    ["accountManagement"] = profile.AccountManagement,
                    ["privacyLevel"] = profile.PrivacyLevel,
                    ["authorizedEmails"] = profile.AuthorizedEmails,
                    ["ownerEmail"] = profile.OwnerEmail,
                    ["createdDate"] = DateTime.UtcNow.ToString("O")
                };

                // Create the profile with TwinId as partition key
                await profilesContainer.CreateItemAsync(profileDict, new PartitionKey(profile.TwinId));

                _logger.LogInformation("👤 Twin profile created successfully: {FirstName} {LastName} for Twin: {TwinID}",
                    profile.FirstName, profile.LastName, profile.TwinId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create Twin profile: {FirstName} {LastName} for Twin: {TwinID}",
                    profile.FirstName, profile.LastName, profile.TwinId);
                return false;
            }
        }

        public async Task<List<TwinProfileData>> SearchProfilesAsync(string searchTerm, string? countryId = null)
        {
            try
            {
                var profilesContainer = _database.GetContainer("TwinProfiles");

                // Build the search query conditions
                var conditions = new List<string>();
                var parameters = new Dictionary<string, object>();

                // Add search term conditions (search across multiple fields)
                if (!string.IsNullOrEmpty(searchTerm))
                {
                    conditions.Add(@"(
                        CONTAINS(LOWER(c.firstName), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.lastName), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.email), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.twinName), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.occupation), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.company), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.city), LOWER(@searchTerm)) OR
                        CONTAINS(LOWER(c.nationality), LOWER(@searchTerm))
                    )");
                    parameters["@searchTerm"] = searchTerm;
                }

                // Add country filter if provided
                if (!string.IsNullOrEmpty(countryId))
                {
                    conditions.Add("c.CountryID = @countryId");
                    parameters["@countryId"] = countryId;
                }

                // Build the complete SQL query
                var whereClause = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";
                var sql = $"SELECT * FROM c {whereClause} ORDER BY c.firstName, c.lastName";

                var query = new QueryDefinition(sql);
                foreach (var param in parameters)
                {
                    query = query.WithParameter(param.Key, param.Value);
                }

                _logger.LogInformation("🔍 Searching profiles with term: '{SearchTerm}', CountryId: '{CountryId}'", 
                    searchTerm, countryId ?? "All");

                var iterator = profilesContainer.GetItemQueryIterator<Dictionary<string, object?>>(
                    query,
                    requestOptions: new QueryRequestOptions { MaxItemCount = 50 } // Limit results
                );

                var profiles = new List<TwinProfileData>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        try
                        {
                            var profile = new TwinProfileData().FromDict(item);
                            profiles.Add(profile);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "⚠️ Error converting document to TwinProfileData: {Id}", 
                                item.GetValueOrDefault("id"));
                        }
                    }
                }

                _logger.LogInformation("👥 Found {Count} profiles matching search criteria", profiles.Count);
                return profiles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to search profiles with term: '{SearchTerm}'", searchTerm);
                return new List<TwinProfileData>();
            }
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
    }
}
