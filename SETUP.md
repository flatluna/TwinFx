# TwinFx - Setup Instructions

## ?? Configuración de Seguridad

### ?? IMPORTANTE: local.settings.json
Este archivo contiene llaves sensibles y **NUNCA debe ser subido al repositorio**.

### Crear local.settings.json

Crea el archivo `local.settings.json` en la raíz del proyecto con esta estructura:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    
    "COSMOS_ACCOUNT_NAME": "[tu-cosmos-account-name]",
    "COSMOS_DATABASE_NAME": "TwinHumanDB",
    "COSMOS_KEY": "[tu-cosmos-primary-key]",
    
    "AZURE_STORAGE_ACCOUNT_NAME": "[tu-storage-account-name]",
    "AZURE_STORAGE_ACCOUNT_KEY": "[tu-storage-account-key]",
    
    "AzureOpenAI:Endpoint": "https://[tu-openai-resource].openai.azure.com/",
    "AzureOpenAI:ApiKey": "[tu-openai-api-key]",
    "AzureOpenAI:DeploymentName": "gpt-4",
    
    "DocumentIntelligence:Endpoint": "https://[tu-doc-intelligence].cognitiveservices.azure.com/",
    "DocumentIntelligence:ApiKey": "[tu-doc-intelligence-key]",
    
    "AZURE_SEARCH_ENDPOINT": "https://[tu-search-service].search.windows.net",
    "AZURE_SEARCH_API_KEY": "[tu-search-api-key]"
  }
}
```

### ??? Seguridad Verificada

? **local.settings.json está en .gitignore**
? **No hay llaves hardcodeadas en el código**
? **Archivos de configuración están excluidos**
? **Variables de entorno están protegidas**

### ??? Servicios de Azure Requeridos

1. **Azure Functions** (Runtime v4, .NET 8)
2. **Azure Cosmos DB** (Containers: TwinProfiles, TwinEducation, TwinContacts, TwinInvoices, TwinPictures)
3. **Azure Data Lake Storage Gen2** (Para archivos y fotos)
4. **Azure Document Intelligence** (Para procesamiento de documentos)
5. **Azure OpenAI** (Modelo GPT-4 recomendado)
6. **Azure AI Search** (Para búsqueda semántica)

### ?? Comandos de Setup

```bash
# Restaurar dependencias
dotnet restore

# Build del proyecto
dotnet build

# Ejecutar localmente
func start

# Ejecutar tests
dotnet test
```

### ?? Verificación de Configuración

Después de configurar, prueba estos endpoints localmente:

- `GET http://localhost:7071/api/profile/{twinId}` - Verificar acceso a Cosmos DB
- `POST http://localhost:7071/api/process-question` - Verificar Azure OpenAI
- `POST http://localhost:7071/api/upload-document` - Verificar Document Intelligence

### ?? Contenedores Cosmos DB

Crea estos contenedores en tu Azure Cosmos DB:

| Container | Partition Key | Descripción |
|-----------|---------------|-------------|
| TwinProfiles | /CountryID | Perfiles de usuarios |
| TwinEducation | /CountryID | Registros educativos |
| TwinContacts | /TwinID | Contactos personales |
| TwinInvoices | /TwinID | Facturas procesadas |
| TwinPictures | /TwinID | Metadatos de fotos |

### ??? Estructura Data Lake

```
/{twinId}/
??? profile/picture/           # Fotos de perfil
??? documentos/               # Documentos procesados
??? semi-estructurado/        # Documentos semi-estructurados
??? facturas/                 # Facturas
```

### ? Variables de Entorno para Producción

Para deployment en Azure, configura estas Application Settings:

- COSMOS_KEY
- AZURE_STORAGE_ACCOUNT_KEY  
- AzureOpenAI:ApiKey
- DocumentIntelligence:ApiKey
- AZURE_SEARCH_API_KEY

### ?? Troubleshooting

**Error: "COSMOS_KEY not found"**
- Verifica que local.settings.json existe y tiene la clave correcta

**Error: "Document Intelligence authentication failed"**  
- Verifica endpoint y API key de Document Intelligence

**Error: "Azure OpenAI quota exceeded"**
- Verifica que tienes quota disponible en tu Azure OpenAI resource

**Error: "Storage account not found"**
- Verifica nombre y key de tu Azure Storage Account

### ?? Soporte

Si encuentras problemas de configuración:
1. Verifica que todos los servicios de Azure están desplegados
2. Confirma que las llaves tienen los permisos correctos
3. Revisa los logs de Azure Functions para errores específicos