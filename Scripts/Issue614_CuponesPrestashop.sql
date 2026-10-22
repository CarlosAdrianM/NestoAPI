/*
    NestoAPI#614 (cupones, corte 4 de #610): el espejo de los cupones en la tienda PrestaShop.

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    DESPUÉS de Issue613_Cupones.sql. Se puede lanzar antes o después de publicar la API, pero SIEMPRE ANTES de encender el
    interruptor Cupones:EspejoPrestashop del Web.config: con él apagado la API no lee estas columnas.

    Columnas nuevas en dbo.Cupones (todas las escribe la API; nadie las toca a mano):
       - IdCartRulePrestashop / IdCustomerPrestashop: el cart_rule de la tienda con el código del cupón y la cuenta del cliente
         (buscada por correo, como el login de PrestaShop).
       - EstadoEspejo: lo que hay en la tienda. NULL (nunca se ha llevado), Activo, Inactivo (active = 0 y quantity = 0) o
         SinCliente (el cliente no tiene cuenta en la tienda con ninguno de sus correos: se vuelve a mirar cada día).
         Lo que TIENE que haber sale del Estado del cupón: Emitido/Activo sin caducar → Activo; el resto → Inactivo. El job
         cupones-prestashop (cada 15 minutos) lleva a la tienda lo que no coincida.
       - EspejoPendienteDesde / EspejoIntentos / EspejoError: los reintentos. FechaEspejo: la última vez que salió bien.
       - FechaAvisoEspejo: ya se ha mandado el correo de «lleva más de Cupones:HorasAvisoEspejo horas sin espejo».

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Cupones', 'IdCartRulePrestashop') IS NULL
    ALTER TABLE dbo.Cupones ADD IdCartRulePrestashop int NULL;
IF COL_LENGTH('dbo.Cupones', 'IdCustomerPrestashop') IS NULL
    ALTER TABLE dbo.Cupones ADD IdCustomerPrestashop int NULL;
IF COL_LENGTH('dbo.Cupones', 'EstadoEspejo') IS NULL
    ALTER TABLE dbo.Cupones ADD EstadoEspejo varchar(12) NULL;
IF COL_LENGTH('dbo.Cupones', 'EspejoPendienteDesde') IS NULL
    ALTER TABLE dbo.Cupones ADD EspejoPendienteDesde datetime NULL;
IF COL_LENGTH('dbo.Cupones', 'EspejoIntentos') IS NULL
    ALTER TABLE dbo.Cupones ADD EspejoIntentos int NOT NULL CONSTRAINT DF_Cupones_EspejoIntentos DEFAULT (0);
IF COL_LENGTH('dbo.Cupones', 'EspejoError') IS NULL
    ALTER TABLE dbo.Cupones ADD EspejoError nvarchar(500) NULL;
IF COL_LENGTH('dbo.Cupones', 'FechaEspejo') IS NULL
    ALTER TABLE dbo.Cupones ADD FechaEspejo datetime NULL;
IF COL_LENGTH('dbo.Cupones', 'FechaAvisoEspejo') IS NULL
    ALTER TABLE dbo.Cupones ADD FechaAvisoEspejo datetime NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Cupones_EstadoEspejo')
    ALTER TABLE dbo.Cupones ADD CONSTRAINT CK_Cupones_EstadoEspejo
        CHECK (EstadoEspejo IS NULL OR EstadoEspejo IN ('Activo', 'Inactivo', 'SinCliente'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Cupones_Espejo' AND object_id = OBJECT_ID('dbo.Cupones'))
    CREATE INDEX IX_Cupones_Espejo ON dbo.Cupones (EstadoEspejo, Estado, Caducidad);
GO

-- Los permisos de la tabla ya los dio Issue613_Cupones.sql (SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$]): sirven para las
-- columnas nuevas.

-- Comprobación
SELECT COUNT(*) AS Cupones,
       SUM(CASE WHEN Estado IN ('Emitido', 'Activo') AND Caducidad >= CAST(GETDATE() AS date) THEN 1 ELSE 0 END) AS IranALaTiendaAlEncender
FROM dbo.Cupones;
GO
