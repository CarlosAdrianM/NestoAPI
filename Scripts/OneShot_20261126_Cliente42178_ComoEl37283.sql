/*
    Carlos 26/11/26: el cliente 42178 (CUSIDÓ FOLCH ALVAR) es el cambio de forma fiscal del 37283 (FOLCH GIRBAU MARIA DEL CARMEN,
    distribuidor de Sabadell) y se dio de alta sin copiarle las condiciones. El estado (67) y el vendedor (CAM) ya se le han puesto
    por la API. Aquí va lo que la API no sabe hacer:
      1) IVA de la ficha: G21 -> E52 (recargo de equivalencia), como el anterior.
      2) Sus 8 descuentos pactados (5 por familia al 50 % y 3 de producto), copiados tal cual.
    No toca pedidos. Lanzar como sa en la BD NV. Se puede lanzar más de una vez (no duplica ni pisa).
*/
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRANSACTION;

-- 1) IVA como el anterior (solo si sigue con el que nació)
UPDATE n SET n.IVA = v.IVA
FROM Clientes n
INNER JOIN Clientes v ON v.Empresa = n.Empresa AND v.[Nº Cliente] = '37283' AND v.Contacto = '0'
WHERE n.Empresa = '1' AND n.[Nº Cliente] = '42178' AND ISNULL(RTRIM(n.IVA), '') IN ('', 'G21') AND RTRIM(v.IVA) = 'E52';
PRINT 'Fichas con el IVA cambiado: ' + CAST(@@ROWCOUNT AS varchar(10));

-- 2) Descuentos pactados: las filas del 37283 que el 42178 todavía no tiene iguales
INSERT INTO DescuentosProducto (Empresa, [Nº Producto], GrupoProducto, [Nº Cliente], Contacto, NºProveedor, ContactoProveedor, CantidadMínima,
       Descuento, Precio, Familia, FiltroProducto, Usuario, [Fecha Modificación], AudienciaOferta, DescuentoPublico, FechaDesde, FechaHasta,
       Campana, SubGrupoProducto)
SELECT d.Empresa, d.[Nº Producto], d.GrupoProducto, '42178', d.Contacto, d.NºProveedor, d.ContactoProveedor, d.CantidadMínima,
       d.Descuento, d.Precio, d.Familia, d.FiltroProducto, 'NUEVAVISION\Carlos', GETDATE(), d.AudienciaOferta, d.DescuentoPublico, d.FechaDesde, d.FechaHasta,
       d.Campana, d.SubGrupoProducto
FROM DescuentosProducto d
WHERE d.Empresa = '1' AND d.[Nº Cliente] = '37283'
  AND NOT EXISTS (SELECT 1 FROM DescuentosProducto x
                  WHERE x.Empresa = d.Empresa AND x.[Nº Cliente] = '42178'
                    AND ISNULL(x.[Nº Producto], '') = ISNULL(d.[Nº Producto], '') AND ISNULL(x.Familia, '') = ISNULL(d.Familia, '')
                    AND ISNULL(x.GrupoProducto, '') = ISNULL(d.GrupoProducto, '') AND ISNULL(x.SubGrupoProducto, '') = ISNULL(d.SubGrupoProducto, '')
                    AND ISNULL(x.Contacto, '') = ISNULL(d.Contacto, '') AND x.CantidadMínima = d.CantidadMínima);
PRINT 'Descuentos copiados: ' + CAST(@@ROWCOUNT AS varchar(10));

COMMIT TRANSACTION;
GO
-- Comprobación: las dos fichas y sus descuentos, lado a lado
SELECT RTRIM([Nº Cliente]) AS Cliente, RTRIM(Contacto) AS Contacto, RTRIM(Nombre) AS Nombre, RTRIM(Vendedor) AS Vendedor, Estado, RTRIM(IVA) AS IVA
FROM Clientes WHERE Empresa = '1' AND [Nº Cliente] IN ('37283', '42178') ORDER BY 1, 2;
SELECT RTRIM([Nº Cliente]) AS Cliente, RTRIM([Nº Producto]) AS Producto, RTRIM(Familia) AS Familia, Descuento, Precio, CantidadMínima
FROM DescuentosProducto WHERE Empresa = '1' AND [Nº Cliente] IN ('37283', '42178') ORDER BY 3, 2, 1;
GO
