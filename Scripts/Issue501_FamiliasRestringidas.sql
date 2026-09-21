-- NestoAPI#501: familias restringidas a venta presencial e incompatibilidades entre familias.
--
-- Kinetics (Kinetics Nail Systems) solo lo pueden ofrecer los vendedores presenciales
-- (Vendedores.Estado = 0), no sale a la tienda online (ni a PrestaShop ni a Odoo, porque de
-- momento no se publica el mensaje) y no se puede vender a un cliente que haya comprado de las
-- familias Faby o Greenik en los ultimos 24 meses.
--
-- El mecanismo es generico: cualquier familia puede marcarse como restringida y cualquier pareja
-- de familias puede declararse incompatible con su propia ventana en meses (0 = todo el historico).
--
-- Ejecutar como sa (lleva DDL). Es idempotente: se puede lanzar varias veces.

SET NOCOUNT ON;
USE NV;

-- ---------------------------------------------------------------------------------------
-- 1. La marca de familia restringida
-- ---------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Familias') AND name = 'SoloVentaPresencial')
BEGIN
    ALTER TABLE dbo.Familias ADD SoloVentaPresencial bit NOT NULL CONSTRAINT DF_Familias_SoloVentaPresencial DEFAULT 0;
    PRINT 'Familias.SoloVentaPresencial creada';
END
ELSE
BEGIN
    PRINT 'Familias.SoloVentaPresencial ya existia';
END
GO

-- ---------------------------------------------------------------------------------------
-- 2. Que familia impide comprar cual
-- ---------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.FamiliasIncompatibles', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FamiliasIncompatibles
    (
        Id                  int IDENTITY(1,1) NOT NULL,
        Empresa             char(3)      NOT NULL,
        -- La familia que se quiere vender (la restringida).
        Familia             char(10)     NOT NULL,
        -- La familia que, si el cliente la ha comprado, impide vender la de arriba.
        FamiliaIncompatible char(10)     NOT NULL,
        -- Cuantos meses hacia atras se mira. 0 = todo el historico.
        Meses               int          NOT NULL CONSTRAINT DF_FamiliasIncompatibles_Meses DEFAULT 24,
        -- Estado >= 0 activa, < 0 desactivada (mismo criterio que el resto de tablas de Nesto).
        Estado              smallint     NOT NULL CONSTRAINT DF_FamiliasIncompatibles_Estado DEFAULT 0,
        Usuario             varchar(30)  NOT NULL CONSTRAINT DF_FamiliasIncompatibles_Usuario DEFAULT suser_sname(),
        FechaModificacion   datetime     NOT NULL CONSTRAINT DF_FamiliasIncompatibles_Fecha DEFAULT GETDATE(),
        CONSTRAINT PK_FamiliasIncompatibles PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_FamiliasIncompatibles UNIQUE (Empresa, Familia, FamiliaIncompatible),
        -- Una familia incompatible consigo misma no tiene sentido y bloquearia su propia venta.
        CONSTRAINT CK_FamiliasIncompatibles_NoConsigoMisma CHECK (Familia <> FamiliaIncompatible),
        CONSTRAINT CK_FamiliasIncompatibles_Meses CHECK (Meses >= 0)
    );
    PRINT 'Tabla FamiliasIncompatibles creada';
END
ELSE
BEGIN
    PRINT 'Tabla FamiliasIncompatibles ya existia';
END
GO

-- La cuenta con la que corre NestoAPI en el servidor.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.FamiliasIncompatibles TO [NUEVAVISION\RDS2016$];
GO

-- ---------------------------------------------------------------------------------------
-- 3. Kinetics
-- ---------------------------------------------------------------------------------------
UPDATE dbo.Familias
SET SoloVentaPresencial = 1
WHERE [Número] = 'Kinetics' AND SoloVentaPresencial = 0;

PRINT 'Familias marcadas como solo venta presencial:';
SELECT Empresa, [Número] AS Familia, [Descripción] AS Descripcion, Estado
FROM dbo.Familias
WHERE SoloVentaPresencial = 1;

-- Kinetics no se vende a quien haya comprado Faby o Greenik en los ultimos 24 meses.
MERGE dbo.FamiliasIncompatibles AS destino
USING (VALUES
        ('1  ', 'Kinetics  ', 'Faby      ', 24),
        ('1  ', 'Kinetics  ', 'Greenik   ', 24)
      ) AS origen (Empresa, Familia, FamiliaIncompatible, Meses)
   ON  destino.Empresa = origen.Empresa
   AND destino.Familia = origen.Familia
   AND destino.FamiliaIncompatible = origen.FamiliaIncompatible
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Empresa, Familia, FamiliaIncompatible, Meses)
    VALUES (origen.Empresa, origen.Familia, origen.FamiliaIncompatible, origen.Meses);

PRINT 'Incompatibilidades:';
SELECT Id, Empresa, Familia, FamiliaIncompatible, Meses, Estado, Usuario, FechaModificacion
FROM dbo.FamiliasIncompatibles
ORDER BY Empresa, Familia, FamiliaIncompatible;
GO

-- ---------------------------------------------------------------------------------------
-- 4. Comprobaciones (descomentar y lanzar despues de publicar la API)
-- ---------------------------------------------------------------------------------------
/*
-- Cuantos clientes quedarian bloqueados hoy para Kinetics (compras de Faby o Greenik en 24 meses,
-- cualquier linea que no sea presupuesto):
SELECT COUNT(DISTINCT l.[Nº Cliente]) AS ClientesBloqueados
FROM LinPedidoVta l WITH (NOLOCK)
INNER JOIN Productos p WITH (NOLOCK) ON p.Empresa = l.Empresa AND p.[Número] = l.Producto
WHERE l.Empresa = '1'
  AND l.Estado > -3
  AND p.Familia IN ('Faby', 'Greenik')
  AND l.Fecha >= DATEADD(month, -24, GETDATE());

-- Que ningun producto de Kinetics esta publicado en la tienda (deberia devolver 0 desde que la
-- puerta de publicacion lo veta; si hubiera alguno publicado de antes, hay que sacarlo a mano):
SELECT COUNT(*) FROM Productos WITH (NOLOCK) WHERE Empresa = '1' AND Familia = 'Kinetics';
*/
