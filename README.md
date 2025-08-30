# TwinFx - Digital Twin Functions Platform

?? **Plataforma de Azure Functions para la gestión completa de Digital Twins con IA integrada**

## ?? Descripción General

TwinFx es una plataforma completa construida sobre Azure Functions que permite la gestión de "Digital Twins" (gemelos digitales) de personas, integrando múltiples servicios de Azure AI y almacenamiento en la nube. La plataforma incluye capacidades avanzadas de procesamiento de documentos, análisis de facturas, gestión de fotos, contactos, educación y más.

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

## ?? Características Principales

### ?? **Agentes IA Inteligentes**
- **TwinAgentClient**: Cliente conversacional principal con clasificación inteligente de intenciones
- **PhotosAgent**: Gestión y búsqueda de fotos con IA
- **InvoicesAgent**: Análisis avanzado de facturas con cálculos dinámicos
- **ContactsAgent**: Gestión inteligente de contactos

### ?? **Procesamiento de Documentos**
- Extracción automática de datos usando Azure Document Intelligence
- Análisis de facturas con detección de LineItems ilimitados
- Procesamiento de documentos semi-estructurados
- Generación de metadatos inteligentes

### ?? **Búsqueda Avanzada**
- Búsqueda semántica con Azure AI Search
- Indexación automática de documentos
- Búsqueda vectorial para contenido similar

### ?? **Gestión de Datos**
- **Perfiles de Twins**: Información personal completa
- **Contactos**: Gestión de relaciones personales/profesionales
- **Educación**: Historial académico con snake_case mapping
- **Facturas**: Análisis financiero detallado
- **Fotos**: Gestión de galería personal

## ?? Inicio Rápido

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

### Configuración

1. **Clona el repositorio:**
```bash
git clone https://github.com/flatluna/TwinFx.git
cd TwinFx
```

2. **Configura las variables de entorno:**

Crea un archivo `local.settings.json` en la raíz del proyecto:

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

?? **IMPORTANTE:** Nunca subas `local.settings.json` al repositorio. Está incluido en `.gitignore`.

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
GET    /api/twins/{twinId}/education      # Listar educación
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
?   ??? PhotosAgent.cs            # Gestión de fotos
?   ??? InvoicesAgent.cs          # Análisis de facturas
?   ??? ContactsAgent.cs          # Gestión de contactos
?   ??? ProcessDocumentDataAgent.cs # Procesamiento de documentos
??? ?? Functions/                 # Azure Functions (API endpoints)
?   ??? ProcessQuestionFunction.cs # Endpoint conversacional
?   ??? TwinEducationFunctions.cs # API de educación
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
?   ??? SearchDocumentsPlugin.cs  # Plugin de búsqueda
??? ?? Models/                    # Modelos de datos
??? ?? Tests/                     # Tests unitarios
```

## ?? Flujos Principales

### 1. **Conversación Inteligente**
```
Usuario ? ProcessQuestionFunction ? TwinAgentClient ? 
Clasificación de Intención ? Agent Específico ? Respuesta IA
```

### 2. **Procesamiento de Documentos**
```
Upload ? Document Intelligence ? Extracción de Datos ? 
Análisis con IA ? Almacenamiento en Cosmos DB
```

### 3. **Gestión de Datos**
```
API Request ? Validation ? Cosmos DB Operation ? 
Response con snake_case mapping
```

## ?? Tecnologías IA Integradas

- **Microsoft Semantic Kernel**: Orquestación de agentes IA
- **Azure OpenAI GPT-4**: Generación de respuestas inteligentes
- **Azure Document Intelligence**: Extracción de datos de documentos
- **Azure AI Search**: Búsqueda semántica y vectorial
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

- ? **No hay llaves hardcodeadas** en el código
- ? **local.settings.json** excluido del repositorio
- ? **Validación de entrada** en todas las APIs
- ? **Logging seguro** sin exposición de datos sensibles
- ? **CORS configurado** para desarrollo y producción

## ?? Testing

Ejecutar tests:
```bash
dotnet test
```

Proyectos de test:
- **TwinFxTests**: Tests unitarios e integración

## ?? Funcionalidades Avanzadas

### ?? **Análisis Dinámico de Facturas**
- Soporte para LineItems ilimitados (no limitado a 10)
- Generación de CSV dinámico para análisis
- Cálculos financieros complejos con IA
- Filtrado inteligente con SQL generado por IA

### ??? **Gestión Inteligente de Fotos**
- Generación automática de SAS URLs (24h)
- Búsqueda de fotos por contenido y personas
- Análisis de metadatos con IA
- Categorización automática

### ?? **Sistema de Educación**
- Mapeo automático snake_case ? PascalCase
- Soporte para múltiples tipos de educación
- Validación de datos académicos
- Búsqueda por institución y período

## ?? Deployment

### Azure Functions
```bash
func azure functionapp publish tu-function-app-name
```

### CI/CD
El proyecto está configurado para deployment automático con Azure DevOps/GitHub Actions.

## ?? Contribución

1. Fork el proyecto
2. Crea una rama de feature (`git checkout -b feature/AmazingFeature`)
3. Commit tus cambios (`git commit -m 'Add some AmazingFeature'`)
4. Push a la rama (`git push origin feature/AmazingFeature`)
5. Abre un Pull Request

## ?? Licencia

Este proyecto está bajo la licencia MIT. Ver `LICENSE` para más detalles.

## ?? Soporte

Para soporte técnico o preguntas:
- ?? Email: [tu-email]
- ?? Issues: [GitHub Issues]
- ?? Wiki: [Project Wiki]

## ??? Roadmap

- [ ] Integración con Microsoft Graph
- [ ] Soporte para múltiples idiomas
- [ ] Dashboard analytics en tiempo real
- [ ] Integración con Power BI
- [ ] Módulo de notificaciones
- [ ] API REST completa con OpenAPI

---

**Desarrollado con ?? usando Azure Functions y .NET 8**

*Última actualización: Enero 2025*