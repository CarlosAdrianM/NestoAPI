-- OneShot 05/11/26 (sugerencia 1065, Alfredo): los pedidos de la empresa 3 salen «a diario» porque la ficha del cliente en la
-- empresa 3 no lleva la misma ruta que en la 1. La ruta propia (AT, 16) es un dato de reparto, igual para las dos empresas.
-- Copia a la empresa 3 la ruta de la empresa 1 en los contactos que en la 1 van por ruta propia y en la 3 tienen otra
-- (05/11/26: 54 FW + 1 00 + 1 MD + 1 NULL con la 16, y 1 FW con la AT = 58). Solo cambia Ruta; no toca pedidos ya grabados.
-- Ejecutar como sa en NV. Idempotente. Revisar el ANTES; si algún contacto debe seguir distinto en la 3, quitarlo del UPDATE.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

SELECT 'ANTES' Momento, RTRIM(c1.Ruta) Ruta1, RTRIM(c3.Ruta) Ruta3, COUNT(*) Contactos
FROM dbo.Clientes c1
JOIN dbo.Clientes c3 ON c3.Empresa = '3' AND c3.[Nº Cliente] = c1.[Nº Cliente] AND c3.Contacto = c1.Contacto
WHERE c1.Empresa = '1' AND c1.Ruta IN ('AT', '16')
GROUP BY c1.Ruta, c3.Ruta ORDER BY 2, 3;

SELECT RTRIM(c3.[Nº Cliente]) Cliente, RTRIM(c3.Contacto) Contacto, RTRIM(c3.Nombre) Nombre, RTRIM(c3.CodPostal) CP,
       RTRIM(c1.Ruta) Ruta1, RTRIM(c3.Ruta) Ruta3, c3.Estado Estado3
FROM dbo.Clientes c1
JOIN dbo.Clientes c3 ON c3.Empresa = '3' AND c3.[Nº Cliente] = c1.[Nº Cliente] AND c3.Contacto = c1.Contacto
WHERE c1.Empresa = '1' AND c1.Ruta IN ('AT', '16') AND ISNULL(c3.Ruta, '') <> c1.Ruta
ORDER BY c3.[Nº Cliente], c3.Contacto;

UPDATE c3 SET c3.Ruta = c1.Ruta
FROM dbo.Clientes c3
JOIN dbo.Clientes c1 ON c1.Empresa = '1' AND c1.[Nº Cliente] = c3.[Nº Cliente] AND c1.Contacto = c3.Contacto
WHERE c3.Empresa = '3' AND c1.Ruta IN ('AT', '16') AND ISNULL(c3.Ruta, '') <> c1.Ruta;
PRINT CONCAT('Fichas de la empresa 3 actualizadas: ', @@ROWCOUNT);

SELECT 'DESPUES' Momento, RTRIM(c1.Ruta) Ruta1, RTRIM(c3.Ruta) Ruta3, COUNT(*) Contactos
FROM dbo.Clientes c1
JOIN dbo.Clientes c3 ON c3.Empresa = '3' AND c3.[Nº Cliente] = c1.[Nº Cliente] AND c3.Contacto = c1.Contacto
WHERE c1.Empresa = '1' AND c1.Ruta IN ('AT', '16')
GROUP BY c1.Ruta, c3.Ruta ORDER BY 2, 3;

COMMIT;
GO
