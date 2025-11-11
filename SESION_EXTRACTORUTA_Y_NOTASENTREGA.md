# Sesión: Implementación de ExtractoRuta y NotasEntrega

**Fecha:** 2025-11-06
**Estado:** En progreso - Pendiente verificación de errores en NotasEntrega

---

## 1. OBJETIVO PRINCIPAL

Implementar el registro de todas las operaciones de facturación de rutas en la tabla `ExtractoRuta`:
- **Facturas**: Copiar datos desde ExtractoCliente (TipoApunte = 1)
- **Albaranes**: Insertar con Nº_Orden negativo (MIN - 1) e Importe = 0
- **Notas de Entrega**: Similar a albaranes + insertar en tabla NotasEntrega

**Restricción importante:** Solo insertar en ExtractoRuta para **Ruta Propia** (AT, 16), NO para **Ruta de Agencias** (00, FW).

---

## 2. CAMBIOS REALIZADOS

### 2.1. Nuevas Entidades (Models/)

#### ExtractoRuta.cs
- Clase principal de la entidad
- Clave primaria compuesta: `(Empresa, Nº_Orden)`
- Propiedades principales: Número, Contacto, Fecha, Concepto, Importe, ImportePdte, Vendedor, Ruta, TipoRuta, etc.
- Configuración con Data Annotations para mapeo Database First

#### ExtractoRuta.Partial.cs
- Atributo `[Table("ExtractoRuta")]`
- Clase partial para extensiones futuras

#### NotaEntrega.cs
- Clase principal de la entidad
- Clave primaria compuesta: `(NºOrden, Numero)`
- Propiedades: NºOrden (del pedido), Numero (número de nota de entrega), Fecha

#### NotaEntrega.Partial.cs
- Atributo `[Table("NotasEntrega")]`
- Clase partial para extensiones futuras

#### NVEntities.Partial.cs
- Agregados DbSets: `ExtractosRuta` y `NotasEntregas`
- Constructor existente mantenido para conexiones compartidas

**Archivos incluidos en NestoAPI.csproj:**
```xml
<Compile Include="Models\ExtractoRuta.cs" />
<Compile Include="Models\ExtractoRuta.Partial.cs" />
<Compile Include="Models\NotaEntrega.cs" />
<Compile Include="Models\NotaEntrega.Partial.cs" />
```

---

### 2.2. Servicios de Infraestructura

#### IServicioExtractoRuta.cs
```csharp
public interface IServicioExtractoRuta
{
    Task InsertarDesdeFactura(CabPedidoVta pedido, string numeroFactura, string usuario, bool autoSave = true);
    Task InsertarDesdeAlbaran(CabPedidoVta pedido, int numeroAlbaran, string usuario, bool autoSave = true);
}
```

#### ServicioExtractoRuta.cs
**Ubicación:** `Infraestructure/ExtractosRuta/`

**Métodos implementados:**

1. **InsertarDesdeFactura:**
   - Copia datos desde ExtractoCliente (TipoApunte = 1)
   - Incluye: Nº_Orden, Importe, ImportePdte, Nº_Documento, Efecto, FechaVto, CCC
   - TipoRuta = "P" (Pedido)
   - Obtiene vendedor del pedido (no del ExtractoCliente)

2. **InsertarDesdeAlbaran:**
   - Calcula Nº_Orden negativo: `MIN([Nº Orden]) - 1` en ExtractoRuta
   - Importe = 0, ImportePdte = 0
   - Nº_Documento = número de albarán con PadLeft(10)
   - Efecto, FechaVto, FormaPago, CCC = null
   - TipoRuta = "P" (Pedido)
   - Usuario y fecha del pedido

**Parámetro autoSave:**
- `true` (default): Llama a `SaveChangesAsync()` al final
- `false`: NO guarda cambios (para permitir transacciones posteriores)

#### ServicioNotasEntrega.cs (ACTUALIZADO)
**Ubicación:** `Infraestructure/NotasEntrega/`

**Flujo completo implementado:**

1. **Validaciones:**
   - Pedido no null
   - Usuario no null o vacío
   - Obtener cliente para nombre

2. **Obtener número de nota de entrega:**
   ```csharp
   var contador = await db.ContadoresGlobales.FirstOrDefaultAsync();
   int numeroNotaEntrega = contador.NotaEntrega;
   contador.NotaEntrega = numeroNotaEntrega + 1;
   ```

3. **Procesar líneas EN_CURSO:**
   - Insertar en NotasEntrega (NºOrden, Numero, Fecha)
   - Cambiar estado a NOTA_ENTREGA (-2)
   - Si `YaFacturado = true`: dar de baja stock via PreExtrProducto

4. **Calcular Nº_Orden negativo para ExtractoRuta:**
   ```csharp
   var minOrden = await db.ExtractosRuta
       .Where(e => e.Empresa == pedido.Empresa.Trim())
       .Select(e => (int?)e.Nº_Orden)
       .MinAsync() ?? 0;
   int nuevoOrdenNegativo = minOrden < 0 ? minOrden - 1 : -1;
   ```

5. **Insertar en ExtractoRuta (SOLO si tipo ruta lo requiere):**
   ```csharp
   var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
   if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
   {
       // Crear ExtractoRuta con Importe = 0
   }
   ```

6. **Guardar todos los cambios en una única transacción**

---

### 2.3. Modificaciones en ITipoRuta

#### Nuevo método en interfaz:
```csharp
bool DebeInsertarEnExtractoRuta();
```

#### RutaPropia.cs
```csharp
public bool DebeInsertarEnExtractoRuta()
{
    return true;  // Ruta Propia SÍ requiere ExtractoRuta
}
```

#### RutaAgencia.cs
```csharp
public bool DebeInsertarEnExtractoRuta()
{
    return false;  // Ruta de Agencias NO requiere ExtractoRuta
}
```

---

### 2.4. Constantes Agregadas

En `Models/Constantes.cs`:

```csharp
public static class ExtractoRuta
{
    public const string TIPO_RUTA_PEDIDO = "P";
}

public static class DiariosProducto
{
    public const int ENTREGA_FACTURADA = 50; // Para PreExtrProducto de notas de entrega
}
```

---

### 2.5. Integración en GestorFacturacionRutas

#### Constructor actualizado:
```csharp
public GestorFacturacionRutas(
    NVEntities db,
    IServicioAlbaranesVenta servicioAlbaranes,
    IServicioFacturas servicioFacturas,
    IGestorFacturas gestorFacturas,
    IServicioTraspasoEmpresa servicioTraspaso,
    IServicioNotasEntrega servicioNotasEntrega,
    IServicioExtractoRuta servicioExtractoRuta)  // NUEVO
```

#### Inserción en ExtractoRuta tras crear albarán (línea ~228):
```csharp
var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
{
    await servicioExtractoRuta.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario, autoSave: false);
}
```

#### SaveChangesAsync ANTES del traspaso (línea ~244):
```csharp
// IMPORTANTE: Guardar ExtractoRuta del albarán ANTES del traspaso
// El traspaso usa BeginTransaction() y no puede tener cambios pendientes
await db.SaveChangesAsync();
```

#### Inserción en ExtractoRuta tras crear factura (línea ~319):
```csharp
var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
{
    await servicioExtractoRuta.InsertarDesdeFactura(pedido, numeroFactura, usuario, autoSave: true);
}
```

---

### 2.6. Mejoras en Manejo de Errores

#### Nueva sobrecarga de RegistrarError:
```csharp
private void RegistrarError(
    CabPedidoVta pedido,
    string tipoError,
    Exception ex,
    FacturarRutasResponseDTO response)
{
    // Construir mensaje completo con InnerException
    var mensajeCompleto = ex.Message;
    if (ex.InnerException != null)
    {
        mensajeCompleto += " | Inner: " + ex.InnerException.Message;
        if (ex.InnerException.InnerException != null)
        {
            mensajeCompleto += " | Inner2: " + ex.InnerException.InnerException.Message;
        }
    }

    RegistrarError(pedido, tipoError, mensajeCompleto, response);
}
```

#### Llamadas actualizadas (8 ubicaciones):
- Todas las llamadas a `RegistrarError` ahora pasan la excepción completa (`ex`) en lugar de solo `ex.Message`
- Esto permite capturar mensajes de InnerException de Entity Framework

---

### 2.7. Inyección de Dependencias

#### Startup.cs (DI Container):
```csharp
_ = services.AddScoped<IServicioNotasEntrega, ServicioNotasEntrega>();
_ = services.AddScoped<IServicioExtractoRuta, ServicioExtractoRuta>();
```

#### FacturacionRutasController.cs:
```csharp
// En método FacturarRutas y PreviewFacturarRutas
var servicioNotasEntrega = new ServicioNotasEntrega(db);
var servicioExtractoRuta = new ServicioExtractoRuta(db);

var gestor = new GestorFacturacionRutas(
    db,
    servicioAlbaranes,
    servicioFacturas,
    gestorFacturas,
    servicioTraspaso,
    servicioNotasEntrega,
    servicioExtractoRuta  // NUEVO
);
```

---

### 2.8. Tests Actualizados

#### GestorFacturacionRutasTests.cs
```csharp
private IServicioExtractoRuta servicioExtractoRuta;

[TestInitialize]
public void Setup()
{
    // ... otros fakes ...
    servicioExtractoRuta = A.Fake<IServicioExtractoRuta>();

    gestor = new GestorFacturacionRutas(
        db,
        servicioAlbaranes,
        servicioFacturas,
        gestorFacturas,
        servicioTraspaso,
        servicioNotasEntrega,
        servicioExtractoRuta  // NUEVO
    );
}
```

Todos los tests actualizados con el nuevo parámetro.

#### ServicioExtractoRutaTests.cs (NUEVO)
- 7 tests creados cubriendo:
  - InsertarDesdeFactura con datos válidos
  - InsertarDesdeAlbaran con cálculo de Nº_Orden negativo
  - Parámetro autoSave (true/false)
  - Manejo de errores

---

## 3. ERRORES ENCONTRADOS Y SOLUCIONES

### 3.1. Error de Transacción (RESUELTO)
**Error:** `SqlException: No se permite una nueva transacción porque hay otros subprocesos en ejecución en la sesión`

**Causa:**
1. `CrearAlbaran()` → SaveChangesAsync()
2. `InsertarDesdeAlbaran()` → SaveChangesAsync()
3. `TraspasarPedidoAEmpresa()` → BeginTransaction() ❌ FAILED

**Solución:**
- Agregado parámetro `autoSave` a métodos de ServicioExtractoRuta
- Llamar con `autoSave: false` en albaranes
- SaveChangesAsync() explícito ANTES del traspaso (línea 244)

---

### 3.2. Conflicto de Namespace (RESUELTO)
**Error:** `CS0118: 'NotasEntrega' es espacio de nombres pero se usa como tipo`

**Causa:** Carpeta `Infraestructure/NotasEntrega/` y clase `NotasEntrega` causaban colisión

**Solución:** Renombrar clase a `NotaEntrega` (singular)

---

### 3.3. Archivos No en Proyecto (RESUELTO)
**Error:** `CS0234: El tipo o el nombre del espacio de nombres 'ExtractosRuta' no existe`

**Solución:** Agregados 4 archivos al .csproj:
- `Infraestructure\ExtractosRuta\IServicioExtractoRuta.cs`
- `Infraestructure\ExtractosRuta\ServicioExtractoRuta.cs`
- `Models\ExtractoRuta.cs`
- `Models\NotaEntrega.cs`

---

### 3.4. Conflicto con OnModelCreating (IDENTIFICADO)
**Error:** El EDMX genera `throw new UnintentionalCodeFirstException();` en OnModelCreating

**Solución aplicada:**
- Crear entidades manualmente con Data Annotations
- NO usar OnModelCreating
- DbSets agregados en NVEntities.Partial.cs

---

### 3.5. Error en NotasEntrega.Add() (PENDIENTE VERIFICACIÓN)
**Estado:** ERROR ACTUAL

**Síntomas:**
- Al facturar rutas con notas de entrega, falla en `db.NotasEntregas.Add()`
- En resumen aparece "Errores: 1"
- Ventana de errores NO muestra detalles (grid vacío)

**Acciones tomadas:**
- Configuradas claves primarias compuestas con Data Annotations
- Mejorado manejo de errores para capturar InnerException completo
- Pendiente: Ejecutar de nuevo para ver mensaje de error completo

**Próximo paso mañana:** Revisar mensaje de error detallado con InnerException

---

## 4. ESTADO ACTUAL

### ✅ COMPLETADO
1. Entidades ExtractoRuta y NotaEntrega creadas con claves primarias
2. ServicioExtractoRuta implementado (facturas y albaranes)
3. ServicioNotasEntrega completamente implementado
4. Integración en GestorFacturacionRutas
5. Lógica condicional por tipo de ruta (DebeInsertarEnExtractoRuta)
6. Manejo de transacciones y autoSave
7. Mejora en captura de errores (InnerException)
8. Tests actualizados
9. Inyección de dependencias configurada

### 🔄 EN PROGRESO
1. **Error en NotasEntrega.Add()**: Pendiente ver mensaje completo
2. **Verificación de Preview**: Lógica de PuedeFacturarPedido puede no considerar estado futuro

### ⏸️ PENDIENTE
1. Corregir error en inserción de NotasEntrega
2. Revisar lógica de Preview para MantenerJunto (método `PodraFacturarDespuesDeAlbaran`)
3. Evaluar manejo de timeout (100 segundos)
4. Pruebas completas con:
   - Facturas (NRM)
   - Albaranes (FDM)
   - Notas de entrega
   - Ruta Propia vs Ruta de Agencias

---

## 5. ESTRUCTURA DE ARCHIVOS MODIFICADOS/CREADOS

```
NestoAPI/
├── Infraestructure/
│   ├── ExtractosRuta/           [NUEVO]
│   │   ├── IServicioExtractoRuta.cs
│   │   └── ServicioExtractoRuta.cs
│   ├── NotasEntrega/
│   │   ├── IServicioNotasEntrega.cs
│   │   └── ServicioNotasEntrega.cs    [ACTUALIZADO]
│   └── Facturas/
│       ├── GestorFacturacionRutas.cs  [ACTUALIZADO]
│       └── IGestorFacturacionRutas.cs
├── Models/
│   ├── ExtractoRuta.cs          [NUEVO]
│   ├── ExtractoRuta.Partial.cs  [NUEVO]
│   ├── NotaEntrega.cs           [NUEVO]
│   ├── NotaEntrega.Partial.cs   [NUEVO]
│   ├── NVEntities.Partial.cs    [ACTUALIZADO]
│   ├── Constantes.cs            [ACTUALIZADO]
│   └── Facturas/
│       ├── ITipoRuta.cs         [ACTUALIZADO]
│       ├── RutaPropia.cs        [ACTUALIZADO]
│       └── RutaAgencia.cs       [ACTUALIZADO]
├── Controllers/
│   └── FacturacionRutasController.cs   [ACTUALIZADO]
├── Startup.cs                   [ACTUALIZADO]
└── NestoAPI.csproj             [ACTUALIZADO]

NestoAPI.Tests/
├── Infrastructure/
│   ├── GestorFacturacionRutasTests.cs  [ACTUALIZADO]
│   └── ServicioExtractoRutaTests.cs    [NUEVO]
```

---

## 6. CÓDIGO CLAVE DE REFERENCIA

### Inserción desde Factura (ServicioExtractoRuta.cs)
```csharp
// Buscar el extracto cliente (TipoApunte = 1)
var extractoCliente = await db.ExtractosCliente
    .FirstOrDefaultAsync(e =>
        e.Empresa == pedido.Empresa &&
        e.Número == pedido.Nº_Cliente &&
        e.Contacto == pedido.Contacto &&
        e.TipoApunte == "1" &&
        e.Nº_Documento == numeroFactura);

// Copiar a ExtractoRuta
var extractoRuta = new ExtractoRuta
{
    Empresa = pedido.Empresa,
    Nº_Orden = extractoCliente.Nº_Orden,
    Número = pedido.Nº_Cliente,
    Contacto = pedido.Contacto,
    CodPostal = cliente?.CodPostal,
    Fecha = DateTime.Now,
    Nº_Documento = numeroFactura,
    Efecto = extractoCliente.Efecto,
    Concepto = pedido.Comentarios,
    Importe = extractoCliente.Importe,
    ImportePdte = extractoCliente.ImportePdte,
    Delegación = primeraLinea.Delegación,
    FormaVenta = primeraLinea.Forma_Venta,
    Vendedor = pedido.Vendedor,  // Del pedido, NO del ExtractoCliente
    FechaVto = extractoCliente.FechaVto,
    FormaPago = pedido.Forma_Pago,
    Ruta = pedido.Ruta,
    Estado = 0,
    TipoRuta = Constantes.ExtractoRuta.TIPO_RUTA_PEDIDO,
    Usuario = usuario,
    Fecha_Modificación = DateTime.Now
};
```

### Cálculo Nº_Orden Negativo (ServicioExtractoRuta.cs)
```csharp
var minOrden = await db.ExtractosRuta
    .Where(e => e.Empresa == pedido.Empresa.Trim())
    .Select(e => (int?)e.Nº_Orden)
    .MinAsync() ?? 0;

int nuevoOrdenNegativo = minOrden < 0 ? minOrden - 1 : -1;
```

### Condicional por Tipo de Ruta (GestorFacturacionRutas.cs)
```csharp
var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
if (tipoRuta?.DebeInsertarEnExtractoRuta() == true)
{
    await servicioExtractoRuta.InsertarDesdeAlbaran(pedido, numeroAlbaran, usuario, autoSave: false);
}
```

---

## 7. TAREAS PARA MAÑANA

### Prioridad ALTA
1. ✅ Ejecutar facturación con nota de entrega
2. ✅ Capturar mensaje de error completo (con InnerException)
3. ✅ Analizar y corregir error en NotasEntrega.Add()
4. ✅ Verificar que ExtractoRuta se inserta correctamente

### Prioridad MEDIA
5. Revisar lógica de Preview para MantenerJunto:
   - Crear método `PodraFacturarDespuesDeAlbaran`
   - Evaluar estado futuro de líneas después de crear albarán

6. Evaluar solución para timeout de 100 segundos:
   - Opción 1: Procesamiento asíncrono con polling
   - Opción 2: Aumentar timeout en cliente WPF
   - Opción 3: Guardar errores en BD durante proceso
   - Opción 4: Procesar por lotes

### Prioridad BAJA
7. Pruebas exhaustivas con:
   - Rutas Propias (AT, 16) → debe insertar ExtractoRuta
   - Rutas Agencias (00, FW) → NO debe insertar ExtractoRuta
   - Facturas NRM con y sin MantenerJunto
   - Albaranes FDM
   - Notas de entrega con YaFacturado = true/false

---

## 8. NOTAS TÉCNICAS

### Database First vs Code First
- Proyecto usa **Database First** con EDMX
- EDMX genera `throw new UnintentionalCodeFirstException();` en OnModelCreating
- Por eso ExtractoRuta y NotaEntrega se crearon manualmente con Data Annotations
- NO se pueden agregar al EDMX desde Visual Studio (problemas con diseñador)

### Claves Primarias Compuestas
```csharp
// ExtractoRuta
[Key]
[Column(Order = 0)]
public string Empresa { get; set; }

[Key]
[Column("Nº Orden", Order = 1)]
public int Nº_Orden { get; set; }

// NotaEntrega
[Key]
[Column("NºOrden", Order = 0)]
public int NºOrden { get; set; }

[Key]
[Column("NotaEntrega", Order = 1)]
public int Numero { get; set; }
```

### Transacciones y SaveChangesAsync
- **autoSave = false**: Permite acumular cambios sin guardar
- **SaveChangesAsync() explícito**: ANTES de operaciones con BeginTransaction()
- Evita error: "No se permite una nueva transacción porque hay otros subprocesos..."

---

## 9. REFERENCIAS SQL

### Tabla ExtractoRuta (estructura)
```sql
CREATE TABLE ExtractoRuta (
    Empresa VARCHAR(2) NOT NULL,
    [Nº Orden] INT NOT NULL,
    Número VARCHAR(6) NOT NULL,
    Contacto VARCHAR(6) NOT NULL,
    CodPostal VARCHAR(8),
    Fecha DATETIME NOT NULL,
    [Nº Documento] VARCHAR(10),
    Efecto VARCHAR(8),
    Concepto VARCHAR(40),
    Importe DECIMAL(19,4) NOT NULL,
    ImportePdte DECIMAL(19,4) NOT NULL,
    Delegación VARCHAR(4),
    FormaVenta VARCHAR(10),
    Vendedor VARCHAR(5),
    FechaVto DATETIME,
    FormaPago VARCHAR(1),
    Ruta VARCHAR(4),
    Estado TINYINT NOT NULL,
    TipoRuta VARCHAR(1),
    Usuario VARCHAR(25),
    [Fecha Modificación] DATETIME NOT NULL,
    PRIMARY KEY (Empresa, [Nº Orden])
)
```

### Tabla NotasEntrega (estructura)
```sql
CREATE TABLE NotasEntrega (
    NºOrden INT NOT NULL,
    NotaEntrega INT NOT NULL,
    Fecha DATETIME NOT NULL,
    PRIMARY KEY (NºOrden, NotaEntrega)
)
```

---

## 10. CONTACTO Y CONTINUACIÓN

**Próxima sesión:** Continuar con depuración del error en NotasEntrega.Add()

**Archivo de sesión:** Este documento
**Ubicación:** `C:\Users\Carlos\source\repos\NestoAPI\SESION_EXTRACTORUTA_Y_NOTASENTREGA.md`

---

*Documentación generada el 2025-11-06*
