/*
    «Ofertas para ti» (25/11/26), EN SOMBRA: lo que se le enseña a cada cliente de la app (la franja de 5, cada página de «Ver todas» y
    lo que propone la push de campaña) y lo que hace con ello (lo ve, lo abre, lo añade al carrito, lo compra en los 7 días siguientes),
    para reentrenar el modelo de propensión más adelante. Solo se recogen datos: no se entrena nada.

    Una fila por oferta SERVIDA y por cliente, día y origen (índice único UX_OfertasClienteMostradas_Dia: no se repite). La inserta la API
    en segundo plano al servir (RegistroOfertasMostradas); los eventos de la app (POST api/Pedidos/Cliente/Ofertas/Eventos) rellenan VistaEl,
    AbiertaEl, AnadidaEl y CantidadAnadida (o crean la fila sin Posicion si no se registró al servir); el job «ofertas-cliente-compras»
    (3:35 Madrid) pone CompradaEl y PedidoCompra. GET api/Campanas/Ofertas/Resultado lo resume (Dirección/Informática).

    Fuera del EDMX. SIN la tabla la API funciona igual y no registra nada (ELMAH una vez). Lanzar como sa en NV, antes o después de
    publicar la API. Idempotente: se puede lanzar dos veces. Sin índices filtrados (rompen los SET de las conexiones viejas de Nesto).
    GRANT: SELECT, INSERT y UPDATE (las filas no se borran).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.OfertasClienteMostradas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OfertasClienteMostradas (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OfertasClienteMostradas PRIMARY KEY CLUSTERED,
        Fecha date NOT NULL,                              -- el día en que se sirvió
        FechaHora datetime NOT NULL CONSTRAINT DF_OfertasClienteMostradas_FechaHora DEFAULT (GETDATE()),
        Empresa varchar(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NULL,
        Campana nvarchar(50) NULL,
        Producto varchar(15) NOT NULL,
        Origen varchar(10) NOT NULL CONSTRAINT CK_OfertasClienteMostradas_Origen CHECK (Origen IN ('Cinco', 'Todas', 'Push')),
        Posicion int NULL,                                -- lugar en lo servido (1 = la primera); NULL si la creó un evento
        PosicionRanking int NULL,                         -- lugar en el ranking completo del cliente
        Probabilidad real NULL,                           -- P1 del modelo 1 (NULL: ordenada por la regla)
        Motivo varchar(30) NULL,                          -- TeToca, LoSuelesPedir, CompradoJunto, Parecido, DeTusMarcas, TePuedeInteresar
        VersionModelo varchar(50) NULL,
        Descuento decimal(9,4) NULL,                      -- 1 - Precio/PrecioAntes (lo que de verdad le mejora)
        Precio decimal(12,4) NULL,                        -- precio unitario con la campaña
        VistaEl datetime2(0) NULL,
        AbiertaEl datetime2(0) NULL,
        AnadidaEl datetime2(0) NULL,
        CantidadAnadida int NULL,
        CompradaEl datetime2(0) NULL,                     -- fecha del primer pedido del producto en los 7 días siguientes
        PedidoCompra int NULL
    );
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_OfertasClienteMostradas_Dia' AND object_id = OBJECT_ID(N'dbo.OfertasClienteMostradas'))
    CREATE UNIQUE NONCLUSTERED INDEX UX_OfertasClienteMostradas_Dia ON dbo.OfertasClienteMostradas (Fecha, Empresa, Cliente, Producto, Origen);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OfertasClienteMostradas_Campana' AND object_id = OBJECT_ID(N'dbo.OfertasClienteMostradas'))
    CREATE NONCLUSTERED INDEX IX_OfertasClienteMostradas_Campana ON dbo.OfertasClienteMostradas (Campana, Fecha);
GO
GRANT SELECT, INSERT, UPDATE ON dbo.OfertasClienteMostradas TO [NUEVAVISION\RDS2016$];
GO
