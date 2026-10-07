/*
    NestoAPI#603 (hotfix 07/10/26): sugerencias de contacto duplicadas.

    Nesto abre Rapports con dos llamadas casi a la vez a GET api/Clientes/SugerenciasContacto. La primera del día de un
    vendedor registra su lista y, sin bloqueo, las dos la vieron vacía y la insertaron entera (CAM, 07/10 17:16:28:
    28 filas, 14 clientes dos veces con el mismo Orden). La API ya serializa el registro (sp_getapplock por vendedor y
    día) y lee deduplicando; este script limpia lo que quedó y pone el índice único como red de seguridad.

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no tiene permiso). Idempotente.
    Se puede lanzar en horario: la tabla es pequeña y solo la usa la API.

    1. Borra los duplicados: de cada grupo (Vendedor, día, Cliente, Contacto) deja la fila de menor Id. Si alguna de
       las borradas estaba Atendida, la que queda hereda Atendida, RapportId y FechaAtendida.
    2. Añade la columna calculada persistida Dia = CAST(Fecha AS date) (deterministica).
    3. Crea el índice único UX_SugerenciasContacto_Dia (Vendedor, Dia, Cliente, Contacto).

    SOBRE LA PRECAUCIÓN DE #294/#542 (columnas calculadas indexadas): el DML sobre una tabla con una columna calculada
    indexada exige QUOTED_IDENTIFIER ON y ANSI_NULLS ON. Con los SPs y triggers legacy (creados con QUOTED_IDENTIFIER
    OFF) eso rompió SeguimientoCliente y CabPedidoVta. Aquí no aplica: ningún módulo de la BD menciona
    SugerenciasContacto (comprobado en sys.sql_modules el 07/10/26; el script lo vuelve a comprobar y se para si
    aparece alguno), la tabla no tiene triggers y solo escribe en ella la API (SqlClient: opciones ANSI correctas).
*/

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 0. Red de seguridad: si algún SP/trigger/vista escribe o lee la tabla, parar (ver precaución arriba)
------------------------------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM sys.sql_modules WHERE definition LIKE '%SugerenciasContacto%')
   OR EXISTS (SELECT 1 FROM sys.triggers WHERE parent_id = OBJECT_ID('dbo.SugerenciasContacto'))
BEGIN
    SELECT OBJECT_NAME(object_id) AS Modulo, uses_quoted_identifier, uses_ansi_nulls
    FROM sys.sql_modules WHERE definition LIKE '%SugerenciasContacto%';
    RAISERROR('Hay módulos que referencian SugerenciasContacto: revisar QUOTED_IDENTIFIER antes de indexar la columna calculada. No se ha tocado nada.', 16, 1);
    SET NOEXEC ON;
END
GO

------------------------------------------------------------------------------------------------
-- 1. Borrar duplicados (conservando Atendida en la fila que queda)
------------------------------------------------------------------------------------------------
BEGIN TRANSACTION;

;WITH Grupos AS (
    SELECT Id, Atendida, RapportId, FechaAtendida,
           ROW_NUMBER() OVER (PARTITION BY Vendedor, CAST(Fecha AS date), Cliente, Contacto ORDER BY Id) AS N,
           MIN(Id) OVER (PARTITION BY Vendedor, CAST(Fecha AS date), Cliente, Contacto) AS IdQueQueda
    FROM dbo.SugerenciasContacto
),
AtendidaBorrada AS (
    SELECT IdQueQueda, RapportId, FechaAtendida,
           ROW_NUMBER() OVER (PARTITION BY IdQueQueda ORDER BY FechaAtendida, Id) AS N
    FROM Grupos
    WHERE N > 1 AND Atendida = 1
)
UPDATE s
SET s.Atendida = 1, s.RapportId = a.RapportId, s.FechaAtendida = a.FechaAtendida
FROM dbo.SugerenciasContacto s
JOIN AtendidaBorrada a ON a.IdQueQueda = s.Id AND a.N = 1
WHERE s.Atendida = 0;

PRINT CONCAT('Filas que heredan Atendida de un duplicado: ', @@ROWCOUNT);

;WITH Grupos AS (
    SELECT Id, ROW_NUMBER() OVER (PARTITION BY Vendedor, CAST(Fecha AS date), Cliente, Contacto ORDER BY Id) AS N
    FROM dbo.SugerenciasContacto
)
DELETE FROM Grupos WHERE N > 1;

PRINT CONCAT('Duplicados borrados: ', @@ROWCOUNT);

COMMIT TRANSACTION;
GO

------------------------------------------------------------------------------------------------
-- 2. Columna Dia (calculada, persistida)
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.SugerenciasContacto', 'Dia') IS NULL
BEGIN
    ALTER TABLE dbo.SugerenciasContacto ADD Dia AS CAST(Fecha AS date) PERSISTED;
    PRINT 'Columna Dia añadida.';
END
GO

------------------------------------------------------------------------------------------------
-- 3. Índice único por vendedor, día y cliente/contacto
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SugerenciasContacto_Dia' AND object_id = OBJECT_ID('dbo.SugerenciasContacto'))
BEGIN
    CREATE UNIQUE INDEX UX_SugerenciasContacto_Dia ON dbo.SugerenciasContacto (Vendedor, Dia, Cliente, Contacto);
    PRINT 'Índice UX_SugerenciasContacto_Dia creado.';
END
GO

SET NOEXEC OFF;
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 0 duplicados, el índice existe y cuántas filas quedan
------------------------------------------------------------------------------------------------
SELECT COUNT(*) AS GruposDuplicados
FROM (SELECT 1 AS x FROM dbo.SugerenciasContacto
      GROUP BY Vendedor, CAST(Fecha AS date), Cliente, Contacto HAVING COUNT(*) > 1) d;
SELECT name, is_unique FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SugerenciasContacto');
SELECT Vendedor, CAST(Fecha AS date) AS Dia, COUNT(*) AS Filas FROM dbo.SugerenciasContacto GROUP BY Vendedor, CAST(Fecha AS date) ORDER BY Dia, Vendedor;
GO
