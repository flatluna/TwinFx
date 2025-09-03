# TwinEducationFunctions API Documentation

## Overview
Este archivo contiene las funciones Azure Functions para el manejo CRUD de registros de educaci�n para los Twins.

## Rutas de la API

### 1. Crear registro de educaci�n
- **Method:** `POST`
- **Route:** `/api/twins/{twinId}/education`
- **Descripci�n:** Crea un nuevo registro de educaci�n para un Twin espec�fico
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
  "description": "Carrera en Ciencias de la Computaci�n",
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
    "description": "Carrera en Ciencias de la Computaci�n",
    "achievements": "Magna Cum Laude",
    "gpa": "3.8",
    "credits": 120,
    "createdDate": "2024-01-15T10:30:00Z",
    "type": "education"
  },
  "message": "Education record created successfully"
}
```

### 2. Obtener todos los registros de educaci�n por Twin ID
- **Method:** `GET`
- **Route:** `/api/twins/{twinId}/education`
- **Descripci�n:** Obtiene todos los registros de educaci�n para un Twin espec�fico

### 3. Obtener registro de educaci�n espec�fico
- **Method:** `GET`
- **Route:** `/api/twins/{twinId}/education/{educationId}`
- **Descripci�n:** Obtiene un registro de educaci�n espec�fico por ID

### 4. Actualizar registro de educaci�n
- **Method:** `PUT`
- **Route:** `/api/twins/{twinId}/education/{educationId}`
- **Descripci�n:** Actualiza un registro de educaci�n existente
- **Body:** JSON con los datos actualizados de `EducationData` (formato snake_case)

### 5. Eliminar registro de educaci�n
- **Method:** `DELETE`
- **Route:** `/api/twins/{twinId}/education/{educationId}`
- **Descripci�n:** Elimina un registro de educaci�n espec�fico

## Caracter�sticas T�cnicas

### Mapeo de Campos JSON
La API acepta datos en formato **snake_case** (como los env�a el UI) y los mapea autom�ticamente a los campos internos:

| Campo UI (snake_case) | Campo Interno (PascalCase) | Descripci�n |
|----------------------|---------------------------|-------------|
| `education_type` | `EducationType` | Tipo de educaci�n |
| `degree_obtained` | `DegreeObtained` | T�tulo o grado obtenido |
| `field_of_study` | `FieldOfStudy` | Campo de estudio |
| `start_date` | `StartDate` | Fecha de inicio |
| `end_date` | `EndDate` | Fecha de finalizaci�n |
| `in_progress` | `InProgress` | Si est� en progreso |
| `institution` | `Institution` | Instituci�n educativa |
| `country` | `Country` | Pa�s |
| `description` | `Description` | Descripci�n |
| `achievements` | `Achievements` | Logros |
| `gpa` | `Gpa` | Promedio acad�mico |
| `credits` | `Credits` | Cr�ditos |

### Tipos de Educaci�n Soportados
- `primaria` - Educaci�n primaria
- `secundaria` - Educaci�n secundaria
- `preparatoria` - Educaci�n preparatoria/bachillerato
- `universidad` - Educaci�n universitaria
- `posgrado` - Estudios de posgrado
- `certificacion` - Certificaciones
- `diploma` - Diplomas
- `curso` - Cursos
- `otro` - Otros tipos de educaci�n

### CORS Support
- Soporta CORS para desarrollo local (`localhost:5173`, `localhost:3000`)
- Incluye manejo de requests OPTIONS para preflight

### Database
- Utiliza Azure Cosmos DB container: `TwinEducation`
- Partition Key: `CountryID`
- Document ID: `Id` (GUID)

### Error Handling
- Validaci�n de par�metros requeridos
- Manejo de errores HTTP est�ndar
- Logging detallado para debugging

### Response Format
Todas las respuestas siguen el formato est�ndar:
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

## Integraci�n con CosmosDbTwinProfileService

Este archivo utiliza los m�todos existentes en `CosmosDbTwinProfileService`:
- `CreateEducationAsync()`
- `GetEducationsByTwinIdAsync()`
- `GetEducationByIdAsync()`
- `UpdateEducationAsync()`
- `DeleteEducationAsync()`

## Seguridad

- Authorization Level: Anonymous (se puede cambiar seg�n los requerimientos)
- CORS configurado para desarrollo y producci�n
- Validaci�n de entrada de datos
- Logging de todas las operaciones para auditor�a

## Notas de Implementaci�n

### JSON Serialization
La API utiliza `JsonPropertyName` attributes para mapear autom�ticamente entre:
- **Input:** Formato snake_case del UI (ej. `education_type`)
- **Output:** Formato snake_case en las respuestas (mantiene consistencia con UI)
- **Internal:** Formato PascalCase para el modelo C# (ej. `EducationType`)

### Configuraci�n JsonSerializer
```csharp
new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
}
```

Esta configuraci�n asegura que:
1. Los campos pueden deserializarse sin importar may�sculas/min�sculas
2. Las respuestas mantienen el formato camelCase para consistencia con el frontend