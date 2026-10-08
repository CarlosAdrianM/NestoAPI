/*
    NestoAPI#577 (corte 3a): LinPedidoVta.FechaCreacion, cuándo se creó cada línea de pedido.

    Para qué: el relleno automático de reposiciones (corte 3b) recibe el instante de corte (día + HoraCierre del
    calendario) y solo cuenta las líneas de pedido anteriores a ese instante, aunque Hangfire arranque unos segundos antes
    o después. LinPedidoVta no guardaba cuándo se crea una línea. prdRellenarReposicionStock2 lee
    ISNULL(FechaCreacion, [Fecha Modificación]).

    Decisión de Carlos (08/10/26): las filas NUEVAS llevan GETDATE() y las históricas NO se rellenan (se quedan a NULL).

    POR QUÉ ES INSTANTÁNEO (no reescribe la tabla ni la bloquea más que un momento):
      - ADD de una columna que admite NULL, con DEFAULT y SIN «WITH VALUES», es un cambio SOLO de metadatos en todas las
        ediciones de SQL Server (también en la Standard 2017 que tenemos): las filas existentes no se tocan y se leen como
        NULL. El DEFAULT solo se aplica a los INSERT que vengan después.
      - Con «WITH VALUES» (o con NOT NULL) SQL Server tendría que dar valor a las filas existentes: en Standard eso
        reescribe la tabla entera bajo bloqueo (y, encima, les pondría a todas la fecha de hoy). Por eso NO se pone.
      - Sí necesita un bloqueo Sch-M sobre LinPedidoVta durante un instante: espera a que terminen las consultas que estén
        leyendo la tabla en ese momento, y mientras espera bloquea a las que lleguen detrás. Por eso va con
        SET LOCK_TIMEOUT: si no consigue el bloqueo en 5 segundos, falla sin hacer nada y se vuelve a lanzar. Mejor fuera
        de las horas de más pedidos (antes de las 8:00 o a mediodía).

    Comprobado el 08/10/26 contra NV (solo lectura):
      - Triggers de LinPedidoVta: trgLinPedidoVtaUpd (FOR INSERT, UPDATE) y trgLinPedidoVtaDlt (FOR DELETE). Ninguno hace
        INSERT en LinPedidoVta ni copia sus filas con SELECT * a otra tabla: los SELECT * que tienen van dentro de EXISTS
        o de tablas derivadas de las que solo se leen columnas con nombre. No les afecta.
      - Procedimientos que insertan en LinPedidoVta (sys.sql_modules): todos con lista de columnas explícita
        (p. ej. prdInsertarCopiaLineaVta); la columna nueva no está en la lista y toma el DEFAULT. Ninguno hace
        «INSERT INTO LinPedidoVta SELECT …» ni «VALUES (…)» sin lista de columnas, que sí se romperían.
      - NestoAPI (EF6) y Nesto: no hay INSERT a mano en LinPedidoVta; EF inserta con la lista de columnas que tiene
        mapeadas, así que la columna (que NO se añade al EDMX) toma el DEFAULT.
      - Las vistas con SELECT * sobre LinPedidoVta no verán la columna hasta un sp_refreshview: no hace falta, nadie la
        necesita en una vista.

    ORDEN: este script va ANTES de Issue577_PropuestaReposicionSinBloqueo.sql (el procedimiento nuevo lee la columna y no
    se puede crear sin ella). Puede lanzarse ANTES del deploy de la API: la API no la usa directamente. Idempotente.

    Ejecutar en SSMS contra NV como sa (el login nuevavision no tiene ALTER).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas. No se crea ningún índice.
*/

SET NOCOUNT ON;
USE NV;
GO

SET LOCK_TIMEOUT 5000;  -- si en 5 s no consigue el Sch-M, error 1222 y no se ha hecho nada: volver a lanzar
GO

IF COL_LENGTH('dbo.LinPedidoVta', 'FechaCreacion') IS NULL
BEGIN
    -- SIN «WITH VALUES»: las filas existentes se quedan a NULL (solo metadatos); las nuevas, GETDATE()
    ALTER TABLE dbo.LinPedidoVta
        ADD FechaCreacion datetime NULL
            CONSTRAINT DF_LinPedidoVta_FechaCreacion DEFAULT (GETDATE());
END
GO

SET LOCK_TIMEOUT -1;
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
--   1) La columna existe, admite NULL y tiene el DEFAULT.
--   2) Las filas que ya había siguen a NULL (no se ha reescrito nada).
--   3) Tras el primer pedido nuevo, sus líneas llevan la hora de creación.
------------------------------------------------------------------------------------------------
SELECT c.name, t.name AS tipo, c.is_nullable, dc.name AS restriccion, dc.definition
FROM sys.columns c
     JOIN sys.types t ON t.user_type_id = c.user_type_id
     LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE c.object_id = OBJECT_ID('dbo.LinPedidoVta') AND c.name = 'FechaCreacion';

SELECT TOP 5 [Nº Orden], Número, FechaCreacion, [Fecha Modificación]
FROM dbo.LinPedidoVta WITH (NOLOCK)
ORDER BY [Nº Orden] DESC;
GO
