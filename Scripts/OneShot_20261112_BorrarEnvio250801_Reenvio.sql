/*
    OneShot 12/11/26 (Novedades 1191, Alfredo): borra el envío 250801 (CTT, agencia 13, pedido 927976, «En curso»), que nació el
    10/11 a las 15:35 de la decisión «Volver a enviar» de DLopez sobre el 250405 devuelto. El paquete no ha vuelto a Algete: lo
    tiene CTT y Alfredo les ha pedido que lo entreguen sin pasar por aquí. Desde Nesto no se puede borrar (FK
    FK_EnviosAgenciaReenvios_Nuevo) y, mientras siga «En curso», «Tramitar todos» lo tramitaría y saldría en el manifiesto.

    Hace lo MISMO que RepositorioReenviosEnvio.AnularPorEntregaAsync (lo que haría el seguimiento cuando CTT lo entregue), solo
    que antes: la decisión pasa a EntregadoPorLaAgencia con EnvioAnulado = 250801, se borra el 250801 con su historia y queda
    constancia en la historia del 250405.

    Como sa en NV. Si algo no es lo esperado, no toca nada y lo dice.
*/
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

SELECT 'ANTES' Momento, Numero, Pedido, Agencia, Estado, Bultos, Usuario FROM dbo.EnviosAgencia WHERE Numero IN (250405, 250801);
SELECT 'ANTES' Momento, Id, EnvioOrigen, Decision, EnvioNuevo, EnvioAnulado, Usuario, Fecha FROM dbo.EnviosAgenciaReenvios WHERE EnvioOrigen = 250405;
GO

BEGIN TRANSACTION;
    IF NOT EXISTS (SELECT 1 FROM dbo.EnviosAgenciaReenvios WITH (UPDLOCK, HOLDLOCK) WHERE EnvioOrigen = 250405 AND Decision = 'Reenviar' AND EnvioNuevo = 250801)
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'NO SE HA TOCADO NADA: el 250405 ya no tiene el reenvío 250801 pendiente (¿ya anulado por el seguimiento?).', 16, 1);
        RETURN;
    END
    IF NOT EXISTS (SELECT 1 FROM dbo.EnviosAgencia WITH (UPDLOCK, HOLDLOCK) WHERE Numero = 250801 AND Pedido = 927976 AND Agencia = 13 AND Estado < 1)
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'NO SE HA TOCADO NADA: el 250801 ya no está sin tramitar en CTT con el pedido 927976 (¿se ha tramitado?).', 16, 1);
        RETURN;
    END

    UPDATE dbo.EnviosAgenciaReenvios SET Decision = 'EntregadoPorLaAgencia', EnvioAnulado = 250801, EnvioNuevo = NULL WHERE EnvioOrigen = 250405;
    DELETE FROM dbo.EnviosHistoria WHERE NumeroEnvio = 250801;
    DELETE FROM dbo.EnviosAgencia WHERE Numero = 250801 AND Estado < 1;
    IF @@ROWCOUNT <> 1
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'NO SE HA TOCADO NADA: el borrado del envío no ha tocado exactamente una fila.', 16, 1);
        RETURN;
    END
    INSERT INTO dbo.EnviosHistoria (NumeroEnvio, Campo, ValorAnterior, Observaciones, Usuario, FechaModificacion)
    VALUES (250405, 'Reenvio', '250801',
            N'Reenvío 250801 anulado a mano (Novedades 1191): el paquete no volvió al almacén, lo entrega CTT directamente.',
            'NUEVAVISION\Carlos', GETDATE());
COMMIT TRANSACTION;
PRINT N'Envío 250801 borrado y reenvío del 250405 anulado.';
GO

SELECT 'DESPUES' Momento, Numero, Pedido, Agencia, Estado, Bultos, Usuario FROM dbo.EnviosAgencia WHERE Numero IN (250405, 250801);
SELECT 'DESPUES' Momento, Id, EnvioOrigen, Decision, EnvioNuevo, EnvioAnulado, Usuario, Fecha FROM dbo.EnviosAgenciaReenvios WHERE EnvioOrigen = 250405;
GO
