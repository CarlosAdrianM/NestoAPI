-- NestoAPI#499: interruptor del guardarraíl de altas de clientes (dirección verificada por Google).
-- NO EJECUTAR hasta que Nesto#480 y NestoApp#180 estén desplegados y no queden versiones viejas:
-- con el interruptor encendido, cualquier cliente que no mande DireccionVerificada = true no podrá dar de alta.
-- Ejecutar en NV (NestoConnection). Para apagarlo: UPDATE ... SET Valor = '0'.
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ExigirDireccionVerificadaAlta')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'ExigirDireccionVerificadaAlta', '1', 'NestoAPI#499', GETDATE());
ELSE
    UPDATE dbo.ParámetrosUsuario SET Valor = '1', Usuario2 = 'NestoAPI#499', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ExigirDireccionVerificadaAlta';

-- Comprobación
SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave = 'ExigirDireccionVerificadaAlta';
GO
