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
-- COSTE MEDIDO EN PRODUCCIÓN (20/07/26), porque el "tarda unos segundos" que decía antes este
-- comentario era falso y puede llevar a ejecutarlo en mal momento:
--   * Contabilidad: 6.292.752 filas, 1,2 GB (78% ya en buffer pool).
--   * Clave del índice = char(3)+char(10)+char(50)+datetime = 71 bytes ⇒ índice resultante ~540 MB.
--   * La ordenación equivalente, medida con ROW_NUMBER sobre esas 4 columnas: **60 segundos**.
--   * Servidor: 2 CPUs y 8 GB de RAM ⇒ poca paralelización y grant de memoria justo (el sort
--     probablemente vuelca a tempdb). Recovery model FULL, pero el log tiene 33 GB con 1,8 GB
--     usados, así que NO habrá autogrowth durante la creación.
--   * Estimación total: **2-4 minutos**; planificar una ventana de 5.
--
-- QUÉ BLOQUEA: es un CREATE INDEX offline (edición Standard, sin ONLINE=ON), así que toma un
-- lock compartido sobre la tabla: las LECTURAS siguen funcionando y las ESCRITURAS en Contabilidad
-- (o sea, cualquier contabilización) se quedan esperando durante esos 2-4 minutos.
-- Ejecutar fuera de horario y con nadie contabilizando.

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
