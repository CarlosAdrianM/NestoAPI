-- NestoAPI#556: evidencia de la preparación de pedidos con Ariadna (la app de almacén).
--
-- Dos tablas nuevas, sin tocar ninguna existente:
--   PreparacionEscaneos  → cada lectura (o toque manual) del picking y del packing. Solo inserciones.
--   EnviosAgenciaBultos  → un registro por bulto de cada pedido, con la foto guardada en Azure.
--
-- IdCliente (uniqueidentifier) lo genera el móvil para cada escaneo y cada foto: la app trabaja con
-- una cola que reenvía cuando vuelve la señal, y con esa clave un reenvío no duplica nada.
--
-- Un bulto compartido por dos pedidos del mismo cliente (dos facturas, una caja) se guarda como una
-- fila por pedido apuntando a la misma foto (misma RutaBlob).
--
-- PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).

USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND type = 'U')
BEGIN
    CREATE TABLE dbo.PreparacionEscaneos (
        Id bigint IDENTITY(1,1) NOT NULL,
        IdCliente uniqueidentifier NOT NULL,       -- Lo genera el móvil: un reenvío de la cola no duplica
        Empresa char(3) NOT NULL,
        Picking int NOT NULL,                      -- LinPedidoVta.Picking
        Pedido int NULL,                           -- NULL en el picking por ola (una lectura sirve a varios pedidos)
        LineaPedido int NULL,                      -- LinPedidoVta.[Nº Orden], cuando se sabe
        Producto char(15) NOT NULL,
        Fase char(4) NOT NULL,                     -- PICK / PACK
        Cantidad smallint NOT NULL,                -- Negativa para deshacer una lectura
        Metodo char(6) NOT NULL,                   -- SCAN (lector o cámara) / MANUAL (toque + cantidad) / FALTA
        Bulto smallint NULL,                       -- En PACK: en qué caja va
        Motivo varchar(100) NULL,                  -- En FALTA: por qué no se ha podido coger
        Usuario varchar(50) NOT NULL,
        Dispositivo varchar(50) NULL,
        FechaEscaneo datetime NOT NULL,            -- La hora del móvil (la lectura puede subir más tarde)
        FechaRegistro datetime NOT NULL CONSTRAINT DF_PreparacionEscaneos_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_PreparacionEscaneos PRIMARY KEY (Id),
        CONSTRAINT UQ_PreparacionEscaneos_IdCliente UNIQUE (IdCliente),
        CONSTRAINT CK_PreparacionEscaneos_Fase CHECK (Fase IN ('PICK', 'PACK')),
        CONSTRAINT CK_PreparacionEscaneos_Metodo CHECK (Metodo IN ('SCAN', 'MANUAL', 'FALTA'))
    );

    CREATE INDEX IX_PreparacionEscaneos_Picking ON dbo.PreparacionEscaneos (Empresa, Picking, Fase);
    CREATE INDEX IX_PreparacionEscaneos_Pedido ON dbo.PreparacionEscaneos (Empresa, Pedido);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.EnviosAgenciaBultos') AND type = 'U')
BEGIN
    CREATE TABLE dbo.EnviosAgenciaBultos (
        Id int IDENTITY(1,1) NOT NULL,
        IdCliente uniqueidentifier NOT NULL,       -- Lo genera el móvil: un reenvío de la cola no duplica
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Picking int NOT NULL,
        Bulto smallint NOT NULL,                   -- 1, 2, 3… dentro del pedido y del picking
        Peso decimal(7, 3) NULL,                   -- De momento se teclea en Nesto al imprimir la etiqueta
        RutaBlob varchar(200) NULL,                -- bultos/{empresa}/{pedido}/{picking}/bulto-{n}-{fecha}.jpg
        HashSha256 char(64) NULL,                  -- De la imagen tal como se guardó: prueba de que no se ha tocado
        TamanoBytes int NULL,
        NumeroEnvio int NULL,                      -- EnviosAgencia.Numero, cuando se crea la etiqueta
        Usuario varchar(50) NOT NULL,
        Dispositivo varchar(50) NULL,
        FechaFoto datetime NULL,                   -- La hora del móvil
        FechaRegistro datetime NOT NULL CONSTRAINT DF_EnviosAgenciaBultos_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_EnviosAgenciaBultos PRIMARY KEY (Id),
        CONSTRAINT UQ_EnviosAgenciaBultos_IdCliente UNIQUE (IdCliente),
        CONSTRAINT UQ_EnviosAgenciaBultos_Bulto UNIQUE (Empresa, Pedido, Picking, Bulto),
        CONSTRAINT CK_EnviosAgenciaBultos_Bulto CHECK (Bulto > 0)
    );

    CREATE INDEX IX_EnviosAgenciaBultos_NumeroEnvio ON dbo.EnviosAgenciaBultos (NumeroEnvio);
END
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina.
-- Los escaneos son evidencia: ni se modifican ni se borran (una corrección es otra fila en negativo).
GRANT SELECT, INSERT ON dbo.PreparacionEscaneos TO [NUEVAVISION\RDS2016$];
-- Un bulto sí se actualiza: repetir la foto, anotar el peso o el número de envío.
GRANT SELECT, INSERT, UPDATE ON dbo.EnviosAgenciaBultos TO [NUEVAVISION\RDS2016$];
GO
