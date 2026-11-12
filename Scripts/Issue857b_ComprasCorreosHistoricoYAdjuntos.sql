/*
    NestoAPI#857 (fase 1, EN SOMBRA, 12/11/26), segunda parte: adjuntos de los correos de seguimiento de compras.

    Añade a dbo.ComprasCorreosSeguimiento (la de Scripts/Issue857_ComprasCorreosSeguimiento.sql):
      - NumAdjuntos int NULL: cuántos adjuntos de verdad trae el correo (sin las imágenes incrustadas de la firma). NULL = sin apuntar
        (correo guardado antes de este script, o Graph no los devolvió).
      - Adjuntos nvarchar(2000) NULL: JSON [{Nombre, Tipo, Tamano}] de los 10 primeros. Nunca el contenido: no se descargan.

    La lectura hacia atrás del buzón (POST api/Compras/AgenciasEntrada/Historico) NO necesita este script: guarda su progreso en una
    fila aparte de dbo.ComprasCorreosSeguimientoEstado (Buzon = 'historico|{buzón}'), sin tocar la del delta del job.

    ORDEN: después de Issue857_ComprasCorreosSeguimiento.sql (si no existe la tabla, no hace nada y lo dice). Puede ir ANTES o DESPUÉS
    de publicar la API: sin las columnas, la API guarda como hasta ahora (sin adjuntos) y GET …/Correos los devuelve a null; la API lo
    vuelve a comprobar cada 10 minutos, así que no hace falta reiniciarla. Conviene lanzarlo ANTES del histórico para que los correos
    antiguos lleven sus adjuntos.

    Lanzar como sa en NV. Idempotente: se puede relanzar. Cada columna nueva en su propio lote. Los permisos ya los tiene la tabla
    (GRANT SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$] en Issue857): no hace falta ninguno nuevo.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'NumAdjuntos') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD NumAdjuntos int NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'Adjuntos') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD Adjuntos nvarchar(2000) NULL;
GO
-- Comprobación
SELECT CASE WHEN COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'NumAdjuntos') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'Adjuntos') IS NOT NULL
            THEN N'OK: columnas NumAdjuntos y Adjuntos en dbo.ComprasCorreosSeguimiento'
            ELSE N'FALTA: no están las columnas (¿existe la tabla?)' END AS Resultado;
GO
