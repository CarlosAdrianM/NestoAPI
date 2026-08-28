-- Nesto#340 (Agencias, slice A4.1): cerrar el envio y contabilizar su reembolso contra NestoAPI
-- (POST api/EnviosAgencias/{id}/ConfirmarTramitacion) en vez de por Entity Framework desde el
-- cliente.
--
-- CORREGIDO EL 28/08/2026. La version anterior de este script estaba mal por partida doble y por
-- eso el piloto no fue tal:
--
--   1. Ponia el usuario como 'NUEVAVISION\Carlos'. En ParametrosUsuario el usuario va SIN dominio:
--      'Carlos'. Esa fila no la leia nadie.
--   2. No creaba la fila '(defecto)'. Un parametro sin '(defecto)' NO se propaga: no se crea para
--      ningun usuario y no hay nada que leer.
--
-- Al arreglarlo a mano se puso '(defecto)' = 'API', y eso activo el camino nuevo para TODA la
-- plantilla. El 28/08 a las 12:48 Enrique tramito 15 envios con el: GLS los acepto todos y el
-- cierre en Nesto reventó (columna 'Numero' duplicada en 'Limit1', ver TramitacionEnviosService).
-- Los 15 quedaron registrados en la agencia y abiertos en Nesto.
--
-- La forma correcta de pilotar con este sistema es: '(defecto)' con el valor SEGURO (para que el
-- parametro exista y se propague) y una fila por cada usuario piloto con el valor nuevo.

SET NOCOUNT ON;
USE NV;

-- 1. El defecto es EF: el camino de siempre, para todo el que no sea piloto.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'TramitarEnvioPorApi' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'TramitarEnvioPorApi', '(defecto)', 'EF', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'EF', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'TramitarEnvioPorApi' AND Usuario = '(defecto)';
END

-- 2. Solo el piloto va por la API. Usuario SIN dominio.
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario
               WHERE Empresa = '1' AND Clave = 'TramitarEnvioPorApi' AND Usuario = 'Carlos')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'TramitarEnvioPorApi', 'Carlos', 'API', SYSTEM_USER, GETDATE());
END
ELSE
BEGIN
    UPDATE ParametrosUsuario SET Valor = 'API', Usuario2 = SYSTEM_USER, [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'TramitarEnvioPorApi' AND Usuario = 'Carlos';
END

-- Solo 'API' (recortado y sin distinguir mayusculas) activa el camino nuevo. Cualquier otro valor,
-- la ausencia de fila o un fallo al leer el parametro llevan al camino de siempre.
--
-- ATENCION antes de ampliar el piloto: este flujo CONTABILIZA (prdContabilizar). Tramitar un envio
-- CON REEMBOLSO y comprobar el asiento en el diario _Reembolso (mismo importe, misma contrapartida
-- y, si liquidaba un movimiento del extracto, mismo Liquidado) ANTES de tocar el defecto.

SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor, [Fecha Modificación] AS FMod
FROM ParametrosUsuario WHERE Clave = 'TramitarEnvioPorApi' ORDER BY Usuario;
