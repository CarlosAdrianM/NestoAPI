-- NestoAPI#534 (corte 1): interruptor del aviso automático de facturas vencidas por transferencia.
-- El job 'aviso-facturas-vencidas' de Hangfire corre de lunes a viernes a las 7:45, pero NO HACE NADA
-- mientras no exista esta fila (sin fila = APAGADO, que es como nace al publicar).
--
-- Este script lo pone en modo SOMBRA: cada mañana manda UN correo a administracion@nuevavision.es con
-- la lista de efectos a los que se avisaría (y los que se quedan fuera y por qué) y el correo que
-- recibiría el primero. NO escribe a ningún cliente ni registra nada (eso es el corte 2).
-- Se lee en cada ejecución: el cambio se nota en la siguiente pasada, sin publicar ni reiniciar.
-- Ejecutar en NV (NestoConnection). Idempotente.
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AvisoFacturasVencidas')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'AvisoFacturasVencidas', 'Sombra', 'NestoAPI#534', GETDATE());
ELSE
    UPDATE dbo.ParámetrosUsuario SET Valor = 'Sombra', Usuario2 = 'NestoAPI#534', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AvisoFacturasVencidas';

-- Comprobación
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave LIKE 'AvisoFacturasVencidas%';
GO

-- APAGAR (vuelve a no hacer nada):
-- UPDATE dbo.ParámetrosUsuario SET Valor = '0', Usuario2 = 'NestoAPI#534', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AvisoFacturasVencidas';

-- OPCIONAL: cambiar el umbral de días desde el vencimiento (sin fila = 5 días):
-- IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'AvisoFacturasVencidasDias')
--     INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
--     VALUES ('1', '(defecto)', 'AvisoFacturasVencidasDias', '5', 'NestoAPI#534', GETDATE());

-- PROBAR SIN ESPERAR A LAS 7:45: en el panel de Hangfire, Recurring jobs → 'aviso-facturas-vencidas' → Trigger now.
