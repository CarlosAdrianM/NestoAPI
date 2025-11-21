# Sistema de Excepciones de Negocio de NestoAPI

## Índice
- [Introducción](#introducción)
- [Arquitectura](#arquitectura)
- [Uso Básico](#uso-básico)
- [Excepciones Disponibles](#excepciones-disponibles)
- [Códigos de Error Estándar](#códigos-de-error-estándar)
- [Respuestas HTTP](#respuestas-http)
- [Guía de Migración](#guía-de-migración)
- [Ejemplos Avanzados](#ejemplos-avanzados)

## Introducción

Este sistema proporciona una infraestructura robusta para el manejo de errores en NestoAPI, con las siguientes características:

✅ **Excepciones tipadas** con contexto rico de negocio
✅ **Respuestas HTTP consistentes** con información útil para debugging
✅ **Códigos de error estandarizados** para facilitar manejo en cliente
✅ **Logging automático** de errores
✅ **Modo DEBUG vs RELEASE** con diferente nivel de detalle

## Arquitectura

```
┌─────────────────────────────────────────────────────┐
│  Controller (FacturasController)                    │
│  - NO captura excepciones                           │
│  - Las deja propagarse                              │
└───────────────────┬─────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────┐
│  Business Logic (GestorFacturas/ServicioFacturas)   │
│  - Lanza FacturacionException con contexto          │
│  - Incluye: empresa, pedido, usuario, etc.          │
└───────────────────┬─────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────┐
│  GlobalExceptionFilter                              │
│  - Captura TODAS las excepciones                    │
│  - Formatea respuestas JSON consistentes            │
│  - Loggea errores automáticamente                   │
│  - Oculta detalles sensibles en producción          │
└───────────────────┬─────────────────────────────────┘
                    │
                    ▼
┌─────────────────────────────────────────────────────┐
│  Cliente (Frontend/API Consumer)                    │
│  - Recibe JSON estructurado con:                    │
│    • code: Código de error                          │
│    • message: Descripción user-friendly             │
│    • details: Contexto de negocio                   │
│    • timestamp: Fecha/hora del error                │
└─────────────────────────────────────────────────────┘
```

## Uso Básico

### 1. Lanzar una excepción simple

```csharp
throw new FacturacionException(
    "El pedido no tiene líneas para facturar",
    "FACTURACION_SIN_LINEAS",
    empresa: "1",
    pedido: 12345);
```

### 2. Lanzar con inner exception (wrapping)

```csharp
catch (SqlException ex)
{
    throw new FacturacionException(
        "Error al ejecutar el procedimiento de facturación",
        "FACTURACION_STORED_PROCEDURE_ERROR",
        ex,  // Inner exception
        empresa: "1",
        pedido: 12345);
}
```

### 3. Agregar datos adicionales

```csharp
throw new FacturacionException(
    "La serie de facturación no es válida",
    "FACTURACION_SERIE_INVALIDA",
    empresa: "3",
    pedido: 12345,
    usuario: "carlos")
    .WithData("SerieIntentada", "XX")
    .WithData("SerieEsperada", "NV");
```

### 4. Marcar como warning (no crítico)

```csharp
throw new FacturacionException(
    "El pedido está pendiente de aprobar",
    "FACTURACION_PENDIENTE_APROBACION",
    empresa: "1",
    pedido: 12345)
    .AsWarning();  // Se loggea como WARNING en lugar de ERROR
```

## Excepciones Disponibles

### NestoBusinessException (Base)
Excepción base para todos los errores de negocio. Úsala cuando ninguna excepción específica aplique.

```csharp
throw new NestoBusinessException(
    "Error genérico de negocio",
    new ErrorContext { ErrorCode = "BUSINESS_ERROR" });
```

### FacturacionException
Para errores en el proceso de facturación.

**Parámetros:**
- `empresa`: Código de empresa (1, 3, etc.)
- `pedido`: Número de pedido
- `factura`: Número de factura
- `usuario`: Usuario que ejecutó la operación

**Códigos de error comunes:**
- `FACTURACION_IVA_FALTANTE`
- `FACTURACION_SIN_LINEAS`
- `FACTURACION_STORED_PROCEDURE_ERROR`
- `FACTURACION_SERIE_INVALIDA`

### PedidoInvalidoException
Para errores de validación de pedidos.

**Parámetros:**
- `empresa`: Código de empresa
- `pedido`: Número de pedido
- `cliente`: Código de cliente
- `usuario`: Usuario que ejecutó la operación

**Códigos de error comunes:**
- `PEDIDO_SIN_LINEAS`
- `PEDIDO_CLIENTE_NO_EXISTE`
- `PEDIDO_ESTADO_INVALIDO`

### TraspasoEmpresaException
Para errores en traspasos de pedidos entre empresas.

**Parámetros:**
- `empresaOrigen`: Empresa de origen (1)
- `empresaDestino`: Empresa destino (3)
- `pedido`: Número de pedido
- `cliente`: Código de cliente
- `usuario`: Usuario que ejecutó la operación

**Códigos de error comunes:**
- `TRASPASO_CLIENTE_ERROR`
- `TRASPASO_PRODUCTO_ERROR`
- `TRASPASO_CUENTA_CONTABLE_ERROR`

## Códigos de Error Estándar

Los códigos de error siguen el formato: `{ÁREA}_{DESCRIPCIÓN}`

### Facturación
| Código | Descripción |
|--------|-------------|
| `FACTURACION_IVA_FALTANTE` | Falta configurar el campo IVA en el pedido |
| `FACTURACION_SIN_LINEAS` | El pedido no tiene líneas para facturar |
| `FACTURACION_STORED_PROCEDURE_ERROR` | Error al ejecutar prdCrearFacturaVta |
| `FACTURACION_SERIE_INVALIDA` | La serie de facturación no es válida |
| `FACTURACION_ERROR_INESPERADO` | Error genérico inesperado |

### Pedidos
| Código | Descripción |
|--------|-------------|
| `PEDIDO_SIN_LINEAS` | El pedido no tiene líneas |
| `PEDIDO_CLIENTE_NO_EXISTE` | El cliente no existe |
| `PEDIDO_ESTADO_INVALIDO` | El estado del pedido no permite la operación |
| `PEDIDO_VALIDACION_FALLO` | El pedido no pasó las validaciones de precios/ofertas/descuentos |

### Traspasos
| Código | Descripción |
|--------|-------------|
| `TRASPASO_CLIENTE_ERROR` | Error al copiar cliente entre empresas |
| `TRASPASO_PRODUCTO_ERROR` | Error al copiar producto entre empresas |
| `TRASPASO_CUENTA_CONTABLE_ERROR` | Error al copiar cuenta contable |

## Respuestas HTTP

### Modo DEBUG (Development)

```json
{
  "error": {
    "code": "FACTURACION_IVA_FALTANTE",
    "message": "El pedido 12345 no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido",
    "details": {
      "empresa": "1",
      "pedido": 12345,
      "usuario": "carlos"
    },
    "timestamp": "2025-01-19T10:30:00Z",
    "stackTrace": "at NestoAPI.Infraestructure.Facturas.ServicioFacturas.CrearFactura...",
    "innerException": {
      "message": "Column 'IVA' cannot be null",
      "type": "SqlException",
      "stackTrace": "..."
    }
  }
}
```

### Modo RELEASE (Production)

```json
{
  "error": {
    "code": "FACTURACION_IVA_FALTANTE",
    "message": "El pedido 12345 no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido",
    "details": {
      "empresa": "1",
      "pedido": 12345,
      "usuario": "carlos"
    },
    "timestamp": "2025-01-19T10:30:00Z"
  }
}
```

**Nota:** En producción se ocultan `stackTrace` e `innerException` para evitar exponer detalles técnicos sensibles.

## Guía de Migración

### Paso 1: Identificar excepciones genéricas

Busca patrones como:
```csharp
throw new Exception("mensaje genérico");
```

### Paso 2: Reemplazar con excepciones específicas

**Antes:**
```csharp
if (string.IsNullOrEmpty(cabPedido.IVA))
{
    throw new Exception("Este pedido no se puede facturar");
}
```

**Después:**
```csharp
if (string.IsNullOrEmpty(cabPedido.IVA))
{
    throw new FacturacionException(
        $"El pedido {pedido} no se puede facturar porque falta configurar el campo IVA en la cabecera del pedido",
        "FACTURACION_IVA_FALTANTE",
        empresa: empresa,
        pedido: pedido,
        usuario: usuario);
}
```

### Paso 3: Envolver excepciones de sistema

**Antes:**
```csharp
catch (Exception ex)
{
    throw new Exception("Error al crear la factura", ex);
}
```

**Después:**
```csharp
catch (SqlException ex)
{
    throw new FacturacionException(
        $"Error al ejecutar el procedimiento almacenado de facturación: {ex.Message}",
        "FACTURACION_STORED_PROCEDURE_ERROR",
        ex,
        empresa: empresa,
        pedido: pedido,
        usuario: usuario)
        .WithData("SqlErrorNumber", ex.Number)
        .WithData("StoredProcedure", "prdCrearFacturaVta");
}
```

### Paso 4: Eliminar try-catch en controllers

**Antes:**
```csharp
try
{
    var result = await gestor.CrearFactura(empresa, pedido, usuario);
    return Ok(result);
}
catch (Exception ex)
{
    return InternalServerError(ex);
}
```

**Después:**
```csharp
// Las excepciones se propagan automáticamente al GlobalExceptionFilter
var result = await gestor.CrearFactura(empresa, pedido, usuario);
return Ok(result);
```

## Ejemplos Avanzados

### Ejemplo 1: Validación con múltiples condiciones

```csharp
public void ValidarPedido(CabPedidoVta pedido)
{
    if (!pedido.LinPedidoVtas.Any())
    {
        throw new PedidoInvalidoException(
            $"El pedido {pedido.Número} no tiene líneas",
            "PEDIDO_SIN_LINEAS",
            empresa: pedido.Empresa,
            pedido: pedido.Número,
            cliente: pedido.Nº_Cliente);
    }

    if (string.IsNullOrEmpty(pedido.Serie))
    {
        throw new PedidoInvalidoException(
            $"El pedido {pedido.Número} no tiene serie asignada",
            "PEDIDO_SIN_SERIE",
            empresa: pedido.Empresa,
            pedido: pedido.Número,
            cliente: pedido.Nº_Cliente)
            .WithData("SeriePorDefecto", "NV");
    }
}
```

### Ejemplo 2: Manejo de errores en bucles

```csharp
var errores = new List<string>();

foreach (var linea in pedido.LinPedidoVtas)
{
    try
    {
        ProcesarLinea(linea);
    }
    catch (Exception ex)
    {
        errores.Add($"Línea {linea.Nº_Orden}: {ex.Message}");
    }
}

if (errores.Any())
{
    throw new PedidoInvalidoException(
        $"El pedido {pedido.Número} tiene {errores.Count} líneas con errores",
        "PEDIDO_LINEAS_INVALIDAS",
        empresa: pedido.Empresa,
        pedido: pedido.Número)
        .WithData("Errores", string.Join("; ", errores));
}
```

### Ejemplo 3: Crear nuevas excepciones específicas

```csharp
// En Infraestructure/Exceptions/StockException.cs
public class StockException : NestoBusinessException
{
    public StockException(
        string message,
        string errorCode = "STOCK_ERROR",
        string empresa = null,
        string producto = null,
        string almacen = null,
        int? cantidad = null)
        : base(message, new ErrorContext
        {
            ErrorCode = errorCode,
            Empresa = empresa
        })
    {
        if (!string.IsNullOrEmpty(producto))
            Context.WithData("Producto", producto);

        if (!string.IsNullOrEmpty(almacen))
            Context.WithData("Almacen", almacen);

        if (cantidad.HasValue)
            Context.WithData("Cantidad", cantidad);
    }
}

// Uso:
throw new StockException(
    "No hay stock suficiente del producto",
    "STOCK_INSUFICIENTE",
    empresa: "1",
    producto: "12345678",
    almacen: "ALG",
    cantidad: 10);
```

## Mejoras Futuras

- [ ] Integración con sistema de logging externo (Serilog, NLog, Application Insights)
- [ ] Traducción de mensajes de error a múltiples idiomas (i18n)
- [ ] Métricas de errores (frecuencia, tipos más comunes)
- [ ] Alertas automáticas para errores críticos
- [ ] Documentación automática de códigos de error en Swagger

## Contacto

Para dudas o sugerencias sobre este sistema:
- Revisa los ejemplos en `Infraestructure/Facturas/ServicioFacturas.cs`
- Consulta con el equipo de desarrollo

---

**Última actualización:** 2025-01-19
**Versión:** 1.0
**Estado:** ✅ Activo en producción (solo facturación)
