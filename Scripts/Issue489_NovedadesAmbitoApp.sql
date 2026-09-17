-- =============================================================================
-- NestoAPI#489: el changelog de NestoApp sale de la misma tabla Novedades (Nesto#372) con ámbito
-- 'NestoApp' y su propio espacio de versiones (2.x). Ejecutar en SSMS contra NV tras publicar la API.
-- =============================================================================
USE NV;
GO

-- 1. Convención de ámbitos como CHECK. Hoy solo hay 'Nesto', 'NestoAPI' y 'NestoApp' (comprobado 17/09/26).
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Novedades_Ambito')
    ALTER TABLE dbo.Novedades WITH CHECK
        ADD CONSTRAINT CK_Novedades_Ambito CHECK (Ambito IN (N'Nesto', N'NestoAPI', N'NestoApp'));
GO

-- 2. Última versión de NestoApp cuyas novedades vio el usuario (fase 2 de NestoApp: popup «qué hay de
--    nuevo»). Clave separada de UltimaVersionNovedades porque un usuario con Nesto y NestoApp pisaría la
--    misma fila con versiones de productos distintos. Valor vacío = bootstrap silencioso.
IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'UltimaVersionNovedadesApp')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'UltimaVersionNovedadesApp', '', SUSER_SNAME(), GETDATE());
GO

SELECT Clave, Valor FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave LIKE 'UltimaVersionNovedades%';
