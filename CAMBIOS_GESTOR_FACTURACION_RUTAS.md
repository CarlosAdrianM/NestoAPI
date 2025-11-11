# Cambios en GestorFacturacionRutas

## 1. Agregar using al inicio del archivo

```csharp
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.Traspasos;
using NestoAPI.Models;
using NestoAPI.Models.Facturas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
```

**AGREGAR ESTA LÍNEA DESPUÉS DE los using existentes:**
```csharp
using static NestoAPI.Models.Facturas.TipoRutaFactory;
```

## 2. Modificar el método GenerarDatosImpresionAlbaran (línea ~361)

**ANTES:**
```csharp
/// <summary>
/// Genera los datos de impresión para un albarán (bytes del PDF, copias, bandeja).
/// </summary>
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
/// <summary>
/// Genera los datos de impresión para un albarán (bytes del PDF, copias, bandeja).
/// Usa el sistema de tipos de ruta para determinar número de copias y bandeja.
/// </summary>
/// <param name="pedido">Pedido para determinar el tipo de ruta y número de copias</param>
/// <param name="empresa">Empresa del albarán</param>
/// <param name="numeroAlbaran">Número de albarán</param>
private DocumentoParaImprimir GenerarDatosImpresionAlbaran(CabPedidoVta pedido, string empresa, int numeroAlbaran)
{
    var lookup = new FacturaLookup { Empresa = empresa, Factura = numeroAlbaran.ToString() };
    var lista = new List<FacturaLookup> { lookup };
    var albaranes = gestorFacturas.LeerAlbaranes(lista);

    var bytesPdf = gestorFacturas.FacturasEnPDF(albaranes, papelConMembrete: false);

    // Determinar tipo de ruta y obtener configuración de impresión
    var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
    bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);
    int numeroCopias = tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO);

    return new DocumentoParaImprimir
    {
        BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
        NumeroCopias = numeroCopias,
        Bandeja = tipoRuta.ObtenerBandeja()
    };
}
```

## 3. Modificar el método GenerarDatosImpresionFactura (línea ~380)

**ANTES:**
```csharp
/// <summary>
/// Genera los datos de impresión para una factura (bytes del PDF, copias, bandeja).
/// </summary>
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
/// <summary>
/// Genera los datos de impresión para una factura (bytes del PDF, copias, bandeja).
/// Usa el sistema de tipos de ruta para determinar número de copias y bandeja.
/// </summary>
/// <param name="pedido">Pedido para determinar el tipo de ruta y número de copias</param>
/// <param name="empresa">Empresa de la factura</param>
/// <param name="numeroFactura">Número de factura</param>
private DocumentoParaImprimir GenerarDatosImpresionFactura(CabPedidoVta pedido, string empresa, string numeroFactura)
{
    var factura = gestorFacturas.LeerFactura(empresa, numeroFactura);
    var facturas = new List<Factura> { factura };

    var bytesPdf = gestorFacturas.FacturasEnPDF(facturas, papelConMembrete: false);

    // Determinar tipo de ruta y obtener configuración de impresión
    var tipoRuta = TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta);
    bool debeImprimir = DebeImprimirDocumento(pedido.Comentarios);
    int numeroCopias = tipoRuta.ObtenerNumeroCopias(pedido, debeImprimir, Constantes.Empresas.EMPRESA_POR_DEFECTO);

    return new DocumentoParaImprimir
    {
        BytesPDF = bytesPdf.ReadAsByteArrayAsync().Result,
        NumeroCopias = numeroCopias,
        Bandeja = tipoRuta.ObtenerBandeja()
    };
}
```

## 4. Actualizar llamadas a estos métodos

### En ProcesarFacturaNRM (línea ~304):

**ANTES:**
```csharp
facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido.Empresa, numeroFactura);
```

**DESPUÉS:**
```csharp
facturaCreada.DatosImpresion = GenerarDatosImpresionFactura(pedido, pedido.Empresa, numeroFactura);
```

### En AgregarDatosImpresionAlbaranSiCorresponde (línea ~354):

**ANTES:**
```csharp
albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido.Empresa, numeroAlbaran);
```

**DESPUÉS:**
```csharp
albaran.DatosImpresion = GenerarDatosImpresionAlbaran(pedido, pedido.Empresa, numeroAlbaran);
```

## Resumen de cambios

1. Se agrega el parámetro `CabPedidoVta pedido` a ambos métodos de generación de datos de impresión
2. Se usa `TipoRutaFactory.ObtenerPorNumeroRuta(pedido.Ruta)` para obtener el tipo de ruta correcto
3. Se usa `tipoRuta.ObtenerNumeroCopias(...)` para determinar dinámicamente el número de copias
4. Se usa `tipoRuta.ObtenerBandeja()` para obtener la bandeja correcta
5. Se actualizan las dos llamadas a estos métodos para pasar el pedido como primer parámetro

## Ventajas

- **Extensibilidad**: Para agregar un nuevo tipo de ruta, solo hay que crear una clase que implemente `ITipoRuta`
- **Mantenibilidad**: La lógica de impresión está encapsulada en cada tipo de ruta
- **Claridad**: El código es más legible y autodocumentado
