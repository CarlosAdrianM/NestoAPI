/*
    NestoAPI#914 (corte 1, Carlos 26/11/26): dbo.CalidadEventos, las señales de calidad que hasta ahora solo salían por correo o push
    y se perdían (p. ej. «X pide cambiar el modo de entrega del pedido N … Nesto no ha dejado cambiarlo porque el pedido ya tiene picking»).

    NADA manual: las escribe la API sola, en el momento en que detecta algo que se sale del pedido perfecto, por UN único punto
    (Infraestructure/Calidad/RegistroCalidad.Apuntar: nunca lanza, nunca retrasa la operación, conexión propia sin alistar).
    Los tipos y dónde se apunta cada uno: docs/claude/calidad-kpi.md (TiposEventoCalidad en el código).

    Columnas:
      Fecha      cuándo pasó (hora del servidor de la API).
      Tipo       TiposEventoCalidad (CambioDeModoConPicking, QuitarPicking, AmpliacionConPickingPedidoNuevo, …).
      Empresa, Pedido, Cliente, Contacto   si el evento es de un pedido.
      Documento  envío, albarán, factura… («Envio 251099»), si lo hay.
      Usuario    quien lo provoca (sin dominio).
      Imputado   a quién se imputa, solo si se sabe en el momento (NULL casi siempre en este corte).
      Detalle    JSON corto «de qué a qué» (≤ 1.000).
      Importe    solo un coste que salga solo (nunca tecleado).

    Fuera del EDMX. Sin el script la API funciona igual: no apunta nada (ELMAH una sola vez) y en el pedido perfecto los motivos que
    dependen de estos eventos quedan «no evaluados».

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT e INSERT. Sin UPDATE ni DELETE: las filas son la constancia.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.CalidadEventos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CalidadEventos (
        Id bigint IDENTITY(1,1) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_CalidadEventos_Fecha DEFAULT (GETDATE()),
        Tipo varchar(40) NOT NULL,
        Empresa char(3) NULL,
        Pedido int NULL,
        Cliente varchar(10) NULL,
        Contacto varchar(3) NULL,
        Documento varchar(30) NULL,
        Usuario varchar(50) NOT NULL,
        Imputado varchar(50) NULL,
        Detalle nvarchar(1000) NULL,
        Importe money NULL,
        CONSTRAINT PK_CalidadEventos PRIMARY KEY CLUSTERED (Id)
    );
    PRINT 'CalidadEventos: creada.';
END
ELSE
    PRINT 'CalidadEventos: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CalidadEventos_Pedido' AND object_id = OBJECT_ID('dbo.CalidadEventos'))
    CREATE INDEX IX_CalidadEventos_Pedido ON dbo.CalidadEventos (Empresa, Pedido);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CalidadEventos_Tipo' AND object_id = OBJECT_ID('dbo.CalidadEventos'))
    CREATE INDEX IX_CalidadEventos_Tipo ON dbo.CalidadEventos (Tipo, Fecha);
GO

GRANT SELECT, INSERT ON dbo.CalidadEventos TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT Tipo, COUNT(*) AS Eventos, MIN(Fecha) AS Primero, MAX(Fecha) AS Ultimo FROM dbo.CalidadEventos GROUP BY Tipo ORDER BY Tipo;
GO
