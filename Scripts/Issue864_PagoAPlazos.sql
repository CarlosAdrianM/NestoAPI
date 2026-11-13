/*
    NestoAPI#864 (13/11/26): pago a plazos de un pedido que ya existe. Lo pasa un empleado (Nesto) o un vendedor (NestoApp) y el
    cliente termina la financiación en la app de clientes (push) o con un enlace. Continuación de #663 (Aplázame en el carrito
    de la app, que NO cambia: sigue con sus columnas de PagosTPV y sus parámetros Aplazame*).

    TODO ES NEUTRO (requisito de Carlos): ni la tabla ni los parámetros llevan el nombre de ninguna financiera. El proveedor
    activo es un DATO (parámetro PagoAPlazosProveedor); hoy el único dado de alta en la API es «Aplazame».

    1. Tabla dbo.SolicitudesPagoAPlazos (fuera del EDMX): una fila por solicitud de financiación de un pedido.
         Proveedor            código del proveedor con el que se abrió (la solicitud sigue con el suyo aunque se cambie el activo)
         Referencia           nuestra referencia ante el proveedor: PAP-{Id} (en Aplázame, el mid / order.id)
         ReferenciaExterna    el identificador que nos da el proveedor (en Aplázame, el id del checkout)
         Importe              lo que se financia: lo pendiente de pagar del pedido al pedirlo, con IVA
         Canal                App (push al cliente) | Enlace (se le manda la dirección)
         Estado               Pendiente → Aprobado → Capturado; o Rechazado (el proveedor no aprueba), Cancelado (nosotros, o el
                              pedido se borra o cambia) o Caducado (el cliente no termina, o el pedido no sale en plazo)
         Pruebas              contra el entorno de pruebas del proveedor (sin dinero)
         FechaAprobacion      cuándo aprobó el proveedor: es la FechaCobro del prepago (#860) y desde ahí cuenta el plazo
         FechaLimiteCaptura   hasta cuándo se puede confirmar el cobro (Aplázame: 15 días desde la aprobación)
         FechaCaptura / ImporteCapturado   la confirmación del cobro, cuando el pedido ha salido
         FechaAvisoPlazo      cuándo se avisó de «quedan N días para enviarlo» (una sola vez)
         FechaCierre / Motivo cuándo y por qué se canceló, caducó o se rechazó
         PrepagoId            el Prepago que se apuntó al pedido al aprobar
    2. Parámetros en ParametrosUsuario, empresa 1, usuario «(defecto)»:
         PagoAPlazosPedidos               = Apagado   (Apagado / Pruebas / Activo; sin fila o cualquier otra cosa = apagado).
                                            Es el interruptor PROPIO de este canal, independiente del de la app (AplazameApp).
         PagoAPlazosPedidosClientesPrueba = ''        (clientes separados por comas a los que se ofrece en modo Pruebas)
         PagoAPlazosProveedor             = Aplazame  (código del proveedor activo; sin fila o desconocido = no se ofrece)
         PagoAPlazosDiasAviso             = 3         (con cuántos días de margen se avisa de un pedido aprobado sin salir)
         PagoAPlazosDiasValidezSolicitud  = 7         (días que vale una solicitud que el cliente no termina)
       Los límites de importe y el plazo de captura NO están aquí: son del proveedor (con Aplázame, AplazameImporteMinimo y
       AplazameImporteMaximo de #663, y 15 días).

    OJO, PRUEBAS: en modo Pruebas la financiación va contra el entorno de pruebas del proveedor (no mueve dinero), pero el
    Prepago SÍ se apunta al pedido de verdad. Es para probar el circuito con un cliente de prueba y un pedido de prueba: después
    hay que cancelar la solicitud (DELETE api/PedidosVenta/{empresa}/{pedido}/PagoAPlazos) o borrar el pedido.

    ORDEN: se puede publicar la API antes o después. Sin este script los endpoints nuevos (…/PagoAPlazos/…) dan 503 con el
    nombre del script, el job no hace nada y TODO lo demás (la app de clientes incluida) sigue como hoy.

    Lanzar en SSMS contra NV como sa. Idempotente (se puede lanzar dos veces).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID(N'dbo.SolicitudesPagoAPlazos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SolicitudesPagoAPlazos (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SolicitudesPagoAPlazos PRIMARY KEY,
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,
        Cliente varchar(10) NULL,
        Contacto varchar(3) NULL,
        Proveedor varchar(30) NOT NULL,
        Referencia varchar(40) NULL,
        ReferenciaExterna nvarchar(100) NULL,
        Importe decimal(18, 2) NOT NULL,
        Canal varchar(10) NOT NULL,
        Estado varchar(15) NOT NULL CONSTRAINT DF_SolicitudesPagoAPlazos_Estado DEFAULT ('Pendiente'),
        Pruebas bit NOT NULL CONSTRAINT DF_SolicitudesPagoAPlazos_Pruebas DEFAULT (0),
        Usuario nvarchar(100) NULL,
        Correo nvarchar(200) NULL,
        FechaCreacion datetime NOT NULL CONSTRAINT DF_SolicitudesPagoAPlazos_FechaCreacion DEFAULT (GETDATE()),
        FechaAprobacion datetime NULL,
        FechaLimiteCaptura datetime NULL,
        FechaCaptura datetime NULL,
        ImporteCapturado decimal(18, 2) NULL,
        FechaAvisoPlazo datetime NULL,
        FechaCierre datetime NULL,
        Motivo nvarchar(300) NULL,
        PrepagoId int NULL,
        CONSTRAINT CK_SolicitudesPagoAPlazos_Estado CHECK (Estado IN ('Pendiente', 'Aprobado', 'Capturado', 'Cancelado', 'Caducado', 'Rechazado')),
        CONSTRAINT CK_SolicitudesPagoAPlazos_Canal CHECK (Canal IN ('App', 'Enlace'))
    );
    PRINT 'SolicitudesPagoAPlazos: creada.';
END
ELSE
    PRINT 'SolicitudesPagoAPlazos: ya existía.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SolicitudesPagoAPlazos_Pedido' AND object_id = OBJECT_ID(N'dbo.SolicitudesPagoAPlazos'))
    CREATE INDEX IX_SolicitudesPagoAPlazos_Pedido ON dbo.SolicitudesPagoAPlazos (Empresa, Pedido);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SolicitudesPagoAPlazos_Referencia' AND object_id = OBJECT_ID(N'dbo.SolicitudesPagoAPlazos'))
    CREATE INDEX IX_SolicitudesPagoAPlazos_Referencia ON dbo.SolicitudesPagoAPlazos (Proveedor, Referencia);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_SolicitudesPagoAPlazos_Estado' AND object_id = OBJECT_ID(N'dbo.SolicitudesPagoAPlazos'))
    CREATE INDEX IX_SolicitudesPagoAPlazos_Estado ON dbo.SolicitudesPagoAPlazos (Estado);
GO

-- La API lee, da de alta y actualiza (nunca borra). Prepagos y ParametrosUsuario ya los escribe.
GRANT SELECT, INSERT, UPDATE ON dbo.SolicitudesPagoAPlazos TO [NUEVAVISION\RDS2016$];
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidos')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'PagoAPlazosPedidos', 'Apagado', 'NestoAPI#864', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidosClientesPrueba')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'PagoAPlazosPedidosClientesPrueba', '', 'NestoAPI#864', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosProveedor')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'PagoAPlazosProveedor', 'Aplazame', 'NestoAPI#864', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosDiasAviso')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'PagoAPlazosDiasAviso', '3', 'NestoAPI#864', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosDiasValidezSolicitud')
    INSERT INTO dbo.ParametrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'PagoAPlazosDiasValidezSolicitud', '7', 'NestoAPI#864', GETDATE());
GO

-- Comprobación
SELECT 'SolicitudesPagoAPlazos' AS Tabla, COUNT(*) AS Filas FROM dbo.SolicitudesPagoAPlazos;
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParametrosUsuario WHERE Usuario = '(defecto)' AND Clave LIKE 'PagoAPlazos%';
GO

-- PROBAR con un cliente de prueba y un pedido de prueba (el prepago se apunta de verdad: cancelar después):
-- UPDATE dbo.ParametrosUsuario SET Valor = '15191', Usuario2 = 'NestoAPI#864', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidosClientesPrueba';
-- UPDATE dbo.ParametrosUsuario SET Valor = 'Pruebas', Usuario2 = 'NestoAPI#864', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidos';

-- ENCENDER DE VERDAD (todos los clientes, dinero real):
-- UPDATE dbo.ParametrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#864', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidos';

-- APAGAR (las solicitudes ya abiertas siguen su curso: aprobación, captura, avisos y cancelaciones):
-- UPDATE dbo.ParametrosUsuario SET Valor = 'Apagado', Usuario2 = 'NestoAPI#864', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PagoAPlazosPedidos';

-- SEGUIMIENTO:
-- SELECT Id, Empresa, Pedido, Cliente, Proveedor, Referencia, ReferenciaExterna, Importe, Canal, Estado, Pruebas, Usuario,
--        FechaCreacion, FechaAprobacion, FechaLimiteCaptura, FechaCaptura, ImporteCapturado, FechaAvisoPlazo, FechaCierre, Motivo, PrepagoId
-- FROM dbo.SolicitudesPagoAPlazos ORDER BY Id DESC;
