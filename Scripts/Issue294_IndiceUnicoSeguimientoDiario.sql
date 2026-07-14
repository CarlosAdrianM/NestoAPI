-- Issue #294: rapports (SeguimientoCliente) duplicados el mismo día.
--
-- CONTEXTO (14/07/26): el check anti-duplicados de PostSeguimientoCliente era read-then-insert
-- sin bloqueo y dos POST concurrentes (doble clic con el servidor lento) insertaban duplicados:
-- ~19 grupos/mes en el último año (caso reportado: cliente 9471, NºOrden 1177583/1177584 con
-- Fecha Modificación idéntica al milisegundo; caso extremo: 6 rapports idénticos en el mismo
-- instante). El fix principal es un applock en el POST; este índice es el CINTURÓN definitivo
-- a nivel de BD (para el propio POST y cualquier otra vía de inserción).
--
-- CLAVE: el índice se FILTRA por fecha (solo filas nuevas) para no tener que limpiar los ~55.000
-- duplicados históricos. Exentos, igual que el check del POST:
--   - Estado = 2 (gestión administrativa: varias el mismo día son legítimas)
--   - Número IS NULL (avisos sin cliente)
--   - Usuario IS NULL (por seguridad; el POST ya exige usuario)
--
-- ⚠️ AJUSTAR LA FECHA DEL FILTRO antes de ejecutar: debe ser el día SIGUIENTE a la ejecución
-- (las filas de hoy podrían tener ya un duplicado y romperían la creación del índice).
--
-- ⚠️ ORDEN SEGURO: ejecutar ANTES de desplegar la API con el fix (el índice no molesta al código
-- viejo: los duplicados que pare devolverían el 500 de siempre, que ya era un error).
--
-- BD: NV (NestoConnection). Un índice no necesita GRANTs.

-- Parte 1: columna computada persistida con el día (necesaria para poder indexar por día).
ALTER TABLE dbo.SeguimientoCliente ADD FechaDia AS CONVERT(date, Fecha) PERSISTED;
GO

-- VERIFICACIÓN parte 1 (debe devolver la columna FechaDia, is_persisted = 1):
SELECT name, is_persisted FROM sys.computed_columns WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente');
GO

-- Parte 2: índice único filtrado, SOLO para filas a partir de la fecha indicada.
CREATE UNIQUE NONCLUSTERED INDEX UQ_SeguimientoCliente_UnoPorClienteUsuarioDia
    ON dbo.SeguimientoCliente ([Número], Contacto, Usuario, FechaDia)
    WHERE Estado <> 2
      AND [Número] IS NOT NULL
      AND Usuario IS NOT NULL
      AND FechaDia >= '20260716';  -- ⚠️ AJUSTAR: día siguiente a la ejecución
GO

-- VERIFICACIÓN parte 2 (debe devolver 1 fila con has_filter = 1):
SELECT name, is_unique, has_filter, filter_definition
FROM sys.indexes
WHERE object_id = OBJECT_ID('dbo.SeguimientoCliente') AND name = 'UQ_SeguimientoCliente_UnoPorClienteUsuarioDia';
GO
