-- OneShot 12/11/26 (incidencia 1227 y comentario 1037 del hilo 1057, Alfredo): en Algete no sale el picking de rutas («Se esta
-- realizando el inventario del producto1») y no se puede contabilizar el inventario de hoy.
-- Causa: Inventarios.NºOrden 948803 (ALG, 21075 CARTUCHO VINOTHERAPYA, fecha 11/11/26, SIN ubicación, StockCalculado 0, StockReal 0,
-- Estado 1, NUEVAVISION\Alfredo, 15:19 de ayer). Mientras siga pendiente, el disparador trdUpdUbicaciones tumba cualquier picking que
-- toque el 21075 (el pedido 930320 lo lleva) y, al ser de AYER, se queda sin salida:
--   · contabilizar hoy → bloqueante «filas con otra fecha»;
--   · contabilizar con fecha de ayer → bloqueantes «sin ubicación» y «otra fecha» (las 6 filas de hoy);
--   · borrarla desde Nesto → «solo se pueden borrar filas del inventario de hoy».
-- La fila es 0/0 y sin ubicación: contabilizarla no movería nada. Se borra. Las 6 filas de hoy (948804-948809) NO se tocan: las
-- contabiliza Alfredo desde Nesto después.
-- Ejecutar como sa en NV.
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRAN;
SELECT 'ANTES' Momento, [NºOrden], [Almacén], Fecha, [Número], StockCalculado, StockReal, Estado, Pasillo, Fila, Columna, Usuario
FROM Inventarios WHERE [Almacén] = 'ALG' AND Estado = 1 AND [NºTraspaso] IS NULL;

DELETE FROM Inventarios
WHERE [NºOrden] = 948803 AND [Almacén] = 'ALG' AND [Número] = '21075' AND CAST(Fecha AS date) = '20261111'
  AND Estado = 1 AND [NºTraspaso] IS NULL AND Pasillo IS NULL AND StockCalculado = 0 AND StockReal = 0;
IF @@ROWCOUNT <> 1 BEGIN RAISERROR('La fila 948803 no está como se esperaba (pendiente, de ayer, sin ubicación, 0/0). No se ha borrado nada.', 16, 1); ROLLBACK; RETURN; END

SELECT 'DESPUES' Momento, [NºOrden], [Almacén], Fecha, [Número], StockCalculado, StockReal, Estado, Pasillo, Fila, Columna, Usuario
FROM Inventarios WHERE [Almacén] = 'ALG' AND Estado = 1 AND [NºTraspaso] IS NULL;
COMMIT;
GO
