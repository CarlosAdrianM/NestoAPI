# Análisis de Descuadres en Facturación (Issues #242/#243)

## Resumen del Problema

Al facturar pedidos (especialmente los creados desde CanalesExternos), el SP `prdCrearFacturaVta` genera un error de descuadre porque el asiento contable no cuadra (debe != haber).

## Vista vstContabilizarFacturaVta

Esta vista es CRÍTICA porque es la fuente de datos para crear el asiento contable:

```sql
CREATE VIEW [dbo].[vstContabilizarFacturaVta]
AS
SELECT
    dbo.CabPedidoVta.Empresa,
    dbo.GruposProducto.CtaVentas,
    SUM(dbo.LinPedidoVta.[Base Imponible]) AS BaseImponible,  -- SUM sin redondear
    dbo.LinPedidoVta.[Nº Factura] AS NumFactura,
    dbo.LinPedidoVta.[Nº Cliente],
    dbo.LinPedidoVta.Delegación,
    dbo.LinPedidoVta.[Forma Venta],
    dbo.CabPedidoVta.IVA AS IVACliente,
    dbo.LinPedidoVta.IVA AS IVAProducto,
    dbo.GruposProducto.CtaAbonosVta,
    SUM(ROUND(dbo.LinPedidoVta.Bruto, 2)) AS ImporteBruto,    -- SUM de ROUND(Bruto,2)
    dbo.ParámetrosIVA.[% IVA],
    dbo.ParámetrosIVA.[% RE],
    dbo.ParámetrosIVA.CtaRepercutido,
    dbo.GruposProducto.CtaDescuentos,
    dbo.LinPedidoVta.DescuentoProducto,
    dbo.CabPedidoVta.Número,
    SUM(dbo.LinPedidoVta.Total) AS Total,                     -- SUM sin redondear
    SUM(ROUND(dbo.LinPedidoVta.Bruto * dbo.LinPedidoVta.DescuentoProducto, 2)) AS ImpDtoProducto,
    dbo.ParámetrosIVA.CtaRecargoRepercutido
FROM dbo.LinPedidoVta
INNER JOIN dbo.CabPedidoVta ON ...
LEFT OUTER JOIN dbo.GruposProducto ON ...
LEFT OUTER JOIN dbo.ParámetrosIVA ON ...
GROUP BY ...
```

### Campos importantes de la vista:
| Campo | Cálculo | Observación |
|-------|---------|-------------|
| `BaseImponible` | `SUM(LinPedidoVta.[Base Imponible])` | SUM directo, sin redondeo adicional |
| `ImporteBruto` | `SUM(ROUND(LinPedidoVta.Bruto, 2))` | Se redondea CADA Bruto antes de sumar |
| `Total` | `SUM(LinPedidoVta.Total)` | SUM directo, sin redondeo adicional |
| `ImpDtoProducto` | `SUM(ROUND(Bruto * DescuentoProducto, 2))` | Se redondea CADA producto antes de sumar |

## SP prdCrearFacturaVta - Flujo de Creación del Asiento

### 1. Apunte de Ventas (cuenta 700) - HABER
```sql
declare crsContabProd cursor for
    select CtaVentas, CtaAbonosVta, sum(round(ImporteBruto,2)) as ImporteBruto, Delegación, [forma venta]
    from vstContabilizarFacturaVta
    where empresa = @Empresa and NumFactura = @NumFactura and ctaventas is not null
    group by ctaventas, ctaabonosvta, delegación, [forma venta]

-- Inserta en HABER: @ImporteBruto (ya viene redondeado de la vista, y se redondea de nuevo al agrupar)
```

### 2. Apuntes de Descuentos (cuenta 665) - DEBE
```sql
-- Calcula descuentos en cadena, línea a línea, redondeando CADA cálculo
select @ImpDtoProducto = sum(round(Bruto*DescuentoProducto,2)) from linpedidovta ...
select @ImpDtoCliente = sum(round(bruto*(1-(1-descuentoProducto)*(1-descuentoCliente)),2)) from linpedidovta ...
select @ImpDtoVta = sum(round(bruto*(1-(1-descuentoProducto)*(1-descuentoCliente)*(1-descuento)),2)) from linpedidovta ...
select @ImpDtoPP = sum(round(bruto*(1-(1-descuentoProducto)*(1-descuentoCliente)*(1-descuento)*(1-descuentopp)),2)) from linpedidovta ...

-- Luego calcula diferencias:
set @ImpDtoPP = @ImpDtoPP - @ImpDtoVta
set @ImpDtoVta = @ImpDtoVta - @ImpDtoCliente
set @ImpDtoCliente = @ImpDtoCliente - @ImpDtoProducto

-- Inserta en DEBE: cada tipo de descuento por separado
```

### 3. Apuntes de IVA (cuenta 477) - HABER
```sql
declare crsIVA cursor for
    select ctaRepercutido, ctaRecargoRepercutido, sum(BaseImponible) as BaseImponible, [% IVA], [% RE]
    from vstContabilizarFacturaVta
    where empresa=@empresa and NumFactura = @NumFactura
    group by ctaRepercutido, [% IVA], [% RE], ctaRecargoRepercutido

-- Por cada tipo de IVA:
set @ImporteIVA = round((@BaseIVA * @PorcIVA) / 100, 2)
set @ImporteRE = round((@BaseIVA * @PorcRE / 100), 2)

-- Acumula el total del cliente:
set @TotalCliente = @TotalCliente + round(@BaseIVA, 2)
set @TotalCliente = @TotalCliente + round(@ImporteIVA, 2)
set @TotalCliente = @TotalCliente + round(@ImporteRE, 2)
```

### 4. Apunte del Cliente (cuenta 430) - DEBE
```sql
-- Inserta en DEBE: @TotalCliente
```

### 5. Verificación de Cuadre
```sql
select @TotalClienteCuadre = sum(round(TotalAgrupado,2)) from (
    select sum(Total) as TotalAgrupado
    from vstContabilizarFacturaVta
    where Empresa = @Empresa and NumFactura = @NumFactura
    group by [% iva], [% re]
) as Resultado

if abs(@TotalCliente - @TotalClienteCuadre) > 0.02 begin
    raiserror('Se ha producido un descuadre. Avise al Dpto. Informática',11,1)
    rollback
    return(-3)
end
```

## Análisis del Cuadre

### @TotalCliente se calcula como:
```
Para cada tipo de IVA:
  + ROUND(SUM(BaseImponible), 2)           -- de la vista
  + ROUND(BaseIVA * %IVA / 100, 2)         -- calculado sobre la suma
  + ROUND(BaseIVA * %RE / 100, 2)          -- calculado sobre la suma
```

### @TotalClienteCuadre se calcula como:
```
SUM(ROUND(
    SUM(Total)  -- Total de las líneas, agrupado por tipo IVA
, 2))
```

### Para que cuadre:
El `Total` de cada línea debe ser coherente con cómo el SP recalcula los valores.

## Campos de LinPedidoVta y cómo se guardan desde C#

### GestorPedidosVenta.CalcularImportesLinea():
```csharp
bruto = Cantidad * Precio;                                    // Sin redondear (restricción CK_LinPedidoVta_5)
sumaDescuentos = 1 - ((1-DtoCliente)*(1-DtoProducto)*(1-Dto)*(1-DtoPP));
importeDescuento = ???                                        // ¿Cómo se calcula?
baseImponible = ???                                           // ¿Cómo se calcula?
importeIVA = baseImponible * porcentajeIVA / 100;            // Sin redondear
importeRE = baseImponible * porcentajeRE;                    // Sin redondear
total = baseImponible + importeIVA + importeRE;              // Sin redondear
```

## Restricciones de BD

### CK_LinPedidoVta_5
```sql
([bruto]=[precio]*[cantidad] OR [tipolinea]<>(1))
```
**IMPORTANTE**: No se puede modificar `Bruto` sin modificar `Precio`. El Bruto DEBE ser exactamente Cantidad * Precio.

## Hipótesis del Problema

El descuadre ocurre porque:

1. **En C#** calculamos `BaseImponible = ROUND(Bruto - ImporteDescuento, 2)`
2. **El SP** recalcula el IVA como `ROUND(SUM(BaseImponible) * %IVA / 100, 2)`
3. **El Total** que guardamos puede no coincidir con: `SUM(BaseImponible) + IVA_recalculado + RE_recalculado`

### Ejemplo de descuadre:
```
Línea 1: BaseImponible = 10.33, IVA 21% → ImporteIVA guardado = 2.1693
Línea 2: BaseImponible = 10.33, IVA 21% → ImporteIVA guardado = 2.1693
Total guardado = 10.33 + 2.1693 + 10.33 + 2.1693 = 24.9986

SP recalcula:
SUM(BaseImponible) = 20.66
IVA = ROUND(20.66 * 0.21, 2) = ROUND(4.3386, 2) = 4.34
Total esperado = 20.66 + 4.34 = 25.00

Diferencia = 25.00 - 24.9986 = 0.0014 (menos de 0.02, OK)
```

Pero con más líneas o porcentajes diferentes, la diferencia puede superar 0.02€.

## Solución Propuesta

### Opción A: Ajustar C# para que calcule igual que el SP
Guardar los campos de forma que al sumarlos y redondearlos como hace el SP, el resultado cuadre.

### Opción B: Ajustar el SP
Si detectamos que C# calcula correctamente pero el SP tiene un bug de redondeo, corregir el SP.

## Pedidos con Descuadre Reportados

| Pedido | Cliente | Total | Error |
|--------|---------|-------|-------|
| 904496 | 39221 | 37,30 € | CK_LinPedidoVta_5 (intentamos modificar Bruto) |
| 904711 | 40604 | 298,81 € | CK_LinPedidoVta_5 |
| 904875 | 2932 | 90,27 € | CK_LinPedidoVta_5 |
| 904880 | 224 | 86,44 € | CK_LinPedidoVta_5 |
| 904887 | 38028 | 138,72 € | CK_LinPedidoVta_5 |

**Nota**: Los errores CK_LinPedidoVta_5 fueron causados por el fix incorrecto que intentaba modificar Bruto.

## Historial de Cambios

### 02/12/2025 - Intento fallido de redondear Bruto
- Se intentó redondear Bruto a 2 decimales en el auto-fix
- Falló por restricción CK_LinPedidoVta_5
- Se revirtió el cambio

### 02/12/2025 - SOLUCIÓN DEFINITIVA: BaseImponible = ROUND(Bruto, 2) - ImporteDto

**Causa raíz identificada**: El auto-fix estaba causando el descuadre del asiento contable.

El SP `prdCrearFacturaVta` construye el asiento usando:
- HABER Ventas (700): `SUM(ROUND(Bruto, 2))`
- DEBE Descuentos (665): `SUM(ROUND(Bruto * Dto, 2))`

Para que cuadre, `SUM(BaseImponible)` debe ser igual a `Ventas - Descuentos`.

**Fórmula INCORRECTA (antes)**:
```
BaseImponible = Bruto - ROUND(Bruto * SumaDescuentos, 2)
```
Con Bruto=67.4325, esto daba BaseImponible=57.3225, pero el SP esperaba 57.32.
La diferencia de 0.0025 por línea se acumulaba y descuadraba el asiento.

**Fórmula CORRECTA (ahora)**:
```
BaseImponible = ROUND(Bruto, 2) - ROUND(Bruto * SumaDescuentos, 2)
```

**Archivos modificados**:
- `NestoAPI/GestorPedidosVenta.cs`: `CalcularImportesLinea()`
- `NestoAPI/ServicioFacturas.cs`: `RecalcularLineasPedido()` (auto-fix SQL)
- `Nesto/LineaPedidoBase.vb`: Propiedades `BaseImponible` e `ImporteDescuento`
- `Nesto/PedidoBase.vb`: Cambiado `Vb6Round` por `AwayFromZeroRound`
- `Nesto/RoundingHelper.vb`: Añadido método `AwayFromZeroRound()`

### Estado Actual del Código
- `Bruto = Cantidad * Precio` (sin redondear, por restricción CK_LinPedidoVta_5)
- `ImporteDto = ROUND(Bruto * SumaDescuentos, 2)` (redondeado)
- `BaseImponible = ROUND(Bruto, 2) - ImporteDto` (usa Bruto redondeado)
- Redondeo: `AwayFromZero` (coherente con SQL Server ROUND)

## Consultas SQL Útiles

### Ver datos de un pedido:
```sql
SELECT
    l.[Nº Orden], l.Producto, l.Cantidad, l.Precio, l.Bruto,
    l.[Base Imponible], l.ImporteDto, l.ImporteIVA, l.ImporteRE, l.Total,
    l.DescuentoCliente, l.DescuentoProducto, l.Descuento, l.DescuentoPP,
    l.SumaDescuentos, l.PorcentajeIVA, l.PorcentajeRE
FROM LinPedidoVta l
WHERE l.Empresa = '1' AND l.Número = 904880
ORDER BY l.[Nº Orden]
```

### Ver cómo lo ve la vista:
```sql
SELECT * FROM vstContabilizarFacturaVta
WHERE Empresa = '1' AND Número = 904880
```

### Simular cálculo del SP:
```sql
-- Simular @TotalCliente
SELECT
    SUM(ROUND(BaseImponible, 2)) as SumaBI,
    ROUND(SUM(BaseImponible) * [% IVA] / 100, 2) as IVA,
    ROUND(SUM(BaseImponible) * [% RE] / 100, 2) as RE
FROM vstContabilizarFacturaVta
WHERE Empresa = '1' AND NumFactura = 'XXXXX'
GROUP BY [% IVA], [% RE]

-- Simular @TotalClienteCuadre
SELECT SUM(ROUND(TotalAgrupado, 2)) FROM (
    SELECT SUM(Total) as TotalAgrupado
    FROM vstContabilizarFacturaVta
    WHERE Empresa = '1' AND NumFactura = 'XXXXX'
    GROUP BY [% IVA], [% RE]
) as Resultado
```
