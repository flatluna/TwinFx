# ?? Guía de Upload Múltiples Recibos en Diario

## ?? **Nueva Funcionalidad: Múltiples Archivos de Recibos**

El sistema ahora puede manejar **múltiples archivos de recibos** en una sola entrada de diario. Esto permite subir varios recibos relacionados con la misma actividad (ej: recibo de comida + recibo de transporte).

## ?? **Cambio Implementado**

### ? **Antes (Solo un archivo)**
```javascript
// Solo podía subir UN archivo por vez
var filePart = parts.FirstOrDefault(p => p.Name == "file" || p.Name == "receipt");
```

### ? **Ahora (Múltiples archivos)**
```javascript
// Puede subir MÚLTIPLES archivos con nombres que contengan "recibo"
var fileParts = parts.Where(p => 
    p.Name != null && 
    p.Name.Contains("recibo") && 
    p.Data != null && 
    p.Data.Length > 0
).ToList();
```

## ?? **Ejemplos de Uso**

### 1. ?? **Frontend: FormData con Múltiples Recibos**

```javascript
const createDiaryEntryWithMultipleReceipts = async (twinId, diaryData, receiptFiles) => {
  const formData = new FormData();
  
  // Agregar datos del diario
  formData.append('titulo', diaryData.titulo);
  formData.append('descripcion', diaryData.descripcion);
  formData.append('fecha', diaryData.fecha);
  formData.append('tipoActividad', diaryData.tipoActividad);
  formData.append('ubicacion', diaryData.ubicacion);
  formData.append('costoComida', diaryData.costoComida);
  formData.append('restauranteLugar', diaryData.restauranteLugar);
  
  // Agregar MÚLTIPLES archivos con nombres específicos
  if (receiptFiles.comida) {
    formData.append('reciboComida', receiptFiles.comida);
  }
  
  if (receiptFiles.viaje) {
    formData.append('reciboViaje', receiptFiles.viaje);
  }
  
  if (receiptFiles.compra) {
    formData.append('reciboCompra', receiptFiles.compra);
  }
  
  if (receiptFiles.entretenimiento) {
    formData.append('reciboEntretenimiento', receiptFiles.entretenimiento);
  }

  const response = await fetch(`/api/twins/${twinId}/diary`, {
    method: 'POST',
    body: formData
  });
  
  return await response.json();
};

// Uso del función
const receiptFiles = {
  comida: document.getElementById('reciboComidaFile').files[0],
  viaje: document.getElementById('reciboViajeFile').files[0],
  compra: document.getElementById('reciboCompraFile').files[0]
};

const result = await createDiaryEntryWithMultipleReceipts('twin123', {
  titulo: 'Día completo de actividades',
  descripcion: 'Comida, compras y viaje',
  fecha: '2025-01-15T10:00:00Z',
  tipoActividad: 'mixed',
  ubicacion: 'Centro comercial',
  costoComida: 25.50,
  restauranteLugar: 'Pizza Express'
}, receiptFiles);
```

### 2. ?? **React Component con Múltiples Uploads**

```jsx
import React, { useState } from 'react';

const MultiReceiptDiaryForm = ({ twinId }) => {
  const [diaryData, setDiaryData] = useState({
    titulo: '',
    descripcion: '',
    fecha: new Date().toISOString(),
    tipoActividad: 'mixed',
    ubicacion: '',
    costoComida: '',
    restauranteLugar: ''
  });

  const [receiptFiles, setReceiptFiles] = useState({
    comida: null,
    viaje: null,
    compra: null,
    entretenimiento: null,
    ejercicio: null,
    estudio: null,
    salud: null
  });

  const [loading, setLoading] = useState(false);

  const handleFileChange = (receiptType, file) => {
    setReceiptFiles(prev => ({
      ...prev,
      [receiptType]: file
    }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);

    try {
      const formData = new FormData();
      
      // Agregar datos del diario
      Object.keys(diaryData).forEach(key => {
        if (diaryData[key]) {
          formData.append(key, diaryData[key]);
        }
      });
      
      // Agregar archivos de recibos (solo los que están seleccionados)
      Object.keys(receiptFiles).forEach(type => {
        if (receiptFiles[type]) {
          formData.append(`recibo${type.charAt(0).toUpperCase() + type.slice(1)}`, receiptFiles[type]);
        }
      });

      const response = await fetch(`/api/twins/${twinId}/diary`, {
        method: 'POST',
        body: formData
      });
      
      const result = await response.json();

      if (result.success) {
        alert(`? Entrada creada con ${result.uploadedReceipts?.length || 0} recibo(s)!`);
        console.log('Recibos subidos:', result.uploadedReceipts);
      } else {
        alert('? Error: ' + result.errorMessage);
      }
    } catch (error) {
      console.error('Error:', error);
      alert('? Error al crear entrada');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="multi-receipt-form">
      <h2>?? Nueva Entrada con Múltiples Recibos</h2>
      
      {/* Campos básicos del diario */}
      <div className="basic-fields">
        <input
          type="text"
          placeholder="Título"
          value={diaryData.titulo}
          onChange={(e) => setDiaryData({...diaryData, titulo: e.target.value})}
          required
        />
        
        <textarea
          placeholder="Descripción"
          value={diaryData.descripcion}
          onChange={(e) => setDiaryData({...diaryData, descripcion: e.target.value})}
        />
        
        <input
          type="text"
          placeholder="Ubicación"
          value={diaryData.ubicacion}
          onChange={(e) => setDiaryData({...diaryData, ubicacion: e.target.value})}
        />
      </div>

      {/* Sección de múltiples recibos */}
      <div className="receipts-section">
        <h3>?? Recibos (Opcional - Puedes subir varios)</h3>
        
        <div className="receipt-upload">
          <label>??? Recibo de Comida:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('comida', e.target.files[0])}
          />
          {receiptFiles.comida && <span>? {receiptFiles.comida.name}</span>}
        </div>
        
        <div className="receipt-upload">
          <label>?? Recibo de Viaje:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('viaje', e.target.files[0])}
          />
          {receiptFiles.viaje && <span>? {receiptFiles.viaje.name}</span>}
        </div>
        
        <div className="receipt-upload">
          <label>?? Recibo de Compra:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('compra', e.target.files[0])}
          />
          {receiptFiles.compra && <span>? {receiptFiles.compra.name}</span>}
        </div>
        
        <div className="receipt-upload">
          <label>?? Recibo de Entretenimiento:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('entretenimiento', e.target.files[0])}
          />
          {receiptFiles.entretenimiento && <span>? {receiptFiles.entretenimiento.name}</span>}
        </div>
        
        <div className="receipt-upload">
          <label>?? Recibo de Ejercicio:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('ejercicio', e.target.files[0])}
          />
          {receiptFiles.ejercicio && <span>? {receiptFiles.ejercicio.name}</span>}
        </div>
        
        <div className="receipt-upload">
          <label>?? Recibo de Estudio:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('estudio', e.target.files[0])}
          />
          {receiptFiles.estudio && <span>? {receiptFiles.estudio.name}</span>}
        </div>
        
        <div className="receipt-upload">
          <label>?? Recibo de Salud:</label>
          <input
            type="file"
            accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
            onChange={(e) => handleFileChange('salud', e.target.files[0])}
          />
          {receiptFiles.salud && <span>? {receiptFiles.salud.name}</span>}
        </div>
      </div>

      <button type="submit" disabled={loading}>
        {loading ? '? Creando...' : '?? Crear Entrada con Recibos'}
      </button>
      
      <div className="file-count">
        ?? Archivos seleccionados: {Object.values(receiptFiles).filter(f => f).length}
      </div>
    </form>
  );
};

export default MultiReceiptDiaryForm;
```

## ?? **Detección Automática de Tipos**

### ?? **Mapeo por Nombre de Campo**
El sistema detecta automáticamente el tipo de recibo basado en el nombre del campo:

| Campo FormData | Tipo Detectado | Campo BD Actualizado |
|---------------|----------------|---------------------|
| `reciboComida` | `comida` | `ReciboComida` |
| `reciboViaje` | `viaje` | `ReciboViaje` |
| `reciboCompra` | `compra` | `ReciboCompra` |
| `reciboEntretenimiento` | `entretenimiento` | `ReciboEntretenimiento` |
| `reciboEjercicio` | `ejercicio` | `ReciboEjercicio` |
| `reciboEstudio` | `estudio` | `ReciboEstudio` |
| `reciboSalud` | `salud` | `ReciboSalud` |

### ?? **Fallback Inteligente**
Si el nombre del campo no es específico, el sistema usa el `tipoActividad`:
- `tipoActividad: "comida"` ? Guarda en `ReciboComida`
- `tipoActividad: "shopping"` ? Guarda en `ReciboCompra`
- `tipoActividad: "travel"` ? Guarda en `ReciboViaje`

## ?? **Respuesta del Backend**

### ? **Respuesta Exitosa con Múltiples Archivos**
```json
{
  "success": true,
  "message": "Diary entry 'Día completo' created successfully with 3 receipt(s)",
  "twinId": "twin123",
  "entry": {
    "id": "diary-entry-456",
    "titulo": "Día completo",
    "reciboComida": "diary/diary-entry-456/recibo_comida_20250115_143052.jpg",
    "reciboViaje": "diary/diary-entry-456/recibo_viaje_20250115_143053.pdf",
    "reciboCompra": "diary/diary-entry-456/recibo_compra_20250115_143054.png"
  },
  "uploadedReceipts": [
    {
      "fieldName": "reciboComida",
      "receiptType": "comida",
      "fileName": "recibo_comida_20250115_143052.jpg",
      "filePath": "diary/diary-entry-456/recibo_comida_20250115_143052.jpg",
      "receiptUrl": "https://datalake.../recibo_comida_20250115_143052.jpg?sastoken...",
      "fileSize": 245760
    },
    {
      "fieldName": "reciboViaje",
      "receiptType": "viaje",
      "fileName": "recibo_viaje_20250115_143053.pdf",
      "filePath": "diary/diary-entry-456/recibo_viaje_20250115_143053.pdf",
      "receiptUrl": "https://datalake.../recibo_viaje_20250115_143053.pdf?sastoken...",
      "fileSize": 512000
    },
    {
      "fieldName": "reciboCompra",
      "receiptType": "compra",
      "fileName": "recibo_compra_20250115_143054.png",
      "filePath": "diary/diary-entry-456/recibo_compra_20250115_143054.png",
      "receiptUrl": "https://datalake.../recibo_compra_20250115_143054.png?sastoken...",
      "fileSize": 156432
    }
  ]
}
```

## ?? **Estructura de Archivos en DataLake**

```
{twinId}/
??? diary/
    ??? {entryId}/
        ??? recibo_comida_20250115_143052.jpg
        ??? recibo_viaje_20250115_143053.pdf
        ??? recibo_compra_20250115_143054.png
        ??? recibo_entretenimiento_20250115_143055.jpg
        ??? recibo_ejercicio_20250115_143056.pdf
```

## ? **Manejo de Errores Inteligente**

### ?? **Continuidad en Caso de Error**
Si uno de los archivos falla, los demás continúan procesándose:

```javascript
// Archivo 1: ? Éxito
// Archivo 2: ? Error (formato inválido) - Se ignora y continúa
// Archivo 3: ? Éxito
// Resultado: 2 archivos subidos exitosamente
```

### ?? **Log Detallado**
```
?? 3 archivo(s) detectado(s) en multipart data
?? Procesando archivo: reciboComida -> comida_receipt.jpg, Size: 245760 bytes
? Recibo subido: comida -> diary/entry123/recibo_comida_20250115_143052.jpg
?? Procesando archivo: reciboViaje -> travel.pdf, Size: 512000 bytes  
? Recibo subido: viaje -> diary/entry123/recibo_viaje_20250115_143053.pdf
?? Archivo inválido ignorado: reciboCompra - invalid_file.txt
? Diary entry with 2 file(s) created successfully: Día completo in 1250ms
```

## ?? **Casos de Uso Típicos**

### 1. **??? Cena con Transporte**
```javascript
// Usuario sube: recibo del restaurante + recibo de Uber
formData.append('reciboComida', restaurantReceipt);
formData.append('reciboViaje', uberReceipt);
```

### 2. **?? Consulta Médica Completa**
```javascript
// Usuario sube: recibo de consulta + recibo de medicamentos + recibo de exámenes
formData.append('reciboSalud', consultaReceipt);
formData.append('reciboCompra', medicamentosReceipt);
```

### 3. **?? Día de Estudio**
```javascript
// Usuario sube: recibo de libros + recibo de café + recibo de transporte
formData.append('reciboEstudio', librosReceipt);
formData.append('reciboComida', cafeReceipt);
formData.append('reciboViaje', transporteReceipt);
```

---

**¡Ahora el sistema puede manejar múltiples recibos por entrada de diario de forma inteligente y eficiente! ??**