-- NestoAPI#663: pago con Aplázame desde el carrito de la app NestoTiendas.
--
-- 1. Columnas nuevas en dbo.PagosTPV (los cobros de Aplázame van en la misma tabla que los de Redsys, Tipo = 'CarritoAplazame'):
--      ReferenciaExterna  nvarchar(64)  NULL      id del checkout en Aplázame (el mid, APP-{Id}, va en NumeroOrden)
--      Sandbox            bit NOT NULL DEFAULT 0  el cobro se hizo contra el entorno de pruebas de Aplázame
--      FechaConfirmacion  datetime      NULL      cuándo lo confirmamos a Aplázame (desde ahí cuentan las 2 h y los 15 días)
--      FechaCaptura       datetime      NULL      cuándo se capturó (al estar todo el pedido en albarán o factura)
--      ImporteCapturado   decimal(18,2) NULL      lo capturado (lo servido; nunca más de lo autorizado)
--      FechaAvisoCaptura  datetime      NULL      cuándo se avisó a Informática de que a los 12 días seguía sin capturar
-- 2. Filas «(defecto)» de los parámetros (empresa 1):
--      AplazameApp                = Apagado  (Apagado / Sandbox / Activo; sin fila o cualquier otra cosa = apagado)
--      AplazameAppClientesPrueba  = ''       (clientes separados por comas que lo ven en Sandbox)
--      AplazameImporteMinimo      = 100      (con IVA; sin fila, sin mínimo)
--      AplazameImporteMaximo      = 2500     (con IVA; sin fila, sin máximo)
--    Son los límites que tiene nuestra cuenta en Aplázame: el sandbox rechazó el 16/10/26 un checkout de 65,09 € con
--    «The amount is below the allowable limit» (min_value_allowed 100, max_value_allowed 2500). Si Aplázame los
--    cambia, se cambian aquí; con un importe fuera de ellos Aplázame no deja ni abrir el checkout.
--
-- OJO, SANDBOX: en modo Sandbox el cobro va contra el entorno de pruebas de Aplázame (no mueve dinero), pero el
-- pedido SÍ se crea en Nesto, como pagado (TRN / PRE con su Prepago en la 57200013). Es para probar el circuito
-- entero con un cliente de prueba: esos pedidos hay que borrarlos (o no servirlos) a mano.
--
-- ORDEN: lanzar ANTES de publicar el API que trae #663. El API nuevo lee y escribe estas columnas en TODAS las
-- consultas de PagosTPV (también los cobros con tarjeta de siempre y los enlaces de pago): sin ellas, fallarían.
-- Se puede lanzar dos veces sin problema.
--
-- Es DDL: desde SSMS con `sa`. No hace falta GRANT: la tabla PagosTPV y ParámetrosUsuario ya las escribe el API.

USE NV;
GO

IF COL_LENGTH('dbo.PagosTPV', 'ReferenciaExterna') IS NULL
    ALTER TABLE dbo.PagosTPV ADD ReferenciaExterna nvarchar(64) NULL;
GO
IF COL_LENGTH('dbo.PagosTPV', 'Sandbox') IS NULL
    ALTER TABLE dbo.PagosTPV ADD Sandbox bit NOT NULL CONSTRAINT DF_PagosTPV_Sandbox DEFAULT 0;
GO
IF COL_LENGTH('dbo.PagosTPV', 'FechaConfirmacion') IS NULL
    ALTER TABLE dbo.PagosTPV ADD FechaConfirmacion datetime NULL;
GO
IF COL_LENGTH('dbo.PagosTPV', 'FechaCaptura') IS NULL
    ALTER TABLE dbo.PagosTPV ADD FechaCaptura datetime NULL;
GO
IF COL_LENGTH('dbo.PagosTPV', 'ImporteCapturado') IS NULL
    ALTER TABLE dbo.PagosTPV ADD ImporteCapturado decimal(18, 2) NULL;
GO
IF COL_LENGTH('dbo.PagosTPV', 'FechaAvisoCaptura') IS NULL
    ALTER TABLE dbo.PagosTPV ADD FechaAvisoCaptura datetime NULL;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameApp')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'AplazameApp', 'Apagado', 'NestoAPI#663', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameAppClientesPrueba')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'AplazameAppClientesPrueba', '', 'NestoAPI#663', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameImporteMinimo')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'AplazameImporteMinimo', '100', 'NestoAPI#663', GETDATE());

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameImporteMaximo')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'AplazameImporteMaximo', '2500', 'NestoAPI#663', GETDATE());
GO

-- Comprobación
SELECT COL_LENGTH('dbo.PagosTPV', 'ReferenciaExterna') AS ReferenciaExterna,
       COL_LENGTH('dbo.PagosTPV', 'Sandbox') AS Sandbox,
       COL_LENGTH('dbo.PagosTPV', 'FechaConfirmacion') AS FechaConfirmacion,
       COL_LENGTH('dbo.PagosTPV', 'FechaCaptura') AS FechaCaptura,
       COL_LENGTH('dbo.PagosTPV', 'ImporteCapturado') AS ImporteCapturado,
       COL_LENGTH('dbo.PagosTPV', 'FechaAvisoCaptura') AS FechaAvisoCaptura;
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave LIKE 'Aplazame%';
GO

-- PROBAR EN SANDBOX con un cliente de prueba (los pedidos se crean de verdad en Nesto: borrarlos después):
-- UPDATE dbo.ParámetrosUsuario SET Valor = '15191', Usuario2 = 'NestoAPI#663', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameAppClientesPrueba';
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Sandbox', Usuario2 = 'NestoAPI#663', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameApp';

-- ENCENDER DE VERDAD (todos los clientes, dinero real):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Activo', Usuario2 = 'NestoAPI#663', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameApp';

-- APAGAR (los cobros ya hechos siguen su curso: confirmación, captura y cancelación de huérfanos):
-- UPDATE dbo.ParámetrosUsuario SET Valor = 'Apagado', Usuario2 = 'NestoAPI#663', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameApp';

-- CAMBIAR LOS LÍMITES DE IMPORTE (con IVA; borrar la fila = sin límite):
-- UPDATE dbo.ParámetrosUsuario SET Valor = '100', Usuario2 = 'NestoAPI#663', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameImporteMinimo';
-- UPDATE dbo.ParámetrosUsuario SET Valor = '2500', Usuario2 = 'NestoAPI#663', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AplazameImporteMaximo';

-- SEGUIMIENTO de los cobros de Aplázame de la app:
-- SELECT Id, NumeroOrden AS Mid, ReferenciaExterna AS CheckoutId, Cliente, Importe, Estado, Documento AS Pedido, Sandbox,
--        FechaCreacion, FechaConfirmacion, FechaCaptura, ImporteCapturado, FechaAvisoCaptura
-- FROM dbo.PagosTPV WHERE Tipo = 'CarritoAplazame' ORDER BY Id DESC;
