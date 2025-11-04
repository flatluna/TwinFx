using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using TwinAgentsLibrary.Models;
using TwinFx.Services;

namespace TwinFx.Plugins;

/// <summary>
/// User Profile plugin for accessing Twin profile information from Cosmos DB
/// </summary>
public sealed class UserProfilePlugin
{
    private readonly ILogger<UserProfilePlugin> _logger;
    private readonly IConfiguration _configuration;
    private readonly ProfileCosmosDB _cosmosService;

    public UserProfilePlugin(ILogger<UserProfilePlugin>? logger = null, IConfiguration? configuration = null)
    {
        _logger = logger ?? LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<UserProfilePlugin>();
        _configuration = configuration ?? new ConfigurationBuilder().Build();
        _cosmosService = _configuration.CreateProfileCosmosService(
            LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ProfileCosmosDB>());
    }

    [KernelFunction, Description("Get comprehensive user profile information by Twin ID from Cosmos DB")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public async Task<string> GetUserProfile(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting user profile for Twin ID: {twinId}");

            // Try to get profile by Twin ID using cross-partition search
            var profile = await _cosmosService.GetProfileById(twinId);

            if (profile == null)
            {
                _logger.LogWarning($"? No profile found for Twin ID: {twinId}");
                return FormatProfileNotFound(twinId);
            }

            // Check for fake/test data
            if (IsTestData(profile))
            {
                _logger.LogWarning($"?? Test data detected for Twin ID: {twinId}");
                return FormatTestDataWarning(twinId, profile);
            }

            _logger.LogInformation($"? Found real profile data for Twin ID: {twinId}");
            return FormatProfileResponse(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting profile for Twin ID: {twinId}");
            return $"? Error al obtener perfil: {ex.Message}";
        }
    }

    [KernelFunction, Description("Update user profile information in Cosmos DB")]
    public async Task<string> UpdateUserProfile(
        [Description("Twin ID of the user")] string twinId,
        [Description("Field to update (e.g., phone, email, address)")] string field,
        [Description("New value for the field")] string newValue)
    {
        try
        {
            _logger.LogInformation($"?? Updating profile for Twin ID: {twinId}, Field: {field}");

            // Get existing profile
            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return $"? No se encontró perfil para Twin ID: {twinId}";
            }

            // Update the specified field
            var updated = UpdateProfileField(profile, field, newValue);
            if (!updated)
            {
                return $"? Campo no válido: {field}";
            }

            // Save updated profile
            var success = await _cosmosService.UpdateProfileAsync(profile);
            if (success)
            {
                _logger.LogInformation($"? Profile updated successfully for Twin ID: {twinId}");
                return FormatUpdateResponse(twinId, field, newValue);
            }
            else
            {
                return $"? Error al actualizar el perfil en la base de datos";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error updating profile for Twin ID: {twinId}");
            return $"? Error al actualizar perfil: {ex.Message}";
        }
    }

    [KernelFunction, Description("Search user profiles by name, email, or other criteria")]
    public async Task<string> SearchUserProfiles(
        [Description("Search term (name, email, etc.)")] string searchTerm,
        [Description("Optional country ID to limit search")] string? countryId = null)
    {
        try
        {
            _logger.LogInformation($"?? Searching profiles with term: {searchTerm}");

            var profiles = await _cosmosService.SearchProfilesAsync(searchTerm, countryId);

            if (profiles.Count == 0)
            {
                return $"? No se encontraron perfiles que coincidan con: {searchTerm}";
            }

            return FormatSearchResults(profiles, searchTerm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error searching profiles with term: {searchTerm}");
            return $"? Error en búsqueda: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get user preferences and settings")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1024:Use properties where appropriate", Justification = "Too smart")]
    public async Task<string> GetUserPreferences(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return $"? No se encontró perfil para Twin ID: {twinId}";
            }

            return FormatPreferencesResponse(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting preferences for Twin ID: {twinId}");
            return $"? Error al obtener preferencias: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get conversation history for user")]
    public async Task<string> GetConversationHistory(
        [Description("Twin ID of the user")] string twinId,
        [Description("Number of recent messages to load")] int limit = 10)
    {
        try
        {
            _logger.LogInformation($"?? Getting conversation history for Twin ID: {twinId}");

            var messages = await _cosmosService.LoadConversationHistoryAsync(twinId, limit);

            if (messages.Count == 0)
            {
                return $"?? No hay historial de conversación para Twin ID: {twinId}";
            }

            return FormatConversationHistory(twinId, messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting conversation history for Twin ID: {twinId}");
            return $"? Error al obtener historial: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get analytics about user's conversation messages")]
    public async Task<string> GetConversationAnalytics(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting conversation analytics for Twin ID: {twinId}");

            var analytics = await _cosmosService.CountConversationMessagesAsync(twinId);

            return FormatAnalyticsResponse(analytics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting analytics for Twin ID: {twinId}");
            return $"? Error al obtener análisis: {ex.Message}";
        }
    }

    // New KernelFunction methods for individual profile data
    [KernelFunction, Description("Get only the user's last name (apellido) from their profile")]
    public async Task<string> GetUserLastName(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting last name for Twin ID: {twinId}");

            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return "? No se encontró perfil para este Twin ID";
            }

            if (IsTestData(profile))
            {
                return "?? Los datos de este perfil son de prueba/falsos";
            }

            var lastName = profile.LastName?.Trim();
            if (string.IsNullOrEmpty(lastName))
            {
                return "?? No hay apellido registrado en mi perfil";
            }

            return $"""
            <div style="background: linear-gradient(135deg, #10b981 0%, #059669 100%); padding: 15px; border-radius: 12px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 12px rgba(16, 185, 129, 0.3);">
                <h4 style="margin: 0 0 8px 0; font-size: 14px; color: #fde047; display: flex; align-items: center;">
                    ?? Mi Apellido
                </h4>
                <p style="margin: 0; font-size: 13px; background: rgba(255,255,255,0.15); padding: 8px 12px; border-radius: 8px; font-weight: 600;">
                    {lastName}
                </p>
                <div style="margin-top: 8px; font-size: 11px; opacity: 0.8;">
                    ? Datos reales desde Cosmos DB
                </div>
            </div>
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting last name for Twin ID: {twinId}");
            return $"? Error al obtener apellido: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get only the user's email address from their profile")]
    public async Task<string> GetUserEmail(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting email for Twin ID: {twinId}");

            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return "? No se encontró perfil para este Twin ID";
            }

            if (IsTestData(profile))
            {
                return "?? Los datos de este perfil son de prueba/falsos";
            }

            var email = profile.Email?.Trim();
            if (string.IsNullOrEmpty(email))
            {
                return "?? No hay email registrado en mi perfil";
            }

            return $"""
            <div style="background: linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%); padding: 15px; border-radius: 12px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 12px rgba(79, 70, 229, 0.3);">
                <h4 style="margin: 0 0 8px 0; font-size: 14px; color: #fbbf24; display: flex; align-items: center;">
                    ?? Mi Email
                </h4>
                <p style="margin: 0; font-size: 13px; background: rgba(255,255,255,0.15); padding: 8px 12px; border-radius: 8px; font-weight: 600; word-break: break-all;">
                    {email}
                </p>
                <div style="margin-top: 8px; font-size: 11px; opacity: 0.8;">
                    ? Datos reales desde Cosmos DB
                </div>
            </div>
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting email for Twin ID: {twinId}");
            return $"? Error al obtener email: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get only the user's phone number from their profile")]
    public async Task<string> GetUserPhone(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting phone for Twin ID: {twinId}");

            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return "? No se encontró perfil para este Twin ID";
            }

            if (IsTestData(profile))
            {
                return "?? Los datos de este perfil son de prueba/falsos";
            }

            var phone = profile.Phone?.Trim();
            if (string.IsNullOrEmpty(phone))
            {
                return "?? No hay teléfono registrado en mi perfil";
            }

            return $"""
            <div style="background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); padding: 15px; border-radius: 12px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 12px rgba(245, 158, 11, 0.3);">
                <h4 style="margin: 0 0 8px 0; font-size: 14px; color: #fef3c7; display: flex; align-items: center;">
                    ?? Mi Teléfono
                </h4>
                <p style="margin: 0; font-size: 13px; background: rgba(255,255,255,0.15); padding: 8px 12px; border-radius: 8px; font-weight: 600;">
                    {phone}
                </p>
                <div style="margin-top: 8px; font-size: 11px; opacity: 0.8;">
                    ? Datos reales desde Cosmos DB
                </div>
            </div>
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting phone for Twin ID: {twinId}");
            return $"? Error al obtener teléfono: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get only the user's first name from their profile")]
    public async Task<string> GetUserFirstName(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting first name for Twin ID: {twinId}");

            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return "? No se encontró perfil para este Twin ID";
            }

            if (IsTestData(profile))
            {
                return "?? Los datos de este perfil son de prueba/falsos";
            }

            var firstName = profile.FirstName?.Trim();
            if (string.IsNullOrEmpty(firstName))
            {
                return "?? No hay nombre registrado en mi perfil";
            }

            return $"""
            <div style="background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%); padding: 15px; border-radius: 12px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 12px rgba(239, 68, 68, 0.3);">
                <h4 style="margin: 0 0 8px 0; font-size: 14px; color: #fecaca; display: flex; align-items: center;">
                    ?? Mi Nombre
                </h4>
                <p style="margin: 0; font-size: 13px; background: rgba(255,255,255,0.15); padding: 8px 12px; border-radius: 8px; font-weight: 600;">
                    {firstName}
                </p>
                <div style="margin-top: 8px; font-size: 11px; opacity: 0.8;">
                    ? Datos reales desde Cosmos DB
                </div>
            </div>
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting first name for Twin ID: {twinId}");
            return $"? Error al obtener nombre: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get only the user's address from their profile")]
    public async Task<string> GetUserAddress(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting address for Twin ID: {twinId}");

            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return "? No se encontró perfil para este Twin ID";
            }

            if (IsTestData(profile))
            {
                return "?? Los datos de este perfil son de prueba/falsos";
            }

            var address = profile.Address?.Trim();
            if (string.IsNullOrEmpty(address))
            {
                return "?? No hay dirección registrada en mi perfil";
            }

            return $"""
            <div style="background: linear-gradient(135deg, #8b5cf6 0%, #7c3aed 100%); padding: 15px; border-radius: 12px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 12px rgba(139, 92, 246, 0.3);">
                <h4 style="margin: 0 0 8px 0; font-size: 14px; color: #e9d5ff; display: flex; align-items: center;">
                    ?? Mi Dirección
                </h4>
                <p style="margin: 0; font-size: 13px; background: rgba(255,255,255,0.15); padding: 8px 12px; border-radius: 8px; font-weight: 600; line-height: 1.4;">
                    {address}
                </p>
                <div style="margin-top: 8px; font-size: 11px; opacity: 0.8;">
                    ? Datos reales desde Cosmos DB
                </div>
            </div>
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting address for Twin ID: {twinId}");
            return $"? Error al obtener dirección: {ex.Message}";
        }
    }

    [KernelFunction, Description("Get only the user's occupation/job from their profile")]
    public async Task<string> GetUserOccupation(
        [Description("Twin ID of the user")] string twinId)
    {
        try
        {
            _logger.LogInformation($"?? Getting occupation for Twin ID: {twinId}");

            var profile = await _cosmosService.GetProfileById(twinId);
            if (profile == null)
            {
                return "? No se encontró perfil para este Twin ID";
            }

            if (IsTestData(profile))
            {
                return "?? Los datos de este perfil son de prueba/falsos";
            }

            var occupation = profile.Occupation?.Trim();
            if (string.IsNullOrEmpty(occupation))
            {
                return "?? No hay ocupación registrada en mi perfil";
            }

            return $"""
            <div style="background: linear-gradient(135deg, #06b6d4 0%, #0891b2 100%); padding: 15px; border-radius: 12px; color: white; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 12px rgba(6, 182, 212, 0.3);">
                <h4 style="margin: 0 0 8px 0; font-size: 14px; color: #cffafe; display: flex; align-items: center;">
                    ?? Mi Ocupación
                </h4>
                <p style="margin: 0; font-size: 13px; background: rgba(255,255,255,0.15); padding: 8px 12px; border-radius: 8px; font-weight: 600;">
                    {occupation}
                </p>
                <div style="margin-top: 8px; font-size: 11px; opacity: 0.8;">
                    ? Datos reales desde Cosmos DB
                </div>
            </div>
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"? Error getting occupation for Twin ID: {twinId}");
            return $"? Error al obtener ocupación: {ex.Message}";
        }
    }

    // Private helper methods
    private static bool IsTestData(TwinProfileData profile)
    {
        var indicators = new[]
        {
            "test", "fake", "example.com", "placeholder", "sample",
            "Usuario TwinFx", "Test User", "user.388a31e7@twinfx.com",
            "+34-600-000-000", "000-000-0000", "Test Occupation"
        };

        var fieldsToCheck = new[]
        {
            profile.FirstName, profile.LastName, profile.Email,
            profile.Phone, profile.Occupation, profile.TwinName
        };

        return fieldsToCheck.Any(field =>
            !string.IsNullOrEmpty(field) &&
            indicators.Any(indicator =>
                field.Contains(indicator, StringComparison.OrdinalIgnoreCase)));
    }

    private static string FormatProfileNotFound(string twinId)
    {
        return $"""
        ? **No se encontró perfil para este Twin ID**

        ?? **Twin ID:** {twinId}
        ?? **Estado:** Perfil no existe en Azure Cosmos DB
        ?? **Base de datos:** Azure Cosmos DB conectada
        ? **Resultado:** No se encontró información del usuario

        ?? **Opciones disponibles:**
           • Verificar que el Twin ID sea correcto
           • Crear perfil usando las funciones de registro
           • Contactar al administrador para configurar el perfil

        ?? **Política:** No se inventan datos personales por seguridad
        """;
    }

    private static string FormatTestDataWarning(string twinId, TwinProfileData profile)
    {
        return $"""
        ?? **Datos de prueba detectados en la base de datos**

        ?? **Twin ID:** {twinId}
        ?? **Estado:** Se encontraron datos falsos/de prueba en Cosmos DB
        ? **Problema:** Los datos contienen información claramente falsa:
           • Nombres genéricos como 'Usuario TwinFx'
           • Emails falsos como 'user.388a31e7@twinfx.com'
           • Teléfonos de prueba como '+34-600-000-000'

        ?? **Solución requerida:**
           • Eliminar el perfil falso actual de Cosmos DB
           • Crear un perfil real con datos verdaderos
           • Usar las funciones de actualización con datos reales

        ?? **Política de honestidad:** No se muestran datos claramente falsos
        """;
    }

    private static string FormatProfileResponse(TwinProfileData profile)
    {
        var response = "?? **Mi Perfil Personal (Datos Reales desde Cosmos DB):**\n\n";
        response += $"?? **Mi Twin ID:** {profile.TwinId}\n";
        response += $"??? **Nombre de Twin:** {profile.TwinName}\n\n";

        // Basic information
        if (!string.IsNullOrEmpty(profile.FirstName) || !string.IsNullOrEmpty(profile.LastName))
        {
            var fullName = $"{profile.FirstName} {profile.MiddleName} {profile.LastName}".Trim();
            response += $"?? **Mi Nombre Completo:** {fullName}\n";
        }

        if (!string.IsNullOrEmpty(profile.Email))
            response += $"?? **Mi Email:** {profile.Email}\n";

        if (!string.IsNullOrEmpty(profile.Phone))
            response += $"?? **Mi Teléfono:** {profile.Phone}\n";

        if (!string.IsNullOrEmpty(profile.Address))
            response += $"?? **Mi Dirección:** {profile.Address}\n";

        if (!string.IsNullOrEmpty(profile.DateOfBirth))
            response += $"?? **Mi Fecha de Nacimiento:** {profile.DateOfBirth}\n";

        if (!string.IsNullOrEmpty(profile.Occupation))
            response += $"?? **Mi Ocupación:** {profile.Occupation}\n";

        if (!string.IsNullOrEmpty(profile.Company))
            response += $"?? **Mi Empresa:** {profile.Company}\n";

        // Location information
        if (!string.IsNullOrEmpty(profile.City) && !string.IsNullOrEmpty(profile.Country))
        {
            var location = $"{profile.City}";
            if (!string.IsNullOrEmpty(profile.State))
                location += $", {profile.State}";
            location += $", {profile.Country}";
            response += $"?? **Mi Ubicación:** {location}\n";
        }

        if (!string.IsNullOrEmpty(profile.Nationality))
            response += $"?? **Mi Nacionalidad:** {profile.Nationality}\n";

        // Personal information
        if (profile.Interests.Count > 0)
            response += $"?? **Mis Intereses:** {string.Join(", ", profile.Interests)}\n";

        if (profile.Languages.Count > 0)
            response += $"??? **Mis Idiomas:** {string.Join(", ", profile.Languages)}\n";

        if (!string.IsNullOrEmpty(profile.PersonalBio))
            response += $"?? **Mi Biografía:** {profile.PersonalBio}\n";

        // Family and relationship information
        if (!string.IsNullOrEmpty(profile.FamilyRelation))
            response += $"??????????? **Relación Familiar:** {profile.FamilyRelation}\n";

        if (!string.IsNullOrEmpty(profile.MaritalStatus))
            response += $"?? **Estado Civil:** {profile.MaritalStatus}\n";

        response += $"\n? **Fuente:** Azure Cosmos DB (DATOS REALES)\n";
        response += $"??? **Base de datos:** TwinHumanDB/TwinProfiles\n";
        response += $"?? **Búsqueda:** Cross-partition query\n";

        return response;
    }

    private static string FormatUpdateResponse(string twinId, string field, string newValue)
    {
        return $"""
        ? **Perfil actualizado en Cosmos DB (REAL)**

        ?? **Twin ID:** {twinId}
        ?? **Campo actualizado:** {field}
        ? **Nuevo valor:** {newValue}
        ? **Actualizado:** {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC
        ?? **Guardado en:** Azure Cosmos DB
        """;
    }

    private static string FormatSearchResults(List<TwinProfileData> profiles, string searchTerm)
    {
        var response = $"?? **Resultados de búsqueda para: {searchTerm}**\n\n";
        response += $"?? **Perfiles encontrados:** {profiles.Count}\n\n";

        foreach (var profile in profiles.Take(5)) // Limit to 5 results
        {
            response += $"?? **{profile.FirstName} {profile.LastName}**\n";
            response += $"   ?? Twin ID: {profile.TwinId}\n";
            response += $"   ?? Email: {profile.Email}\n";
            response += $"   ?? Empresa: {profile.Company}\n\n";
        }

        if (profiles.Count > 5)
        {
            response += $"... y {profiles.Count - 5} perfiles más\n";
        }

        return response;
    }

    private static string FormatPreferencesResponse(TwinProfileData profile)
    {
        return $"""
        ?? **Preferencias del Usuario:** {profile.TwinId}

        ?? **Información Personal:**
           • Idiomas: {string.Join(", ", profile.Languages)}
           • Nacionalidad: {profile.Nationality}
           • País de residencia: {profile.Country}

        ?? **Configuración de Privacidad:**
           • Nivel de privacidad: {profile.PrivacyLevel ?? "No configurado"}
           • Gestión de cuenta: {profile.AccountManagement ?? "Cuenta propia"}
           • Email autorizado: {profile.OwnerEmail ?? "No configurado"}

        ??????????? **Información Familiar:**
           • Relación familiar: {profile.FamilyRelation ?? "No especificada"}
           • Estado civil: {profile.MaritalStatus ?? "No especificado"}

        ?? **Fuente:** Azure Cosmos DB
        """;
    }

    private static string FormatConversationHistory(string twinId, List<Dictionary<string, object?>> messages)
    {
        var response = $"?? **Historial de conversación:** {twinId}\n\n";
        response += $"?? **Mensajes encontrados:** {messages.Count}\n";
        response += $"?? **Fuente:** Azure Cosmos DB\n\n";

        foreach (var message in messages)
        {
            var messageType = message.GetValueOrDefault("messageType")?.ToString() ?? "unknown";
            var messageText = message.GetValueOrDefault("message")?.ToString() ?? "";
            var timestamp = message.GetValueOrDefault("timestamp")?.ToString() ?? "";

            var role = messageType == "user" ? "?? Usuario" : "?? TwinAgent";
            response += $"**{role}** ({timestamp}):\n";
            response += $"{messageText}\n\n";
        }

        return response;
    }

    private static string FormatAnalyticsResponse(Dictionary<string, object> analytics)
    {
        var twinId = analytics.GetValueOrDefault("twin_id")?.ToString() ?? "";
        var totalMessages = analytics.GetValueOrDefault("total_messages")?.ToString() ?? "0";
        var userMessages = analytics.GetValueOrDefault("user_messages")?.ToString() ?? "0";
        var agentMessages = analytics.GetValueOrDefault("agent_messages")?.ToString() ?? "0";
        var success = analytics.GetValueOrDefault("success")?.ToString() ?? "false";

        if (success == "false")
        {
            var error = analytics.GetValueOrDefault("error")?.ToString() ?? "Error desconocido";
            return $"? Error en análisis para {twinId}: {error}";
        }

        return $"""
        ?? **Análisis de Conversación:** {twinId}

        ?? **Estadísticas:**
           • Total de mensajes: {totalMessages}
           • Mensajes del usuario: {userMessages}
           • Respuestas del agente: {agentMessages}

        ?? **Participación:**
           • Ratio usuario/agente: {userMessages}:{agentMessages}

        ?? **Fuente:** Azure Cosmos DB - TwinConversations
        ? **Estado:** Análisis completado exitosamente
        """;
    }

    private static bool UpdateProfileField(TwinProfileData profile, string field, string newValue)
    {
        return field.ToLowerInvariant() switch
        {
            "phone" or "telefono" => SetValue(() => profile.Phone = newValue),
            "email" => SetValue(() => profile.Email = newValue),
            "address" or "direccion" => SetValue(() => profile.Address = newValue),
            "occupation" or "ocupacion" => SetValue(() => profile.Occupation = newValue),
            "company" or "empresa" => SetValue(() => profile.Company = newValue),
            "city" or "ciudad" => SetValue(() => profile.City = newValue),
            "state" or "estado" => SetValue(() => profile.State = newValue),
            "country" or "pais" => SetValue(() => profile.Country = newValue),
            "bio" or "biografia" => SetValue(() => profile.PersonalBio = newValue),
            "nickname" or "apodo" => SetValue(() => profile.Nickname = newValue),
            _ => false
        };

        static bool SetValue(Action setter)
        {
            setter();
            return true;
        }
    }
}