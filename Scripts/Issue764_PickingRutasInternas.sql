/*
    NestoAPI#764: interruptor del picking AUTOMÁTICO de las rutas internas (AT/16, las de RutasRepartoPropio) a las 9:00.

    Job de Hangfire «picking-rutas-internas» (L-V 9:00, Romance Standard Time, solo laborables de Algete). Es el mismo picking
    que el botón «Solo nuestra ruta» de Nesto, con la forma del automático de las 11 (correo al almacén si no sale nada o
    falla). De los pedidos con día de reparto por código postal (dbo.CodigosPostalesDiaReparto), solo los que tocan hoy.

    Nace APAGADO: sin la fila, o con Valor distinto de '1', el job no hace nada. Se lee en cada ejecución: encender y apagar
    no necesita publicar. Va en ParametrosUsuario «(defecto)» (no en el Web.config, que vuelve a su valor al publicar).

    Uso (como sa en NV; idempotente):
      @Encender = 1 → '1' (encendido)
      @Encender = 0 → '0' (apagado). Primero se crea siempre la fila «(defecto)» a '0' si no existe.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @Encender bit = 1;

-- Antes
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PickingAutomaticoRutasInternas';

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PickingAutomaticoRutasInternas')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'PickingAutomaticoRutasInternas', '(defecto)', '0', 'Issue764', GETDATE());
END

UPDATE ParametrosUsuario
   SET Valor = CASE WHEN @Encender = 1 THEN '1' ELSE '0' END, Usuario2 = 'Issue764', [Fecha Modificación] = GETDATE()
 WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PickingAutomaticoRutasInternas';

-- Después
SELECT Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación]
FROM ParametrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'PickingAutomaticoRutasInternas';

-- Comprobación de la tabla de días (debe tener filas 01..05)
SELECT RTRIM(Ruta) Ruta, COUNT(*) CodigosPostales FROM dbo.CodigosPostalesDiaReparto WITH (NOLOCK) WHERE Empresa = '1' GROUP BY RTRIM(Ruta) ORDER BY 1;
