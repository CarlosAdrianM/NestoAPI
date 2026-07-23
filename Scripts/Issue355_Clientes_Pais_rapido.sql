-- ===========================================================================
-- Script: Issue355_Clientes_Pais_rapido.sql
-- Fecha:  23/07/2026
-- Issue:  NestoAPI#355 (columna Pais en Clientes) - PARTE MINIMA PARA DESPLEGAR HOY
--
-- Anade SOLO la columna Clientes.Pais. Es una operacion de METADATOS (instantanea,
-- sin reconstruir nada), asi que NO bloquea la tabla como el ensanchado del CIF y se
-- puede ejecutar en horario de trabajo. Es lo UNICO que el codigo nuevo exige para
-- arrancar (su EF hace SELECT de Pais).
--
-- LOCK_TIMEOUT: si no consigue el lock de esquema en 5 s (tabla ocupada), falla y hace
-- rollback en vez de quedarse colgado bloqueando a la gente. Reintentar en cualquier hueco.
--
-- NO se rellena 'ES' en el historico a proposito: dejar el default hace el ADD instantaneo,
-- y un Pais NULL se comporta EXACTAMENTE como ES en el codigo nuevo (no es UE distinta de ES).
-- El DEFAULT 'ES' cubre los INSERT nuevos. El backfill del historico se hace luego, junto con
-- el ensanchado del CIF (Issue356_355_Clientes_CIF_Pais.sql, en ventana tranquila).
--
-- PENDIENTE para off-hours (NO bloquea el deploy, solo afecta a clientes EXTRANJEROS con
-- NIF de mas de 9 caracteres):
--   * Issue356_355_Clientes_CIF_Pais.sql -> ensanchar Clientes.[CIF/NIF] a varchar(20)
--     (dropea/recrea 13 indices + borra 13 estadisticas; es idempotente, saltara el ADD de
--      Pais porque ya existira). EJECUTAR CON LA TABLA TRANQUILA (bloquea Clientes al reconstruir).
--   * Los 3 SPs con @cif char(9) (ver cabecera de ese script).
--   * Issue356_ExtractoCliente_CIF.sql (opcional, muy pesado).
-- ===========================================================================

SET XACT_ABORT ON;
SET NOCOUNT ON;
SET LOCK_TIMEOUT 5000;   -- si la tabla esta ocupada, falla rapido en vez de colgarse

IF COL_LENGTH('dbo.Clientes', 'Pais') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD Pais varchar(2) NULL
        CONSTRAINT DF_Clientes_Pais DEFAULT 'ES';
    PRINT 'OK: columna Clientes.Pais anadida (metadatos, DEFAULT ES). Ya se puede desplegar.';
END
ELSE
    PRINT 'La columna Clientes.Pais ya existe: nada que hacer.';
GO
