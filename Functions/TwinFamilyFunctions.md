# TwinFamilyFunctions API Documentation

## Overview
Este archivo contiene las funciones Azure Functions para el manejo CRUD de registros de familia para los Twins.

## Rutas de la API

### 1. Crear familiar
- **Method:** `POST`
- **Route:** `/api/twins/{twinId}/family`
- **Descripci�n:** Crea un nuevo registro de familiar para un Twin espec�fico
- **Body:** JSON con los datos de `FamilyData`

#### Ejemplo de request body (formato esperado desde UI):
```json
{
  "parentesco": "Hijo",
  "nombre": "Mar�a",
  "apellido": "Gonz�lez",
  "fecha_nacimiento": "01/15/1995",
  "numero_celular": "(555) 123-4567",
  "email": "maria.gonzalez@email.com",
  "url_foto": "https://ejemplo.com/foto.jpg",
  "notas": "Hija mayor, vive en otra ciudad"
}
```

#### Ejemplo de response body (formato de salida):
```json
{
  "success": true,
  "family": {
    "id": "generated-guid",
    "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
    "parentesco": "Hijo",
    "nombre": "Mar�a",
    "apellido": "Gonz�lez",
    "fechaNacimiento": "01/15/1995",
    "numeroCelular": "(555) 123-4567",
    "email": "maria.gonzalez@email.com",
    "urlFoto": "https://ejemplo.com/foto.jpg",
    "notas": "Hija mayor, vive en otra ciudad",
    "createdDate": "2024-01-15T10:30:00Z",
    "type": "family"
  },
  "message": "Family member created successfully"
}
```

### 2. Obtener todos los familiares por Twin ID
- **Method:** `GET`
- **Route:** `/api/twins/{twinId}/family`
- **Descripci�n:** Obtiene todos los registros de familiares para un Twin espec�fico

#### Ejemplo de response body:
```json
{
  "success": true,
  "family": [
    {
      "id": "family-guid-1",
      "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
      "parentesco": "Padre",
      "nombre": "Carlos",
      "apellido": "Gonz�lez",
      "fechaNacimiento": "05/10/1965",
      "numeroCelular": "(555) 987-6543",
      "email": "carlos.gonzalez@email.com",
      "urlFoto": "",
      "notas": "Jubilado, aficionado al golf",
      "createdDate": "2024-01-15T10:30:00Z",
      "type": "family"
    },
    {
      "id": "family-guid-2",
      "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
      "parentesco": "Madre",
      "nombre": "Ana",
      "apellido": "L�pez",
      "fechaNacimiento": "08/22/1968",
      "numeroCelular": "(555) 456-7890",
      "email": "ana.lopez@email.com",
      "urlFoto": "https://ejemplo.com/foto-ana.jpg",
      "notas": "Profesora de primaria",
      "createdDate": "2024-01-15T10:30:00Z",
      "type": "family"
    }
  ],
  "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
  "count": 2
}
```

### 3. Obtener familiar espec�fico
- **Method:** `GET`
- **Route:** `/api/twins/{twinId}/family/{familyId}`
- **Descripci�n:** Obtiene un registro espec�fico de familiar por ID

#### Ejemplo de response body:
```json
{
  "success": true,
  "family": {
    "id": "family-guid-1",
    "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
    "parentesco": "Hermano",
    "nombre": "Luis",
    "apellido": "Gonz�lez",
    "fechaNacimiento": "03/08/1992",
    "numeroCelular": "+1234567890",
    "email": "luis.gonzalez@email.com",
    "urlFoto": "https://ejemplo.com/foto-luis.jpg",
    "notas": "Ingeniero de software, vive en el extranjero",
    "createdDate": "2024-01-15T10:30:00Z",
    "type": "family"
  },
  "familyId": "family-guid-1",
  "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836"
}
```

### 4. Actualizar familiar
- **Method:** `PUT`
- **Route:** `/api/twins/{twinId}/family/{familyId}`
- **Descripci�n:** Actualiza un registro existente de familiar
- **Body:** JSON con los datos actualizados de `FamilyData`

#### Ejemplo de request body:
```json
{
  "parentesco": "Hermano",
  "nombre": "Luis Fernando",
  "apellido": "Gonz�lez",
  "fecha_nacimiento": "03/08/1992",
  "numero_celular": "+1234567890",
  "email": "luis.fernando.gonzalez@email.com",
  "url_foto": "https://ejemplo.com/foto-luis-actualizada.jpg",
  "notas": "Ingeniero de software, recientemente promovido a Senior Developer"
}
```

### 5. Eliminar familiar
- **Method:** `DELETE`
- **Route:** `/api/twins/{twinId}/family/{familyId}`
- **Descripci�n:** Elimina un registro de familiar por ID

#### Ejemplo de response body:
```json
{
  "success": true,
  "familyId": "family-guid-1",
  "twinId": "388a31e7-d408-40f0-844c-4d2efedaa836",
  "message": "Family member deleted successfully"
}
```

## Tipos de Parentesco Sugeridos

- **Padre**
- **Madre**
- **Hijo**
- **Hija**
- **Hermano**
- **Hermana**
- **Abuelo**
- **Abuela**
- **Nieto**
- **Nieta**
- **T�o**
- **T�a**
- **Primo**
- **Prima**
- **Esposo**
- **Esposa**
- **Suegro**
- **Suegra**
- **Cu�ado**
- **Cu�ada**
- **Yerno**
- **Nuera**

## Validaciones

### Campos Requeridos
- `nombre`: Nombre del familiar (obligatorio)
- `parentesco`: Tipo de relaci�n familiar (obligatorio)

### Campos Opcionales
- `apellido`: Apellido del familiar
- `fecha_nacimiento`: Fecha de nacimiento en formato mm/dd/yyyy
- `numero_celular`: N�mero de tel�fono celular (formato: (XXX) XXX-XXXX o +1234567890)
- `email`: Direcci�n de correo electr�nico
- `url_foto`: URL de la foto del familiar
- `notas`: Notas adicionales sobre el familiar

## Formatos de Datos

### Fecha de Nacimiento
- Formato esperado: `mm/dd/yyyy`
- Ejemplo: `"01/15/1995"`

### N�mero Celular
- Formato con par�ntesis: `"(555) 123-4567"`
- Formato internacional: `"+1234567890"`

### Email
- Formato est�ndar: `"ejemplo@email.com"`

### URL de Foto
- Formato completo: `"https://ejemplo.com/foto.jpg"`

## Estados de Respuesta HTTP

- **200 OK**: Operaci�n exitosa (GET, PUT, DELETE)
- **201 Created**: Familiar creado exitosamente (POST)
- **400 Bad Request**: Datos inv�lidos o faltantes
- **404 Not Found**: Familiar no encontrado
- **500 Internal Server Error**: Error interno del servidor

## CORS Support

Las funciones incluyen soporte completo para CORS con los siguientes or�genes permitidos:
- `http://localhost:5173`
- `http://localhost:5174`
- `http://localhost:3000`
- `http://127.0.0.1:5173`
- `http://127.0.0.1:5174`
- `http://127.0.0.1:3000`

## Container de Cosmos DB

- **Container Name**: `TwinFamily`
- **Partition Key**: `/TwinID`
- **Document Type**: `family`

## Mapping de Campos (C# ? Cosmos DB)

| Campo C# | Campo Cosmos DB | Descripci�n |
|----------|-----------------|-------------|
| `Id` | `id` | Identificador �nico |
| `TwinID` | `TwinID` | ID del Twin (partition key) |
| `Parentesco` | `parentesco` | Tipo de relaci�n familiar |
| `Nombre` | `nombre` | Nombre del familiar |
| `Apellido` | `apellido` | Apellido del familiar |
| `FechaNacimiento` | `fecha_nacimiento` | Fecha de nacimiento |
| `NumeroCelular` | `numero_celular` | N�mero celular |
| `Email` | `email` | Direcci�n de correo |
| `UrlFoto` | `url_foto` | URL de la foto |
| `Notas` | `notas` | Notas adicionales |
| `CreatedDate` | `createdDate` | Fecha de creaci�n |
| `Type` | `type` | Tipo de documento |

---

*Documentaci�n generada para TwinFx v1.0*