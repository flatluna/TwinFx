# ?? Gu�a de Subida de Documentos de Viaje con Contexto

## ?? Problema Resuelto

Anteriormente los documentos de viaje se guardaban con `travelId`, `itineraryId` y `activityId` como `null` porque el frontend no los enviaba. Ahora hemos actualizado el endpoint para:

1. ? **Validar y loggear** el contexto de viaje recibido
2. ? **Asignar correctamente** los IDs a la base de datos
3. ? **Proporcionar logging detallado** para debugging

## ?? Endpoint Actualizado

**POST** `/api/twins/{twinId}/travels/upload-document`

## ?? Estructura del Request Body

### ? Campos Requeridos
```json
{
  "fileName": "receipt.pdf",        // ? Requerido
  "fileContent": "base64string...", // ? Requerido (PDF/imagen en base64)
  "documentType": "Receipt",        // ? Requerido
  "establishmentType": "Restaurant" // ? Requerido
}
```

### ?? Campos de Contexto de Viaje (IMPORTANTE)
```json
{
  "travelId": "viaje-123",      // ?? Para asociar con un viaje espec�fico
  "itineraryId": "itinerario-456", // ?? Para asociar con un itinerario espec�fico  
  "activityId": "actividad-789"    // ?? Para asociar con una actividad espec�fica
}
```

### ?? Campos Opcionales
```json
{
  "titulo": "Recibo de Restaurant",
  "descripcion": "Cena en Par�s",
  "filePath": "custom/path"  // Override del path por defecto
}
```

## ?? Ejemplos de Uso

### 1. ?? Documento General de Viaje
```javascript
const uploadGeneralTravelDocument = async (twinId, fileData) => {
  const requestBody = {
    fileName: "hotel-booking.pdf",
    fileContent: fileData, // base64 string
    titulo: "Reserva Hotel Par�s", 
    descripcion: "Confirmaci�n de reserva del Hotel Marriott",
    documentType: "BookingConfirmation",
    establishmentType: "Hotel",
    travelId: "67890abc-def1-2345-6789-abcdef123456" // ? Asociar con viaje espec�fico
  };

  const response = await fetch(`/api/twins/${twinId}/travels/upload-document`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(requestBody)
  });
  
  return await response.json();
};
```

### 2. ??? Documento de Itinerario Espec�fico
```javascript
const uploadItineraryDocument = async (twinId, travelId, itineraryId, fileData) => {
  const requestBody = {
    fileName: "flight-ticket.pdf",
    fileContent: fileData,
    titulo: "Boleto de Avi�n Par�s-Madrid",
    documentType: "Ticket",
    establishmentType: "Airline",
    travelId: travelId,        // ? Viaje padre
    itineraryId: itineraryId   // ? Itinerario espec�fico
  };

  const response = await fetch(`/api/twins/${twinId}/travels/upload-document`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(requestBody)
  });
  
  return await response.json();
};
```

### 3. ?? Documento de Actividad Espec�fica
```javascript
const uploadActivityDocument = async (twinId, travelId, itineraryId, activityId, fileData) => {
  const requestBody = {
    fileName: "museum-receipt.jpg",
    fileContent: fileData,
    titulo: "Entrada Museo del Louvre",
    descripcion: "Recibo de entrada al museo",
    documentType: "Receipt",
    establishmentType: "Museum",
    travelId: travelId,        // ? Viaje padre
    itineraryId: itineraryId,  // ? Itinerario padre  
    activityId: activityId     // ? Actividad espec�fica
  };

  const response = await fetch(`/api/twins/${twinId}/travels/upload-document`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(requestBody)
  });
  
  return await response.json();
};
```

### 4. ?? Documento Sin Contexto (No recomendado)
```javascript
const uploadIndependentDocument = async (twinId, fileData) => {
  const requestBody = {
    fileName: "receipt-general.pdf", 
    fileContent: fileData,
    documentType: "Receipt",
    establishmentType: "Other"
    // ? Sin travelId, itineraryId, activityId - Se guardar� como independiente
  };

  const response = await fetch(`/api/twins/${twinId}/travels/upload-document`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(requestBody)
  });
  
  return await response.json();
};
```

## ?? Logging y Debugging

El endpoint actualizado ahora proporciona logging detallado:

```
?? Travel Context - TravelId: viaje-123, ItineraryId: itinerario-456, ActivityId: actividad-789
?? Starting TravelAgentAI processing for travel document...
? TravelAgentAI processing completed successfully
?? Extracted text: 1250 characters
?? Establishment: Restaurant Le Bernardin
??? Travel Category: Alimentaci�n
?? Saving document with TravelId: viaje-123, ItineraryId: itinerario-456, ActivityId: actividad-789
?? Travel document saved to Cosmos DB successfully
```

## ? Verificaci�n en Cosmos DB

Despu�s de la actualizaci�n, los documentos se guardar�n con:

```json
{
  "id": "doc-guid-123",
  "travelId": "viaje-123",      // ? Ya no ser� null
  "itineraryId": "itinerario-456", // ? Ya no ser� null  
  "activityId": "actividad-789",   // ? Ya no ser� null
  "fileName": "receipt.pdf",
  "vendorName": "Restaurant Le Bernardin",
  "totalAmount": 125.50,
  // ... otros campos
}
```

## ?? Component Frontend Ejemplo (React)

```jsx
import React, { useState } from 'react';

const TravelDocumentUploader = ({ twinId, travelId, itineraryId, activityId }) => {
  const [file, setFile] = useState(null);
  const [uploading, setUploading] = useState(false);

  const handleFileUpload = async (event) => {
    const selectedFile = event.target.files[0];
    if (!selectedFile) return;

    setUploading(true);
    
    // Convert file to base64
    const fileContent = await convertToBase64(selectedFile);
    
    const requestBody = {
      fileName: selectedFile.name,
      fileContent: fileContent,
      titulo: `Documento de ${selectedFile.name}`,
      documentType: "Receipt", // Could be dynamic based on file type
      establishmentType: "Restaurant", // Could be from user selection
      travelId: travelId,        // ? Contexto de viaje
      itineraryId: itineraryId,  // ? Contexto de itinerario
      activityId: activityId     // ? Contexto de actividad
    };

    try {
      const response = await fetch(`/api/twins/${twinId}/travels/upload-document`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestBody)
      });
      
      const result = await response.json();
      
      if (result.success) {
        console.log('? Document uploaded successfully:', result.document);
        // Document will now have proper travel context!
      } else {
        console.error('? Upload failed:', result.errorMessage);
      }
    } catch (error) {
      console.error('? Upload error:', error);
    } finally {
      setUploading(false);
    }
  };

  const convertToBase64 = (file) => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.readAsDataURL(file);
      reader.onload = () => {
        const base64 = reader.result.split(',')[1]; // Remove data:type;base64, prefix
        resolve(base64);
      };
      reader.onerror = error => reject(error);
    });
  };

  return (
    <div>
      <h3>?? Subir Documento de Viaje</h3>
      <p>Viaje: {travelId || 'No especificado'}</p>
      <p>Itinerario: {itineraryId || 'No especificado'}</p>
      <p>Actividad: {activityId || 'No especificado'}</p>
      
      <input 
        type="file" 
        accept=".pdf,.jpg,.jpeg,.png"
        onChange={handleFileUpload}
        disabled={uploading}
      />
      
      {uploading && <p>? Subiendo documento...</p>}
    </div>
  );
};

export default TravelDocumentUploader;
```

## ?? Checklist para Frontend

- [ ] ? Asegurar que se env�e `fileName` con el nombre del archivo
- [ ] ? Convertir archivo a base64 para `fileContent`
- [ ] ? Seleccionar `documentType` apropiado (Receipt, Invoice, Ticket, etc.)
- [ ] ? Seleccionar `establishmentType` apropiado (Restaurant, Hotel, Museum, etc.)
- [ ] ?? **IMPORTANTE**: Incluir `travelId` cuando se suba desde un viaje espec�fico
- [ ] ?? **IMPORTANTE**: Incluir `itineraryId` cuando se suba desde un itinerario espec�fico  
- [ ] ?? **IMPORTANTE**: Incluir `activityId` cuando se suba desde una actividad espec�fica
- [ ] ?? Incluir `titulo` y `descripcion` opcionales para mejor organizaci�n

## ?? Casos de Uso por Contexto

| Contexto | TravelId | ItineraryId | ActivityId | Uso |
|----------|----------|-------------|------------|-----|
| **Viaje General** | ? | ? | ? | Documentos generales del viaje |
| **Itinerario** | ? | ? | ? | Documentos espec�ficos del itinerario |
| **Actividad** | ? | ? | ? | Documentos espec�ficos de la actividad |
| **Independiente** | ? | ? | ? | Sin asociaci�n (no recomendado) |

---

**�Ahora los documentos de viaje se organizar�n correctamente en la base de datos! ??**