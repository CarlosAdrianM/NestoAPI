/*
    OneShot 17/11/26 (Novedades 1280, Alfredo; OK de Carlos): borra el reenvío 250950 de CTT (pedido 930487). Se creó con
    «Reenviar» sobre el 250875, que estaba incidentado, pero CTT va a entregar el original con la dirección corregida: la
    caja no vuelve y el 250950 se quedaría para siempre «En curso» esperando caja (#870). La API no deja borrarlo (409,
    FK_EnviosAgenciaReenvios) y hoy no hay forma de anular la decisión de reenviar.

    Borra: la decisión de reenvío (EnviosAgenciaReenvios Id 5), la historia del 250950 y el envío 250950. El 250875 NO se toca.
    Como sa en NV. Se puede lanzar entero. Si algo no es lo esperado, no borra nada y lo dice.
*/
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

SELECT 'ANTES' Momento, Numero, Pedido, Agencia, Estado, Bultos, Reembolso, CodigoBarras FROM dbo.EnviosAgencia WHERE Pedido = 930487;
SELECT 'ANTES' Momento, Id, EnvioOrigen, Decision, EnvioNuevo, Usuario, Fecha FROM dbo.EnviosAgenciaReenvios WHERE EnvioNuevo = 250950 OR EnvioOrigen = 250875;
GO

DECLARE @motivo nvarchar(200) = NULL;

IF NOT EXISTS (SELECT 1 FROM dbo.EnviosAgencia WHERE Numero = 250950 AND Pedido = 930487 AND Agencia = 13 AND Estado <= 0)
    SET @motivo = N'El envío 250950 ya no está pendiente o en curso en la agencia 13 con el pedido 930487 (¿ya borrado o tramitado?).';
ELSE IF EXISTS (SELECT 1 FROM dbo.EnviosAgencia WHERE Numero = 250950 AND ISNULL(RTRIM(CodigoBarras), '') <> '')
    SET @motivo = N'El 250950 tiene código de barras: está dado de alta en CTT. Hay que anularlo allí antes.';
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.EnviosAgencia WHERE Numero = 250875 AND Pedido = 930487)
    SET @motivo = N'No encuentro el envío original 250875 del pedido 930487.';
ELSE IF (SELECT COUNT(*) FROM dbo.EnviosAgenciaReenvios WHERE EnvioNuevo = 250950) <> 1
     OR NOT EXISTS (SELECT 1 FROM dbo.EnviosAgenciaReenvios WHERE EnvioNuevo = 250950 AND EnvioOrigen = 250875 AND Decision = 'Reenviar')
    SET @motivo = N'La decisión de reenvío del 250950 no es la esperada (una sola, Reenviar, sobre el 250875).';
ELSE IF EXISTS (SELECT 1 FROM dbo.EnviosAgenciaReenvios WHERE EnvioOrigen = 250950)
    SET @motivo = N'El 250950 tiene a su vez una decisión de reenvío.';
ELSE IF EXISTS (SELECT 1 FROM dbo.EnviosAgenciaCargaBultos WHERE EnvioId = 250950)
    SET @motivo = N'El 250950 tiene bultos cargados.';
ELSE IF EXISTS (SELECT 1 FROM dbo.RepartoAutorizacionesReembolso WHERE EnvioId = 250950)
    SET @motivo = N'El 250950 tiene una autorización de reembolso.';
ELSE IF EXISTS (SELECT 1 FROM dbo.EnviosAgenciaPruebasEntrega WHERE EnvioId = 250950)
    SET @motivo = N'El 250950 tiene prueba de entrega.';
ELSE IF EXISTS (SELECT 1 FROM dbo.EnviosAgenciaReembolsosAlEntregar WHERE EnvioId = 250950)
    SET @motivo = N'El 250950 tiene un reembolso al entregar apuntado.';

IF @motivo IS NOT NULL
BEGIN
    PRINT N'NO SE HA BORRADO NADA: ' + @motivo;
END
ELSE
BEGIN
    BEGIN TRAN;
    DELETE FROM dbo.EnviosAgenciaReenvios WHERE EnvioNuevo = 250950 AND EnvioOrigen = 250875;
    IF OBJECT_ID('dbo.EnviosAgenciaCoordenadas', 'U') IS NOT NULL
        EXEC(N'DELETE FROM dbo.EnviosAgenciaCoordenadas WHERE EnvioAgenciaId = 250950');
    DELETE FROM dbo.EnviosHistoria WHERE NumeroEnvio = 250950;
    DELETE FROM dbo.EnviosAgencia WHERE Numero = 250950;
    IF @@ROWCOUNT <> 1
    BEGIN
        ROLLBACK;
        PRINT N'NO SE HA BORRADO NADA: el borrado del envío no ha afectado a una fila.';
    END
    ELSE
    BEGIN
        COMMIT;
        PRINT N'Borrado el reenvío 250950 y su decisión. El 250875 sigue como estaba.';
    END
END
GO

SELECT 'DESPUES' Momento, Numero, Pedido, Agencia, Estado, Bultos, Reembolso, CodigoBarras FROM dbo.EnviosAgencia WHERE Pedido = 930487;
SELECT 'DESPUES' Momento, Id, EnvioOrigen, Decision, EnvioNuevo FROM dbo.EnviosAgenciaReenvios WHERE EnvioNuevo = 250950 OR EnvioOrigen = 250875;
GO
