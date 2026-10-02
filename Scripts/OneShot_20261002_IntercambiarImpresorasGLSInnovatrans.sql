-- 02/10/26: intercambiar las impresoras de etiquetas de ASM/GLS e Innovatrans para Alfredo y Andre (empresa 1).
--   ASM/GLS      -> ImpresoraAgenciaGLS (hoy \\RDS2016\Etiquetas3)
--   Innovatrans  -> ImpresoraBolsas     (hoy \\RDS2016\Etiquetas4)
-- OJO: ImpresoraBolsas también la usan Correos Express y OnTime: se van con Innovatrans a la otra impresora.
-- Solo empresa 1: en las demás empresas no tienen ImpresoraAgenciaGLS.

BEGIN TRAN;

SELECT Empresa, Usuario, Clave, RTRIM(Valor) AS Antes
FROM ParametrosUsuario
WHERE Empresa = '1' AND RTRIM(Usuario) IN ('Alfredo', 'Andre') AND Clave IN ('ImpresoraAgenciaGLS', 'ImpresoraBolsas')
ORDER BY Usuario, Clave;

UPDATE p
SET Valor = o.Valor
FROM ParametrosUsuario p
JOIN ParametrosUsuario o
  ON o.Empresa = p.Empresa
 AND RTRIM(o.Usuario) = RTRIM(p.Usuario)
 AND o.Clave = CASE p.Clave WHEN 'ImpresoraAgenciaGLS' THEN 'ImpresoraBolsas' ELSE 'ImpresoraAgenciaGLS' END
WHERE p.Empresa = '1' AND RTRIM(p.Usuario) IN ('Alfredo', 'Andre') AND p.Clave IN ('ImpresoraAgenciaGLS', 'ImpresoraBolsas');
-- Tienen que salir 4 filas

SELECT Empresa, Usuario, Clave, RTRIM(Valor) AS Despues
FROM ParametrosUsuario
WHERE Empresa = '1' AND RTRIM(Usuario) IN ('Alfredo', 'Andre') AND Clave IN ('ImpresoraAgenciaGLS', 'ImpresoraBolsas')
ORDER BY Usuario, Clave;

-- Si está bien: COMMIT. Si no: ROLLBACK.
-- COMMIT;
-- ROLLBACK;
