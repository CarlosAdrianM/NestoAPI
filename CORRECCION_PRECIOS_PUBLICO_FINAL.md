# Corrección de Precios para Cliente PUBLICO_FINAL (10458)

**Fecha:** 17 de noviembre de 2025
**Desarrollador:** Claude Code
**Ticket/Issue:** Diferencia de precios entre PlantillaVenta y DetallePedidoVenta

## Problema Identificado

Los módulos **PlantillaVenta** y **DetallePedidoVenta** mostraban precios diferentes para el cliente especial **"10458" (PUBLICO_FINAL)**. Este cliente representa ventas al público final (B2C) y debe usar precios de la tienda online de PrestaShop en lugar del sistema de precios profesionales (B2B).

### Causa Raíz

El endpoint `ProductosController.GetProducto` no implementaba la lógica especial para el cliente `PUBLICO_FINAL`, mientras que `PlantillaVentasController.GetCargarPrecio` sí la tenía. Esto causaba que:

- **PlantillaVenta:** Consultaba PrestaShop y mostraba precios públicos correctos ✅
- **DetallePedidoVenta:** Usaba el sistema B2B con descuentos profesionales ❌

## Solución Implementada

Se agregó la lógica de detección de `PUBLICO_FINAL` en el método `ProductosController.GetProducto` para unificar el comportamiento.

### Archivo Modificado

**`NestoAPI\Controllers\ProductosController.cs`** - Líneas 256-268

### Cambio Realizado

```csharp
// Antes (solo usaba GestorPrecios para todos los clientes)
GestorPrecios.calcularDescuentoProducto(precio);
productoDTO.precio = precio.precioCalculado;

// Después (detecta PUBLICO_FINAL y usa PrestaShop)
if (cliente == Constantes.ClientesEspeciales.PUBLICO_FINAL)
{
    var porcentajeIVA = 1.21M;
    if (producto.IVA_Repercutido == Constantes.Empresas.IVA_REDUCIDO)
    {
        porcentajeIVA = 1.1m;
    }
    precio.precioCalculado = await ProductoDTO.LeerPrecioPublicoFinal(id) / porcentajeIVA;
}
else
{
    GestorPrecios.calcularDescuentoProducto(precio);
}

productoDTO.precio = precio.precioCalculado;
```

## Lógica de Negocio

### Cliente PUBLICO_FINAL (10458)

1. Se detecta que el cliente es `Constantes.ClientesEspeciales.PUBLICO_FINAL`
2. Se llama a `ProductoDTO.LeerPrecioPublicoFinal()` que:
   - Consulta la API de PrestaShop
   - Obtiene el `final_price` con IVA incluido
   - URL: `http://www.productosdeesteticaypeluqueriaprofesional.com/api/products`
3. Se divide el precio por el porcentaje de IVA correspondiente:
   - IVA estándar: 21% (1.21)
   - IVA reducido: 10% (1.1)
4. Se devuelve la base imponible sin IVA

### Otros Clientes

- Usan el sistema normal de precios/descuentos B2B
- Aplican `GestorPrecios.calcularDescuentoProducto()`
- Consideran descuentos por cliente, familia, ofertas, etc.

## Clientes Especiales Definidos

Según `Models\Constantes.cs:276-282`:

```csharp
public static class ClientesEspeciales
{
    public const string EL_EDEN = "15191";      // Bypass validaciones
    public const string TIENDA_ONLINE = "31517"; // Tienda online
    public const string AMAZON = "32624";        // Amazon marketplace
    public const string PUBLICO_FINAL = "10458"; // B2C - PrestaShop
}
```

## Tests Implementados

Se crearon tests unitarios en `NestoAPI.Tests\Controllers\ProductosControllerTest.cs`:

1. **`GetProducto_ConClientePublicoFinal_UsaPrecioPrestaShop`**
   - Verifica que se consulte PrestaShop para PUBLICO_FINAL
   - Valida el cálculo correcto dividiendo por IVA

2. **`GetProducto_ConClienteNormal_UsaGestorPrecios`**
   - Verifica que clientes normales usen el sistema B2B
   - Valida que se aplique GestorPrecios

## Impacto

### Módulos Afectados
- ✅ `ProductosController.GetProducto` - Corregido
- ✅ `PlantillaVentasController.GetCargarPrecio` - Ya funcionaba correctamente

### Beneficios
- **Consistencia:** Ambos módulos ahora muestran precios idénticos
- **Corrección:** El cliente PUBLICO_FINAL obtiene precios B2C correctos
- **Mantenibilidad:** Lógica unificada en ambos endpoints

### Riesgos
- **Bajo:** El cambio solo afecta al cliente "10458"
- **Dependencia externa:** PrestaShop API debe estar disponible
- **Fallback:** Si PrestaShop falla, devuelve precio 0 (comportamiento existente)

## Validación

### Tests Unitarios
```bash
dotnet test NestoAPI.Tests/NestoAPI.Tests.csproj --filter "FullyQualifiedName~ProductosController"
```

### Pruebas Manuales Recomendadas

1. **Cliente PUBLICO_FINAL (10458):**
   - Abrir PlantillaVenta con cliente 10458
   - Abrir DetallePedidoVenta con cliente 10458
   - Verificar que los precios son idénticos
   - Confirmar que coinciden con PrestaShop

2. **Cliente Normal:**
   - Abrir PlantillaVenta con cualquier otro cliente
   - Abrir DetallePedidoVenta con el mismo cliente
   - Verificar que los precios son idénticos
   - Confirmar que se aplican descuentos B2B

3. **Cliente EL_EDEN (15191):**
   - Verificar que sigue aplicando descuentos especiales
   - Confirmar que bypassa validaciones

## Referencias

- **Constantes:** `NestoAPI\Models\Constantes.cs:281`
- **GestorPrecios:** `NestoAPI\Infraestructure\GestorPrecios.cs`
- **ProductoDTO:** `NestoAPI\Models\ProductoDTO.cs:114` (LeerPrecioPublicoFinal)
- **PlantillaVentasController:** `NestoAPI\Controllers\PlantillaVentasController.cs:263-275`

## Notas Adicionales

- Este cambio es parte de la unificación de la lógica de precios entre módulos
- Se recomienda considerar la creación de un servicio centralizado de precios en el futuro
- La lógica de PrestaShop está también implementada en `PlantillaVentasController` (duplicada)

## Estado

✅ **Implementado y Validado**
- Código modificado
- Tests creados y ejecutados exitosamente
- Documentación completa
