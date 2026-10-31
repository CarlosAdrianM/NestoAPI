/*
    NestoAPI#771 / Ariadna (gemela): las fotos del packing dejan de ir ligadas a los bultos.

    Hasta ahora cada caja se cerraba con SU foto (EnviosAgenciaBultos.RutaBlob). A veces una sola foto lo saca todo antes de
    repartirlo en varias cajas, y otras hacen falta tres para una caja. Regla nueva (Carlos, 31/10/26): MÍNIMO 1 foto por
    entrega del packing, y las que quieran, sin relación con el número de bultos.

    - Las cajas se siguen guardando en EnviosAgenciaBultos (son lo que cuenta Agencias para la etiqueta), ahora también sin
      foto (RutaBlob NULL, la columna ya lo admitía).
    - Las fotos libres del packing van a esta tabla nueva: una fila por foto y pedido (una foto de una entrega con dos pedidos
      del mismo cliente tiene una fila por pedido apuntando al mismo blob, como los bultos compartidos).
    - Las fotos de antes (una por bulto) se siguen viendo igual en Nesto, en Ariadna y en la página de seguimiento.

    También la usa la página pública de seguimiento del reparto propio (GET /seguimiento/{token}) para la galería de fotos.

    Fuera del EDMX: la API la lee y escribe por SQL y comprueba que existe (OBJECT_ID). Sin el script, las fotos libres dan
    400 «falta el script» y todo lo demás sigue como antes.

    Ejecutar en SSMS contra NV como sa (CREATE). Idempotente.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PreparacionFotosPacking', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PreparacionFotosPacking (
        Id int IDENTITY(1,1) NOT NULL,
        IdCliente uniqueidentifier NOT NULL,       -- Lo genera el móvil para cada foto: un reenvío de la cola no duplica
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Picking int NOT NULL,
        RutaBlob varchar(200) NOT NULL,            -- bultos/{empresa}/{pedido}/{picking}/packing-{IdCliente}.jpg
        HashSha256 char(64) NULL,                  -- De la imagen tal como se guardó: prueba de que no se ha tocado
        TamanoBytes int NULL,
        Usuario varchar(50) NOT NULL,
        Dispositivo varchar(50) NULL,
        FechaFoto datetime NULL,                   -- La hora del móvil
        FechaRegistro datetime NOT NULL CONSTRAINT DF_PreparacionFotosPacking_FechaRegistro DEFAULT (GETDATE()),
        CONSTRAINT PK_PreparacionFotosPacking PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_PreparacionFotosPacking_IdCliente UNIQUE (IdCliente)
    );
    CREATE INDEX IX_PreparacionFotosPacking_Pedido ON dbo.PreparacionFotosPacking (Empresa, Pedido, Picking);
    CREATE INDEX IX_PreparacionFotosPacking_Picking ON dbo.PreparacionFotosPacking (Empresa, Picking) INCLUDE (Pedido);
    PRINT 'PreparacionFotosPacking: creada.';
END
ELSE
    PRINT 'PreparacionFotosPacking: ya existía.';
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina. Solo se lee y se inserta (evidencia).
GRANT SELECT, INSERT ON dbo.PreparacionFotosPacking TO [NUEVAVISION\RDS2016$];
GO

SELECT name FROM sys.tables WHERE name = 'PreparacionFotosPacking';
