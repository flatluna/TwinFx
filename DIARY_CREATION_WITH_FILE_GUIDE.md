# ?? Guía de Creación de Entradas de Diario con Archivos

## ?? Problema Resuelto

El endpoint `CreateDiaryEntry` ahora soporta **dos métodos de envío**:
1. ? **JSON tradicional** - Solo datos sin archivos
2. ? **Multipart/form-data** - Datos + archivo de recibo

## ?? Endpoint Actualizado

**POST** `/api/twins/{twinId}/diary`

**Content-Type soportados:**
- `application/json` - Solo datos
- `multipart/form-data` - Datos + archivo

## ?? Ejemplos de Uso

### 1. ?? Crear Entrada Solo con Datos (JSON)

```javascript
const createDiaryEntryJSON = async (twinId, diaryData) => {
  const response = await fetch(`/api/twins/${twinId}/diary`, {
    method: 'POST',
    headers: { 
      'Content-Type': 'application/json' 
    },
    body: JSON.stringify({
      titulo: "Almuerzo en restaurante",
      descripcion: "Deliciosa comida italiana",
      fecha: "2025-01-15T14:30:00Z",
      tipoActividad: "comida",
      ubicacion: "Centro comercial",
      costoComida: 25.50,
      restauranteLugar: "Pizza Express",
      tipoCocina: "Italiana",
      platosOrdenados: "Pizza Margherita, Ensalada César",
      calificacionComida: 8,
      ambienteComida: "Casual y acogedor",
      recomendariaComida: true,
      estadoEmocional: "feliz",
      nivelEnergia: 8
    })
  });
  
  return await response.json();
};
```

### 2. ?? Crear Entrada con Archivo (Multipart)

```javascript
const createDiaryEntryWithFile = async (twinId, diaryData, receiptFile) => {
  const formData = new FormData();
  
  // Agregar archivo
  formData.append('file', receiptFile); // o 'receipt', 'recibo'
  
  // Agregar datos del diario
  formData.append('titulo', diaryData.titulo);
  formData.append('descripcion', diaryData.descripcion);
  formData.append('fecha', diaryData.fecha);
  formData.append('tipoActividad', diaryData.tipoActividad);
  formData.append('ubicacion', diaryData.ubicacion);
  
  // Campos específicos de comida
  formData.append('costoComida', diaryData.costoComida);
  formData.append('restauranteLugar', diaryData.restauranteLugar);
  formData.append('tipoCocina', diaryData.tipoCocina);
  formData.append('platosOrdenados', diaryData.platosOrdenados);
  formData.append('calificacionComida', diaryData.calificacionComida);
  formData.append('ambienteComida', diaryData.ambienteComida);
  formData.append('recomendariaComida', diaryData.recomendariaComida);
  
  // Estado emocional y energía
  formData.append('estadoEmocional', diaryData.estadoEmocional);
  formData.append('nivelEnergia', diaryData.nivelEnergia);

  const response = await fetch(`/api/twins/${twinId}/diary`, {
    method: 'POST',
    body: formData // NO establecer Content-Type, el browser lo hace automáticamente
  });
  
  return await response.json();
};
```

### 3. ?? Ejemplo Completo con React

```jsx
import React, { useState } from 'react';

const DiaryEntryForm = ({ twinId }) => {
  const [diaryData, setDiaryData] = useState({
    titulo: '',
    descripcion: '',
    fecha: new Date().toISOString(),
    tipoActividad: 'comida',
    ubicacion: '',
    costoComida: '',
    restauranteLugar: '',
    tipoCocina: '',
    platosOrdenados: '',
    calificacionComida: 5,
    ambienteComida: '',
    recomendariaComida: true,
    estadoEmocional: 'neutral',
    nivelEnergia: 5
  });

  const [receiptFile, setReceiptFile] = useState(null);
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);

    try {
      let result;
      
      if (receiptFile) {
        // Enviar con archivo usando multipart
        result = await createDiaryEntryWithFile(twinId, diaryData, receiptFile);
      } else {
        // Enviar solo datos usando JSON
        result = await createDiaryEntryJSON(twinId, diaryData);
      }

      if (result.success) {
        alert('? Entrada de diario creada exitosamente!');
        console.log('Entry created:', result.entry);
      } else {
        alert('? Error: ' + result.errorMessage);
      }
    } catch (error) {
      console.error('Error:', error);
      alert('? Error al crear entrada de diario');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="diary-form">
      <h2>?? Nueva Entrada de Diario</h2>
      
      {/* Campos básicos */}
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
        type="datetime-local"
        value={diaryData.fecha.slice(0, 16)}
        onChange={(e) => setDiaryData({...diaryData, fecha: e.target.value + ':00Z'})}
      />
      
      <select
        value={diaryData.tipoActividad}
        onChange={(e) => setDiaryData({...diaryData, tipoActividad: e.target.value})}
      >
        <option value="comida">??? Comida</option>
        <option value="compra">?? Compras</option>
        <option value="viaje">?? Viaje</option>
        <option value="entretenimiento">?? Entretenimiento</option>
        <option value="ejercicio">?? Ejercicio</option>
        <option value="estudio">?? Estudio</option>
        <option value="trabajo">?? Trabajo</option>
        <option value="salud">?? Salud</option>
      </select>

      {/* Campos específicos de comida */}
      <h3>??? Detalles de Comida</h3>
      
      <input
        type="number"
        placeholder="Costo de comida"
        step="0.01"
        value={diaryData.costoComida}
        onChange={(e) => setDiaryData({...diaryData, costoComida: e.target.value})}
      />
      
      <input
        type="text"
        placeholder="Restaurante/Lugar"
        value={diaryData.restauranteLugar}
        onChange={(e) => setDiaryData({...diaryData, restauranteLugar: e.target.value})}
      />
      
      <input
        type="text"
        placeholder="Tipo de cocina"
        value={diaryData.tipoCocina}
        onChange={(e) => setDiaryData({...diaryData, tipoCocina: e.target.value})}
      />
      
      <textarea
        placeholder="Platos ordenados"
        value={diaryData.platosOrdenados}
        onChange={(e) => setDiaryData({...diaryData, platosOrdenados: e.target.value})}
      />
      
      <label>
        Calificación de comida: {diaryData.calificacionComida}/10
        <input
          type="range"
          min="1"
          max="10"
          value={diaryData.calificacionComida}
          onChange={(e) => setDiaryData({...diaryData, calificacionComida: parseInt(e.target.value)})}
        />
      </label>
      
      <input
        type="text"
        placeholder="Ambiente"
        value={diaryData.ambienteComida}
        onChange={(e) => setDiaryData({...diaryData, ambienteComida: e.target.value})}
      />
      
      <label>
        <input
          type="checkbox"
          checked={diaryData.recomendariaComida}
          onChange={(e) => setDiaryData({...diaryData, recomendariaComida: e.target.checked})}
        />
        ¿Recomendarías este lugar?
      </label>

      {/* Estado emocional y energía */}
      <h3>?? Estado Personal</h3>
      
      <select
        value={diaryData.estadoEmocional}
        onChange={(e) => setDiaryData({...diaryData, estadoEmocional: e.target.value})}
      >
        <option value="muy-feliz">?? Muy feliz</option>
        <option value="feliz">?? Feliz</option>
        <option value="neutral">?? Neutral</option>
        <option value="triste">?? Triste</option>
        <option value="muy-triste">?? Muy triste</option>
      </select>
      
      <label>
        Nivel de energía: {diaryData.nivelEnergia}/10
        <input
          type="range"
          min="1"
          max="10"
          value={diaryData.nivelEnergia}
          onChange={(e) => setDiaryData({...diaryData, nivelEnergia: parseInt(e.target.value)})}
        />
      </label>

      {/* Upload de recibo */}
      <h3>?? Recibo (Opcional)</h3>
      
      <input
        type="file"
        accept=".pdf,.jpg,.jpeg,.png,.gif,.webp"
        onChange={(e) => setReceiptFile(e.target.files[0])}
      />
      
      {receiptFile && (
        <p>?? Archivo seleccionado: {receiptFile.name}</p>
      )}

      <button type="submit" disabled={loading}>
        {loading ? '? Creando...' : '?? Crear Entrada de Diario'}
      </button>
    </form>
  );
};

export default DiaryEntryForm;
```

## ?? Campos Disponibles por Tipo de Actividad

### ?? **Compras (tipoActividad: "compra")**
```javascript
{
  gastoTotal: 150.75,
  productosComprados: "Ropa, zapatos, accesorios",
  tiendaLugar: "Centro Comercial Plaza",
  metodoPago: "tarjeta_credito",
  categoriaCompra: "ropa",
  satisfaccionCompra: 8
}
```

### ??? **Comida (tipoActividad: "comida")**
```javascript
{
  costoComida: 25.50,
  restauranteLugar: "Pizza Express",
  tipoCocina: "Italiana",
  platosOrdenados: "Pizza Margherita",
  calificacionComida: 9,
  ambienteComida: "Casual",
  recomendariaComida: true
}
```

### ?? **Viaje (tipoActividad: "viaje")**
```javascript
{
  costoViaje: 500.00,
  destinoViaje: "París, Francia",
  transporteViaje: "avion",
  propositoViaje: "vacaciones",
  calificacionViaje: 10,
  duracionViaje: 7 // días
}
```

### ?? **Entretenimiento (tipoActividad: "entretenimiento")**
```javascript
{
  costoEntretenimiento: 15.00,
  calificacionEntretenimiento: 8,
  tipoEntretenimiento: "cine",
  tituloNombre: "Avatar 3",
  lugarEntretenimiento: "Cinemark Plaza"
}
```

### ?? **Ejercicio (tipoActividad: "ejercicio")**
```javascript
{
  costoEjercicio: 25.00,
  energiaPostEjercicio: 9,
  caloriasQuemadas: 350,
  tipoEjercicio: "cardio",
  duracionEjercicio: 60, // minutos
  intensidadEjercicio: 8,
  lugarEjercicio: "Gimnasio Olympic",
  rutinaEspecifica: "Treadmill + elíptica"
}
```

### ?? **Estudio (tipoActividad: "estudio")**
```javascript
{
  costoEstudio: 50.00,
  dificultadEstudio: 7,
  estadoAnimoPost: 8,
  materiaTema: "Matemáticas Avanzadas",
  materialEstudio: "Libro + videos online",
  duracionEstudio: 120, // minutos
  progresoEstudio: 75 // porcentaje
}
```

### ?? **Trabajo (tipoActividad: "trabajo")**
```javascript
{
  horasTrabajadas: 8,
  proyectoPrincipal: "Sistema de gestión",
  reunionesTrabajo: 3,
  logrosHoy: "Completé la funcionalidad de usuarios",
  desafiosTrabajo: "Integración con API externa",
  moodTrabajo: 7
}
```

### ?? **Salud (tipoActividad: "salud")**
```javascript
{
  costoSalud: 80.00,
  tipoConsulta: "chequeo_general",
  profesionalCentro: "Dr. García - Clínica San Juan",
  motivoConsulta: "Chequeo anual preventivo",
  tratamientoRecetado: "Vitaminas D3 + Omega 3",
  proximaCita: "2025-07-15T10:00:00Z"
}
```

### ?? **Comunicación (tipoActividad: "comunicacion")**
```javascript
{
  contactoLlamada: "María González",
  duracionLlamada: 45, // minutos
  motivoLlamada: "Planificar viaje familiar",
  temasConversacion: "Destinos, fechas, presupuesto",
  tipoLlamada: "video_llamada",
  seguimientoLlamada: true
}
```

## ?? **Configuración Automática de Recibos**

El sistema **detecta automáticamente** el tipo de recibo basado en `tipoActividad`:

| Tipo Actividad | Campo de Recibo Usado |
|----------------|----------------------|
| compra | `reciboCompra` |
| comida | `reciboComida` |
| viaje | `reciboViaje` |
| entretenimiento | `reciboEntretenimiento` |
| ejercicio | `reciboEjercicio` |
| estudio | `reciboEstudio` |
| salud | `reciboSalud` |

## ?? **Estructura de Archivos en DataLake**

```
{twinId}/
??? diary/
    ??? {entryId}/
        ??? recibo_comida_20250115_143052.jpg
        ??? recibo_viaje_20250115_143053.pdf
        ??? recibo_compra_20250115_143054.png
```

## ? **Validaciones del Sistema**

### ?? **Tipos de Archivo Soportados**
- **Imágenes**: JPG, JPEG, PNG, GIF, WEBP
- **Documentos**: PDF

### ?? **Validación de Magic Numbers**
- El sistema verifica el contenido real del archivo
- No se basa solo en la extensión del nombre

### ?? **Límites**
- Tamaño máximo de archivo: Configurado en DataLake
- Máximo 1 archivo por entrada de diario en la creación

---

**¡El sistema está listo para crear entradas de diario con o sin archivos! ??**