-- NestoAPI#823 (05/11/26), segunda parte: una fila de FechasPermitidas por PERSONA. Ejecutar como sa en NV DESPUÉS de publicar
-- la API con #823 (antes, borrar las filas sin dominio dejaría a Andre sin fechas desde Ariadna).
--
-- 1) Andre tiene DOS identidades en FechasPermitidas («Andre» y «NUEVAVISION\Andre», empresas 1 y 3). Con #823 la API resuelve
--    «Andre» a «NUEVAVISION\Andre», así que las filas sin dominio sobran: se borran.
-- 2) Pedro tiene cuenta de dominio (NUEVAVISION\pedro aparece en LogModificaciones) pero NINGUNA fila en FechasPermitidas, ni con
--    dominio ni sin él, y por eso #823 no lo resuelve y desde Ariadna da «Fecha no permitida». Se le crea su única fila
--    (NUEVAVISION\pedro, empresas 1 y 3) copiando las fechas de NUEVAVISION\Andre; a partir de ahí la API lo resuelve sola.
-- 3) Miguel NO tiene cuenta de dominio (sin rastro en ninguna tabla): su fila «Miguel» es la única identidad y se queda.
-- Idempotente. Comprueba al final que ningún usuario sin dominio tenga gemelo con dominio.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

-- 2) Pedro: su única fila, copiada de NUEVAVISION\Andre (misma empresa)
INSERT INTO dbo.FechasPermitidas
    (Empresa, Usuario, FechaInicialCnt, FechaFinalCnt, FechaInicialVta, FechaFinalVta, FechaInicialCmp, FechaFinalCmp,
     FechaInicialRapports, FechaFinalRapports, FechaInicialExtrProd, FechaFinalExtrProd, FechaInicialBanco, FechaFinalBanco,
     FechaInicialPagare, FechaFinalPagare, Usuario2, [Fecha Modificación])
SELECT a.Empresa, 'NUEVAVISION\pedro', a.FechaInicialCnt, a.FechaFinalCnt, a.FechaInicialVta, a.FechaFinalVta, a.FechaInicialCmp, a.FechaFinalCmp,
       a.FechaInicialRapports, a.FechaFinalRapports, a.FechaInicialExtrProd, a.FechaFinalExtrProd, a.FechaInicialBanco, a.FechaFinalBanco,
       a.FechaInicialPagare, a.FechaFinalPagare, 'NestoAPI#823 05/11/26', GETDATE()
FROM dbo.FechasPermitidas a
WHERE a.Usuario = 'NUEVAVISION\Andre'
  AND NOT EXISTS (SELECT 1 FROM dbo.FechasPermitidas f WHERE f.Empresa = a.Empresa AND f.Usuario IN ('NUEVAVISION\pedro', 'Pedro'));
PRINT CONCAT('Filas creadas para NUEVAVISION\pedro: ', @@ROWCOUNT, ' (se esperan 2)');

-- 1) Filas sin dominio que tienen gemelo con dominio en la misma empresa (hoy: Andre en 1 y 3)
DELETE f
FROM dbo.FechasPermitidas f
WHERE f.Usuario NOT LIKE '%\%'
  AND EXISTS (SELECT 1 FROM dbo.FechasPermitidas g WHERE g.Empresa = f.Empresa AND g.Usuario = 'NUEVAVISION\' + f.Usuario);
PRINT CONCAT('Filas sin dominio borradas (con gemelo): ', @@ROWCOUNT, ' (se esperan 2)');

-- Comprobación: ninguna fila sin dominio debe tener gemelo
SELECT f.Empresa, f.Usuario,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.FechasPermitidas g WHERE g.Usuario = 'NUEVAVISION\' + f.Usuario) THEN 'TIENE GEMELO' ELSE 'ok' END Estado
FROM dbo.FechasPermitidas f WHERE f.Usuario NOT LIKE '%\%' ORDER BY f.Usuario, f.Empresa;
SELECT Empresa, Usuario, CONVERT(varchar(10), FechaInicialVta, 120) IniVta, CONVERT(varchar(10), FechaFinalVta, 120) FinVta
FROM dbo.FechasPermitidas WHERE Usuario IN ('NUEVAVISION\pedro', 'NUEVAVISION\Andre', 'Miguel') ORDER BY Usuario, Empresa;

COMMIT;
GO
