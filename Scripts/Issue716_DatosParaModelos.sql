/*
    NestoAPI#716 (puntos 2.1, 2.2 y 2.3) + NestoAPI#623 corte 1: datos que hay que empezar a guardar YA para los modelos
    ML.NET de la épica #689 (lo que una persona vio y decidió no se puede reconstruir del histórico).

    Ejecutar en SSMS contra NV como sa (ALTER, CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede
    lanzar dos veces. Se puede lanzar antes o después del deploy: la API comprueba con COL_LENGTH (una vez por proceso) si
    las columnas existen y, si no, no escribe nada y todo sigue como hasta ahora. Tras lanzarlo con la API arrancada no
    hace falta reciclar: la primera escritura que lo encuentre ya lo usa.

    1) 2.1 Rapports: SeguimientoCliente.Resultado (catálogo: Pedido, PediraMasTarde, NoNecesita, Precio, Competencia,
       CerradoTraspaso, SinStock, NoDecide, SoloInformacion, Otro) y ProximoContacto. Los rellenan POST/PUT
       api/SeguimientosClientes cuando el cliente los manda (chips de Nesto y NestoApp, issues gemelas).
       Tabla SeguimientoClienteClasificacion: lo que deduce OpenAI de los Comentarios cada noche (job
       «clasificar-rapports-nocturno», 23:15) sin pisar lo que marque el vendedor; con Rapports:ClasificacionBackfill
       rellena 2024-2026 por tandas.
    2) 2.2 Motivo del cambio de estado: SeguimientoCambiosEstadoCliente.Motivo y Comentario. La fila la sigue creando el
       trigger trgClientesUpd; la API le pone el porqué justo después (UPDATE de la última fila sin motivo del cliente):
       hace falta GRANT UPDATE.
    3) 2.3 Alta del cliente: Clientes.FechaAlta, UsuarioAlta y AplicacionAlta (Nesto, NestoApp, TiendaOnline,
       Backfill…). Las rellena GestorClientes.CrearCliente. Relleno APROXIMADO del histórico al final (punto 5).
    4) #623 corte 1 (mismo script, un solo cambio de esquema en Clientes): catálogo OrigenesCliente y Clientes.Origen,
       FechaOrigen y UsuarioOrigen, todo opcional. Primer valor del catálogo: META-HARMONIA-0926. El código de la API
       llegará con #623.
    5) Backfill de FechaAlta: la fecha del primer pedido (CabPedidoVta) o del primer rapport (SeguimientoCliente), la
       que sea anterior, con AplicacionAlta = 'Backfill' para que nadie lo confunda con un alta real. Solo donde
       FechaAlta IS NULL. Es un UPDATE sobre ~64.000 filas que dispara trgClientesUpd (no toca NIF, CCC ni Estado, así
       que el trigger no hace nada): lanzarlo FUERA DE HORARIO.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------------------------------
-- 1) 2.1 Rapports: resultado y próximo contacto
------------------------------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.SeguimientoCliente', 'Resultado') IS NULL
BEGIN
    ALTER TABLE dbo.SeguimientoCliente ADD Resultado varchar(20) NULL;  -- código del catálogo ResultadoRapport (C#)
    PRINT 'SeguimientoCliente.Resultado creada';
END
GO
IF COL_LENGTH('dbo.SeguimientoCliente', 'ProximoContacto') IS NULL
BEGIN
    ALTER TABLE dbo.SeguimientoCliente ADD ProximoContacto date NULL;
    PRINT 'SeguimientoCliente.ProximoContacto creada';
END
GO
IF OBJECT_ID('dbo.CK_SeguimientoCliente_Resultado') IS NULL
BEGIN
    ALTER TABLE dbo.SeguimientoCliente WITH NOCHECK ADD CONSTRAINT CK_SeguimientoCliente_Resultado
        CHECK (Resultado IS NULL OR Resultado IN ('Pedido', 'PediraMasTarde', 'NoNecesita', 'Precio', 'Competencia',
                                                  'CerradoTraspaso', 'SinStock', 'NoDecide', 'SoloInformacion', 'Otro'));
END
GO

IF OBJECT_ID('dbo.SeguimientoClienteClasificacion') IS NULL
BEGIN
    CREATE TABLE dbo.SeguimientoClienteClasificacion (
        NºOrden int NOT NULL,                              -- SeguimientoCliente.NºOrden
        Resultado varchar(20) NOT NULL,                    -- mismo catálogo que SeguimientoCliente.Resultado
        ProximoContacto date NULL,                         -- si el comentario dice cuándo volver a contactar
        Confianza decimal(4, 3) NULL,                      -- 0..1 según el modelo
        Modelo varchar(40) NOT NULL,                       -- p. ej. gpt-4o-mini
        Fecha datetime NOT NULL CONSTRAINT DF_SeguimientoClienteClasificacion_Fecha DEFAULT (GETDATE()),

        CONSTRAINT PK_SeguimientoClienteClasificacion PRIMARY KEY CLUSTERED (NºOrden),
        CONSTRAINT CK_SeguimientoClienteClasificacion_Resultado
            CHECK (Resultado IN ('Pedido', 'PediraMasTarde', 'NoNecesita', 'Precio', 'Competencia',
                                 'CerradoTraspaso', 'SinStock', 'NoDecide', 'SoloInformacion', 'Otro'))
    );
    PRINT 'SeguimientoClienteClasificacion creada';
END
GO
GRANT SELECT, UPDATE ON dbo.SeguimientoCliente TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SeguimientoClienteClasificacion TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------------------------------
-- 2) 2.2 Motivo del cambio de estado del cliente
------------------------------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.SeguimientoCambiosEstadoCliente', 'Motivo') IS NULL
BEGIN
    ALTER TABLE dbo.SeguimientoCambiosEstadoCliente ADD Motivo varchar(30) NULL;  -- catálogo MotivoCambioEstadoCliente (C#)
    PRINT 'SeguimientoCambiosEstadoCliente.Motivo creada';
END
GO
IF COL_LENGTH('dbo.SeguimientoCambiosEstadoCliente', 'Comentario') IS NULL
BEGIN
    ALTER TABLE dbo.SeguimientoCambiosEstadoCliente ADD Comentario nvarchar(200) NULL;
    PRINT 'SeguimientoCambiosEstadoCliente.Comentario creada';
END
GO
-- El trigger inserta; la API solo actualiza la fila recién creada (Id, Empresa, Cliente, Contacto, EstadoNuevo, Fecha).
GRANT SELECT, UPDATE ON dbo.SeguimientoCambiosEstadoCliente TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------------------------------
-- 3) 2.3 Alta del cliente  +  4) #623 corte 1: origen del cliente (un solo cambio de esquema en Clientes)
------------------------------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Clientes', 'FechaAlta') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD FechaAlta datetime NULL;
    PRINT 'Clientes.FechaAlta creada';
END
GO
IF COL_LENGTH('dbo.Clientes', 'UsuarioAlta') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD UsuarioAlta nvarchar(100) NULL;
    PRINT 'Clientes.UsuarioAlta creada';
END
GO
IF COL_LENGTH('dbo.Clientes', 'AplicacionAlta') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD AplicacionAlta varchar(20) NULL;  -- Nesto, NestoApp, TiendaOnline, ImportacionMeta, Backfill…
    PRINT 'Clientes.AplicacionAlta creada';
END
GO

IF OBJECT_ID('dbo.OrigenesCliente') IS NULL
BEGIN
    CREATE TABLE dbo.OrigenesCliente (
        Codigo varchar(30) NOT NULL,
        Descripcion nvarchar(100) NOT NULL,
        Campana nvarchar(100) NULL,
        FechaDesde date NULL,
        FechaHasta date NULL,
        Activo bit NOT NULL CONSTRAINT DF_OrigenesCliente_Activo DEFAULT (1),
        EtiquetaMailchimp nvarchar(100) NULL,
        Usuario nvarchar(100) NOT NULL CONSTRAINT DF_OrigenesCliente_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_OrigenesCliente_FechaModificacion DEFAULT (GETDATE()),

        CONSTRAINT PK_OrigenesCliente PRIMARY KEY CLUSTERED (Codigo)
    );
    PRINT 'OrigenesCliente creada';
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.OrigenesCliente WHERE Codigo = 'META-HARMONIA-0926')
BEGIN
    INSERT INTO dbo.OrigenesCliente (Codigo, Descripcion, Campana, FechaDesde, Activo, Usuario)
    VALUES ('META-HARMONIA-0926', N'Lead del formulario de Meta Ads (Harmonia)', N'Harmonia 09/26', '20260901', 1, N'Issue716');
END
GO
IF COL_LENGTH('dbo.Clientes', 'Origen') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD Origen varchar(30) NULL;
    PRINT 'Clientes.Origen creada';
END
GO
IF COL_LENGTH('dbo.Clientes', 'FechaOrigen') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD FechaOrigen datetime NULL;
    PRINT 'Clientes.FechaOrigen creada';
END
GO
IF COL_LENGTH('dbo.Clientes', 'UsuarioOrigen') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes ADD UsuarioOrigen nvarchar(100) NULL;
    PRINT 'Clientes.UsuarioOrigen creada';
END
GO
IF OBJECT_ID('dbo.FK_Clientes_OrigenesCliente') IS NULL
BEGIN
    ALTER TABLE dbo.Clientes WITH NOCHECK ADD CONSTRAINT FK_Clientes_OrigenesCliente
        FOREIGN KEY (Origen) REFERENCES dbo.OrigenesCliente (Codigo);
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.OrigenesCliente TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.Clientes TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------------------------------
-- 5) BACKFILL APROXIMADO de Clientes.FechaAlta  ***  LANZAR FUERA DE HORARIO  ***
--    La fecha real del alta no existe en ninguna tabla. Se toma la más antigua entre el primer pedido y el primer rapport
--    del cliente/contacto; AplicacionAlta = 'Backfill' deja claro que es una aproximación (los modelos pueden tratarla
--    distinto). Solo las fichas sin FechaAlta: lanzarlo dos veces no cambia nada. Las fichas sin pedidos ni rapports se
--    quedan a NULL (no hay de dónde sacarlo).
------------------------------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Clientes', 'FechaAlta') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.Clientes WHERE AplicacionAlta = 'Backfill')   -- ya se hizo: no repetir el UPDATE masivo
BEGIN
    ;WITH Primeras AS (
        SELECT c.Empresa, c.[Nº Cliente] AS Cliente, c.Contacto,
               (SELECT MIN(p.Fecha) FROM dbo.CabPedidoVta p WITH (NOLOCK)
                 WHERE p.Empresa = c.Empresa AND p.[Nº Cliente] = c.[Nº Cliente] AND p.Contacto = c.Contacto) AS PrimerPedido,
               (SELECT MIN(s.Fecha) FROM dbo.SeguimientoCliente s WITH (NOLOCK)
                 WHERE s.Empresa = c.Empresa AND s.Número = c.[Nº Cliente] AND s.Contacto = c.Contacto) AS PrimerRapport
        FROM dbo.Clientes c
        WHERE c.FechaAlta IS NULL
    )
    UPDATE c
       SET FechaAlta = CASE WHEN p.PrimerPedido IS NULL THEN p.PrimerRapport
                            WHEN p.PrimerRapport IS NULL THEN p.PrimerPedido
                            WHEN p.PrimerRapport < p.PrimerPedido THEN p.PrimerRapport
                            ELSE p.PrimerPedido END,
           UsuarioAlta = N'Backfill Issue716',
           AplicacionAlta = 'Backfill'
      FROM dbo.Clientes c
      INNER JOIN Primeras p ON p.Empresa = c.Empresa AND p.Cliente = c.[Nº Cliente] AND p.Contacto = c.Contacto
     WHERE c.FechaAlta IS NULL
       AND (p.PrimerPedido IS NOT NULL OR p.PrimerRapport IS NOT NULL);
    PRINT CONCAT('Backfill de FechaAlta: ', @@ROWCOUNT, ' fichas');
END
GO

-- Comprobaciones:
-- SELECT COUNT(*) Total, SUM(CASE WHEN FechaAlta IS NULL THEN 1 ELSE 0 END) SinFecha, SUM(CASE WHEN AplicacionAlta = 'Backfill' THEN 1 ELSE 0 END) Backfill
-- FROM dbo.Clientes;
-- SELECT AplicacionAlta, COUNT(*) FROM dbo.Clientes WHERE FechaAlta >= DATEADD(day, -30, GETDATE()) GROUP BY AplicacionAlta;
-- SELECT Resultado, COUNT(*) FROM dbo.SeguimientoCliente WHERE Resultado IS NOT NULL GROUP BY Resultado;
-- SELECT Resultado, COUNT(*), AVG(Confianza) FROM dbo.SeguimientoClienteClasificacion GROUP BY Resultado ORDER BY 2 DESC;
-- SELECT TOP 50 * FROM dbo.SeguimientoCambiosEstadoCliente WHERE Motivo IS NOT NULL ORDER BY Id DESC;
