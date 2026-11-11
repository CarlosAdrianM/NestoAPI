-- =============================================================================
-- NestoAPI#495: el aviso de novedades pasa de «versión posterior a la última vista» a «novedades con Id mayor
-- que la última que vi» (GET api/Novedades?ambitos=NestoApp,NestoAPI&desdeId=n). Cada cliente guarda el Id en
-- su parámetro de usuario: UltimaNovedadVistaApp (NestoApp) y UltimaNovedadVista (Nesto).
--
-- El GET de ParametrosUsuario copia la fila «(defecto)» al usuario la primera vez que lo lee: sin ella da 404.
-- Valor vacío = bootstrap silencioso (el cliente guarda el Id máximo sin enseñar nada).
--
-- Ejecutar en SSMS contra NV. Se puede lanzar antes o después de publicar la API; ANTES de publicar NestoApp.
-- Se puede lanzar varias veces.
-- =============================================================================
USE NV;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'UltimaNovedadVistaApp')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'UltimaNovedadVistaApp', '', SUSER_SNAME(), GETDATE());
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'UltimaNovedadVista')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'UltimaNovedadVista', '', SUSER_SNAME(), GETDATE());
GO

SELECT Clave, Valor FROM dbo.ParámetrosUsuario
WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave IN ('UltimaNovedadVistaApp', 'UltimaNovedadVista');
