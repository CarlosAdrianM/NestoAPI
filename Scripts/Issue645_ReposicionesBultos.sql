/*
    NestoAPI#645 (corte 1): tabla ReposicionesBultos.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. No está en el EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy:
    sin ella, los endpoints de bultos responden 503 y la recepción de reposiciones no exige etiqueta (como hoy).

    Una fila por bulto de cada reposición. La crea la API al cerrar la reposición (POST api/Reposiciones/{traspaso}/Bultos,
    desde Nesto «Enviar reposición» o Ariadna), que imprime una etiqueta por bulto con el código «R81010-2». Al recibir
    (api/Almacen/Recepciones/REPO/{traspaso}/Terminar), si el traspaso tiene filas aquí hay que leer al menos una etiqueta
    suya; los bultos leídos se marcan (FechaLectura, UsuarioLectura) en la misma transacción que la entrada. Las reposiciones
    cerradas sin bultos (las de antes de este script) se reciben como siempre.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT, UPDATE y DELETE (al cambiar el número de bultos antes de leer ninguno, se rehacen).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ReposicionesBultos') IS NULL
BEGIN
    CREATE TABLE dbo.ReposicionesBultos (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,
        Bulto smallint NOT NULL,                        -- 1..Total
        Total smallint NOT NULL,
        Codigo varchar(20) NOT NULL,                    -- R{traspaso}-{bulto}, lo que lleva el código de barras
        FechaCreacion datetime NOT NULL CONSTRAINT DF_ReposicionesBultos_FechaCreacion DEFAULT (GETDATE()),
        UsuarioCreacion varchar(50) NULL,
        FechaLectura datetime NULL,                     -- la primera vez que se lee al recibir
        UsuarioLectura varchar(50) NULL,

        CONSTRAINT PK_ReposicionesBultos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ReposicionesBultos_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_ReposicionesBultos_Bulto CHECK (Bulto >= 1 AND Bulto <= Total AND Total <= 50)
    );
END
GO

-- Los bultos de un traspaso (al recibir y al rehacerlos)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ReposicionesBultos_Traspaso' AND object_id = OBJECT_ID('dbo.ReposicionesBultos'))
    CREATE INDEX IX_ReposicionesBultos_Traspaso ON dbo.ReposicionesBultos (Empresa, NumTraspaso);
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ReposicionesBultos TO [NUEVAVISION\RDS2016$];
GO
