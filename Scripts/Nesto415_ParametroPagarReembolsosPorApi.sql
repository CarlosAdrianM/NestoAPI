-- Nesto#415 / Nesto#340 (Agencias, slice A4.3): el pago de reembolsos de la pestana Reembolsos de
-- la ventana de Agencias se contabiliza contra NestoAPI (POST api/EnviosAgencias/PagarReembolsos)
-- en vez de por Entity Framework desde el cliente. Era el ULTIMO sitio de Nesto que llamaba a
-- prdContabilizar por su cuenta (sin ELMAH, sin reintento ante deadlock, con el login de Windows
-- del usuario en vez del canal centralizado del API).
--
-- Mismo protocolo que Nesto340_ParametroTramitarEnvioPorApi.sql (y sus dos lecciones del 28/08):
--   1. El usuario va SIN dominio ('Carlos', no 'NUEVAVISION\Carlos').
--   2. Hace falta la fila '(defecto)' con el valor SEGURO para que el parametro exista y se
--      propague; el valor nuevo va en una fila por cada usuario piloto.

SET NOCOUNT ON;
USE NV;

-- 1. El defecto es EF: el camino de siempre, para todo el que no sea piloto.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'PagarReembolsosPorApi' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'PagarReembolsosPorApi', '(defecto)', 'EF', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'EF', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'PagarReembolsosPorApi' AND Usuario = '(defecto)';
END

-- 2. El piloto. Usuario SIN dominio, y que PAGUE reembolsos de verdad (quien cuadra las
-- liquidaciones de las agencias). La primera ejecucion: UN pago, temprano, avisando antes y con
-- alguien mirando la comprobacion de abajo inmediatamente despues.
DECLARE @Piloto varchar(30) = 'Carlos';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'PagarReembolsosPorApi' AND Usuario = @Piloto)
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'PagarReembolsosPorApi', @Piloto, 'API', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'API', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'PagarReembolsosPorApi' AND Usuario = @Piloto;
END

-- Solo 'API' (recortado y sin distinguir mayusculas) activa el camino nuevo. Cualquier otro valor,
-- la ausencia de fila o un fallo al leer el parametro llevan al camino de siempre.

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación] AS FMod
FROM ParametrosUsuario WHERE Clave = 'PagarReembolsosPorApi' ORDER BY Usuario;

/*
    COMPROBACION tras el primer pago por API (lanzar en los 15 minutos siguientes).
    Lo que tiene que salir, en este orden, y es IDENTICO a lo que dejaba el camino EF:
      - Un asiento en el diario _PagoReemb con N+1 lineas: N al haber (una por envio, cuenta de
        reembolsos de SU agencia, Nº documento = 10 primeras letras del nombre de la agencia,
        delegacion ALG, forma de venta VAR) y 1 al debe del cliente por la suma (contacto 0,
        forma de pago en efectivo de la empresa, vendedor NV).
      - Los envios pagados con FechaPagoReembolso = hoy y Usuario = quien pago (antes no se
        estampaba el usuario en el envio).
      - Nada en ELMAH.

    DECLARE @Desde datetime = DATEADD(minute, -15, GETDATE());

    SELECT 'APUNTES' AS Paso, Asiento, Fecha, TipoCuenta, RTRIM(Nº_Cuenta) AS Cuenta, RTRIM(Contacto) AS Contacto,
           Debe, Haber, RTRIM(Concepto) AS Concepto, RTRIM(Nº_Documento) AS Documento, RTRIM(Delegación) AS Deleg,
           RTRIM(FormaVenta) AS FV, RTRIM(FormaPago) AS FP, RTRIM(Vendedor) AS Vend, RTRIM(Usuario) AS Usuario
    FROM Contabilidad
    WHERE Empresa = '1' AND Diario = '_PagoReemb' AND [Fecha Modificación] >= @Desde
    ORDER BY Asiento, Nº_Orden;

    SELECT 'ENVIOS' AS Paso, Numero, Pedido, Agencia, Reembolso, FechaPagoReembolso, RTRIM(Usuario) AS Usuario
    FROM EnviosAgencia
    WHERE FechaPagoReembolso = CONVERT(date, GETDATE())
    ORDER BY Numero;

    SELECT 'ELMAH' AS Paso, DATEADD(hour, 2, TimeUtc) AS HoraLocal, Type, LEFT(Message, 90) AS Mensaje, RTRIM([User]) AS Usuario
    FROM ELMAH_Error
    WHERE TimeUtc >= DATEADD(minute, -15, GETUTCDATE())
    ORDER BY TimeUtc DESC;
*/
