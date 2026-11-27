/*
    NestoAPI#876 (Carlos 27/11/26): MOTIVO Y COMENTARIO DE CADA ENLACE de dbo.ClientesAnteriores.
    La tabla ya no es solo del cambio de forma fiscal: también enlaza las fichas DUPLICADAS (la misma persona dada de alta dos veces;
    OneShot_20261126_ClientesDuplicados_Enlazar.sql) y, desde hoy, la API las enlaza sin script (POST api/Clientes/Equivalentes, con
    su DELETE y su GET api/Clientes/{empresa}/{cliente}/Equivalentes). Aquí se guarda por qué se enlazaron:
      - Motivo varchar(20) NULL: «Duplicado» o «CambioFiscal» (lo valida la API; sin CHECK a propósito, para no tener que tocarlo si
        un día hay un motivo nuevo). DEFAULT 'CambioFiscal': el alta y la copia de condiciones de #876 no lo mandan y son eso.
      - Comentario nvarchar(255) NULL.
      Las filas que ya hay: las del script del 26/11/26 (Usuario «Duplicado 26/11/26») → «Duplicado»; las demás → «CambioFiscal».

    SIN ESTE SCRIPT: la API enlaza, consulta y deshace igual; el motivo y el comentario solo quedan en Modificaciones (Tabla
    «ClientesAnteriores») y el GET devuelve Motivo y Comentario null. Tras lanzarlo, basta reciclar el app pool (la API recuerda
    que las columnas existen).

    Lanzar como sa en NV, entero. Idempotente (marca de «ya aplicado»: la columna Motivo). El GRANT de la tabla
    (Issue876_ClientesAnteriores.sql) ya cubre las columnas nuevas.
*/
SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.ClientesAnteriores', N'U') IS NULL
BEGIN
    RAISERROR(N'Falta dbo.ClientesAnteriores: lance antes Scripts/Issue876_ClientesAnteriores.sql. No se ha hecho nada.', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH(N'dbo.ClientesAnteriores', N'Motivo') IS NULL
BEGIN
    ALTER TABLE dbo.ClientesAnteriores ADD Motivo varchar(20) NULL CONSTRAINT DF_ClientesAnteriores_Motivo DEFAULT ('CambioFiscal');
    PRINT 'Añadida dbo.ClientesAnteriores.Motivo.';
END
ELSE
    PRINT 'dbo.ClientesAnteriores.Motivo ya existía (ya aplicado).';
GO

IF COL_LENGTH(N'dbo.ClientesAnteriores', N'Comentario') IS NULL
BEGIN
    ALTER TABLE dbo.ClientesAnteriores ADD Comentario nvarchar(255) NULL;
    PRINT 'Añadida dbo.ClientesAnteriores.Comentario.';
END
ELSE
    PRINT 'dbo.ClientesAnteriores.Comentario ya existía (ya aplicado).';
GO

-- Las filas de antes (solo las que no tienen motivo: se puede repetir)
UPDATE dbo.ClientesAnteriores SET Motivo = 'Duplicado'
WHERE Motivo IS NULL AND Usuario LIKE 'Duplicado%';
PRINT CONCAT('Filas marcadas como Duplicado: ', @@ROWCOUNT);
UPDATE dbo.ClientesAnteriores SET Motivo = 'CambioFiscal'
WHERE Motivo IS NULL;
PRINT CONCAT('Filas marcadas como CambioFiscal: ', @@ROWCOUNT);
GO

-- VERIFICACIÓN
SELECT RTRIM(Empresa) AS Empresa, RTRIM(Cliente) AS Cliente, RTRIM(ClienteAnterior) AS ClienteAnterior, Motivo, Comentario, Fecha, RTRIM(Usuario) AS Usuario
FROM dbo.ClientesAnteriores ORDER BY Fecha DESC, Cliente;
GO
SET NOEXEC OFF;
GO
