/*
    NestoAPI#593 (c2): cheque regalo por campañas. Tablas, producto de la campaña de octubre y la campaña (INACTIVA).

    Ejecutar en SSMS contra NV como sa (crea tablas: el login nuevavision no tiene ALTER). Idempotente: se puede
    lanzar dos veces. Se puede lanzar ANTES o DESPUÉS de publicar la API: sin las tablas, la API no genera nada.

    Qué NO hace: no activa nada. Para lanzar la campaña hacen falta las dos cosas (el día que se decida):
      1) UPDATE dbo.ChequesRegaloCampanas SET Activa = 1 WHERE Codigo = 'CHEQUE50_OCT_2026';
      2) En el Web.config del REPO: ChequesRegalo:Generar = "true", y publicar la API.

    Lo pendiente de decidir en el tablero compartido son columnas de la campaña (se cambian con un UPDATE, sin
    programar): ImporteBase, CanjeHasta, MinimoFacturaQueGenera, DiasEsperaTrasEntrega. Valores provisionales:
    50 €, 31/10/2026, sin mínimo y sin espera.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Campañas
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.ChequesRegaloCampanas') IS NULL
BEGIN
    CREATE TABLE dbo.ChequesRegaloCampanas (
        Codigo varchar(20) NOT NULL,                    -- CHEQUE50_OCT_2026
        Empresa char(3) NOT NULL,                       -- donde vive el cliente (la 1)
        Producto char(15) NOT NULL,                     -- producto ficticio del cheque: la línea negativa del canje
        Descripcion nvarchar(200) NULL,
        ImporteBase decimal(10, 2) NOT NULL,            -- base imponible del cheque (+ el IVA que corresponda)
        GeneraDesde date NOT NULL,                      -- primer día (incluido) en que una factura genera
        GeneraHasta date NOT NULL,                      -- último día (incluido)
        CanjeHasta date NOT NULL,                       -- último día (incluido) para usarlo
        MinimoFacturaQueGenera decimal(10, 2) NOT NULL CONSTRAINT DF_ChequesRegaloCampanas_MinimoFactura DEFAULT (0),
        MinimoCanje decimal(10, 2) NOT NULL,            -- el pedido del canje tiene que SUPERARLO (estrictamente mayor)
        DiasEsperaTrasEntrega int NOT NULL CONSTRAINT DF_ChequesRegaloCampanas_DiasEspera DEFAULT (0),
        Activa bit NOT NULL CONSTRAINT DF_ChequesRegaloCampanas_Activa DEFAULT (0),
        Usuario varchar(50) NOT NULL CONSTRAINT DF_ChequesRegaloCampanas_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ChequesRegaloCampanas_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_ChequesRegaloCampanas PRIMARY KEY (Codigo),
        CONSTRAINT CK_ChequesRegaloCampanas_Fechas CHECK (GeneraDesde <= GeneraHasta AND GeneraHasta <= CanjeHasta),
        CONSTRAINT CK_ChequesRegaloCampanas_Importes CHECK (ImporteBase > 0 AND MinimoCanje >= 0 AND MinimoFacturaQueGenera >= 0 AND DiasEsperaTrasEntrega >= 0)
    );
END
GO

------------------------------------------------------------------------------------------------
-- 2. Cheques: uno por campaña y código de cliente (la UNIQUE es el candado)
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.ChequesRegalo') IS NULL
BEGIN
    CREATE TABLE dbo.ChequesRegalo (
        Id int IDENTITY(1, 1) NOT NULL,
        Campana varchar(20) NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente varchar(10) NOT NULL,                   -- código de cliente, sin contacto
        EmpresaFactura char(3) NULL,                    -- la de la factura que lo generó (la 3 para la serie GB)
        FacturaOrigen char(10) NULL,
        FechaFactura datetime NULL,
        FechaGeneracion datetime NOT NULL,
        FechaActivacion datetime NULL,                  -- NULL hasta que se cumple la espera tras la entrega completa
        Estado varchar(10) NOT NULL CONSTRAINT DF_ChequesRegalo_Estado DEFAULT ('Generado'),
        EmpresaCanje char(3) NULL,
        PedidoCanje int NULL,
        FechaCanje datetime NULL,
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ChequesRegalo_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_ChequesRegalo PRIMARY KEY (Id),
        CONSTRAINT UQ_ChequesRegalo_CampanaCliente UNIQUE (Campana, Empresa, Cliente),
        CONSTRAINT FK_ChequesRegalo_Campana FOREIGN KEY (Campana) REFERENCES dbo.ChequesRegaloCampanas (Codigo),
        CONSTRAINT CK_ChequesRegalo_Estado CHECK (Estado IN ('Generado', 'Canjeado', 'Anulado', 'Caducado'))
    );

    CREATE INDEX IX_ChequesRegalo_Pedido ON dbo.ChequesRegalo (EmpresaCanje, PedidoCanje);
END
GO

-- BD de negocio: la API corre con la cuenta de máquina de RDS2016
GRANT SELECT ON dbo.ChequesRegaloCampanas TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE ON dbo.ChequesRegalo TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 3. Producto de la campaña: CHEQUE50_OCT26, copia de TiCKET (familia Bonificac, grupo COS, ficticio).
--    En la empresa 1 y en la 3 (la serie GB se factura en la espejo). Ficticio = 1 en las dos: no va a la
--    tienda online (PuertaPublicacionTienda) ni a la plantilla, y no cuenta como producto vendido.
------------------------------------------------------------------------------------------------
INSERT INTO dbo.Productos (Empresa, [Número], Nombre, Grupo, PVP, [IVA Soportado], [IVA Repercutido], Comentarios, Estado,
       CodBarras, [Aplicar Dto], SubGrupo, [Tamaño], UnidadMedida, Familia, Foto, PrecioMedio, Ficticio, FechaInicio, FechaFinal,
       NumeroSesiones, MateriaPrima, UnidadesPorEtiqueta, VariasOpciones, Ubicar, ProductoAnterior, GestionStockManual,
       RoturaStockProveedor, ProductosPorCaja, CantidadMinimaPedido, Revisado, ComentariosFactura, NecesitaNumSerie, ExclusivoProfesional)
SELECT e.Empresa, 'CHEQUE50_OCT26', 'CHEQUE REGALO 50 € (OCT 2026)', t.Grupo, 50, t.[IVA Soportado], t.[IVA Repercutido],
       N'NestoAPI#593: cheque regalo de la campaña CHEQUE50_OCT_2026 (línea negativa del canje).', t.Estado,
       NULL, t.[Aplicar Dto], t.SubGrupo, t.[Tamaño], t.UnidadMedida, t.Familia, NULL, 0, 1, NULL, NULL,
       t.NumeroSesiones, t.MateriaPrima, t.UnidadesPorEtiqueta, t.VariasOpciones, t.Ubicar, NULL, t.GestionStockManual,
       t.RoturaStockProveedor, t.ProductosPorCaja, t.CantidadMinimaPedido, t.Revisado, NULL, t.NecesitaNumSerie, t.ExclusivoProfesional
FROM dbo.Productos t
     CROSS JOIN (SELECT '1' AS Empresa UNION ALL SELECT '3') e
WHERE t.Empresa = '1' AND t.[Número] = 'TiCKET'
      AND NOT EXISTS (SELECT 1 FROM dbo.Productos x WHERE x.Empresa = e.Empresa AND x.[Número] = 'CHEQUE50_OCT26');

SELECT @@ROWCOUNT AS ProductosCreadosAhora;
GO

------------------------------------------------------------------------------------------------
-- 4. La campaña de octubre de 2026, INACTIVA (Activa = 0) hasta el lanzamiento.
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.ChequesRegaloCampanas WHERE Codigo = 'CHEQUE50_OCT_2026')
    INSERT INTO dbo.ChequesRegaloCampanas (Codigo, Empresa, Producto, Descripcion, ImporteBase, GeneraDesde, GeneraHasta,
           CanjeHasta, MinimoFacturaQueGenera, MinimoCanje, DiasEsperaTrasEntrega, Activa)
    VALUES ('CHEQUE50_OCT_2026', '1', 'CHEQUE50_OCT26', N'Cheque regalo de octubre 2026: primera factura del 12 al 29/10',
            50, '20261012', '20261029', '20261031', 0, 250, 0, 0);
GO

-- Comprobación
SELECT Codigo, RTRIM(Producto) Producto, ImporteBase, GeneraDesde, GeneraHasta, CanjeHasta, MinimoFacturaQueGenera, MinimoCanje,
       DiasEsperaTrasEntrega, Activa
FROM dbo.ChequesRegaloCampanas;
SELECT RTRIM(Empresa) Empresa, RTRIM([Número]) Producto, Nombre, PVP, Ficticio, Familia, Grupo
FROM dbo.Productos WHERE [Número] = 'CHEQUE50_OCT26';
GO
