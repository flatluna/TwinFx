# TwinFx - Digital Twin Functions Platform

?? **Plataforma de Azure Functions para la gesti�n completa de Digital Twins con IA integrada**

## ?? Descripci�n General

TwinFx es una plataforma completa construida sobre Azure Functions que permite la gesti�n de "Digital Twins" (gemelos digitales) de personas, integrando m�ltiples servicios de Azure AI y almacenamiento en la nube. La plataforma incluye capacidades avanzadas de procesamiento de documentos, an�lisis de facturas, gesti�n de fotos, contactos, educaci�n y m�s.

## ??? Arquitectura

```
TwinFx Platform
??? ?? AI Agents (Semantic Kernel)
??? ?? Document Processing (Azure Document Intelligence)
??? ?? Search & Analytics (Azure AI Search)
??? ?? Data Storage (Azure Cosmos DB)
??? ?? File Storage (Azure Data Lake)
??? ? API Layer (Azure Functions)
??? ?? Management Tools
```

## ?? Caracter�sticas Principales

### ?? **Agentes IA Inteligentes**
- **TwinAgentClient**: Cliente conversacional principal con clasificaci�n inteligente de intenciones
- **PhotosAgent**: Gesti�n y b�squeda de fotos con IA
- **InvoicesAgent**: An�lisis avanzado de facturas con c�lculos din�micos
- **ContactsAgent**: Gesti�n inteligente de contactos

### ?? **Procesamiento de Documentos**
- Extracci�n autom�tica de datos usando Azure Document Intelligence
- An�lisis de facturas con detecci�n de LineItems ilimitados
- Procesamiento de documentos semi-estructurados
- Generaci�n de metadatos inteligentes

### ?? **B�squeda Avanzada**
- B�squeda sem�ntica con Azure AI Search
- Indexaci�n autom�tica de documentos
- B�squeda vectorial para contenido similar

### ?? **Gesti�n de Datos**
- **Perfiles de Twins**: Informaci�n personal completa
- **Contactos**: Gesti�n de relaciones personales/profesionales
- **Educaci�n**: Historial acad�mico con snake_case mapping
- **Facturas**: An�lisis financiero detallado
- **Fotos**: Gesti�n de galer�a personal

## ?? Inicio R�pido

### Prerrequisitos

1. **Azure Subscription** con los siguientes servicios:
   - Azure Functions (Runtime v4, .NET 8)
   - Azure Cosmos DB
   - Azure Data Lake Storage Gen2
   - Azure Document Intelligence
   - Azure OpenAI
   - Azure AI Search

2. **Herramientas de desarrollo:**
   - Visual Studio 2022 / VS Code
   - Azure Functions Core Tools
   - .NET 8 SDK

### Configuraci�n

1. **Clona el repositorio:**
```bash
git clone [repository-url]
cd TwinFx
```

2. **Configura las variables de entorno:**

Crea un archivo `local.settings.json` en la ra�z del proyecto:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    
    "COSMOS_ACCOUNT_NAME": "tu-cosmos-account",
    "COSMOS_DATABASE_NAME": "TwinHumanDB",
    "COSMOS_KEY": "tu-cosmos-key",
    
    "AZURE_STORAGE_ACCOUNT_NAME": "tu-storage-account",
    "AZURE_STORAGE_ACCOUNT_KEY": "tu-storage-key",
    
    "AzureOpenAI:Endpoint": "https://tu-openai.openai.azure.com/",
    "AzureOpenAI:ApiKey": "tu-openai-key",
    "AzureOpenAI:DeploymentName": "gpt-4",
    
    "DocumentIntelligence:Endpoint": "https://tu-doc-intelligence.cognitiveservices.azure.com/",
    "DocumentIntelligence:ApiKey": "tu-doc-intelligence-key",
    
    "AZURE_SEARCH_ENDPOINT": "https://tu-search.search.windows.net",
    "AZURE_SEARCH_API_KEY": "tu-search-key"
  }
}
```

?? **IMPORTANTE:** Nunca subas `local.settings.json` al repositorio. Est� incluido en `.gitignore`.

3. **Restaura dependencias:**
```bash
dotnet restore
```

4. **Ejecuta localmente:**
```bash
func start
```

## ?? API Documentation

### ????? **Twin Profiles**
```http
GET    /api/profile/{twinId}              # Obtener perfil
POST   /api/profile                       # Crear perfil
PUT    /api/profile/{twinId}              # Actualizar perfil
```

### ?? **Education Management**
```http
GET    /api/twins/{twinId}/education      # Listar educaci�n
POST   /api/twins/{twinId}/education      # Crear registro
PUT    /api/twins/{twinId}/education/{id} # Actualizar registro
DELETE /api/twins/{twinId}/education/{id} # Eliminar registro
```

### ?? **Contacts Management**
```http
GET    /api/twins/{twinId}/contacts       # Listar contactos
POST   /api/twins/{twinId}/contacts       # Crear contacto
PUT    /api/twins/{twinId}/contacts/{id}  # Actualizar contacto
DELETE /api/twins/{twinId}/contacts/{id}  # Eliminar contacto
```

### ?? **Document Processing**
```http
POST   /api/upload-document               # Subir y procesar documento
POST   /api/process-question              # Procesar pregunta conversacional
```

### ?? **Photo Management**
```http
POST   /api/upload-photo                  # Subir foto
GET    /api/photos/{twinId}               # Listar fotos
```

## ?? Estructura del Proyecto

```
TwinFx/
??? ?? Agents/                    # Agentes IA
?   ??? TwinAgentClient.cs        # Cliente conversacional principal
?   ??? PhotosAgent.cs            # Gesti�n de fotos
?   ??? InvoicesAgent.cs          # An�lisis de facturas
?   ??? ContactsAgent.cs          # Gesti�n de contactos
?   ??? ProcessDocumentDataAgent.cs # Procesamiento de documentos
??? ?? Functions/                 # Azure Functions (API endpoints)
?   ??? ProcessQuestionFunction.cs # Endpoint conversacional
?   ??? TwinEducationFunctions.cs # API de educaci�n
?   ??? ContactsFunction.cs       # API de contactos
?   ??? UploadDocumentFunction.cs # Upload de documentos
?   ??? UploadPhotoFunction.cs    # Upload de fotos
??? ?? Services/                  # Servicios core
?   ??? CosmosDbServices.cs       # Acceso a Cosmos DB
?   ??? PhotoDocument.cs          # Modelos de fotos
?   ??? DataLakeClientFactory.cs  # Cliente de Data Lake
??? ?? Plugins/                   # Plugins para Semantic Kernel
?   ??? UserProfilePlugin.cs      # Plugin de perfiles
?   ??? ManagePicturesPlugin.cs   # Plugin de fotos
?   ??? SearchDocumentsPlugin.cs  # Plugin de b�squeda
??? ?? Models/                    # Modelos de datos
??? ?? Tests/                     # Tests unitarios
```

## ?? Flujos Principales

### 1. **Conversaci�n Inteligente**
```
Usuario ? ProcessQuestionFunction ? TwinAgentClient ? 
Clasificaci�n de Intenci�n ? Agent Espec�fico ? Respuesta IA
```

### 2. **Procesamiento de Documentos**
```
Upload ? Document Intelligence ? Extracci�n de Datos ? 
An�lisis con IA ? Almacenamiento en Cosmos DB
```

### 3. **Gesti�n de Datos**
```
API Request ? Validation ? Cosmos DB Operation ? 
Response con snake_case mapping
```

## ?? Tecnolog�as IA Integradas

- **Microsoft Semantic Kernel**: Orquestaci�n de agentes IA
- **Azure OpenAI GPT-4**: Generaci�n de respuestas inteligentes
- **Azure Document Intelligence**: Extracci�n de datos de documentos
- **Azure AI Search**: B�squeda sem�ntica y vectorial
- **Custom AI Agents**: Agentes especializados por dominio

## ?? Bases de Datos

### Azure Cosmos DB Containers:
- **TwinProfiles**: Perfiles de usuarios
- **TwinEducation**: Historial educativo (Partition Key: CountryID)
- **TwinContacts**: Contactos personales (Partition Key: TwinID)
- **TwinInvoices**: Facturas procesadas (Partition Key: TwinID)
- **TwinPictures**: Metadatos de fotos (Partition Key: TwinID)

### Azure Data Lake:
```
/{twinId}/
??? profile/picture/           # Fotos de perfil
??? documentos/               # Documentos procesados
??? semi-estructurado/        # Documentos semi-estructurados
??? facturas/                 # Facturas
```

## ??? Seguridad

- ? **No hay llaves hardcodeadas** en el c�digo
- ? **local.settings.json** excluido del repositorio
- ? **Validaci�n de entrada** en todas las APIs
- ? **Logging seguro** sin exposici�n de datos sensibles
- ? **CORS configurado** para desarrollo y producci�n

## ?? Testing

Ejecutar tests:
```bash
dotnet test
```

Proyectos de test:
- **TwinFxTests**: Tests unitarios e integraci�n

## ?? Funcionalidades Avanzadas

### ?? **An�lisis Din�mico de Facturas**
- Soporte para LineItems ilimitados (no limitado a 10)
- Generaci�n de CSV din�mico para an�lisis
- C�lculos financieros complejos con IA
- Filtrado inteligente con SQL generado por IA

### ??? **Gesti�n Inteligente de Fotos**
- Generaci�n autom�tica de SAS URLs (24h)
- B�squeda de fotos por contenido y personas
- An�lisis de metadatos con IA
- Categorizaci�n autom�tica

### ?? **Sistema de Educaci�n**
- Mapeo autom�tico snake_case ? PascalCase
- Soporte para m�ltiples tipos de educaci�n
- Validaci�n de datos acad�micos
- B�squeda por instituci�n y per�odo

## ?? Deployment

### Azure Functions
```bash
func azure functionapp publish tu-function-app-name
```

### CI/CD
El proyecto est� configurado para deployment autom�tico con Azure DevOps/GitHub Actions.

## ?? Contribuci�n

1. Fork el proyecto
2. Crea una rama de feature (`git checkout -b feature/AmazingFeature`)
3. Commit tus cambios (`git commit -m 'Add some AmazingFeature'`)
4. Push a la rama (`git push origin feature/AmazingFeature`)
5. Abre un Pull Request

## ?? Licencia

Este proyecto est� bajo la licencia MIT. Ver `LICENSE` para m�s detalles.

## ?? Soporte

Para soporte t�cnico o preguntas:
- ?? Email: [tu-email]
- ?? Issues: [GitHub Issues]
- ?? Wiki: [Project Wiki]

## ??? Roadmap

- [ ] Integraci�n con Microsoft Graph
- [ ] Soporte para m�ltiples idiomas
- [ ] Dashboard analytics en tiempo real
- [ ] Integraci�n con Power BI
- [ ] M�dulo de notificaciones
- [ ] API REST completa con OpenAPI

---

**Desarrollado con ?? usando Azure Functions y .NET 8**

*�ltima actualizaci�n: Enero 2025*