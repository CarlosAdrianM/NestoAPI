-- ===========================================================================
-- Script: Issue356_ExtractoCliente_CIF.sql
-- Fecha:  23/07/2026
-- Issue:  NestoAPI#356 (parte pesada, separada del script de Clientes)
--
-- Amplia ExtractoCliente.[CIF/NIF] de char(9) a varchar(20). [CIF/NIF] es aqui
-- una copia denormalizada del NIF del cliente en cada movimiento del extracto
-- (la usa, p. ej., prdInformeIRPFLineas). Verifactu NO lee de aqui (lee de
-- CabFacturaVta.CifNif, ya varchar(20)), asi que esto NO bloquea la facturacion
-- electronica: puede ejecutarse mas tarde que el script de Clientes.
--
-- ATENCION - OPERACION PESADA (VENTANA DE MANTENIMIENTO):
--   * ExtractoCliente tiene ~2,7M filas: el cambio char->varchar reescribe cada
--     fila (size-of-data) y ademas [CIF/NIF] es columna INCLUDED en 62 indices
--     _dta_, que se actualizan con el cambio. Puede tardar y crece el log.
--   * [CIF/NIF] NO es clave de ningun indice aqui, asi que NO hay que soltar
--     indices (a diferencia de Clientes).
--   * Ejecutar en horario de baja carga. Vigilar el log de transacciones.
-- ===========================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;

ALTER TABLE dbo.ExtractoCliente ALTER COLUMN [CIF/NIF] varchar(20) NULL;
PRINT 'ExtractoCliente.[CIF/NIF] ampliado a varchar(20).';

-- Quitar el padding heredado del char(9). Por lotes para no inflar el log de golpe.
DECLARE @filas INT = 1;
WHILE @filas > 0
BEGIN
    UPDATE TOP (50000) dbo.ExtractoCliente
    SET [CIF/NIF] = RTRIM([CIF/NIF])
    WHERE [CIF/NIF] IS NOT NULL AND [CIF/NIF] <> RTRIM([CIF/NIF]);
    SET @filas = @@ROWCOUNT;
END
PRINT 'ExtractoCliente.[CIF/NIF] sin padding.';
GO
