-- Issue #282: alta de la oferta Lisap real (caduca 31/07/2026). Ejecutar en NV con login admin.
--
-- Semántica (reencuadre acordado en la issue): el pedido debe CONTENER 72 tintes LK OPC
-- (36 de pago + 36 de regalo) + 6 aguas oxigenadas (3 + 3) + los 2 regalos, y el importe
-- total de esas líneas no puede bajar del precio de lo que se paga:
--   ImporteMinimo = 36 x 11,65 (tinte a tarifa) + 3 x 11,65 (agua a tarifa) = 454,35 €
-- Da igual cómo se repartan precios/descuentos entre las unidades mientras se llegue a ese suelo.
--
-- Verificado en prod el 13/07/26: tarifa (PVP) de los tintes LK OPC activos = 11,65;
-- tarifa de las aguas AGUA OXIGENADA DEVELOPER activas = 11,65; regalos 45473 (9,95)
-- y 45472 (130,00) existen y están activos.

SET NOCOUNT ON;
BEGIN TRAN;

DECLARE @Usuario nvarchar(30) = ORIGINAL_LOGIN();
DECLARE @OfertaId int;

INSERT INTO OfertasCombinadas (Empresa, Nombre, ImporteMinimo, FechaDesde, FechaHasta, Usuario, FechaModificacion)
VALUES ('1', N'Oferta Lisap: 72 tintes LK OPC + 6 aguas oxigenadas (36+36 / 3+3) con regalo limpiador cepillos',
        454.35, '20260713', '20260731', @Usuario, GETDATE());

SET @OfertaId = SCOPE_IDENTITY();

-- Filas de FILTRO (Producto NULL): la cantidad se cuenta agregada sobre todas las líneas que casan.
INSERT INTO OfertasCombinadasDetalle (Empresa, OfertaId, Producto, Familia, FiltroProducto, Cantidad, Precio, GrupoAlternativa, PermitirCantidadMenor, Usuario, FechaModificacion)
VALUES
    ('1', @OfertaId, NULL, 'Lisap', N'LK OPC', 72, 0, NULL, 0, @Usuario, GETDATE()),
    ('1', @OfertaId, NULL, 'Lisap', N'AGUA OXIGENADA DEVELOPER', 6, 0, NULL, 0, @Usuario, GETDATE()),
-- Regalos (filas de producto concreto, precio 0):
    ('1', @OfertaId, '45473', NULL, NULL, 1, 0, NULL, 0, @Usuario, GETDATE()),
    ('1', @OfertaId, '45472', NULL, NULL, 1, 0, NULL, 0, @Usuario, GETDATE());

COMMIT;

-- Verificación
SELECT o.Id, o.Nombre, o.ImporteMinimo, o.FechaDesde, o.FechaHasta,
       d.Producto, d.Familia, d.FiltroProducto, d.Cantidad, d.Precio
FROM OfertasCombinadas o
    INNER JOIN OfertasCombinadasDetalle d ON d.OfertaId = o.Id
WHERE o.Id = @OfertaId;
