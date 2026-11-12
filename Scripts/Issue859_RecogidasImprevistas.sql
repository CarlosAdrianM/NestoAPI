/*
    NestoAPI#859 (12/11/26): recogida imprevista del reparto propio («Me dan algo que no esperaba»). El repartidor, en casa de un
    cliente, recibe algo sin nota de recogida: desde la PDA (Ariadna) la API crea la nota de recogida NR{pedido} al vuelo, como una
    solicitud de #855 que nace ya «Confirmada», y apunta sus bultos como «Recogido» en la furgoneta de ese repartidor.

    Columnas nuevas en RecogidasSolicitadas (requiere Scripts/Issue855_RecogidasSolicitadas.sql):
      - Origen: 'Solicitud' (las de #855, pedidas por el vendedor) o 'Repartidor' (las imprevistas). Las de antes, 'Solicitud'.
      - Clave: la clave de idempotencia del aparato (repetir con la misma clave devuelve la misma nota).
      - FotoRuta: la foto que hizo el repartidor (blob «bultos» de las fotos del packing), si la hizo.
      - AvisoUrgente: se recogió aceptando un envío de agencia con retorno en camino (salió el aviso urgente a Almacén).

    Sin el script, POST api/RepartoPropio/RecogidasImprevistas(/Comprobar) da 503. Lanzar en SSMS contra NV como sa ANTES de publicar
    la API. Idempotente (cada columna en su propio lote). PRECAUCIÓN (#542, #294): nada de índices filtrados.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.RecogidasSolicitadas', N'U') IS NULL
    RAISERROR('Falta Scripts/Issue855_RecogidasSolicitadas.sql: lánzelo antes.', 16, 1);
GO

IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'Origen') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD Origen varchar(12) NOT NULL
        CONSTRAINT DF_RecogidasSolicitadas_Origen DEFAULT ('Solicitud') WITH VALUES;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_RecogidasSolicitadas_Origen')
    ALTER TABLE dbo.RecogidasSolicitadas ADD CONSTRAINT CK_RecogidasSolicitadas_Origen CHECK (Origen IN ('Solicitud', 'Repartidor'));
GO
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'Clave') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD Clave uniqueidentifier NULL;
GO
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'FotoRuta') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD FotoRuta nvarchar(300) NULL;
GO
-- Se recogió con un envío de agencia con retorno en camino (aceptado por el repartidor): salió el aviso urgente a Almacén
IF COL_LENGTH(N'dbo.RecogidasSolicitadas', N'AvisoUrgente') IS NULL
    ALTER TABLE dbo.RecogidasSolicitadas ADD AvisoUrgente bit NOT NULL CONSTRAINT DF_RecogidasSolicitadas_AvisoUrgente DEFAULT (0) WITH VALUES;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RecogidasSolicitadas_Clave' AND object_id = OBJECT_ID(N'dbo.RecogidasSolicitadas'))
    CREATE INDEX IX_RecogidasSolicitadas_Clave ON dbo.RecogidasSolicitadas (Clave);
GO

SELECT Origen, COUNT(*) AS Filas FROM dbo.RecogidasSolicitadas GROUP BY Origen;
