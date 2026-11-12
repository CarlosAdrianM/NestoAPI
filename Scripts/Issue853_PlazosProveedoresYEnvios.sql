/*
    NestoAPI#853 (corte 2, 12/11/26): plazo real por proveedor, la fecha de recepción al ENVIAR un pedido de compra y el registro de
    cada envío para medir quién acierta.

    Crea:
      - dbo.ProveedoresPlazosEntrega (fuera del EDMX): una fila por empresa y proveedor con Recepciones (pedido + día de albarán de
        líneas de producto, TipoLínea '1', de los últimos 24 meses, la empresa 1 junto con la espejo 3), DiasMediana y DiasP80 (días
        naturales entre CabPedidoCmp.[Fecha Modificación] y LinPedidoCmp.FechaAlbarán; se descartan los negativos),
        IntervaloPedidosMediano (días entre pedidos de producto al proveedor del último año; NULL con menos de 3), UltimoPedido y
        FechaCalculo. La fila Proveedor = '*' es el plazo de todos juntos (para los proveedores sin datos). La rellena el job de Hangfire
        'proveedores-plazos-entrega' (todos los días a las 3:40) o, a mano, POST api/Compras/PlazosProveedores/Recalcular (Dirección o
        Informática). Se ve con GET api/Compras/PlazosProveedores (Compras, Dirección, Informática).
      - dbo.PedidosCompraEnvios: una fila por pedido de compra ENVIADO (el primer envío; reenviar no duplica): la FechaRecepcion que se
        puso (FechaRecepcionPuesta), la que daría la regla del plazo real (FechaRegla = hoy + P80, aunque no se haya usado), DiasEnServir,
        Origen de la fecha puesta y FechaModelo: la del modelo ML.NET en sombra (NULL sin modelo; ver más abajo).
        Se rellena con el interruptor encendido o apagado.
      - dbo.vPedidosCompraEnviosError: cada envío con su primer y último albarán de líneas de producto y el error en días (real −
        prevista) de FechaRecepcionPuesta, FechaRegla y FechaModelo.
      - En ParametrosUsuario (empresa '1', usuario '(defecto)') el interruptor 'Compras.FechaRecepcionPorPlazoReal' a '0' (APAGADO) si no
        existe. Con '0' (o sin fila) la fecha al enviar es la de siempre (hoy + DíasEnServir). Carlos lo enciende cuando haya visto la
        tabla de plazos:
          UPDATE ParametrosUsuario SET Valor = '1', Usuario2 = 'Compras853', [Fecha Modificación] = GETDATE()
          WHERE Empresa = '1' AND Clave = 'Compras.FechaRecepcionPorPlazoReal' AND Usuario = '(defecto)';
        Encendido: hoy + P80 del proveedor (con 5 recepciones o más); si no, hoy + DíasEnServir si es mayor que 0; si no, hoy + P80
        genérico; y nunca menos que hoy + DíasEnServir (el dato a mano de Compras manda si es más prudente).

    Modelo ML.NET EN SOMBRA (mismo script, lotes propios):
      - dbo.PlazosProveedoresModeloEntrenamientos: una fila por entrenamiento del job 'plazos-proveedores-modelo' (todos los días a las
        4:10, cola «entrenamiento») o de POST api/Compras/PlazosProveedores/Modelo/Entrenar: ejemplos, métricas de VALIDACIÓN (últimos 3
        meses) del modelo y de la regla del P80 recalculada «como si» (error absoluto medio, % de pedidos en que se quedó corto, días de
        más cuando se pasa de largo), margen, versión/algoritmo y ModeloActivo (la fila cuyo modelo está en uso). Sin esta tabla el job no
        hace nada y los endpoints del modelo dan 503.
      - En ParametrosUsuario (empresa '1', '(defecto)') 'Compras.FechaRecepcionQuienManda' = 'Regla' si no existe. Con 'Modelo' (y
        'Compras.FechaRecepcionPorPlazoReal' = '1' y modelo guardado) la fecha al enviar es la del modelo, nunca antes de hoy + DíasEnServir:
          UPDATE ParametrosUsuario SET Valor = 'Modelo', Usuario2 = 'Compras853', [Fecha Modificación] = GETDATE()
          WHERE Empresa = '1' AND Clave = 'Compras.FechaRecepcionQuienManda' AND Usuario = '(defecto)';
        Con o sin él, FechaModelo de dbo.PedidosCompraEnvios se rellena en cuanto hay modelo. Comparación: GET api/Compras/PlazosProveedores/Comparacion.

    Además, con la tabla de plazos llena, el cálculo de la fecha de entrega (NestoAPI#606) da una fecha ORIENTATIVA a lo que aún no se ha
    pedido al proveedor (no cambia ninguna fecha prometida).

    ORDEN: puede ir antes o después de publicar la API. Sin este script, la API hace EXACTAMENTE lo de hoy (sin plazos, sin orientativas,
    sin registro, sin errores; los endpoints dan 503); lo vuelve a comprobar cada 10 minutos sin reiniciar. Tras lanzarlo, para no esperar
    a la noche: POST api/Compras/PlazosProveedores/Recalcular.

    Lanzar como sa en NV. Idempotente: se puede relanzar.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ProveedoresPlazosEntrega', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProveedoresPlazosEntrega (
        Empresa char(3) NOT NULL,
        Proveedor varchar(10) NOT NULL,                -- '*' = todos los proveedores juntos
        Recepciones int NOT NULL,
        DiasMediana int NULL,
        DiasP80 int NULL,
        IntervaloPedidosMediano int NULL,
        UltimoPedido date NULL,
        FechaCalculo datetime NOT NULL CONSTRAINT DF_ProveedoresPlazosEntrega_FechaCalculo DEFAULT (GETDATE()),
        CONSTRAINT PK_ProveedoresPlazosEntrega PRIMARY KEY CLUSTERED (Empresa, Proveedor)
    );
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ProveedoresPlazosEntrega TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.PedidosCompraEnvios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PedidosCompraEnvios (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PedidosCompraEnvios PRIMARY KEY CLUSTERED,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Proveedor varchar(10) NULL,
        FechaEnvio datetime NOT NULL,
        FechaRecepcionPuesta date NOT NULL,
        FechaRegla date NULL,                          -- hoy + P80 (aunque no se haya usado)
        FechaModelo date NULL,                         -- día del envío + días prudentes del modelo ML.NET en sombra
        DiasEnServir int NOT NULL,
        Origen varchar(20) NULL,                       -- PlazoProveedor, DiasEnServir, PlazoGenerico, Hoy o ComoAntes (interruptor apagado)
        Usuario nvarchar(50) NULL
    );
    CREATE UNIQUE INDEX UX_PedidosCompraEnvios_Pedido ON dbo.PedidosCompraEnvios (Empresa, Pedido);
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.PedidosCompraEnvios TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.vPedidosCompraEnviosError', N'V') IS NOT NULL
    DROP VIEW dbo.vPedidosCompraEnviosError;
GO
CREATE VIEW dbo.vPedidosCompraEnviosError
AS
SELECT e.Empresa, e.Pedido, e.Proveedor, e.FechaEnvio, e.DiasEnServir, e.Origen,
    e.FechaRecepcionPuesta, e.FechaRegla, e.FechaModelo,
    a.PrimerAlbaran, a.UltimoAlbaran, a.LineasPendientes,
    CAST(CASE WHEN a.LineasPendientes = 0 AND a.UltimoAlbaran IS NOT NULL THEN 1 ELSE 0 END AS bit) AS Completo,
    DATEDIFF(day, e.FechaRecepcionPuesta, a.PrimerAlbaran) AS ErrorPuestaPrimero,
    DATEDIFF(day, e.FechaRegla, a.PrimerAlbaran) AS ErrorReglaPrimero,
    DATEDIFF(day, e.FechaModelo, a.PrimerAlbaran) AS ErrorModeloPrimero,
    DATEDIFF(day, e.FechaRecepcionPuesta, a.UltimoAlbaran) AS ErrorPuestaUltimo,
    DATEDIFF(day, e.FechaRegla, a.UltimoAlbaran) AS ErrorReglaUltimo,
    DATEDIFF(day, e.FechaModelo, a.UltimoAlbaran) AS ErrorModeloUltimo
FROM dbo.PedidosCompraEnvios e
OUTER APPLY (
    SELECT MIN(CAST(l.[FechaAlbarán] AS date)) AS PrimerAlbaran,
           MAX(CAST(l.[FechaAlbarán] AS date)) AS UltimoAlbaran,
           SUM(CASE WHEN l.Estado IN (-1, 1) THEN 1 ELSE 0 END) AS LineasPendientes
    FROM dbo.LinPedidoCmp l
    WHERE l.Empresa = e.Empresa AND l.[Número] = e.Pedido AND l.[TipoLínea] = '1'
) a;
GO
GRANT SELECT ON dbo.vPedidosCompraEnviosError TO [NUEVAVISION\RDS2016$];
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Compras.FechaRecepcionPorPlazoReal' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Compras.FechaRecepcionPorPlazoReal', '(defecto)', '0', 'Compras853', GETDATE());
GO
IF OBJECT_ID(N'dbo.PlazosProveedoresModeloEntrenamientos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PlazosProveedoresModeloEntrenamientos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PlazosProveedoresModeloEntrenamientos PRIMARY KEY CLUSTERED,
        Fecha datetime NOT NULL,
        Empresa char(3) NOT NULL,
        Ejemplos int NOT NULL,                         -- pedidos recibidos con plazo válido (0-120 días) del histórico
        EjemplosEntrenamiento int NOT NULL,            -- los anteriores a la validación
        EjemplosValidacion int NOT NULL,               -- los de los últimos 3 meses
        ValidacionDesde date NULL,
        PedidosComparados int NOT NULL,                -- de validación con regla calculable: sobre ellos se miden los dos
        ErrorMedioModelo decimal(9,2) NULL,            -- media de |real - previsto| en días
        ErrorMedioRegla decimal(9,2) NULL,
        PorcentajeCortoModelo decimal(9,2) NULL,       -- % de pedidos que llegaron DESPUÉS de lo previsto
        PorcentajeCortoRegla decimal(9,2) NULL,
        DiasDeMasModelo decimal(9,2) NULL,             -- días de más de media cuando se pasa de largo
        DiasDeMasRegla decimal(9,2) NULL,
        MargenLog float NULL,                          -- margen prudente del modelo (P80 de los residuos en log)
        Version varchar(20) NULL,
        Algoritmo varchar(100) NULL,
        ModeloActivo bit NOT NULL CONSTRAINT DF_PlazosProveedoresModeloEntrenamientos_Activo DEFAULT (0),
        Motivo nvarchar(500) NULL,
        Origen nvarchar(100) NULL,
        Segundos int NULL
    );
END
GO
GRANT SELECT, INSERT, UPDATE ON dbo.PlazosProveedoresModeloEntrenamientos TO [NUEVAVISION\RDS2016$];
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Compras.FechaRecepcionQuienManda' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Compras.FechaRecepcionQuienManda', '(defecto)', 'Regla', 'Compras853', GETDATE());
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.ProveedoresPlazosEntrega', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.PedidosCompraEnvios', N'U') IS NOT NULL
             AND OBJECT_ID(N'dbo.vPedidosCompraEnviosError', N'V') IS NOT NULL
             AND OBJECT_ID(N'dbo.PlazosProveedoresModeloEntrenamientos', N'U') IS NOT NULL
            THEN N'OK: NestoAPI#853 (ProveedoresPlazosEntrega, PedidosCompraEnvios, vPedidosCompraEnviosError y PlazosProveedoresModeloEntrenamientos)'
            ELSE N'FALTA algo' END AS Resultado;
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave IN ('Compras.FechaRecepcionPorPlazoReal', 'Compras.FechaRecepcionQuienManda');
GO
