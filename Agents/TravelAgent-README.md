# TravelAgent - Gesti�n de Viajes TwinFx

?? **Agente especializado para la gesti�n completa de viajes y experiencias de viaje**

## ?? Descripci�n General

El TravelAgent es un agente inteligente que forma parte del ecosistema TwinFx para la gesti�n completa de viajes y experiencias de viaje. Permite crear, actualizar, consultar y eliminar registros de viajes con capacidades avanzadas de filtrado y an�lisis estad�stico.

## ? Caracter�sticas Principales

### ?? **Gesti�n Completa de Viajes**
- **Creaci�n de viajes**: Registra nuevos viajes con informaci�n detallada
- **Actualizaci�n**: Modifica cualquier aspecto de un viaje existente
- **Eliminaci�n**: Borra registros de viajes
- **Consulta avanzada**: Filtros m�ltiples y b�squeda de texto libre

### ?? **An�lisis y Estad�sticas**
- **Estad�sticas por estado**: Planeando, confirmado, en progreso, completado, cancelado
- **An�lisis por tipo**: Vacaciones, negocios, familiar, aventura, cultural, otro
- **Presupuesto total**: Suma de todos los presupuestos de viajes
- **Pa�ses m�s visitados**: Ranking de destinos frecuentes

### ?? **Filtrado Avanzado**
- **Por estado del viaje**: Filtra por estado espec�fico
- **Por tipo de viaje**: Filtra por categor�a de viaje
- **Por destino**: Pa�s y ciudad de destino
- **Por fechas**: Rango de fechas de viaje
- **Por calificaci�n**: Viajes con calificaci�n m�nima
- **Por presupuesto**: Viajes hasta cierto presupuesto m�ximo
- **B�squeda de texto**: En t�tulo, descripci�n, notas y actividades

### ??? **Estructura de Datos**
- **Contenedor Cosmos DB**: `TwinTravel`
- **Partition Key**: `TwinID`
- **Documento Type**: `travel`

## ?? API Endpoints

### **POST** `/api/twins/{twinId}/travels`
Crear un nuevo viaje

**Request Body:**
```json
{
  "titulo": "Viaje a Par�s",
  "descripcion": "Viaje rom�ntico a la ciudad de la luz",
  "paisDestino": "Francia",
  "ciudadDestino": "Par�s",
  "fechaInicio": "2025-06-15T00:00:00Z",
  "fechaFin": "2025-06-22T00:00:00Z",
  "presupuesto": 2500.00,
  "moneda": "EUR",
  "tipoViaje": "vacaciones",
  "estado": "planeando",
  "transporte": "Avi�n",
  "alojamiento": "Hotel 4 estrellas",
  "compa�eros": "Pareja",
  "actividades": "Torre Eiffel, Louvre, Crucero por el Sena",
  "notas": "Reservar con anticipaci�n"
}
```

**Response:**
```json
{
  "success": true,
  "travel": {
    "id": "guid-generated",
    "twinID": "twin123",
    "titulo": "Viaje a Par�s",
    "descripcion": "Viaje rom�ntico a la ciudad de la luz",
    "paisDestino": "Francia",
    "ciudadDestino": "Par�s",
    "fechaInicio": "2025-06-15T00:00:00Z",
    "fechaFin": "2025-06-22T00:00:00Z",
    "duracionDias": 8,
    "presupuesto": 2500.00,
    "moneda": "EUR",
    "tipoViaje": "vacaciones",
    "estado": "planeando",
    "transporte": "Avi�n",
    "alojamiento": "Hotel 4 estrellas",
    "compa�eros": "Pareja",
    "actividades": "Torre Eiffel, Louvre, Crucero por el Sena",
    "notas": "Reservar con anticipaci�n",
    "fechaCreacion": "2025-01-15T10:00:00Z",
    "fechaActualizacion": "2025-01-15T10:00:00Z"
  },
  "message": "Travel 'Viaje a Par�s' created successfully"
}
```

### **GET** `/api/twins/{twinId}/travels`
Obtener todos los viajes de un Twin con filtros opcionales

**Query Parameters:**
- `estado`: `planeando`, `confirmado`, `en_progreso`, `completado`, `cancelado`
- `tipoViaje`: `vacaciones`, `negocios`, `familiar`, `aventura`, `cultural`, `otro`
- `paisDestino`: Filtro por pa�s (b�squeda parcial)
- `ciudadDestino`: Filtro por ciudad (b�squeda parcial)
- `fechaDesde`: Fecha m�nima de inicio (ISO 8601)
- `fechaHasta`: Fecha m�xima de inicio (ISO 8601)
- `calificacionMin`: Calificaci�n m�nima (1-5)
- `presupuestoMax`: Presupuesto m�ximo
- `searchTerm`: B�squeda en t�tulo, descripci�n, notas, actividades
- `page`: N�mero de p�gina (default: 1)
- `pageSize`: Tama�o de p�gina (default: 20, max: 100)
- `sortBy`: Campo de ordenamiento (`fechaInicio`, `fechaCreacion`, `titulo`, `presupuesto`, `calificacion`)
- `sortDirection`: Direcci�n (`asc`, `desc`)

**Ejemplo:**
```
GET /api/twins/twin123/travels?estado=completado&tipoViaje=vacaciones&page=1&pageSize=10&sortBy=fechaInicio&sortDirection=desc
```

**Response:**
```json
{
  "success": true,
  "travels": [
    {
      "id": "travel1",
      "titulo": "Viaje a Par�s",
      "descripcion": "Viaje rom�ntico...",
      "paisDestino": "Francia",
      "estado": "completado",
      "calificacion": 5
    }
  ],
  "stats": {
    "total": 15,
    "planeando": 3,
    "confirmado": 2,
    "enProgreso": 1,
    "completado": 8,
    "cancelado": 1,
    "byType": {
      "vacaciones": 10,
      "negocios": 3,
      "familiar": 2
    },
    "totalBudget": 15000.00,
    "topCountries": {
      "Francia": 3,
      "Espa�a": 2,
      "Italia": 2
    }
  },
  "twinId": "twin123",
  "totalTravels": 15,
  "message": "Found 15 travel records for Twin twin123"
}
```

### **GET** `/api/twins/{twinId}/travels/{travelId}`
Obtener un viaje espec�fico por ID

**Response:**
```json
{
  "success": true,
  "travel": {
    "id": "travel1",
    "titulo": "Viaje a Par�s",
    "descripcion": "Viaje rom�ntico a la ciudad de la luz",
    "paisDestino": "Francia",
    "ciudadDestino": "Par�s",
    "fechaInicio": "2025-06-15T00:00:00Z",
    "fechaFin": "2025-06-22T00:00:00Z",
    "duracionDias": 8,
    "presupuesto": 2500.00,
    "moneda": "EUR",
    "tipoViaje": "vacaciones",
    "estado": "completado",
    "calificacion": 5,
    "highlights": "Torre Eiffel al atardecer fue incre�ble"
  },
  "message": "Travel record retrieved successfully",
  "travelId": "travel1",
  "twinId": "twin123"
}
```

### **PUT** `/api/twins/{twinId}/travels/{travelId}`
Actualizar un viaje existente

**Request Body** (todos los campos son opcionales):
```json
{
  "estado": "completado",
  "calificacion": 5,
  "highlights": "Torre Eiffel al atardecer fue incre�ble",
  "notas": "Excelente viaje, definitivamente regresar�"
}
```

### **DELETE** `/api/twins/{twinId}/travels/{travelId}`
Eliminar un viaje

**Response:**
```json
{
  "success": true,
  "travel": {
    "id": "travel1",
    "titulo": "Viaje a Par�s"
  },
  "message": "Travel record 'Viaje a Par�s' deleted successfully",
  "travelId": "travel1",
  "twinId": "twin123"
}
```

## ?? Modelos de Datos

### **TravelData** - Modelo principal
```csharp
public class TravelData
{
    public string Id { get; set; }                    // GUID �nico
    public string Titulo { get; set; }                // T�tulo del viaje (requerido)
    public string Descripcion { get; set; }           // Descripci�n (requerido)
    public string? PaisDestino { get; set; }          // Pa�s de destino
    public string? CiudadDestino { get; set; }        // Ciudad de destino
    public DateTime? FechaInicio { get; set; }        // Fecha de inicio
    public DateTime? FechaFin { get; set; }           // Fecha de fin
    public int? DuracionDias { get; set; }            // Duraci�n calculada
    public decimal? Presupuesto { get; set; }         // Presupuesto
    public string? Moneda { get; set; }               // Moneda (default: USD)
    public TravelType TipoViaje { get; set; }         // Tipo de viaje
    public TravelStatus Estado { get; set; }          // Estado del viaje
    public string? Transporte { get; set; }           // Medio de transporte
    public string? Alojamiento { get; set; }          // Informaci�n de alojamiento
    public string? Compa�eros { get; set; }           // Compa�eros de viaje
    public string? Actividades { get; set; }          // Actividades planificadas/realizadas
    public string? Notas { get; set; }                // Notas adicionales
    public int? Calificacion { get; set; }            // Calificaci�n 1-5 estrellas
    public string? Highlights { get; set; }           // Momentos destacados
    public DateTime FechaCreacion { get; set; }       // Timestamp de creaci�n
    public DateTime FechaActualizacion { get; set; }  // Timestamp de actualizaci�n
    public string TwinID { get; set; }                // ID del Twin (partition key)
    public string DocumentType { get; set; } = "travel"; // Tipo de documento
}
```

### **TravelType** - Tipos de viaje
```csharp
public enum TravelType
{
    Vacaciones,    // Viajes de placer/descanso
    Negocios,      // Viajes de trabajo
    Familiar,      // Viajes familiares
    Aventura,      // Viajes de aventura/deportes
    Cultural,      // Viajes culturales/educativos
    Otro           // Otros tipos
}
```

### **TravelStatus** - Estados del viaje
```csharp
public enum TravelStatus
{
    Planeando,     // En fase de planificaci�n
    Confirmado,    // Confirmado y reservado
    EnProgreso,    // Viaje en curso
    Completado,    // Viaje terminado
    Cancelado      // Viaje cancelado
}
```

## ?? Configuraci�n

### **Cosmos DB Container**
- **Container Name**: `TwinTravel`
- **Partition Key**: `/TwinID`
- **Throughput**: Compartido con la base de datos

### **Variables de Entorno Requeridas**
```json
{
  "COSMOS_ACCOUNT_NAME": "tu-cosmos-account",
  "COSMOS_DATABASE_NAME": "TwinHumanDB", 
  "COSMOS_KEY": "tu-cosmos-key"
}
```

## ?? Ejemplos de Uso

### **Crear un viaje de negocios**
```bash
curl -X POST https://tu-function-app.azurewebsites.net/api/twins/twin123/travels \
  -H "Content-Type: application/json" \
  -d '{
    "titulo": "Conferencia Tech Madrid",
    "descripcion": "Asistir a conferencia de tecnolog�a",
    "paisDestino": "Espa�a",
    "ciudadDestino": "Madrid",
    "fechaInicio": "2025-03-10T09:00:00Z",
    "fechaFin": "2025-03-12T18:00:00Z",
    "presupuesto": 1200.00,
    "moneda": "EUR",
    "tipoViaje": "negocios",
    "estado": "confirmado",
    "transporte": "Avi�n",
    "alojamiento": "Hotel cerca del centro de convenciones",
    "actividades": "Keynotes, workshops, networking"
  }'
```

### **Buscar viajes completados por pa�s**
```bash
curl "https://tu-function-app.azurewebsites.net/api/twins/twin123/travels?estado=completado&paisDestino=Francia&sortBy=fechaInicio&sortDirection=desc"
```

### **Actualizar calificaci�n de un viaje**
```bash
curl -X PUT https://tu-function-app.azurewebsites.net/api/twins/twin123/travels/travel123 \
  -H "Content-Type: application/json" \
  -d '{
    "estado": "completado",
    "calificacion": 5,
    "highlights": "La comida francesa super� todas las expectativas"
  }'
```

## ?? An�lisis y Estad�sticas

El TravelAgent proporciona estad�sticas autom�ticas en cada consulta:

- **Por Estado**: Conteo de viajes en cada estado
- **Por Tipo**: Distribuci�n de tipos de viaje
- **Presupuesto Total**: Suma de todos los presupuestos
- **Top Pa�ses**: Pa�ses m�s visitados con frecuencia

Estas estad�sticas son �tiles para:
- Analizar patrones de viaje
- Presupuestaci�n y planning financiero
- Identificar destinos favoritos
- Tracking de objetivos de viaje

## ??? Seguridad y Validaci�n

### **Validaciones Implementadas**
- ? **T�tulo requerido**: M�nimo 2 caracteres
- ? **Descripci�n requerida**: Texto obligatorio
- ? **Twin ID v�lido**: Partition key requerido
- ? **Calificaci�n v�lida**: Entre 1 y 5 estrellas
- ? **Fechas l�gicas**: Fecha fin posterior a fecha inicio
- ? **URLs v�lidas**: Validaci�n de formato si se proporciona

### **CORS Configurado**
- ? **Origins permitidos**: localhost:5173, localhost:3000
- ? **M�todos**: GET, POST, PUT, DELETE, OPTIONS
- ? **Headers**: Content-Type, Authorization, Accept

## ?? Integraci�n con TwinFx

El TravelAgent se integra perfectamente con el ecosistema TwinFx:

- **TwinAgentClient**: Puede ser invocado desde conversaciones
- **CosmosDB**: Utiliza la misma infraestructura de datos
- **Logging**: Sistema de logging unificado
- **Configuraci�n**: Usa las mismas variables de entorno

## ?? Roadmap Futuro

- [ ] **Integraci�n con APIs de viajes**: Precios de vuelos, hoteles
- [ ] **Geolocalizaci�n**: Mapas y coordenadas
- [ ] **Fotos de viaje**: Integraci�n con PhotosAgent
- [ ] **Itinerarios detallados**: Planificaci�n d�a a d�a
- [ ] **Gastos por viaje**: Tracking de gastos detallado
- [ ] **Recomendaciones IA**: Sugerencias basadas en historial
- [ ] **Compartir viajes**: Funcionalidad social
- [ ] **Reportes avanzados**: Analytics e insights m�s profundos

---

**Desarrollado como parte del ecosistema TwinFx** ??  
*Gesti�n inteligente de viajes con IA integrada*