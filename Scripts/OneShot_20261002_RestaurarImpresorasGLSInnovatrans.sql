-- 02/10/26: deshace OneShot_20261002_IntercambiarImpresorasGLSInnovatrans.sql.
-- Deja las impresoras de Alfredo y Andre (empresa 1) como estaban al principio:
--   ASM/GLS      -> ImpresoraAgenciaGLS = \\RDS2016\Etiquetas3
--   Innovatrans  -> ImpresoraBolsas     = \\RDS2016\Etiquetas4  (también Correos Express y OnTime)

BEGIN TRAN;

UPDATE ParametrosUsuario
SET Valor = CASE Clave WHEN 'ImpresoraAgenciaGLS' THEN '\\RDS2016\Etiquetas3' ELSE '\\RDS2016\Etiquetas4' END
WHERE Empresa = '1' AND RTRIM(Usuario) IN ('Alfredo', 'Andre') AND Clave IN ('ImpresoraAgenciaGLS', 'ImpresoraBolsas');
-- Tienen que salir 4 filas

SELECT Empresa, Usuario, Clave, RTRIM(Valor) AS Despues
FROM ParametrosUsuario
WHERE Empresa = '1' AND RTRIM(Usuario) IN ('Alfredo', 'Andre') AND Clave IN ('ImpresoraAgenciaGLS', 'ImpresoraBolsas')
ORDER BY Usuario, Clave;

-- Si está bien: COMMIT. Si no: ROLLBACK.
-- COMMIT;
-- ROLLBACK;
