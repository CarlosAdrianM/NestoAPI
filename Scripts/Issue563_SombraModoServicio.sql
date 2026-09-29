-- NestoAPI#563: tabla de la SOMBRA del modo de servicio por CAUSAS.
-- Cada vez que se calcula la sugerencia de siempre (plantilla de Nesto/NestoApp: POST api/PedidosVenta/ModoServicioSugerido)
-- o se crea un pedido/presupuesto (POST api/PedidosVenta), la API calcula EN SEGUNDO PLANO la sugerencia por colores
-- (la de hoy) y la nueva por causas, con los mismos datos, y guarda aquí la comparación. NO cambia nada de lo que
-- ven los usuarios. Al crear, las líneas del propio pedido se excluyen de los pendientes (stock de antes de grabarlo).
--
-- Tabla nueva, sin índices filtrados ni columnas calculadas. Lo ÚNICO que escribe la sombra es ESTA tabla.
-- Mientras no se ejecute este script, la sombra (si está encendida) no lee nada y lo deja en ELMAH (como mucho
-- una vez cada 30 minutos). Apagada, no hace nada exista o no la tabla.
-- Ejecutar en NV (NestoConnection) como sa. Idempotente.
USE NV;
GO

IF OBJECT_ID('dbo.ModoServicioSombra', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModoServicioSombra (
        Id                 int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModoServicioSombra PRIMARY KEY,
        Fecha              datetime       NOT NULL CONSTRAINT DF_ModoServicioSombra_Fecha DEFAULT (GETDATE()),
        Origen             varchar(20)    NOT NULL,   -- 'Plantilla' (sugerencia antes de crear) / 'Crear' (pedido recién grabado)
        Empresa            char(3)        NULL,
        Pedido             int            NULL,       -- NULL en 'Plantilla' (el pedido aún no existe)
        EsPresupuesto      bit            NOT NULL CONSTRAINT DF_ModoServicioSombra_EsPresupuesto DEFAULT (0),
        Cliente            varchar(10)    NULL,
        Contacto           varchar(3)     NULL,
        Usuario            varchar(50)    NULL,
        ModoPedido         tinyint        NULL,       -- el modo que vio el usuario (Plantilla) o con el que se grabó (Crear)
        ModoColores        tinyint        NOT NULL,   -- SugeridorModoServicio (lo de hoy)
        PermitidosColores  varchar(10)    NULL,       -- '1,2,4'
        MotivoColores      nvarchar(400)  NULL,
        ModoCausas         tinyint        NOT NULL,   -- ClasificadorCausasModoServicio (#563)
        PermitidosCausas   varchar(10)    NULL,
        MotivoCausas       nvarchar(400)  NULL,
        MismoModo          bit            NOT NULL,
        MismosPermitidos   bit            NOT NULL,
        Causas             nvarchar(max)  NULL        -- una línea por producto@almacén: reparto de causas y [color]
    );
END
GO

-- La API corre en RDS2016 con la cuenta de máquina (misma cadena NestoConnection que la sombra de precios medios).
GRANT SELECT, INSERT ON dbo.ModoServicioSombra TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.ModoServicioSombra') AS Tabla;
SELECT Empresa, Usuario, Clave, Valor FROM dbo.ParámetrosUsuario WHERE Clave = 'ModoServicioSombra';
GO

-- ENCENDER (sin fila = APAGADO, que es como nace al publicar). Se relee cada minuto, sin publicar.
--   '1' (o 'Todo'): registra todas las comparaciones (recomendado la primera semana, para ver el % de coincidencia).
--   'Diferencias': solo las que no coinciden.
-- IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ModoServicioSombra')
--     INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', '(defecto)', 'ModoServicioSombra', '1', 'NestoAPI#563', GETDATE());
-- ELSE
--     UPDATE dbo.ParámetrosUsuario SET Valor = '1', Usuario2 = 'NestoAPI#563', [Fecha Modificación] = GETDATE()
--     WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ModoServicioSombra';

-- APAGAR:
-- UPDATE dbo.ParámetrosUsuario SET Valor = '0', Usuario2 = 'NestoAPI#563', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ModoServicioSombra';

-- CONSULTAS ÚTILES
-- Coincidencia por día y origen:
--   SELECT CAST(Fecha AS date) AS Dia, Origen, COUNT(*) AS Total,
--          SUM(CASE WHEN MismoModo = 1 AND MismosPermitidos = 1 THEN 1 ELSE 0 END) AS Coinciden
--   FROM dbo.ModoServicioSombra GROUP BY CAST(Fecha AS date), Origen ORDER BY Dia DESC, Origen;
-- Qué cambia (de qué modo a qué modo):
--   SELECT ModoColores, ModoCausas, PermitidosColores, PermitidosCausas, COUNT(*) AS Veces
--   FROM dbo.ModoServicioSombra WHERE MismoModo = 0 OR MismosPermitidos = 0
--   GROUP BY ModoColores, ModoCausas, PermitidosColores, PermitidosCausas ORDER BY Veces DESC;
-- Las diferencias de los pedidos creados, con sus causas:
--   SELECT TOP 100 Fecha, Pedido, EsPresupuesto, Cliente, Usuario, ModoPedido, ModoColores, ModoCausas,
--          PermitidosColores, PermitidosCausas, MotivoCausas, Causas
--   FROM dbo.ModoServicioSombra WHERE Origen = 'Crear' AND (MismoModo = 0 OR MismosPermitidos = 0) ORDER BY Id DESC;
-- Limpiar al retirar la sombra:
--   DROP TABLE dbo.ModoServicioSombra;
