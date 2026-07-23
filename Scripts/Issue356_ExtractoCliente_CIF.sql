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

-- NOTA: NO se hace RTRIM del padding heredado (a diferencia de Clientes). Aqui saldria
-- MUY caro y no aporta nada:
--   * Tras el ALTER, los ~2,7M valores conservan sus espacios finales; un UPDATE los tocaria
--     TODOS y ademas [CIF/NIF] esta INCLUIDO en 6 indices (~4,2 GB), asi que cada fila
--     actualizaria esos 6 indices + el clustered -> decenas de GB de log por puro cosmetico.
--   * Los espacios finales son inocuos: TODO el codigo hace .Trim() al leer el CIF, y el char(9)
--     ya venia con ese padding igualmente. No cambia nada funcional.
-- Si algun dia se quiere limpiar de verdad, hacerlo en una ventana aparte y por lotes pequenos.
PRINT 'ExtractoCliente.[CIF/NIF] ampliado (sin RTRIM: el padding es inocuo y el codigo hace Trim).';
GO
