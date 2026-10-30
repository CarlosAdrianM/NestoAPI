/*
    NestoAPI#737 (sugerencias 866/867, Paloma; 831, María José): tabla EnlacesDescargaDocumentos.

    Los enlaces públicos de descarga del pedido o de la factura que Nesto manda por WhatsApp Web (Nesto#570):
        POST api/Documentos/EnlaceDescarga   (solo empleados) da de alta una fila y devuelve https://api.nuevavision.es/d/{Token}
        GET  /d/{Token}                       (anónimo) devuelve el PDF (QuestPDF) mientras no haya caducado y suma una descarga

    Token:     32 bytes aleatorios en Base64Url (43 caracteres). No lleva nada dentro: no es adivinable ni enumerable.
    Caduca:    Creado + 30 días. Caducado → página «Este enlace ha caducado» (410); inexistente → «No encontramos este documento» (404).
    Usuario:   quien lo creó (DOMINIO\Usuario del token de Windows de Nesto).
    Descargas / UltimaDescarga: cuántas veces lo ha abierto el cliente y cuándo fue la última.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como ClientesRiesgoAbandono #714): se puede lanzar antes o después
    del deploy. Sin ella, crear un enlace contesta 503 «Los enlaces de descarga todavía no están activados…».

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE (contar las descargas). Sin DELETE.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EnlacesDescargaDocumentos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnlacesDescargaDocumentos (
        Token varchar(64) NOT NULL,
        Tipo varchar(10) NOT NULL,                      -- Pedido / Factura
        Empresa varchar(3) NOT NULL,
        Numero varchar(20) NOT NULL,                    -- nº de pedido (929498) o de factura (NV2616199)
        Creado datetime NOT NULL CONSTRAINT DF_EnlacesDescargaDocumentos_Creado DEFAULT (GETDATE()),
        Caduca datetime NOT NULL,
        Usuario nvarchar(100) NULL,                     -- quien lo creó
        Descargas int NOT NULL CONSTRAINT DF_EnlacesDescargaDocumentos_Descargas DEFAULT (0),
        UltimaDescarga datetime NULL,

        CONSTRAINT PK_EnlacesDescargaDocumentos PRIMARY KEY CLUSTERED (Token),
        CONSTRAINT CK_EnlacesDescargaDocumentos_Tipo CHECK (Tipo IN ('Pedido', 'Factura'))
    );
END
GO

-- Para consultar los enlaces de un documento (¿se lo hemos mandado? ¿lo ha abierto?)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EnlacesDescargaDocumentos_Documento' AND object_id = OBJECT_ID('dbo.EnlacesDescargaDocumentos'))
    CREATE INDEX IX_EnlacesDescargaDocumentos_Documento ON dbo.EnlacesDescargaDocumentos (Empresa, Tipo, Numero)
        INCLUDE (Creado, Caduca, Usuario, Descargas, UltimaDescarga);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.EnlacesDescargaDocumentos TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.EnlacesDescargaDocumentos', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.EnlacesDescargaDocumentos) AS Filas;
GO

-- Los últimos enlaces y si el cliente los ha abierto:
-- SELECT TOP 50 Creado, Usuario, Tipo, Numero, Caduca, Descargas, UltimaDescarga FROM dbo.EnlacesDescargaDocumentos ORDER BY Creado DESC;
