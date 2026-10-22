/*
    NestoAPI#694: los cupones en la tienda PrestaShop por el módulo nestocheques (como el cheque regalo, #652).

    Ejecutar en SSMS contra NV como sa (ALTER TABLE: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    DESPUÉS de Issue613_Cupones.sql. Se puede lanzar antes o después de publicar la API: la API solo lee y escribe estas columnas
    con sp_executesql detrás de COL_LENGTH, así que sin ellas lo único que no funciona es POST api/Cupones/{codigo}/Usar desde la
    tienda (contesta 503 y el módulo lo reintenta con su cron). Hay que lanzarlo ANTES de activar los cupones en el módulo.

    Columnas nuevas en dbo.Cupones (las escribe la API; nadie las toca a mano):
       - PedidoCanalUso: la referencia del pedido de la tienda donde se gastó (POST api/Cupones/{codigo}/Usar con PedidoCanal).
         Mientras ese pedido no entra en Nesto, el cupón queda Estado = 'Usado' con UsadoEnPedido = NULL (no está libre); cuando
         CanalesExternos lo importa con la línea del cupón, se ata al pedido de Nesto sin volver a canjearlo.
       - ImporteDescontadoUso: lo que descontó la tienda (IVA incluido), para revisar si no coincide con el importe del cupón.

    El espejo de cart_rules por webservice de #614 (Cupones:EspejoPrestashop) sigue apagado; sus columnas no se tocan.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Cupones', 'PedidoCanalUso') IS NULL
    ALTER TABLE dbo.Cupones ADD PedidoCanalUso varchar(50) NULL;
IF COL_LENGTH('dbo.Cupones', 'ImporteDescontadoUso') IS NULL
    ALTER TABLE dbo.Cupones ADD ImporteDescontadoUso decimal(10, 2) NULL;
GO

-- Los permisos de la tabla ya los dio Issue613_Cupones.sql (SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$]): sirven para las
-- columnas nuevas.

-- Comprobación
SELECT COL_LENGTH('dbo.Cupones', 'PedidoCanalUso') AS PedidoCanalUso, COL_LENGTH('dbo.Cupones', 'ImporteDescontadoUso') AS ImporteDescontadoUso;
GO
