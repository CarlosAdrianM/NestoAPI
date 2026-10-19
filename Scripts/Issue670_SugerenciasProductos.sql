/*
    NestoAPI#670 (corte 1 de la épica #669): sugerencias de productos en la plantilla de venta, por reglas.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. Ninguna tabla está en el EDMX (la API las lee y escribe con SQL a mano), así que se puede lanzar antes o después
    del deploy: sin ellas, POST api/SugerenciasProductos/Calcular devuelve la lista vacía, la respuesta da 404, el job
    nocturno no hace nada y el guardado del pedido no se entera.

    1) SugerenciasProductosSesiones: una fila por plantilla abierta (Id = el Guid que manda Nesto o NestoApp). Pedido se
       rellena al guardar el pedido con PedidoVentaDTO.IdSesionSugerencias. Carrito guarda el último carrito que se calculó
       («producto:cantidad;...»), para poder buscar la sustituta cuando el vendedor pulsa «No le interesa».
    2) SugerenciasProductosMostradas: una fila por sugerencia enseñada. Respuesta NULL = no se ha tocado (no cuenta como
       ofrecida); «Anadido» = ofrecida y aceptada; «NoLeInteresa» = ofrecida y rechazada (no se le vuelve a sugerir a ese
       cliente en 60 días). EntroEnPedido/CantidadEnPedido se rellenan al guardar el pedido (sin respuesta y dentro del
       pedido = señal débil). Texto es la frase que vio el vendedor.
    3) SugerenciasProductosPares: «comprados juntos», lo que calcula cada madrugada el job «sugerencias-productos-pares»
       (24 meses de albaranes, cesta = cliente y día). Se vacía y se rellena entera en una transacción.
    4) SugerenciasProductosPerfilesProductos: para «A potenciar», cuántos clientes de cada perfil (grupo y subgrupo que
       más compran) se han llevado cada producto en los últimos 12 meses. La rellena el mismo job.

    Interruptor del job: ParámetrosUsuario «SugerenciasProductosJob» bajo «(defecto)»: Activo (lo calcula) o cualquier otra
    cosa (no hace nada). Nace Activo: el job es de madrugada (05:00, de lunes a sábado; el domingo corre el SP de precios
    medios), lee LinPedidoVta con NOLOCK por el mismo índice que el recordatorio de reposición (#532, ~3 s) y solo escribe
    en sus tablas.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT, UPDATE en sesiones y mostradas (nunca se borran); SELECT, INSERT, DELETE en las del job.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.SugerenciasProductosSesiones') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosSesiones (
        Id uniqueidentifier NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente varchar(10) NOT NULL,
        Contacto varchar(3) NULL,
        Vendedor varchar(10) NULL,                      -- el que manda la plantilla (decide el grupo de los productos)
        Usuario varchar(50) NULL,                       -- quien llama a la API
        Aplicacion varchar(10) NOT NULL,                -- Nesto | NestoApp
        FechaInicio datetime NOT NULL CONSTRAINT DF_SugerenciasProductosSesiones_FechaInicio DEFAULT (GETDATE()),
        FechaUltimoCalculo datetime NULL,
        Carrito varchar(max) NULL,                      -- último carrito calculado: «producto:cantidad;...»
        Pedido int NULL,                                -- NULL hasta guardar el pedido
        FechaPedido datetime NULL,

        CONSTRAINT PK_SugerenciasProductosSesiones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_SugerenciasProductosSesiones_Aplicacion CHECK (Aplicacion IN ('Nesto', 'NestoApp'))
    );
END
GO

-- Los rechazos de un cliente (60 días) y el informe por cliente
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SugerenciasProductosSesiones_Cliente' AND object_id = OBJECT_ID('dbo.SugerenciasProductosSesiones'))
    CREATE INDEX IX_SugerenciasProductosSesiones_Cliente ON dbo.SugerenciasProductosSesiones (Empresa, Cliente);
GO

IF OBJECT_ID('dbo.SugerenciasProductosMostradas') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosMostradas (
        Id int IDENTITY(1, 1) NOT NULL,
        SesionId uniqueidentifier NOT NULL,
        Producto varchar(15) NOT NULL,
        Motivo varchar(20) NOT NULL,                    -- CompradoJunto | LeToca | Potenciar
        Puntuacion decimal(12, 4) NOT NULL,
        Posicion tinyint NOT NULL,                      -- 1..4, el hueco en el que se enseñó
        VersionMotor varchar(20) NOT NULL,
        Texto nvarchar(200) NULL,
        FechaMostrada datetime NOT NULL CONSTRAINT DF_SugerenciasProductosMostradas_FechaMostrada DEFAULT (GETDATE()),
        Respuesta varchar(20) NULL,                     -- NULL (no tocada) | Anadido | NoLeInteresa
        FechaRespuesta datetime NULL,
        EntroEnPedido bit NULL,                         -- NULL hasta guardar el pedido
        CantidadEnPedido int NULL,

        CONSTRAINT PK_SugerenciasProductosMostradas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_SugerenciasProductosMostradas_Sesion FOREIGN KEY (SesionId) REFERENCES dbo.SugerenciasProductosSesiones (Id),
        CONSTRAINT CK_SugerenciasProductosMostradas_Motivo CHECK (Motivo IN ('CompradoJunto', 'LeToca', 'Potenciar')),
        CONSTRAINT CK_SugerenciasProductosMostradas_Respuesta CHECK (Respuesta IS NULL OR Respuesta IN ('Anadido', 'NoLeInteresa')),
        CONSTRAINT CK_SugerenciasProductosMostradas_Posicion CHECK (Posicion BETWEEN 1 AND 4)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SugerenciasProductosMostradas_Sesion' AND object_id = OBJECT_ID('dbo.SugerenciasProductosMostradas'))
    CREATE INDEX IX_SugerenciasProductosMostradas_Sesion ON dbo.SugerenciasProductosMostradas (SesionId);
GO

IF OBJECT_ID('dbo.SugerenciasProductosPares') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosPares (
        ProductoA varchar(15) NOT NULL,                 -- el que está en el carrito
        ProductoB varchar(15) NOT NULL,                 -- el que se sugiere
        VecesJuntos int NOT NULL,                       -- cestas (cliente y día) con los dos
        Soporte decimal(9, 6) NOT NULL,                 -- VecesJuntos / cestas
        Confianza decimal(9, 6) NOT NULL,               -- VecesJuntos / cestas con A
        Lift decimal(12, 4) NOT NULL,                   -- Confianza / (cestas con B / cestas)
        FechaCalculo datetime NOT NULL,

        CONSTRAINT PK_SugerenciasProductosPares PRIMARY KEY CLUSTERED (ProductoA, ProductoB)
    );
END
GO

IF OBJECT_ID('dbo.SugerenciasProductosPerfilesProductos') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosPerfilesProductos (
        Grupo varchar(3) NOT NULL,                      -- perfil: grupo y subgrupo en el que más compra el cliente
        SubGrupo varchar(3) NOT NULL,
        Producto varchar(15) NOT NULL,
        Clientes int NOT NULL,                          -- clientes de ese perfil que lo han comprado en 12 meses
        FechaCalculo datetime NOT NULL,

        CONSTRAINT PK_SugerenciasProductosPerfilesProductos PRIMARY KEY CLUSTERED (Grupo, SubGrupo, Producto)
    );
END
GO

-- Interruptor del job nocturno (nace Activo; con cualquier otro valor, el job no hace nada)
IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'SugerenciasProductosJob')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'SugerenciasProductosJob', 'Activo', 'NestoAPI#670', GETDATE());
GO

GRANT SELECT, INSERT, UPDATE ON dbo.SugerenciasProductosSesiones TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.SugerenciasProductosMostradas TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, DELETE ON dbo.SugerenciasProductosPares TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, DELETE ON dbo.SugerenciasProductosPerfilesProductos TO [NUEVAVISION\RDS2016$];
GO

-- Para apagar el job:
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Apagado', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'SugerenciasProductosJob';
--
-- Tasa ofrecido → aceptado por motivo (lo que no se ha tocado no cuenta):
-- SELECT m.Motivo, COUNT(*) AS Ofrecidas, SUM(CASE WHEN m.Respuesta = 'Anadido' THEN 1 ELSE 0 END) AS Aceptadas,
--        SUM(CASE WHEN m.EntroEnPedido = 1 THEN 1 ELSE 0 END) AS EnElPedido
-- FROM dbo.SugerenciasProductosMostradas m WHERE m.Respuesta IS NOT NULL GROUP BY m.Motivo;
