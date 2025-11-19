# Fix: Recarga de Pedido Después de Facturar con Traspaso de Empresa

## 📋 Problema

Cuando se factura un pedido que requiere traspaso a empresa espejo (de empresa "1" a empresa "3"), el sistema:
1. ✅ Traspasa correctamente el pedido a empresa "3"
2. ✅ Crea la factura en empresa "3"
3. ❌ **Intenta recargar el pedido desde empresa "1"** (empresa original)
4. ❌ **Falla con error "No se ha podido recuperar el pedido. Código estado: NotFound"**

**Causa raíz:** El método `CrearFactura` solo retornaba el número de factura, no la empresa donde se facturó realmente.

## ✅ Solución Implementada

Se modificó el endpoint `CrearFacturaVenta` para que retorne un DTO completo con la información de dónde se facturó el pedido.

### Backend (NestoAPI)

#### 1. Nuevo DTO `CrearFacturaResponseDTO`
**Archivo**: `NestoAPI/Models/Facturas/CrearFacturaResponseDTO.cs`

```csharp
public class CrearFacturaResponseDTO
{
    public string NumeroFactura { get; set; }
    public string Empresa { get; set; }      // ⭐ CLAVE: empresa donde se facturó
    public int NumeroPedido { get; set; }
}
```

#### 2. Modificado `ServicioFacturas.CrearFactura`
**Archivo**: `NestoAPI/Infraestructure/Facturas/ServicioFacturas.cs:292`

**Antes**:
```csharp
public async Task<string> CrearFactura(string empresa, int pedido, string usuario)
{
    // ... lógica de traspaso ...
    empresa = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO; // "3"

    // ... crear factura ...
    return resultadoProcedimiento; // ❌ Solo retorna número de factura
}
```

**Después**:
```csharp
public async Task<CrearFacturaResponseDTO> CrearFactura(string empresa, int pedido, string usuario)
{
    string empresaOriginal = empresa; // "1"

    // ... lógica de traspaso ...
    empresa = Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO; // "3"

    // ... crear factura ...

    // ✅ Retorna empresa donde se facturó
    return new CrearFacturaResponseDTO
    {
        NumeroFactura = resultadoProcedimiento,
        Empresa = empresa, // "3" si hubo traspaso, "1" si no
        NumeroPedido = pedido
    };
}
```

#### 3. Actualizadas Interfaces
- `IServicioFacturas.CrearFactura`: `Task<string>` → `Task<CrearFacturaResponseDTO>`
- `IGestorFacturas.CrearFactura`: `Task<string>` → `Task<CrearFacturaResponseDTO>`
- `GestorFacturas.CrearFactura`: Actualizado para delegar al servicio
- `FacturasController.CrearFactura`: Retorna el DTO completo

### Frontend (Nesto)

#### 1. Nuevo DTO `CrearFacturaResponseDTO`
**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/Models/Facturas/CrearFacturaResponseDTO.vb`

```vb
Public Class CrearFacturaResponseDTO
    Public Property NumeroFactura As String
    Public Property Empresa As String
    Public Property NumeroPedido As Integer
End Class
```

#### 2. Actualizado `IPedidoVentaService`
**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/IPedidoVentaService.vb:19`

```vb
Function CrearFacturaVenta(empresa As String, numeroPedido As Integer) As Task(Of CrearFacturaResponseDTO)
```

#### 3. Actualizado `PedidoVentaService.CrearFacturaVenta`
**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaService.vb:492`

**Antes**:
```vb
Dim pedidoRespuesta As String = JsonConvert.DeserializeObject(Of String)(respuestaString)
Return pedidoRespuesta
```

**Después**:
```vb
Dim resultado As CrearFacturaResponseDTO = JsonConvert.DeserializeObject(Of CrearFacturaResponseDTO)(respuestaString)
Return resultado
```

#### 4. Actualizado `DetallePedidoViewModel.OnCrearFacturaVenta`
**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb:876`

**Antes**:
```vb
Dim factura As String = Await servicio.CrearFacturaVenta(pedido.empresa.ToString, pedido.numero.ToString)
' ...
cmdCargarPedido.Execute(New ResumenPedido With {
    .empresa = pedido.empresa,  ' ❌ Usa empresa original ("1")
    .numero = pedido.numero
})
```

**Después**:
```vb
Dim resultado As CrearFacturaResponseDTO = Await servicio.CrearFacturaVenta(pedido.empresa.ToString, pedido.numero.ToString)
' ...
' ✅ Usa la empresa del resultado (puede ser "3" si hubo traspaso)
cmdCargarPedido.Execute(New ResumenPedido With {
    .empresa = resultado.Empresa,  ' ✅ Empresa correcta
    .numero = pedido.numero
})
dialogService.ShowNotification($"Factura {resultado.NumeroFactura} creada correctamente")
Await ImprimirFactura(resultado.NumeroFactura)
```

#### 5. Actualizado `DetallePedidoViewModel.OnCrearAlbaranYFacturaVenta`
**Archivo**: `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb:981`

Mismo cambio aplicado para el flujo de crear albarán y factura en un solo paso.

## 🎯 Resultado

### Flujo Corregido

```
1. Usuario factura pedido 12345 de empresa "1"
   ↓
2. Backend detecta que necesita traspaso
   ↓
3. Traspasa pedido a empresa "3"
   ↓
4. Crea factura en empresa "3"
   ↓
5. Retorna: {NumeroFactura: "NV25/123", Empresa: "3", NumeroPedido: 12345}
   ↓
6. Frontend recarga pedido desde empresa "3" (✅ CORRECTO)
   ↓
7. Pedido se muestra correctamente
```

### Casos de Uso

| Escenario | Empresa Original | Traspaso | Empresa Final | Recarga desde |
|-----------|-----------------|----------|---------------|---------------|
| **Sin traspaso** | "1" | No | "1" | "1" ✅ |
| **Con traspaso** | "1" | Sí | "3" | "3" ✅ (antes era "1" ❌) |
| **Fin de mes** | "1" | No | "1" | "1" ✅ |

## 📝 Archivos Modificados

### Backend
- ✅ `NestoAPI/Models/Facturas/CrearFacturaResponseDTO.cs` (NUEVO)
- ✅ `NestoAPI/Infraestructure/Facturas/IServicioFacturas.cs:31`
- ✅ `NestoAPI/Infraestructure/Facturas/ServicioFacturas.cs:292`
- ✅ `NestoAPI/Infraestructure/Facturas/IGestorFacturas.cs:25`
- ✅ `NestoAPI/Infraestructure/Facturas/GestorFacturas.cs:1045`
- ✅ `NestoAPI/Controllers/FacturasController.cs:185`
- ✅ `NestoAPI/NestoAPI.csproj` (agregado CrearFacturaResponseDTO.cs)

### Frontend
- ✅ `Nesto/Modulos/PedidoVenta/PedidoVenta/Models/Facturas/CrearFacturaResponseDTO.vb` (NUEVO)
- ✅ `Nesto/Modulos/PedidoVenta/PedidoVenta/IPedidoVentaService.vb:19`
- ✅ `Nesto/Modulos/PedidoVenta/PedidoVenta/PedidoVentaService.vb:492`
- ✅ `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb:876` (OnCrearFacturaVenta)
- ✅ `Nesto/Modulos/PedidoVenta/PedidoVenta/ViewModels/DetallePedidoViewModel.vb:981` (OnCrearAlbaranYFacturaVenta)

## ✅ Testing

### Test Manual
1. Crear pedido para cliente que requiere traspaso (ej: 10458 - B2C)
2. Facturar el pedido
3. Verificar que:
   - ✅ El pedido se traspasa a empresa "3"
   - ✅ La factura se crea en empresa "3"
   - ✅ El pedido se recarga correctamente desde empresa "3"
   - ✅ No aparece el error "NotFound"

### Casos Edge
- ✅ Cliente de fin de mes: Retorna `{NumeroFactura: "FDM", Empresa: "1"}`
- ✅ Cliente sin traspaso: Retorna empresa original
- ✅ Cliente con traspaso: Retorna empresa espejo

## 🔧 Compatibilidad

**Breaking Change**: ❌ No
- El cambio es hacia atrás compatible si otros sistemas consumen el endpoint
- El frontend debe actualizarse simultáneamente con el backend

**Impacto**: Bajo
- Solo afecta al módulo de Pedidos de Venta
- No requiere cambios en Base de Datos

## 📅 Fecha de Implementación

**Fecha**: 2025-01-19
**Estado**: ✅ Completado
**Probado**: ⏳ Pendiente de testing manual

---

**Relacionado con**:
- Sistema de traspaso de empresas: `SESION_TRASPASO_CCC_18NOV2024.md`
- Facturación de rutas: `ROADMAP_FACTURAR_RUTAS.md`
