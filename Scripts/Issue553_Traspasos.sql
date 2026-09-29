-- NestoAPI#553 (fase 1): documento Traspaso entre almacenes (reposición de tiendas fuera de Nesto viejo).
--
-- PROPUESTA — NO EJECUTADO todavía y NO mapeado en el EDMX (siguiente corte).
--
-- Cabecera (Traspasos) + líneas (TraspasosLineas) con el ciclo propuesto → preparado → enviado → recibido.
-- NumTraspaso es el número que sale del contador compartido ContadoresGlobales.TraspasoAlmacén
-- (el mismo que acaba en PreExtrProducto.NºTraspaso / ExtractoProducto), así que enlaza el documento
-- con los movimientos de stock que ya genera Nesto viejo. NO es único por sí solo en la tabla: el
-- contador lo comparten kits, notas de entrega, albaranes e inventario, pero dentro de Traspasos
-- sí debe serlo por empresa (UNIQUE normal, sin filtro).
--
-- Estado (tinyint, ver NestoAPI.Models.Traspasos.EstadoTraspaso):
--   0 Propuesto · 1 EnPreparacion · 2 Enviado · 3 Recibido · 9 Anulado
--
-- PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas; hay muchos
-- módulos legacy con QUOTED_IDENTIFIER OFF que fallarían al escribir en tablas con ese tipo de índices.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).

USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.Traspasos') AND type = 'U')
BEGIN
    CREATE TABLE dbo.Traspasos (
        Id int IDENTITY(1,1) NOT NULL,
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,                  -- ContadoresGlobales.TraspasoAlmacén = PreExtrProducto.NºTraspaso
        Origen char(3) NOT NULL,                   -- Almacén de salida (ALG/REI/ALC)
        Destino char(3) NOT NULL,                  -- Almacén de entrada
        Estado tinyint NOT NULL CONSTRAINT DF_Traspasos_Estado DEFAULT (0),
        Usuario varchar(30) NOT NULL,              -- Mismo ancho que PreExtrProducto.Usuario
        FechaCreacion datetime NOT NULL CONSTRAINT DF_Traspasos_FechaCreacion DEFAULT (GETDATE()),
        FechaEnvio datetime NULL,                  -- Salida contabilizada (fase 2)
        FechaRecepcion datetime NULL,              -- Entrada contabilizada (fase 3)
        FechaModificacion datetime NOT NULL CONSTRAINT DF_Traspasos_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_Traspasos PRIMARY KEY (Id),
        CONSTRAINT UQ_Traspasos_Empresa_NumTraspaso UNIQUE (Empresa, NumTraspaso),
        CONSTRAINT CK_Traspasos_OrigenDistintoDestino CHECK (Origen <> Destino),
        CONSTRAINT CK_Traspasos_Estado CHECK (Estado IN (0, 1, 2, 3, 9))
    );

    -- Búsqueda de traspasos abiertos por destino (la guardia «hay una reposición pendiente»).
    CREATE INDEX IX_Traspasos_Destino_Estado ON dbo.Traspasos (Destino, Estado);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID('dbo.TraspasosLineas') AND type = 'U')
BEGIN
    CREATE TABLE dbo.TraspasosLineas (
        Id int IDENTITY(1,1) NOT NULL,
        TraspasoId int NOT NULL,
        Producto char(15) NOT NULL,                -- Productos.Número
        CantidadPropuesta smallint NOT NULL,       -- Lo que calculó prdRellenarReposicionStock (o puso el usuario)
        CantidadPreparada smallint NULL,           -- Leída con el lector en el origen (fase 2)
        CantidadEnviada smallint NULL,             -- Salida contabilizada
        CantidadRecibida smallint NULL,            -- Leída con el lector en el destino (fase 3)
        Motivo varchar(100) NULL,                  -- Motivo de la diferencia (rotura, no encontrado…)
        FechaPreparacion datetime NULL,
        FechaRecepcion datetime NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_TraspasosLineas_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_TraspasosLineas PRIMARY KEY (Id),
        CONSTRAINT FK_TraspasosLineas_Traspasos FOREIGN KEY (TraspasoId) REFERENCES dbo.Traspasos (Id),
        CONSTRAINT UQ_TraspasosLineas_Traspaso_Producto UNIQUE (TraspasoId, Producto),
        CONSTRAINT CK_TraspasosLineas_Cantidades CHECK (
            CantidadPropuesta >= 0
            AND (CantidadPreparada IS NULL OR CantidadPreparada >= 0)
            AND (CantidadEnviada IS NULL OR CantidadEnviada >= 0)
            AND (CantidadRecibida IS NULL OR CantidadRecibida >= 0))
    );

    -- Para vstStockTransito (fase 4) y para «dónde va este producto».
    CREATE INDEX IX_TraspasosLineas_Producto ON dbo.TraspasosLineas (Producto);
END
GO

-- BD de negocio (NestoConnection): el API accede con la cuenta de máquina.
-- Sin DELETE en la cabecera: un traspaso se anula (Estado = 9), no se borra.
GRANT SELECT, INSERT, UPDATE ON dbo.Traspasos TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TraspasosLineas TO [NUEVAVISION\RDS2016$];
GO
