-- Issue #322: la consulta de trgContabilidadDlt (MAX(Fecha) del asiento de cierre) se ejecuta
-- en CADA insert/update/delete de Contabilidad para cualquier usuario distinto de NUEVAVISION\Carlos
-- (el trigger tiene un bypass hardcodeado para ese login). Sin índice que la cubra cuesta ~50-80 ms
-- por disparo (key lookups sobre las ~8.300 filas de _ASIENTCIE), y una contabilización de ~640
-- líneas (pago grande de Amazon en _PagoReemb) supera los 360 s de timeout de forma determinista
-- para cualquier usuario que no sea Carlos. Incidente real: 20/07/26 09:25 (usuario Magan).
--
-- Con este índice la consulta pasa a ser un seek instantáneo y la contabilización deja de
-- depender de con qué usuario se lance.
--
-- IMPORTANTE: índice NORMAL, no filtrado. Un índice filtrado en Contabilidad rompería los
-- INSERT de cualquier sesión con QUOTED_IDENTIFIER OFF (lección de la Issue #294).
--
-- Contabilidad tiene 6,3M de filas: ejecutar fuera de horario de oficina (tarda unos segundos
-- pero bloquea escrituras mientras se crea; edición Standard, sin ONLINE=ON).

USE NV;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.Contabilidad') AND name = 'IX_Contabilidad_CierreEjercicio'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_Contabilidad_CierreEjercicio
        ON dbo.Contabilidad (Empresa, Diario, Concepto, Fecha);
END
GO

-- ---------------------------------------------------------------------------------------------
-- Issue #321 (documentación, YA EJECUTADO en prod el 20/07/26 por Carlos):
-- el diagnóstico de bloqueos (#312) necesita VIEW SERVER STATE para que sys.dm_exec_sessions
-- devuelva las sesiones de otros usuarios; sin él, devuelve solo la propia y el diagnóstico
-- salía vacío en silencio. Es permiso de SERVIDOR (no de BD), para el login del app pool del API.
--
-- GRANT VIEW SERVER STATE TO [NUEVAVISION\RDS2016$];
-- ---------------------------------------------------------------------------------------------
