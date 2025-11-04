// Copyright (c) Microsoft. All rights reserved.
using System.ComponentModel;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using TwinFx.Plugins;
using TwinFx.Agents;
using TwinFx.Services;
using TwinFx.Models;

namespace TwinFx.Clients;

/// <summary>
/// Twin Agent Client using ChatCompletionAgent with function invocation filtering
/// Demonstrates usage of IAutoFunctionInvocationFilter for both direct invocation and AgentChat
/// </summary>
public class TwinAgentClient : BaseTwinAgentTest<object>
{
    private readonly ILogger<TwinAgentClient> _logger;
    private readonly IConfiguration _configuration;

    public TwinAgentClient(ILogger<TwinAgentClient> logger, IConfiguration configuration)
        : base(logger)
    {
        _logger = logger;
        _configuration = configuration;
        
        _logger.LogInformation("🔧 TwinAgentClient initialized with ChatCompletionAgent pattern");
    }

    /// <summary>
    /// Processes a human question and determines the user's intention using AI classification
    /// </summary>
    public async Task<UserIntentionResult> ProcessHumanQuestionAndDetermineIntention(string humanQuestion, string twinId, bool useChatClient = true)
    {
        _logger.LogInformation($"🧠 Processing human question to determine intention for Twin ID: {twinId}");
        _logger.LogInformation($"❓ Human Question: {humanQuestion}");

        try
        {
            // First, try basic keyword detection as fallback
            var keywordBasedResult = ClassifyUsingKeywords(humanQuestion, twinId);
            
            // Create a specialized agent for intention classification
            ChatCompletionAgent intentClassificationAgent = new()
            {
                Instructions = """
                🤖 **Clasificador de Intenciones para Sistema Digital**

                Eres un clasificador especializado en determinar la intención del usuario en un sistema de gestión digital.

                🎯 **CATEGORÍAS DE CLASIFICACIÓN:**

                ## 📊 **BUSQUEDA_FACTURAS** - Consultas financieras
                ⚠️ **REGLA:** Términos como "facturas", "gastos", "pagos", "cargos"
                
                Ejemplos:
                - "¿Cuánto he gastado?"
                - "Busca facturas de Microsoft"
                - "Total pagado en 2024"
                
                PALABRAS CLAVE: facturas, factura, gastos, pagué, total, suma, cargo, monto

                ## 📋 **BUSQUEDA_DOCUMENTOS** - Documentos no financieros
                ⚠️ **REGLA:** Solo contratos, licencias, certificados
                
                Ejemplos:
                - "Busca contratos"
                - "¿Tienes mi licencia?"
                - "Encuentra certificados"
                
                Sub-tipos: CONTRATOS, LICENCIAS, CERTIFICADOS, LEGALES, OTROS_DOCUMENTOS

                ## 👤 **BUSQUEDA_PERFIL** - Información personal del usuario
                ⚠️ **REGLA:** Preguntas sobre datos personales específicos
                
                Ejemplos:
                - "¿Cuál es mi nombre?"
                - "¿Dónde vivo?"
                - "¿Cuál es mi email?"
                - "¿Qué idiomas hablas?"
                
                PALABRAS CLAVE: mi nombre, mi email, mi teléfono, dónde vivo, mi trabajo

                ## 👥 **BUSCA_CONTACTOS** - Búsqueda de contactos y información de personas
                ⚠️ **REGLA:** Preguntas sobre contactos, personas específicas, teléfonos de contactos
                
                Ejemplos:
                - "Envíame el teléfono de mi contacto Angeles Ruiz"
                - "Busca el número de Jorge Luna"
                - "¿Tienes el email de María García?"
                - "Dame los contactos de mi familia"
                - "Muéstrame contactos de trabajo"
                - "Encuentra a Pedro Martinez"
                - "Busca el teléfono de Angeles"
                - "¿Cuál es el email de mi hermana?"
                - "Dame la dirección de Juan"
                - "Contactos de Microsoft"
                - "Teléfonos de mis amigos"
                - "Lista de contactos"
                
                PALABRAS CLAVE: contacto, contactos, teléfono de, email de, número de, encuentra a, busca a, dame el, dirección de

                ## 🗣️ **GENERICA** - Conversación general
                ⚠️ **REGLA:** Temas generales sin datos personales
                
                Ejemplos:
                - "Hola, ¿cómo estás?"
                - "¿Qué hora es?"
                - "¿Cómo funcionas?"
                
                PALABRAS CLAVE: hola, qué hora, funcionas, puedes hacer, gracias

                ## 📸 **BUSCA_FOTOS** - Búsqueda de imágenes
                ⚠️ **REGLA:** Preguntas sobre fotos, imágenes, galería
                
                Ejemplos:
                - "Muéstrame fotos"
                - "Busca imágenes"
                - "Encuentra fotos de familia"
                - "Fotos con Juan"
                - "Busca fotos de María"
                - "Encuentra a Pedro en las fotos"
                
                PALABRAS CLAVE: fotos, foto, imágenes, imagen, busca, encuentra, muéstrame

                📋 **FORMATO DE RESPUESTA:**
                INTENCION: [GENERICA|BUSQUEDA_FACTURAS|BUSQUEDA_DOCUMENTOS|BUSQUEDA_PERFIL|BUSCA_CONTACTOS|BUSCA_FOTOS]
                SUB_TIPO: [CONTRATOS|LICENCIAS|CERTIFICADOS|LEGALES|OTROS_DOCUMENTOS|NO_APLICA]
                REQUIERE_CALCULO: [SI|NO]
                REQUIERE_FILTRO: [SI|NO]
                CONFIANZA: [número entre 0.0 y 1.0]
                REAZON: [explicación breve]

                🔍 **LÓGICA DE DECISIÓN:**

                **PASO 1:** ¿Menciona "facturas", "gastos"?
                → SÍ: BUSQUEDA_FACTURAS
                → NO: Paso 2

                **PASO 2:** ¿Menciona "contratos", "licencia"?
                → SÍ: BUSQUEDA_DOCUMENTOS
                → NO: Paso 3

                **PASO 3:** ¿Pregunta datos personales? (mi nombre, mi email, etc.)
                → SÍ: BUSQUEDA_PERFIL
                → NO: Paso 4

                **PASO 4:** ¿Menciona "contacto", "teléfono de", "email de", nombres específicos como "Angeles", "Jorge"?
                → SÍ: BUSCA_CONTACTOS
                → NO: Paso 5

                **PASO 5:** ¿Menciona "fotos", "imágenes", "busca", "encuentra"?
                → SÍ: BUSCA_FOTOS
                → NO: Paso 6

                **PASO 6:** Por defecto → GENERICA

                🧪 **CASOS DE EJEMPLO:**

                ❓ "Envíame el teléfono de mi contacto Angeles Ruiz"
                ✅ CORRECTO: BUSCA_CONTACTOS (búsqueda de información específica de contacto)

                ❓ "Busca el número de Jorge Luna"
                ✅ CORRECTO: BUSCA_CONTACTOS (búsqueda de teléfono de persona específica)

                ❓ "Dame los contactos de mi familia"
                ✅ CORRECTO: BUSCA_CONTACTOS (búsqueda de contactos por relación)

                ❓ "Encuentra fotos de María"
                ✅ CORRECTO: BUSCA_FOTOS (búsqueda de fotos)

                ❓ "¿Cuál es mi nombre?"
                ✅ CORRECTO: BUSQUEDA_PERFIL (información personal)

                ❓ "¿Qué hora es?"
                ✅ CORRECTO: GENERICA (información general)

                🎯 **IMPORTANTE:**
                - Sé directo y específico
                - RAZON máximo 30 palabras
                - Enfócate en palabras clave principales
                - BUSCA_CONTACTOS es para información de OTRAS PERSONAS
                - BUSQUEDA_PERFIL es para información PERSONAL del usuario
                """,
                Kernel = CreateKernelWithFilter(useChatClient),
                Arguments = new KernelArguments(new PromptExecutionSettings() 
                { 
                    FunctionChoiceBehavior = FunctionChoiceBehavior.None()
                }),
            };

            // Create a chat for the classification
            AgentGroupChat classificationChat = new();

            // Send the human question for classification
            ChatMessageContent classificationMessage = new(AuthorRole.User, humanQuestion);
            classificationChat.AddChatMessage(classificationMessage);
            this.WriteAgentChatMessage(classificationMessage);

            // Get the classification response
            var classificationResponseBuilder = new StringBuilder();
            await foreach (ChatMessageContent response in classificationChat.InvokeAsync(intentClassificationAgent))
            {
                this.WriteAgentChatMessage(response);
                
                if (!string.IsNullOrEmpty(response.Content) && response.Role == AuthorRole.Assistant)
                {
                    classificationResponseBuilder.Append(response.Content);
                }
            }

            var classificationText = classificationResponseBuilder.ToString().Trim();
            _logger.LogInformation($"📝 Raw classification response: {classificationText}");

            // Parse the classification response
            var intentionResult = ParseIntentionResponse(classificationText, humanQuestion, twinId);
            
            _logger.LogInformation($"🎯 Intention determined: {intentionResult.Intention} (Confidence: {intentionResult.Confidence:F2})");
            _logger.LogInformation($"💭 Reason: {intentionResult.Reason}");

            return intentionResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing human question for intention classification");
            
            // Check if it's a content filter error
            if (ex.Message.Contains("content_filter") || ex.Message.Contains("content management policy"))
            {
                _logger.LogWarning("⚠️ Content filter triggered, using keyword-based fallback classification");
                
                // Use keyword-based classification as fallback
                var fallbackResult = ClassifyUsingKeywords(humanQuestion, twinId);
                fallbackResult.Reason = "Clasificación por palabras clave (filtro de contenido activado)";
                return fallbackResult;
            }
            
            // Return a default result in case of other errors
            return new UserIntentionResult
            {
                Intention = UserIntention.Generica,
                Confidence = 0.0f,
                Reason = $"Error en clasificación: {ex.Message}",
                OriginalQuestion = humanQuestion,
                TwinId = twinId,
                ProcessedAt = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Parse the AI response and extract intention classification
    /// </summary>
    private UserIntentionResult ParseIntentionResponse(string classificationText, string originalQuestion, string twinId)
    {
        try
        {
            var lines = classificationText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            string intentionText = "";
            string subTipo = "";
            bool requiereCalculo = false;
            bool requiereFiltro = false;
            float confidence = 0.0f;
            string reason = "";

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("INTENCION:", StringComparison.OrdinalIgnoreCase))
                {
                    intentionText = trimmedLine.Substring("INTENCION:".Length).Trim();
                }
                else if (trimmedLine.StartsWith("SUB_TIPO:", StringComparison.OrdinalIgnoreCase))
                {
                    subTipo = trimmedLine.Substring("SUB_TIPO:".Length).Trim();
                }
                else if (trimmedLine.StartsWith("REQUIERE_CALCULO:", StringComparison.OrdinalIgnoreCase))
                {
                    var calculoText = trimmedLine.Substring("REQUIERE_CALCULO:".Length).Trim();
                    requiereCalculo = calculoText.Equals("SI", StringComparison.OrdinalIgnoreCase);
                }
                else if (trimmedLine.StartsWith("REQUIERE_FILTRO:", StringComparison.OrdinalIgnoreCase))
                {
                    var filtroText = trimmedLine.Substring("REQUIERE_FILTRO:".Length).Trim();
                    requiereFiltro = filtroText.Equals("SI", StringComparison.OrdinalIgnoreCase);
                }
                else if (trimmedLine.StartsWith("CONFIANZA:", StringComparison.OrdinalIgnoreCase))
                {
                    var confidenceText = trimmedLine.Substring("CONFIANZA:".Length).Trim();
                    float.TryParse(confidenceText, out confidence);
                }
                else if (trimmedLine.StartsWith("RAZON:", StringComparison.OrdinalIgnoreCase))
                {
                    reason = trimmedLine.Substring("RAZON:".Length).Trim();
                }
            }

            // Parse intention enum
            var intention = intentionText.ToUpperInvariant() switch
            {
                "GENERICA" => UserIntention.Generica,
                "BUSQUEDA_FACTURAS" => UserIntention.BusquedaFacturas,
                "BUSQUEDA_DOCUMENTOS" => UserIntention.BusquedaDocumentos,
                "BUSQUEDA_PERFIL" => UserIntention.BusquedaPerfil,
                "BUSCA_CONTACTOS" => UserIntention.BuscaContactos,
                "BUSCA_FOTOS" => UserIntention.BuscaFotos,
                _ => UserIntention.Generica // Default fallback
            };

            return new UserIntentionResult
            {
                Intention = intention,
                SubTipo = subTipo,
                RequiereCalculo = requiereCalculo,
                RequiereFiltro = requiereFiltro,
                Confidence = confidence,
                Reason = reason,
                OriginalQuestion = originalQuestion,
                TwinId = twinId,
                ProcessedAt = DateTime.UtcNow,
                Success = true,
                ErrorMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error parsing intention response: {ClassificationText}", classificationText);
            
            // Return default fallback
            return new UserIntentionResult
            {
                Intention = UserIntention.Generica,
                SubTipo = "NO_APLICA",
                RequiereCalculo = false,
                RequiereFiltro = false,
                Confidence = 0.0f,
                Reason = $"Error parsing response: {ex.Message}",
                OriginalQuestion = originalQuestion,
                TwinId = twinId,
                ProcessedAt = DateTime.UtcNow,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Process invoice search requests using InvoicesAgent
    /// </summary>
    public async Task<string> ProcessInvoiceSearchRequest(string question, string twinId, bool useChatClient, UserIntentionResult? intentionResult = null)
    {
        try
        {
            _logger.LogInformation("💰 Processing invoice search request");

            // Create InvoicesAgent instance
            var invoicesAgent = new InvoicesAgent(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<InvoicesAgent>(),
                _configuration);

            // Determinar si requiere cálculo y filtro basado en el intention result
            bool requiereCalculo = intentionResult?.RequiereCalculo ?? false;
            bool requiereFiltro = intentionResult?.RequiereFiltro ?? false;

            _logger.LogInformation("🔍 Using smart processing with automatic dynamic CSV detection");
            var result = await invoicesAgent.ProcessInvoiceQuestionSmartAsync(question, twinId, requiereCalculo, requiereFiltro);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing invoice search request");
            return $"❌ Error consultando facturas: {ex.Message}";
        }
    }

    /// <summary>
    /// Process user profile requests by fetching profile data directly from CosmosDB and using AI to answer questions
    /// </summary>
    private async Task<string> ProcessUserProfileRequest(string question, string twinId, bool useChatClient)
    {
        try
        {
            _logger.LogInformation("🔍 Processing user profile request with AI-powered response");

            var cosmosService = new ProfileCosmosDB(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ProfileCosmosDB>(),
                (Microsoft.Extensions.Options.IOptions<CosmosDbSettings>)_configuration);

            var profileData = await cosmosService.GetProfileById(twinId);
            
            if (profileData == null)
            {
                _logger.LogWarning("❌ No profile found for Twin ID: {TwinId}", twinId);
                return $"""
                ❌ **No se encontró perfil**
                
                🆔 **Twin ID:** {twinId}
                📂 **Estado:** Perfil no existe en Azure Cosmos DB
                🔍 **Búsqueda:** Cross-partition query realizada
                
                💡 **Sugerencias:**
                • Verifica que el Twin ID sea correcto
                • El perfil podría no estar creado aún
                • Contacta al administrador para configurar el perfil
                """;
            }

            _logger.LogInformation("✅ Profile data retrieved successfully for: {FirstName} {LastName}", 
                profileData.FirstName, profileData.LastName);

            // Create AI agent with profile context to answer specific questions
            ChatCompletionAgent profileAgent = new()
            {
                Instructions = $"""
                🤖 **Asistente de Perfil Personal**

                 Eres el Twin de  {profileData.FirstName} {profileData.MiddleName} {profileData.LastName}
                 , un asistente digital que responde preguntas sobre el perfil personal del usuario.
                 respondes solo con datos reales del perfil almacenado en Azure Cosmos DB.
                 no digas que la data esta en cosmos. Siempre refere en tu respuesta como primera persona yo soy el Twin
                 de  {profileData.FirstName} {profileData.MiddleName} {profileData.LastName}
                 usa HTML elegante con colores y estilos inline.

                👤 **DATOS DEL PERFIL REAL DEL USUARIO:**
                
                📋 **Información Básica:**
                • Twin ID: {profileData.TwinId}
                • Nombre de Twin: {profileData.TwinName}
                • Nombre completo: {profileData.FirstName} {profileData.MiddleName} {profileData.LastName}
                • Apodo: {profileData.Nickname}
                
                📧 **Contacto:**
                • Email: {profileData.Email}
                • Teléfono: {profileData.Phone}
                • Dirección: {profileData.Address}
                
                🌍 **Ubicación:**
                • Ciudad: {profileData.City}
                • Estado/Provincia: {profileData.State}
                • País: {profileData.Country}
                • Nacionalidad: {profileData.Nationality}
                
                💼 **Información Profesional:**
                • Ocupación: {profileData.Occupation}
                • Empresa: {profileData.Company}
                
                👥 **Información Personal:**
                • Fecha de nacimiento: {profileData.DateOfBirth}
                • Estado civil: {profileData.MaritalStatus}
                • Relación familiar: {profileData.FamilyRelation}
                • Biografía personal: {profileData.PersonalBio}
                
                🌐 **Preferencias:**
                • Idiomas: {string.Join(", ", profileData.Languages)}
                • Intereses: {string.Join(", ", profileData.Interests)}
                
                🔒 **Configuración:**
                • Nivel de privacidad: {profileData.PrivacyLevel}
                • Gestión de cuenta: {profileData.AccountManagement}
                • Email autorizado: {profileData.OwnerEmail}

                📋 **INSTRUCCIONES PARA RESPONDER:**
                
                1. **Responde directamente** a la pregunta específica del usuario
                2. **Usa los datos reales** del perfil proporcionado arriba
                3. **Si no tienes un dato específico**, di que no está registrado en el perfil
                4. **Mantén un tono personal y amigable**, como si fueras el Twin Digital del usuario
                5. **Usa formato HTML elegante** con colores y estilos inline
                6. **Incluye emojis relevantes** para hacer la respuesta más amigable
                
                🚫 **NO HAGAS:**
                • No inventes datos que no estén en el perfil
                • No muestres datos claramente falsos o de prueba
                • No des información que no esté relacionada con la pregunta
                
                ✅ **EJEMPLOS DE RESPUESTAS:**
                
                Para "¿Cuál es mi email?":
                Responde con HTML elegante mostrando el email del perfil.
                
                Para "¿Dónde vivo?":
                Responde con HTML elegante mostrando la dirección del perfil.

                🎯 **IMPORTANTE:**
                • Siempre responde en español
                • Usa "tu/tus" cuando hables con el usuario sobre su información
                • Sé específico y directo en tu respuesta
                • Nunca digar este es tu xxxx siempre di yo soy el Twin de {profileData.FirstName} {profileData.MiddleName} {profileData.LastName}
                y esta es mi informacion. TU ERES EL TWIN Y ESTE ES TU PROFILE
                """,
                Kernel = CreateKernelWithFilter(useChatClient),
                Arguments = new KernelArguments(new PromptExecutionSettings() 
                { 
                    FunctionChoiceBehavior = FunctionChoiceBehavior.None()
                }),
            };

            // Create a chat for the profile question
            AgentGroupChat profileChat = new();

            // Send the user's specific question about their profile
            ChatMessageContent profileMessage = new(AuthorRole.User, question);
            profileChat.AddChatMessage(profileMessage);
            this.WriteAgentChatMessage(profileMessage);

            // Get the AI response about the user's profile
            var profileResponseBuilder = new StringBuilder();
            await foreach (ChatMessageContent response in profileChat.InvokeAsync(profileAgent))
            {
                this.WriteAgentChatMessage(response);
                
                if (!string.IsNullOrEmpty(response.Content) && response.Role == AuthorRole.Assistant)
                {
                    profileResponseBuilder.Append(response.Content);
                }
            }

            var aiProfileResponse = profileResponseBuilder.ToString().Trim();
            
            if (string.IsNullOrEmpty(aiProfileResponse))
            {
                _logger.LogWarning("⚠️ AI response was empty, falling back to basic profile info");
                return $"👤 Perfil encontrado para {profileData.FirstName} {profileData.LastName}";
            }

            _logger.LogInformation("✅ AI profile response generated successfully");
            return aiProfileResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing user profile request with AI");
            
            // Fallback to basic response if AI fails
            try
            {
                var cosmosService = new ProfileCosmosDB(
                    LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ProfileCosmosDB>(),
                    (Microsoft.Extensions.Options.IOptions<CosmosDbSettings>)_configuration);

                var profileData = await cosmosService.GetProfileById(twinId);
                
                if (profileData != null)
                {
                    return $"👤 Perfil encontrado para {profileData.FirstName} {profileData.LastName} (modo básico - {ex.Message})";
                }
            }
            catch
            {
                // Ignore fallback errors
            }
            
            return $"❌ Error en consulta de perfil: {ex.Message}";
        }
    }

    /// <summary>
    /// Process contact search requests using ContactsAgent
    /// </summary>
    private async Task<string> ProcessContactSearchRequest(string question, string twinId, bool useChatClient, UserIntentionResult? intentionResult = null)
    {
        try
        {
            _logger.LogInformation("👥 Processing contact search request");

            // Create ContactsAgent instance
            var contactsAgent = new ContactsAgent(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ContactsAgent>(),
                _configuration);

            // Determinar si requiere análisis y filtro basado en el intention result
            bool requiresAnalysis = intentionResult?.RequiereCalculo ?? false;
            bool requiresFiltering = intentionResult?.RequiereFiltro ?? false;

            // Determine if the question requires advanced filtering or analysis based on content
            if (!requiresFiltering)
            {
                requiresFiltering = ContainsContactFilterCriteria(question);
            }
            
            if (!requiresAnalysis)
            {
                requiresAnalysis = RequiresContactComplexAnalysis(question);
            }

            _logger.LogInformation("🔍 Contact search analysis: RequiresFiltering={RequiresFiltering}, RequiresAnalysis={RequiresAnalysis}", 
                requiresFiltering, requiresAnalysis);

            // Use ContactsAgent's intelligent processing
            var result = await contactsAgent.ProcessContactQuestionAsync(question, twinId, requiresAnalysis, requiresFiltering);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing contact search request");
            return $"❌ Error buscando contactos: {ex.Message}";
        }
    }

    /// <summary>
    /// Process photo search requests
    /// </summary>
    private async Task<string> ProcessPhotoSearchRequest(string question, string twinId, bool useChatClient)
    {
        try
        {
            _logger.LogInformation("📸 Processing photo search request");

            // Create PhotosAgent instance to handle the intelligent photo search
            var photosAgent = new PhotosAgent(
                LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<PhotosAgent>(),
                _configuration);

            // Determine if the question requires advanced filtering or analysis
            bool requiresFiltering = ContainsFilterCriteria(question);
            bool requiresAnalysis = RequiresComplexAnalysis(question);

            _logger.LogInformation("🔍 Photo search analysis: RequiresFiltering={RequiresFiltering}, RequiresAnalysis={RequiresAnalysis}", 
                requiresFiltering, requiresAnalysis);

            // Use PhotosAgent's intelligent processing
            var result = await photosAgent.ProcessPhotoQuestionAsync(question, twinId, requiresAnalysis, requiresFiltering);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing photo search request");
            return $"❌ Error buscando fotos: {ex.Message}";
        }
    }

    /// <summary>
    /// Process document search requests for unstructured documents (contracts, policies, etc.)
    /// </summary>
    private async Task<string> ProcessDocumentSearchRequest(string question, string twinId, bool useChatClient)
    {
        try
        {
            _logger.LogInformation("📄 Processing document search request");
            
            return $"""
            📄 **Búsqueda de Documentos No Estructurados**
            
            📋 **Procesando:** {question}
            🆔 **Twin ID:** {twinId}
            
            🚧 **Funcionalidad en desarrollo**
            Esta característica está siendo implementada para documentos no estructurados como:
            
            • 📄 Contratos y acuerdos comerciales
            • 🆔 Licencias y documentos de identidad  
            • 🏆 Certificados y credenciales
            • ⚖️ Documentos legales
            • 📑 Documentos PDF generales
            • 🛡️ Pólizas y seguros
            
            💡 **Nota:** Para facturas e invoices, el sistema automáticamente 
            las procesa con el motor financiero especializado.
            """;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing document search request");
            return $"❌ Error buscando documentos: {ex.Message}";
        }
    }

    /// <summary>
    /// Process general conversation requests
    /// </summary>
    private async Task<string> ProcessGeneralConversationRequest(string question, string twinId, bool useChatClient)
    {
        try
        {
            _logger.LogInformation("🗣️ Processing general conversation request");
            
            // Get current date and time for Austin, Texas
            var austinTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Central Standard Time");
            var currentTimeUtc = DateTime.UtcNow;
            var currentTimeAustin = TimeZoneInfo.ConvertTimeFromUtc(currentTimeUtc, austinTimeZone);
            var currentDateFormatted = currentTimeAustin.ToString("dddd, MMMM dd, yyyy", new System.Globalization.CultureInfo("es-ES"));
            var currentTimeFormatted = currentTimeAustin.ToString("HH:mm:ss");
            var timeZoneDisplayName = austinTimeZone.IsDaylightSavingTime(currentTimeAustin) ? "CDT" : "CST";

            // Create simple response with current time
            var htmlResponse = $"""
            <div style="background: linear-gradient(135deg, #ff9a9e 0%, #fecfef 50%, #fecfef 100%); padding: 20px; border-radius: 15px; color: #2c3e50; font-family: 'Segoe UI', Arial, sans-serif; box-shadow: 0 4px 15px rgba(0,0,0,0.1);">
                <h3 style="color: #e91e63; margin: 0 0 15px 0; display: flex; align-items: center;">
                    🗣️ Soy el Twin Digital 🗣️
                </h3>
                <div style="background: rgba(255,255,255,0.9); padding: 15px; border-radius: 10px; margin: 10px 0;">
                    <p style="margin: 0; line-height: 1.8; font-size: 16px;">¡Hola! Soy tu Twin Digital. Tu pregunta "{question}" ha sido procesada correctamente.</p>
                </div>
                <div style="background: rgba(76, 175, 80, 0.2); padding: 12px; border-radius: 8px; margin: 15px 0;">
                    <h4 style="color: #2e7d32; margin: 0 0 5px 0; font-size: 14px;">🕐 Hora actual aquí en Austin, Texas:</h4>
                    <p style="margin: 0; font-size: 15px; font-weight: 600; color: #1b5e20;">{currentDateFormatted} - {currentTimeFormatted} {timeZoneDisplayName}</p>
                </div>
                <div style="margin-top: 15px; font-size: 14px; color: #666; text-align: center;">
                    🌎 Austin, Texas, United States • 🆔 Twin ID: {twinId}
                </div>
            </div>
            """;

            return htmlResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing general conversation request");
            
            return $"""
            <div style="background: linear-gradient(135deg, #ff6b6b 0%, #ee5a52 100%); padding: 20px; border-radius: 15px; color: white; font-family: Arial, sans-serif;">
                <h3 style="color: #ffe66d; margin: 0 0 15px 0;">❌ Error en conversación general</h3>
                <p style="margin: 0; line-height: 1.6;">Lo siento, tuve un problema técnico: {ex.Message}</p>
                <p style="margin: 10px 0 0 0; font-size: 14px; opacity: 0.9;">🆔 Twin ID: {twinId}</p>
            </div>
            """;
        }
    }

    /// <summary>
    /// Determine if the contact question contains filter criteria
    /// </summary>
    private static bool ContainsContactFilterCriteria(string question)
    {
        var filterKeywords = new[] 
        { 
            "de familia", "de trabajo", "familia", "trabajo", "empresa", "compañía", 
            "con email", "con teléfono", "mexico", "+52", "microsoft", "google", 
            "amigos", "colegas", "relación",
            // ✅ NUEVO: Agregar palabras que indican búsqueda específica
            "encuentra a", "busca a", "encuentra en contactos a", "busca en contactos a",
            "jorge", "maria", "pedro", "juan", "angeles"
        };
        
        return filterKeywords.Any(keyword => question.ToLowerInvariant().Contains(keyword));
    }

    /// <summary>
    /// Determine if the contact question requires complex analysis
    /// </summary>
    private static bool RequiresContactComplexAnalysis(string question)
    {
        var analysisKeywords = new[] 
        { 
            "analiza", "compara", "estadísticas", "estadisticas", "resumen", 
            "cuántos", "total", "lista todos", "muéstrame todos", "todos los contactos"
        };
        
        return analysisKeywords.Any(keyword => question.ToLowerInvariant().Contains(keyword));
    }

    /// <summary>
    /// Determine if the question contains filter criteria
    /// </summary>
    private static bool ContainsFilterCriteria(string question)
    {
        var filterKeywords = new[] 
        { 
            "de familia", "de vacaciones", "con", "en", "desde", "hasta", "del", "año", "mes",
            "categoria", "categoría", "fecha", "persona", "lugar", "ubicación", "ubicacion"
        };
        
        return filterKeywords.Any(keyword => question.ToLowerInvariant().Contains(keyword));
    }

    /// <summary>
    /// Determine if the question requires complex analysis
    /// </summary>
    private static bool RequiresComplexAnalysis(string question)
    {
        var analysisKeywords = new[] 
        { 
            "analiza", "analiza", "compara", "estadísticas", "estadisticas", "resumen", 
            "patrones", "tendencias", "más", "mas", "menos", "mejor", "peor", "total", "suma"
        };
        
        return analysisKeywords.Any(keyword => question.ToLowerInvariant().Contains(keyword));
    }

    /// <summary>
    /// Processes a human question with complete intention-based routing to appropriate agents
    /// </summary>
    public async Task<string> ProcessHumanQuestionWithIntentionRouting(string humanQuestion, string twinId, bool useChatClient = true)
    {
        _logger.LogInformation($"🚀 Processing human question with intention routing for Twin ID: {twinId}");
        _logger.LogInformation($"❓ Human Question: {humanQuestion}");

        try
        {
            // Step 1: Determine the user's intention
            var intentionResult = await ProcessHumanQuestionAndDetermineIntention(humanQuestion, twinId, useChatClient);

            if (!intentionResult.Success)
            {
                _logger.LogWarning($"⚠️ Intention classification failed: {intentionResult.ErrorMessage}");
                return $"❌ **Error en clasificación de intención**\n\n" +
                       $"❓ **Pregunta:** {humanQuestion}\n" +
                       $"🚨 **Error:** {intentionResult.ErrorMessage}\n" +
                       $"💡 **Sugerencia:** Intenta reformular tu pregunta de manera más clara.";
            }

            _logger.LogInformation($"🎯 Intention classified as: {intentionResult.Intention} (Confidence: {intentionResult.Confidence:F2})");

            // Step 2: Route to appropriate agent based on intention
            string agentResponse;

            switch (intentionResult.Intention)
            {
                case UserIntention.BusquedaPerfil:
                    _logger.LogInformation("👤 Routing to UserProfile Agent");
                    agentResponse = await ProcessUserProfileRequest(humanQuestion, twinId, useChatClient);
                    break;

                case UserIntention.BusquedaFacturas:
                    _logger.LogInformation("💰 Routing to Invoices Agent");
                    agentResponse = await ProcessInvoiceSearchRequest(humanQuestion, twinId, useChatClient, intentionResult);
                    break;

                case UserIntention.BusquedaDocumentos:
                    _logger.LogInformation("📄 Routing to SearchDocuments Agent");
                    agentResponse = await ProcessDocumentSearchRequest(humanQuestion, twinId, useChatClient);
                    break;

                case UserIntention.BuscaContactos:
                    _logger.LogInformation("👥 Routing to Contacts Agent");
                    agentResponse = await ProcessContactSearchRequest(humanQuestion, twinId, useChatClient, intentionResult);
                    break;

                case UserIntention.BuscaFotos:
                    _logger.LogInformation("📸 Routing to ManagePictures Agent");
                    agentResponse = await ProcessPhotoSearchRequest(humanQuestion, twinId, useChatClient);
                    break;

                case UserIntention.Generica:
                default:
                    _logger.LogInformation("🗣️ Routing to General Conversation Agent");
                    agentResponse = await ProcessGeneralConversationRequest(humanQuestion, twinId, useChatClient);
                    break;
            }

            _logger.LogInformation($"✅ Complete intention-based response generated with {agentResponse.Length} characters");
            
            return agentResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error in intention-based question processing");
            return $"❌ **Error procesando tu pregunta**\n\n" +
                   $"❓ **Pregunta:** {humanQuestion}\n" +
                   $"🚨 **Error:** {ex.Message}\n" +
                   $"💡 **Sugerencia:** Por favor, intenta nuevamente con una pregunta más específica.";
        }
    }

    /// <summary>
    /// Classify user intention using keyword-based approach (fallback when AI classification fails)
    /// </summary>
    private UserIntentionResult ClassifyUsingKeywords(string humanQuestion, string twinId)
    {
        _logger.LogInformation("🔤 Using keyword-based classification as fallback");
        
        var questionLower = humanQuestion.ToLowerInvariant();
        UserIntention intention = UserIntention.Generica;
        float confidence = 0.7f; // Default confidence for keyword matching
        string reason = "";
        bool requiresCalculation = false;
        bool requiresFiltering = false;

        // Check for invoice-related keywords
        var invoiceKeywords = new[] { "facturas", "factura", "gastos", "gastado", "pagué", "pagado", "total", "suma", "cargo", "monto", "costo", "dinero" };
        if (invoiceKeywords.Any(keyword => questionLower.Contains(keyword)))
        {
            intention = UserIntention.BusquedaFacturas;
            reason = "Detectadas palabras relacionadas con facturas y gastos";
            requiresCalculation = questionLower.Contains("total") || questionLower.Contains("suma") || questionLower.Contains("cuánto");
            requiresFiltering = true;
            confidence = 0.9f;
        }
        // Check for contact-related keywords
        else if (questionLower.Contains("contacto") || questionLower.Contains("contactos") ||
                 questionLower.Contains("teléfono de") || questionLower.Contains("telefono de") ||
                 questionLower.Contains("email de") || questionLower.Contains("número de") ||
                 questionLower.Contains("encuentra a") || questionLower.Contains("busca a") ||
                 questionLower.Contains("dame el") || questionLower.Contains("dirección de") ||
                 (questionLower.Contains("angeles") && (questionLower.Contains("ruiz") || questionLower.Contains("teléfono") || questionLower.Contains("telefono"))) ||
                 (questionLower.Contains("jorge") && (questionLower.Contains("luna") || questionLower.Contains("teléfono") || questionLower.Contains("telefono"))) ||
                 (questionLower.Contains("maría") && (questionLower.Contains("garcía") || questionLower.Contains("teléfono") || questionLower.Contains("telefono"))))
        {
            intention = UserIntention.BuscaContactos;
            reason = "Detectadas palabras relacionadas con búsqueda de contactos y personas";
            requiresFiltering = questionLower.Contains("familia") || questionLower.Contains("trabajo") || questionLower.Contains("empresa");
            confidence = 0.9f;
        }
        // Check for photo-related keywords
        else if (questionLower.Contains("fotos") || questionLower.Contains("foto") || 
                 questionLower.Contains("imágenes") || questionLower.Contains("imagen") ||
                 questionLower.Contains("busca") || questionLower.Contains("encuentra") ||
                 questionLower.Contains("muéstrame") || questionLower.Contains("enséñame") ||
                 questionLower.Contains("ver") || questionLower.Contains("galería") ||
                 questionLower.Contains("álbum"))
        {
            intention = UserIntention.BuscaFotos;
            reason = "Detectadas palabras relacionadas con búsqueda de fotos";
            requiresFiltering = questionLower.Contains("de ") || questionLower.Contains("con ") || questionLower.Contains("en ");
            confidence = 0.8f;
        }
        // Check for profile-related keywords
        else if (questionLower.Contains("mi nombre") || questionLower.Contains("mi email") ||
                 questionLower.Contains("mi teléfono") || questionLower.Contains("dónde vivo") ||
                 questionLower.Contains("mi trabajo") || questionLower.Contains("mi ocupación") ||
                 questionLower.Contains("qué idiomas hablas") || questionLower.Contains("mi perfil") ||
                 questionLower.Contains("mi edad") || questionLower.Contains("años tengo"))
        {
            intention = UserIntention.BusquedaPerfil;
            reason = "Detectadas palabras relacionadas con información personal";
            confidence = 0.8f;
        }
        // Check for document-related keywords
        else if (questionLower.Contains("contratos") || questionLower.Contains("contrato") ||
                 questionLower.Contains("licencia") || questionLower.Contains("certificado") ||
                 questionLower.Contains("documentos") || questionLower.Contains("documento"))
        {
            intention = UserIntention.BusquedaDocumentos;
            reason = "Detectadas palabras relacionadas con documentos";
            requiresFiltering = true;
            confidence = 0.8f;
        }
        // Default to general conversation
        else
        {
            intention = UserIntention.Generica;
            reason = "No se detectaron palabras clave específicas, clasificado como conversación general";
            confidence = 0.6f;
        }

        return new UserIntentionResult
        {
            Intention = intention,
            SubTipo = "NO_APLICA",
            RequiereCalculo = requiresCalculation,
            RequiereFiltro = requiresFiltering,
            Confidence = confidence,
            Reason = reason,
            OriginalQuestion = humanQuestion,
            TwinId = twinId,
            ProcessedAt = DateTime.UtcNow,
            Success = true,
            ErrorMessage = null
        };
    }

    private Kernel CreateKernelWithFilter(bool useChatClient)
    {
        IKernelBuilder builder = Kernel.CreateBuilder();

        if (useChatClient)
        {
            base.AddChatClientToKernel(builder);
        }
        else
        {
            base.AddChatCompletionToKernel(builder);
        }

        builder.Services.AddSingleton<IAutoFunctionInvocationFilter>(new AutoInvocationFilter());

        return builder.Build();
    }

    private sealed class AutoInvocationFilter(bool terminate = true) : IAutoFunctionInvocationFilter
    {
        public async Task OnAutoFunctionInvocationAsync(AutoFunctionInvocationContext context, Func<AutoFunctionInvocationContext, Task> next)
        {
            // Execute the function
            await next(context);

            // Signal termination if the function is from specific plugins
            if (context.Function.PluginName == nameof(MenuPlugin) || 
                context.Function.PluginName == nameof(DateTimePlugin) ||
                context.Function.PluginName == nameof(UserProfilePlugin) ||
                context.Function.PluginName == nameof(SearchDocumentsPlugin) ||
                context.Function.PluginName == nameof(ManagePicturesPlugin) ||
                context.Function.PluginName == nameof(InvoicesAgent))
            {
                context.Terminate = terminate;
            }
        }
    }

    /// <summary>
    /// Compatibility methods for tests
    /// </summary>
    public async Task UseDateTimeAgentWithDirectInvocation(bool useChatClient = true)
    {
        _logger.LogInformation("⏰ UseDateTimeAgentWithDirectInvocation compatibility method called");
        await Task.CompletedTask;
    }

    public async Task<string> UseUserProfileAgentWithAgentChat(string userQuestion, bool useChatClient = true, string twinId = "388a31e7-d408-40f0-844c-4d2efedaa836")
    {
        _logger.LogInformation($"👤 UseUserProfileAgentWithAgentChat compatibility method called for: {userQuestion}");
        return await ProcessUserProfileRequest(userQuestion, twinId, useChatClient);
    }

    public async Task UseMenuAgentWithStreamingInvocation(bool useChatClient = true)
    {
        _logger.LogInformation("📋 UseMenuAgentWithStreamingInvocation compatibility method called");
        await Task.CompletedTask;
    }

    public async Task UseMultiPluginAgentWithStreamingChat(bool useChatClient = true)
    {
        _logger.LogInformation("🔄 UseMultiPluginAgentWithStreamingChat compatibility method called");
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("🗑️ TwinAgentClient disposed");
        await Task.CompletedTask;
    }
}

/// <summary>
/// Enumeration of possible user intentions
/// </summary>
public enum UserIntention
{
    /// <summary>
    /// General questions, greetings, small talk, casual conversation
    /// </summary>
    Generica,
    
    /// <summary>
    /// Questions about financial data, invoices, expenses, payments
    /// </summary>
    BusquedaFacturas,
    
    /// <summary>
    /// Questions about searching documents, files, reports, analysis (non-financial)
    /// </summary>
    BusquedaDocumentos,
    
    /// <summary>
    /// Questions about user profile, personal information, preferences
    /// </summary>
    BusquedaPerfil,
    
    /// <summary>
    /// Questions about contacts, people information, phone numbers, emails of other people
    /// </summary>
    BuscaContactos,
    
    /// <summary>
    /// Questions about photos, pictures, images, galleries
    /// </summary>
    BuscaFotos
}

/// <summary>
/// Result of user intention classification
/// </summary>
public class UserIntentionResult
{
    /// <summary>
    /// The classified intention
    /// </summary>
    public UserIntention Intention { get; set; }
    
    /// <summary>
    /// Sub-type classification for document searches
    /// </summary>
    public string SubTipo { get; set; } = "NO_APLICA";
    
    /// <summary>
    /// Whether the request requires calculations/analysis
    /// </summary>
    public bool RequiereCalculo { get; set; } = false;
    
    /// <summary>
    /// Whether the request requires specific filters (vendor, date, amount, etc.)
    /// </summary>
    public bool RequiereFiltro { get; set; } = false;
    
    /// <summary>
    /// Confidence level of the classification (0.0 to 1.0)
    /// </summary>
    public float Confidence { get; set; }
    
    /// <summary>
    /// Reason for the classification in Spanish
    /// </summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>
    /// The original question from the user
    /// </summary>
    public string OriginalQuestion { get; set; } = string.Empty;
    
    /// <summary>
    /// The Twin ID for which the question was processed
    /// </summary>
    public string TwinId { get; set; } = string.Empty;
    
    /// <summary>
    /// When the processing was completed
    /// </summary>
    public DateTime ProcessedAt { get; set; }
    
    /// <summary>
    /// Whether the classification was successful
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Error message if classification failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Check if the confidence level meets the minimum threshold
    /// </summary>
    public bool HasSufficientConfidence(float minimumThreshold = 0.6f)
    {
        return Success && Confidence >= minimumThreshold;
    }

    /// <summary>
    /// Determine if this is an invoice-related request
    /// </summary>
    public bool IsInvoiceRelated => Intention == UserIntention.BusquedaFacturas;

    /// <summary>
    /// Determine if this is a license-related request
    /// </summary>
    public bool IsLicenseRelated => Intention == UserIntention.BusquedaDocumentos && 
                                   SubTipo == "LICENCIAS";

    /// <summary>
    /// Determine if this request needs advanced search capabilities (filters + calculations)
    /// </summary>
    public bool NeedsAdvancedSearch => RequiereFiltro || RequiereCalculo;

    /// <summary>
    /// Get search complexity level based on requirements
    /// </summary>
    public string GetSearchComplexity()
    {
        if (RequiereFiltro && RequiereCalculo)
            return "🔬 Búsqueda Avanzada (Filtros + Cálculos)";
        else if (RequiereFiltro)
            return "🔍 Búsqueda con Filtros";
        else if (RequiereCalculo)
            return "🧮 Búsqueda con Cálculos";
        else
            return "🔎 Búsqueda Simple";
    }

    /// <summary>
    /// Extract potential filter criteria from the original question
    /// </summary>
    public Dictionary<string, string> ExtractFilterCriteria()
    {
        var filters = new Dictionary<string, string>();
        var question = OriginalQuestion.ToLowerInvariant();

        // Company/Vendor filters
        var companies = new[] { "microsoft", "amazon", "google", "apple", "oracle", "salesforce", "adobe" };
        foreach (var company in companies)
        {
            if (question.Contains(company))
            {
                filters["vendor"] = company;
                break;
            }
        }

        // Date filters
        if (question.Contains("2024")) filters["year"] = "2024";
        if (question.Contains("2025")) filters["year"] = "2025";
        if (question.Contains("este año")) filters["year"] = DateTime.Now.Year.ToString();
        if (question.Contains("año pasado")) filters["year"] = (DateTime.Now.Year - 1).ToString();
        if (question.Contains("diciembre")) filters["month"] = "12";
        if (question.Contains("enero")) filters["month"] = "1";
        if (question.Contains("q4") || question.Contains("cuarto trimestre")) filters["quarter"] = "4";

        // Amount filters (basic detection)
        if (question.Contains("más de") && question.Contains("$"))
        {
            filters["amount_operator"] = ">";
        }
        if (question.Contains("menos de") && question.Contains("$"))
        {
            filters["amount_operator"] = "<";
        }
        if (question.Contains("entre") && question.Contains("$"))
        {
            filters["amount_operator"] = "BETWEEN";
        }

        return filters;
    }
}