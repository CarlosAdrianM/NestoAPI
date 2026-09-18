-- Nesto#340 (Agencias, slice A4.4): modificar reembolso, retorno, estado y fecha de entrega de un
-- envio TRAMITADO (pestana Tramitados, botones Modificar y Rehusar) contra NestoAPI
-- (POST api/EnviosAgencias/{id}/ModificarDatos) en vez de por Entity Framework desde el cliente.
-- Son los ultimos bloques de EF de Nesto: validado esto y el pago de reembolsos (A4.3), Nesto se
-- queda sin Entity Framework.
--
-- ANTES: Nesto340_GrantSpsModificarEnvio.sql (prdDesliquidar y prdModificarEfectoCliente no tenian
-- EXECUTE para la cuenta de la API el 18/09/2026).
--
-- Mismo protocolo que TramitarEnvioPorApi y PagarReembolsosPorApi: usuario SIN dominio y fila
-- '(defecto)' con el valor SEGURO para que el parametro exista y se propague.

SET NOCOUNT ON;
USE NV;

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'ModificarEnvioPorApi' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'ModificarEnvioPorApi', '(defecto)', 'EF', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'EF', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'ModificarEnvioPorApi' AND Usuario = '(defecto)';
END

DECLARE @Piloto varchar(30) = 'Carlos';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'ModificarEnvioPorApi' AND Usuario = @Piloto)
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'ModificarEnvioPorApi', @Piloto, 'API', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'API', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'ModificarEnvioPorApi' AND Usuario = @Piloto;
END

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación] AS FMod
FROM ParametrosUsuario WHERE Clave = 'ModificarEnvioPorApi' ORDER BY Usuario;

/*
    COMPROBACION tras la primera modificacion por API (15 minutos). Probar primero SOLO la fecha de
    entrega (sin contabilidad), despues un cambio de reembolso, y el rehusar el ultimo.

    DECLARE @Envio int = 0;   -- <<< el envio modificado
    DECLARE @Desde datetime = DATEADD(minute, -15, GETDATE());

    -- Una fila por campo cambiado (antes solo quedaba la ultima), con el usuario del Identity.
    SELECT 'HISTORIA' AS Paso, Numero, RTRIM(Campo) AS Campo, RTRIM(ValorAnterior) AS Anterior,
           RTRIM(Observaciones) AS Obs, RTRIM(Usuario) AS Usuario, FechaModificacion
    FROM EnviosHistoria WHERE NumeroEnvio = @Envio ORDER BY Numero;

    SELECT 'ENVIO' AS Paso, Numero, Pedido, Reembolso, Retorno, Estado, FechaEntrega
    FROM EnviosAgencia WHERE Numero = @Envio;

    -- Si cambio el reembolso: deshago (debe, asiento 1) y rehago (haber, asiento 2) en _Reembolso.
    SELECT 'APUNTES' AS Paso, Asiento, Fecha, RTRIM(Nº_Cuenta) AS Cuenta, Debe, Haber, RTRIM(Concepto) AS Concepto,
           Liquidado, RTRIM(Usuario) AS Usuario
    FROM Contabilidad
    WHERE Empresa = '1' AND Diario = '_Reembolso' AND [Fecha Modificación] >= @Desde
    ORDER BY Asiento, Nº_Orden;

    SELECT 'ELMAH' AS Paso, DATEADD(hour, 2, TimeUtc) AS HoraLocal, Type, LEFT(Message, 90) AS Mensaje
    FROM ELMAH_Error WHERE TimeUtc >= DATEADD(minute, -15, GETUTCDATE()) ORDER BY TimeUtc DESC;
*/
