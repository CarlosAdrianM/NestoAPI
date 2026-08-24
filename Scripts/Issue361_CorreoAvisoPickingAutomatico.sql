-- NestoAPI#361: destinatarios del aviso del picking AUTOMÁTICO de las 11h.
--
-- Contexto: ese picking lo lanza una tarea del Task Scheduler, sin nadie mirando la pantalla.
-- Si no salía picking, el almacén no podía distinguir entre "no había nada que sacar", "ha
-- fallado algo" y "la tarea ni se ha ejecutado", y acababa preguntando a Informática, que lo
-- miraba en ELMAH. Ahora la API avisa por correo en los dos primeros casos, así que el silencio
-- pasa a significar una sola cosa: la tarea no se ejecutó.
--
-- Este parámetro solo decide QUIÉN recibe el aviso, para poder cambiarlo sin desplegar. Si la
-- fila no existe o está vacía, el aviso va igualmente a Constantes.Correos.ALMACEN
-- (almacen@nuevavision.es), así que el script NO es imprescindible para que funcione.
--
-- Varias direcciones separadas por ; o por coma.
--
-- ⚠️ ORDEN SEGURO: se puede ejecutar antes o después de desplegar la API; es inocuo en ambos
-- casos. BD: NV (NestoConnection). Sin GRANTs (INSERT de datos).

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'CorreoAvisoPickingAutomatico' AND Usuario = '(defecto)')
BEGIN
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'CorreoAvisoPickingAutomatico', '(defecto)', 'almacen@nuevavision.es', 'NestoAPI', GETDATE());
END
GO

-- VERIFICACIÓN (debe devolver 1 fila con Valor = 'almacen@nuevavision.es'):
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Clave = 'CorreoAvisoPickingAutomatico' AND Usuario = '(defecto)';
GO

-- Para añadir a alguien más (ejemplo), basta con actualizar el valor:
-- UPDATE ParametrosUsuario
--    SET Valor = 'almacen@nuevavision.es;carlosadrian@nuevavision.es', [Fecha Modificación] = GETDATE()
--  WHERE Empresa = '1' AND Clave = 'CorreoAvisoPickingAutomatico' AND Usuario = '(defecto)';
