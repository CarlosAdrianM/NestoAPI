# Sesion 26 Nov 2025 - Correccion Impresion AgenciasViewModel

## Problema Original

AgenciasViewModel no imprimia facturas de forma consistente, mientras que Facturar Rutas funcionaba correctamente.

## Causas Identificadas

### 1. Bug en ExtractoRuta (API)
- `InsertarDesdeFactura` buscaba el registro incorrecto en ExtractoCliente
- Buscaba TipoApunte=1 (factura) que tiene Efecto=NULL e ImportePdte=0
- Debia buscar TipoApunte=2 (efecto) con Efecto="1" que tiene los valores correctos

### 2. Bug en HayDocumentosParaImprimir (API)
- Retornaba `true` si `DatosImpresion != null` aunque `NumeroCopias = 0`
- Corregido para verificar `NumeroCopias > 0`

### 3. ShowNotification bloqueante (Cliente WPF)
- `ShowDialog` de Prism es BLOQUEANTE
- Se mostraba la notificacion "Pedido X facturado" ANTES de imprimir
- El usuario tenia que cerrar el dialogo para que se ejecutara la impresion
- **Solucion**: Mover la notificacion al FINAL, despues de imprimir

### 4. Faltaba auto-marcado del checkbox (Cliente WPF)
- El checkbox "Imprimir documento" no se marcaba automaticamente
- Ahora se llama al API para verificar si los comentarios contienen palabras clave

## Cambios Realizados

### NestoAPI (Backend)

#### ServicioExtractoRuta.cs
```csharp
// Antes: buscaba TipoApunte="1" (factura)
// Ahora: busca TipoApunte="2" (efecto) con Efecto="1"
var extractoEfecto = await db.ExtractosCliente
    .Where(e => e.TipoApunte == Constantes.Clientes.TiposExtracto.TIPO_CARTERA &&
                e.Efecto == "1")
    .FirstOrDefaultAsync();
```

#### GestorFacturacionRutas.cs
- Anadida opcion "factura en papel" a `DebeImprimirDocumento()`

#### DocumentosImpresionPedidoDTO.cs
```csharp
// Antes: solo verificaba DatosImpresion != null
// Ahora: verifica NumeroCopias > 0
public bool HayDocumentosParaImprimir =>
    (Facturas?.Any(f => f.DatosImpresion != null && f.DatosImpresion.NumeroCopias > 0) ?? false) || ...
```

#### PedidosVentaController.cs
- Nuevo endpoint: `GET /api/PedidosVenta/DebeImprimirDocumento?comentarios=...`

### Nesto (Cliente WPF)

#### IPedidoVentaService.vb
```vb
Function DebeImprimirDocumento(comentarios As String) As Task(Of Boolean)
```

#### PedidoVentaService.vb
- Implementacion que llama al endpoint del API

#### AgenciasViewModel.vb

1. **Auto-marcado del checkbox** en `ActualizarPedidoSeleccionado()`:
```vb
ImprimirDocumentoAlFacturar = Await _servicioPedidos.DebeImprimirDocumento(pedidoSeleccionado.Comentarios)
```

2. **Mover notificacion al final** en `ImprimirEtiquetaPedido()`:
```vb
' ANTES: ShowNotification ANTES de imprimir (bloqueaba)
' AHORA: ShowNotification DESPUES de imprimir
```

## Tests Anadidos

### GestorFacturacionRutasTests.cs
- **Grupo 7**: Tests para "factura en papel"
- **Grupo 8**: Tests para HayDocumentosParaImprimir con NumeroCopias > 0

## Flujo Corregido

1. Usuario selecciona pedido -> checkbox se auto-marca si comentarios contienen palabras clave
2. Usuario pulsa "Imprimir etiqueta" con checkbox de facturar marcado
3. Se imprime etiqueta de agencia
4. Se crea albaran y factura
5. Si checkbox "Imprimir documento" marcado:
   - Se obtienen documentos via `ObtenerDocumentosImpresion` (misma logica que Facturar Rutas)
   - Se imprimen documentos
   - Se muestra notificacion de exito (DESPUES de imprimir, no antes)
6. Si checkbox no marcado:
   - Se muestra notificacion de exito

## Palabras Clave para Impresion

- "factura fisica"
- "factura en papel"
- "albaran fisico"

(Case insensitive, con o sin tildes)
