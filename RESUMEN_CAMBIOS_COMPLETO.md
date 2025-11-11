# Resumen Completo de Cambios - Facturación de Rutas

## Archivos Nuevos a Agregar al Proyecto

### 1. Sistema de Tipos de Ruta (Models/Facturas/)
- ✅ `ITipoRuta.cs` - Interfaz base
- ✅ `RutaPropia.cs` - Rutas AT y 16 (siempre 2 copias)
- ✅ `RutaAgencia.cs` - Rutas 00 y FW (copias condicionales)
- ✅ `TipoRutaFactory.cs` - Factory para gestión dinámica

## Archivos a Reemplazar

### 2. ServicioTraspasoEmpresa (Infraestructure/Traspasos/)
**Reemplazar:**
- `ServicioTraspasoEmpresa.cs` → `ServicioTraspasoEmpresa_V2.cs`

**Mejoras aplicadas:**
- ✅ Reutiliza conexión del DbContext (evita inconsistencias)
- ✅ Usa SqlCommand con parámetros (previene inyección SQL)
- ✅ Verifica estado de conexión antes de abrirla
- ✅ Usa transacción del DbContext
- ✅ Evita UnintentionalCodeFirstException

### 3. ServicioPedidosParaFacturacion (Infraestructure/Pedidos/)
**Reemplazar:**
- `ServicioPedidosParaFacturacion.cs` → `ServicioPedidosParaFacturacion_Refactorizado.cs`

**Mejoras aplicadas:**
- ✅ Obtiene rutas dinámicamente desde TipoRutaFactory
- ✅ Elimina constantes hardcodeadas
- ✅ Método `ObtenerRutasSegunTipo()` usa `TipoRutaFactory.ObtenerPorId()`

## Archivos a Modificar (Aplicar cambios manualmente)

### 4. GestorFacturacionRutas.cs (Infraestructure/Facturas/)

**Paso 1:** Agregar using al inicio
```csharp
using NestoAPI.Models.Facturas; // Si no está ya
```

**Paso 2:** Modificar `GenerarDatosImpresionAlbaran` (línea ~361)

**ANTES:**
```csharp
private DocumentoParaImprimir GenerarDatosImpresionAlbaran(string empresa, int numeroAlbaran)
{
    var lookup = new FacturaLookup { Empresa = empresa, Factura = numeroAlbaran.ToString() };
    var lista = new List<FacturaLookup> { lookup };
    var albaranes = gestorFacturas.LeerAlbaranes(lista);

    var bytesPdf = gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete: false);

    return new DocumentoParaImprimir
    {
        BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
        NumeroCopias = 1, // TODO: Configurar según reglas de negocio
        Bandeja = "Default" // TODO: Configurar según reglas de negocio
    };
}
```

**DESPUÉS:**
```csharp
private DocumentoParaImprimir GenerarDatosImpresionAlbaran(CabPedidoVta pedido, string empresa, int numeroAlbaran)
{
    var lookup = new FacturaLookup { Empresa = empresa, Factura = numeroAlbaran.ToString() };
    var lista = new List<FacturaLookup> { lookup };
    var albaranes = gestorFacturas.LeerAlbaranes(lista);

    var bytesPdf = gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete: false);

    // Determinar tipo de ruta y obtener configuración de impresión
    var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
    bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

    // Si la ruta no está manejada por ningún tipo, no imprimir
    int numeroCopias = tipoRuta != null
        ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
        : 0;

    string bandeja = tipoRuta != null ? tipoRuta.ObtenerBandeja() : "Default";

    return new DocumentoParaImprimir
    {
        BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
        NumeroCopias = numeroCopias,
        Bandeja = bandeja
    };
}
```

**Paso 3:** Modificar `GenerarDatosImpresionFactura` (línea ~380)

**ANTES:**
```csharp
private DocumentoParaImprimir GenerarDatosImpresionFactura(string empresa, string numeroFactura)
{
    var factura = gestorFacturas.LeerFactura(empresa, numeroFactura);
    var facturas = new List<Factura> { factura };

    var bytesPdf = gestorFacturas.FacturasEnPDF(facturas, papelConMembrete: false);

    return new DocumentoParaImprimir
    {
        BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
        NumeroCopias = 1, // TODO: Configurar según reglas de negocio
        Bandeja = "Default" // TODO: Configurar según reglas de negocio
    };
}
```

**DESPUÉS:**
```csharp
private DocumentoParaImprimir GenerarDatosImpresionFactura(CabPedidoVta pedido, string empresa, string numeroFactura)
{
    var factura = gestorFacturas.LeerFactura(empresa, numeroFactura);
    var facturas = new List<Factura> { factura };

    var bytesPdf = gestorFacturas.FacturasEnPDF(facturas, papelConMembrete: false);

    // Determinar tipo de ruta y obtener configuración de impresión
    var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
    bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);

    // Si la ruta no está manejada por ningún tipo, no imprimir
    int numeroCopias = tipoRuta != null
        ? tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO)
        : 0;

    string bandeja = tipoRuta != null ? tipoRuta.ObtenerBandeja() : "Default";

    return new DocumentoParaImprimir
    {
        BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
        NumeroCopias = numeroCopias,
        Bandeja = bandeja
    };
}
```

**Paso 4:** Actualizar llamadas a estos métodos

En `ProcesarFacturaNRM` (línea ~304):
```csharp
// ANTES:
facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido.Empresa, numeroFactura);

// DESPUÉS:
facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido, pedido.Empresa, numeroFactura);
```

En `AgregarDatosImpresionAlbaranSiCorresponde` (línea ~354):
```csharp
// ANTES:
albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido.Empresa, numeroAlbaran);

// DESPUÉS:
albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido, pedido.Empresa, numeroAlbaran);
```

## Comportamiento del Sistema

### Rutas Propias (AT, 16)
- **Siempre 2 copias** (original + 1 copia)
- Independiente de empresa y comentarios

### Rutas de Agencias (00, FW)
- **Traspasadas a empresa 3**: 0 copias
- **En empresa 1 CON "factura física"/"albarán físico"**: 1 copia (solo original)
- **En empresa 1 SIN comentario**: 0 copias

### Rutas No Manejadas
- Si una ruta no está en ninguna implementación, no se procesa (se filtra en la consulta inicial)

## Extensibilidad

Para agregar un tercer tipo de ruta (ej: "Ruta Express"):

1. Crear `RutaExpress.cs`:
```csharp
public class RutaExpress : ITipoRuta
{
    private static readonly List<string> rutasExpress = new List<string> { "EX", "XP" };

    public string Id => "EXPRESS";
    public string NombreParaMostrar => "Ruta Express";
    public string Descripcion => "Descripción del comportamiento";
    public IReadOnlyList<string> RutasContenidas => rutasExpress.AsReadOnly();

    public bool ContieneRuta(string numeroRuta) { /* implementación */ }
    public int ObtenerNumeroCopias(...) { /* lógica específica */ }
    public string ObtenerBandeja() { return "Tray2"; }
}
```

2. Agregar en `TipoRutaFactory.cs`:
```csharp
private static readonly List<ITipoRuta> tiposRutaRegistrados = new List<ITipoRuta>
{
    new RutaPropia(),
    new RutaAgencia(),
    new RutaExpress() // ← AGREGAR AQUÍ
};
```

3. ¡Listo! El sistema automáticamente:
   - Incluye las rutas EX y XP en el filtrado
   - Aplica la lógica de impresión específica
   - Está disponible en la UI para selección

## Validación

Después de aplicar los cambios:

1. **Compilar** el proyecto en Visual Studio
2. **Verificar** que no hay errores de compilación
3. **Probar** en ejecución:
   - Ruta propia (AT/16) → debe generar 2 copias siempre
   - Ruta agencia (00/FW) en empresa 1 con "factura física" → 1 copia
   - Ruta agencia (00/FW) en empresa 1 sin comentario → 0 copias
   - Ruta agencia (00/FW) en empresa 3 (traspasada) → 0 copias
4. **Verificar** que el traspaso de empresas funciona sin errores
