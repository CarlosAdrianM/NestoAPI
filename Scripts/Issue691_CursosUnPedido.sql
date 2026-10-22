/*
    NestoAPI#691 (decisión de Carlos del 22/10/26): todos los cursos de la plantilla van en UN pedido (serie CV), una línea por
    curso, y cada línea se factura en cuanto se paga su inscripción (sin esperar al evento).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE). Idempotente: se puede lanzar dos veces. Hay que lanzarlo ANTES de publicar
    la API (después de Scripts/Issue691_CursosPlantilla.sql, si no se había lanzado). Sin él, la API sigue leyendo
    EventosInscripcionesCurso como hasta ahora (#612), pero el pedido de cursos de la plantilla no se puede crear: el POST
    devuelve el aviso sin crear los cursos.

    EventosInscripcionesCurso.LineaPedido: el Nº Orden de la línea de la inscripción en el pedido de cursos. Con ella se sabe qué
    línea facturar al pagarse cada inscripción (las de las inscripciones sin pagar se quedan Pendientes, −1, fuera del albarán).
    NULL en las filas de #612 (un pedido por inscripción).

    El estado «PendienteEvento» que añadió Issue691_CursosPlantilla.sql ya no se usa (las filas nacen «EnCurso»); se deja en el
    CHECK porque no estorba.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NULL
BEGIN
    RAISERROR('Falta dbo.EventosInscripcionesCurso: lance antes Scripts/Issue612_FacturacionCursosEventos.sql.', 16, 1);
    RETURN;
END
GO

IF COL_LENGTH('dbo.EventosInscripcionesCurso', 'LineaPedido') IS NULL
BEGIN
    ALTER TABLE dbo.EventosInscripcionesCurso ADD LineaPedido int NULL;
    PRINT N'Columna LineaPedido creada.';
END
ELSE
    PRINT N'La columna LineaPedido ya existía.';
GO

SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EventosInscripcionesCurso' AND COLUMN_NAME = 'LineaPedido';
GO
