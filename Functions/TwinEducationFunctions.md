# TwinEducationFunctions API Documentation

## Overview
Este archivo contiene las funciones Azure Functions para el manejo CRUD de registros de educación para los Twins.

## Rutas de la API

### 1. Crear registro de educación
- **Method:** `POST`
- **Route:** `/api/twins/{twinId}/education`
- **Descripción:** Crea un nuevo registro de educación para un Twin específico
- **Body:** JSON con los datos de `EducationData`

#### Ejemplo de request body (formato esperado desde UI):
```json
{
  "institution": "Universidad de Texas",
  "education_type": "universidad",
  "degree_obtained": "Bachelor of Science",
  "field_of_study": "Computer Science",
  "start_date": "2018-08-01",
  "end_date": "2022-05-15",
  "in_progress": false,
  "country": "United States",
  "description": "Carrera en Ciencias de la Computación",
  "achievements": "Magna Cum Laude",
  "gpa": "3.8",
  "credits": 120
}
```

#### Ejemplo de response body (formato de salida):
```json
{
  "success": true,
  "education": {
    "id": "generated-guid",
    "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
    "countryId": "US",
    "institution": "Universidad de Texas",
    "education_type": "universidad",
    "degree_obtained": "Bachelor of Science",
    "field_of_study": "Computer Science",
    "start_date": "2018-08-01",
    "end_date": "2022-05-15",
    "in_progress": false,
    "country": "United States",
    "description": "Carrera en Ciencias de la Computación",
    "achievements": "Magna Cum Laude",
    "gpa": "3.8",
    "credits": 120,
    "createdDate": "2024-01-15T10:30:00Z",
    "type": "education"
  },
  "message": "Education record created successfully"
}
```

### 2. Obtener todos los registros de educación por Twin ID
- **Method:** `GET`
- **Route:** `/api/twins/{twinId}/education`
- **Descripción:** Obtiene todos los registros de educación para un Twin específico

### 3. Obtener registro de educación específico
- **Method:** `GET`
- **Route:** `/api/twins/{twinId}/education/{educationId}`
- **Descripción:** Obtiene un registro de educación específico por ID

### 4. Actualizar registro de educación
- **Method:** `PUT`
- **Route:** `/api/twins/{twinId}/education/{educationId}`
- **Descripción:** Actualiza un registro de educación existente
- **Body:** JSON con los datos actualizados de `EducationData` (formato snake_case)

### 5. Eliminar registro de educación
- **Method:** `DELETE`
- **Route:** `/api/twins/{twinId}/education/{educationId}`
- **Descripción:** Elimina un registro de educación específico

## Características Técnicas

### Mapeo de Campos JSON
La API acepta datos en formato **snake_case** (como los envía el UI) y los mapea automáticamente a los campos internos:

| Campo UI (snake_case) | Campo Interno (PascalCase) | Descripción |
|----------------------|---------------------------|-------------|
| `education_type` | `EducationType` | Tipo de educación |
| `degree_obtained` | `DegreeObtained` | Título o grado obtenido |
| `field_of_study` | `FieldOfStudy` | Campo de estudio |
| `start_date` | `StartDate` | Fecha de inicio |
| `end_date` | `EndDate` | Fecha de finalización |
| `in_progress` | `InProgress` | Si está en progreso |
| `institution` | `Institution` | Institución educativa |
| `country` | `Country` | País |
| `description` | `Description` | Descripción |
| `achievements` | `Achievements` | Logros |
| `gpa` | `Gpa` | Promedio académico |
| `credits` | `Credits` | Créditos |

### Tipos de Educación Soportados
- `primaria` - Educación primaria
- `secundaria` - Educación secundaria
- `preparatoria` - Educación preparatoria/bachillerato
- `universidad` - Educación universitaria
- `posgrado` - Estudios de posgrado
- `certificacion` - Certificaciones
- `diploma` - Diplomas
- `curso` - Cursos
- `otro` - Otros tipos de educación

### CORS Support
- Soporta CORS para desarrollo local (`localhost:5173`, `localhost:3000`)
- Incluye manejo de requests OPTIONS para preflight

### Database
- Utiliza Azure Cosmos DB container: `TwinEducation`
- Partition Key: `CountryID`
- Document ID: `Id` (GUID)

### Error Handling
- Validación de parámetros requeridos
- Manejo de errores HTTP estándar
- Logging detallado para debugging

### Response Format
Todas las respuestas siguen el formato estándar:
```json
{
  "success": boolean,
  "education": EducationData | null,
  "educationRecords": EducationData[] | null,
  "errorMessage": string | null,
  "message": string | null,
  "twinId": string,
  "educationId": string | null,
  "count": number | null
}
```

## Integración con CosmosDbTwinProfileService

Este archivo utiliza los métodos existentes en `CosmosDbTwinProfileService`:
- `CreateEducationAsync()`
- `GetEducationsByTwinIdAsync()`
- `GetEducationByIdAsync()`
- `UpdateEducationAsync()`
- `DeleteEducationAsync()`

## Seguridad

- Authorization Level: Anonymous (se puede cambiar según los requerimientos)
- CORS configurado para desarrollo y producción
- Validación de entrada de datos
- Logging de todas las operaciones para auditoría

## Notas de Implementación

### JSON Serialization
La API utiliza `JsonPropertyName` attributes para mapear automáticamente entre:
- **Input:** Formato snake_case del UI (ej. `education_type`)
- **Output:** Formato snake_case en las respuestas (mantiene consistencia con UI)
- **Internal:** Formato PascalCase para el modelo C# (ej. `EducationType`)

### Configuración JsonSerializer
```csharp
new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
}
```

Esta configuración asegura que:
1. Los campos pueden deserializarse sin importar mayúsculas/minúsculas
2. Las respuestas mantienen el formato camelCase para consistencia con el frontend