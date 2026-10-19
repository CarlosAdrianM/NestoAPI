-- Ariadna#47 (19/10/26), OPCIONAL: índice para la consulta de los productos del mismo hueco (ProductosMismoHueco).
-- Sin él, la consulta lee unas 42.000 páginas (~340 ms) en cada GET de picking, recogida, ubicar y reposición en preparación.
-- Ubicaciones tiene 2,75 M de filas: crearlo bloquea la tabla un rato. Lanzar como sa FUERA DE HORARIO (de noche o en fin de semana).
USE NV;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Ubicaciones_Hueco' AND object_id = OBJECT_ID('dbo.Ubicaciones'))
    CREATE NONCLUSTERED INDEX IX_Ubicaciones_Hueco ON dbo.Ubicaciones (Empresa, [Almacén], Pasillo, Fila, Columna)
        INCLUDE ([Número], Estado, Cantidad);
