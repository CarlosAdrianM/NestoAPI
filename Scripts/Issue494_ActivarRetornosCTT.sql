-- =============================================================================
-- NestoAPI#494: interruptor de las recogidas y retornos por CTT Express. Ejecutar en SSMS contra NV
-- SOLO cuando esté decidido quién imprime/entrega la etiqueta de una recogida (ver la issue) y el
-- comercial de CTT haya confirmado el uso de has_pickup_asap y del adicional RET en nuestra cuenta.
--
-- Mientras el parámetro no exista o no valga '1', un envío de CTT con tipo de retorno distinto de
-- «NO» se rechaza al tramitar con un mensaje claro (nunca sale como envío normal).
--   Retorno 1 = «Con retorno»: se entrega y el repartidor trae algo de vuelta (adicional RET).
--   Retorno 2 = «Recogida en origen»: CTT recoge en casa del cliente/proveedor y lo trae a Algete.
--
-- No hace falta publicar nada: la API lee el parámetro en cada tramitación.
-- VUELTA ATRÁS: UPDATE ... SET Valor = '0' (misma clave).
-- =============================================================================

USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTRetornosActivos')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'CTTRetornosActivos', '1', 'NestoAPI#494', GETDATE());
ELSE
    UPDATE dbo.ParámetrosUsuario SET Valor = '1', Usuario2 = 'NestoAPI#494', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTRetornosActivos';

-- Comprobación
SELECT Empresa, Usuario, Clave, Valor FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'CTTRetornosActivos';
