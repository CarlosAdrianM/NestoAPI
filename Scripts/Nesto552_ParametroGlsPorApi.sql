-- NestoAPI#552 (fase 4): GLS por la API, sin cambiar la forma de trabajar (decision de Carlos del 21/10/26): primero
-- imprimir y tramitar al cerrar el dia («Tramitar todos»), pero desde NestoAPI en vez de desde Nesto (AgenciaASM).
--   - La etiqueta de GLS (agencia 1) la monta la API (GET api/EnviosAgencias/{id}/EtiquetaZpl) sin grabar en GLS, y Nesto
--     la manda a la Zebra de siempre (ImpresoraAgenciaGLS).
--   - Tramitar y «Tramitar todos» de GLS van por POST api/EnviosAgencias/TramitarTodos (graba en GLS y cierra cada envio).
--   - La agencia 5 (GLS de la empresa espejo) sigue por AgenciaASM (pendiente de decidir).
--
-- ORDEN PARA ENCENDERLO:
--   1. NestoAPI: Agencias:GlsTramitarPorApi = true en el Web.config DEL REPO y publicar (a mano en el servidor lo apagaria
--      la siguiente publicacion). Sin esto, la API contesta 400 y Nesto ensenaria el error.
--      Con esto encendido, el packing de Ariadna (F8 «Etiqueta de agencia») ya imprime las de GLS en el servidor.
--   2. Nesto publicado con este parametro.
--   3. Este script (nace APAGADO para todos) y despues el UPDATE del piloto de abajo.
--
-- Mismo protocolo que Nesto595_ParametroInsertarEnvioPorApi.sql:
--   1. El usuario va SIN dominio ('Alfredo', no 'NUEVAVISION\Alfredo').
--   2. Hace falta la fila '(defecto)' con el valor SEGURO para que el parametro exista y se propague.

SET NOCOUNT ON;
USE NV;

-- 1. El defecto es NESTO: AgenciaASM como siempre.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'GlsPorApi' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'GlsPorApi', '(defecto)', 'NESTO', SYSTEM_USER, GETDATE());
END

-- 2. El piloto, de momento TAMBIEN apagado (NESTO). Usuario SIN dominio.
DECLARE @Piloto varchar(30) = 'Alfredo';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'GlsPorApi' AND Usuario = @Piloto)
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'GlsPorApi', @Piloto, 'NESTO', SYSTEM_USER, GETDATE());
END

-- Solo 'API' (recortado y sin distinguir mayusculas) activa el camino nuevo. Cualquier otro valor, la ausencia de fila o un
-- fallo al leer el parametro llevan al camino de siempre.

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación] AS FMod
FROM ParametrosUsuario WHERE Clave = 'GlsPorApi' ORDER BY Usuario;

/*
    ENCENDERLO PARA EL PILOTO (con la API ya publicada con Agencias:GlsTramitarPorApi = true):

    UPDATE ParametrosUsuario SET Valor = 'API', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'GlsPorApi' AND Usuario = 'Alfredo';

    Nesto lee el parametro en cada etiqueta y en cada tramitacion: no hace falta reiniciar.
    APAGARLO (vuelta atras inmediata): el mismo UPDATE con Valor = 'NESTO'.

    PRIMERA PRUEBA: una etiqueta de GLS del piloto comparada con una de Nesto (misma plaza, TSP y codigo de barras), y un
    «Tramitar todos» al cierre. COMPROBACION en los 15 minutos siguientes al cierre:
      - Cada envio de GLS de hoy en Estado 1 (tramitado), con Fecha de hoy.
      - Una fila por envio en AgenciasLlamadasWeb (Agencia 'ASM', Exito 1, 'OK'), como las de Nesto: la sombra diaria las sigue
        leyendo igual.
      - Nada en ELMAH salvo avisos ya conocidos.

    DECLARE @Desde datetime = DATEADD(minute, -15, GETDATE());

    SELECT 'ENVIOS' AS Paso, Numero, Pedido, Agencia, Estado, Fecha, Bultos, Reembolso, RTRIM(CodigoBarras) AS CodigoBarras,
           RTRIM(Usuario) AS Usuario, FechaModificacion
    FROM EnviosAgencia
    WHERE Agencia = 1 AND FechaModificacion >= @Desde
    ORDER BY Numero;

    SELECT 'LLAMADAS' AS Paso, Id, Fecha, Exito, LEFT(TextoRespuestaError, 80) AS Respuesta, RTRIM(Usuario) AS Usuario
    FROM AgenciasLlamadasWeb
    WHERE Agencia = 'ASM' AND Fecha >= @Desde
    ORDER BY Fecha;
*/
