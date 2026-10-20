-- NestoAPI#595 (slice 2): «Insertar» e «Imprimir e insertar» de la ventana de Agencias crean el envío en el
-- servidor (POST api/EnviosAgencias/DesdePedido) en vez de decidirlo y grabarlo Nesto (InsertarRegistro). El
-- servidor aplica la propuesta, los cambios de la pantalla y las confirmaciones (409), calcula la plaza y el
-- código de barras y avisa al cliente. CTT e Innovatrans se siguen registrando al imprimir; la etiqueta de
-- recogida y las agencias que el servidor no soporta siguen por el camino de siempre.
--
-- Mismo protocolo que Nesto415_ParametroPagarReembolsosPorApi.sql (y sus dos lecciones del 28/08):
--   1. El usuario va SIN dominio ('Alfredo', no 'NUEVAVISION\Alfredo').
--   2. Hace falta la fila '(defecto)' con el valor SEGURO para que el parametro exista y se
--      propague; el valor nuevo va en una fila por cada usuario piloto.
--
-- Decision de Carlos (20/10/26): el parametro nace APAGADO para todos, piloto incluido. Antes de encenderlo,
-- una semana de sombra (la propuesta, PropuestaEnvioDifiere, y la plaza y el codigo de barras,
-- PlazaCodigoBarrasDifiere, en ELMAH). El piloto sera Alfredo.

SET NOCOUNT ON;
USE NV;

-- 1. El defecto es NESTO: el camino de siempre, para todo el que no sea piloto.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'InsertarEnvioPorApi' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'InsertarEnvioPorApi', '(defecto)', 'NESTO', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'NESTO', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'InsertarEnvioPorApi' AND Usuario = '(defecto)';
END

-- 2. El piloto, de momento TAMBIEN apagado (NESTO). Usuario SIN dominio.
DECLARE @Piloto varchar(30) = 'Alfredo';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'InsertarEnvioPorApi' AND Usuario = @Piloto)
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'InsertarEnvioPorApi', @Piloto, 'NESTO', SYSTEM_USER, GETDATE());
END

-- Solo 'API' (recortado y sin distinguir mayusculas) activa el camino nuevo. Cualquier otro valor,
-- la ausencia de fila o un fallo al leer el parametro llevan al camino de siempre.

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación] AS FMod
FROM ParametrosUsuario WHERE Clave = 'InsertarEnvioPorApi' ORDER BY Usuario;

/*
    ENCENDERLO PARA ALFREDO (tras la semana de sombra, con Nesto y NestoAPI ya publicados):

    UPDATE ParametrosUsuario SET Valor = 'API', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'InsertarEnvioPorApi' AND Usuario = 'Alfredo';

    Nesto lee el parametro en cada insercion: no hace falta reiniciar.
    APAGARLO (vuelta atras inmediata): el mismo UPDATE con Valor = 'NESTO'.

    COMPROBACION tras los primeros envios de Alfredo (lanzar en los 15 minutos siguientes):
      - Cada envio con Usuario = el de Alfredo, Estado 0, plaza (Nemonico) y codigo de barras de GLS
        (61197140 + Numero en 7 cifras con BusinessParcel), ImporteGasto > 0 y Observaciones <= 80.
      - Un pendiente reutilizado (tienda online) pasa de Estado -1 a 0 con su destino de siempre.
      - Nada en ELMAH salvo los avisos de sombra ya conocidos.

    DECLARE @Desde datetime = DATEADD(minute, -15, GETDATE());

    SELECT 'ENVIOS' AS Paso, Numero, Pedido, Agencia, Estado, Bultos, Peso, Reembolso, ImporteGasto,
           RTRIM(Nemonico) AS Nemonico, RTRIM(CodigoBarras) AS CodigoBarras, RTRIM(Usuario) AS Usuario, FechaModificacion
    FROM EnviosAgencia
    WHERE FechaModificacion >= @Desde
    ORDER BY Numero;

    SELECT 'ELMAH' AS Paso, DATEADD(hour, 2, TimeUtc) AS HoraLocal, Type, LEFT(Message, 120) AS Mensaje, RTRIM([User]) AS Usuario
    FROM ELMAH_Error
    WHERE TimeUtc >= DATEADD(minute, -15, GETUTCDATE())
    ORDER BY TimeUtc DESC;
*/
