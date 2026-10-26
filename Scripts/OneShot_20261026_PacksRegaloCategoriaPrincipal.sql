-- 26/10/26, sugerencia 780 (Laura): los packs de regalo, con «Pack Regalo» (COS/PRG) como categoría PRINCIPAL.
--
-- Hasta hoy Pack Regalo era una categoría SECUNDARIA (NestoAPI#414, OneShot_20261001_PacksACategoriaPackRegalo.sql)
-- y la principal salía del subgrupo de la ficha (casi siempre COS/025 «Cremas mantenimiento»). Decisión de Carlos:
--   - Solo los productos del grupo COS que están vivos (Estado >= 0) y tienen Pack Regalo como secundaria.
--     Los de peluquería (PEL/TRA, PEL/APA) NO se tocan: cambiar el grupo no se contempla; lo deciden Laura/Manuel.
--   - Se cambia su subgrupo a PRG y se les quitan TODAS las categorías secundarias (solo deben estar en Pack Regalo).
--   - El trigger trgProductosUpd encola cada producto en Nesto_sync (empresa 1) al cambiar el subgrupo, así que la
--     pasada de los 5 minutos los republica en PrestaShop con la categoría nueva. El trigger solo deja cambiar el
--     subgrupo a unos usuarios concretos o a sa: EJECUTAR COMO sa.
--   - @TambienEmpresa3 = 1 cambia también el subgrupo de la copia de la ficha en la empresa 3 (55 de ellos la tienen).
--
-- Comprobado el 26/10 (solo lectura): 81 productos, 110 filas de categorías secundarias a borrar.
-- Idempotente: si se vuelve a lanzar, ya no quedan productos con Pack Regalo como secundaria y no cambia nada.
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @TambienEmpresa3 bit = 1;   -- Carlos 26/10: también la empresa 3

IF NOT EXISTS (SELECT 1 FROM SubGruposProducto WHERE Empresa = '1' AND Grupo = 'COS' AND Número = 'PRG')
BEGIN
    RAISERROR('No existe el subgrupo COS/PRG (Pack Regalo). No se cambia nada.', 16, 1);
    RETURN;
END

DECLARE @Packs TABLE (Producto varchar(15) PRIMARY KEY, SubgrupoAntes varchar(3));
INSERT INTO @Packs (Producto, SubgrupoAntes)
SELECT p.Número, p.SubGrupo
FROM Productos p
WHERE p.Empresa = '1' AND p.Grupo = 'COS' AND p.Estado >= 0
  AND EXISTS (SELECT 1 FROM ProductosCategoriasSecundarias s
              WHERE s.Empresa = '1' AND s.Número = p.Número AND s.Grupo = 'COS' AND s.SubGrupo = 'PRG');

DECLARE @ACambiar int = (SELECT COUNT(*) FROM @Packs);
PRINT CONCAT('Productos a cambiar: ', @ACambiar, ' (esperados 81).');

BEGIN TRANSACTION;

-- 1. Fuera todas sus categorías secundarias (Pack Regalo incluida: pasa a ser la principal).
DELETE s
FROM ProductosCategoriasSecundarias s
JOIN @Packs k ON s.Empresa = '1' AND s.Número = k.Producto;
DECLARE @Borradas int = @@ROWCOUNT;

-- 2. Subgrupo PRG en la ficha (el trigger lo apunta en logmodificaciones y lo encola en Nesto_sync).
UPDATE p
SET SubGrupo = 'PRG', Usuario = SUSER_SNAME(), [Fecha Modificación] = GETDATE()
FROM Productos p
JOIN @Packs k ON p.Empresa = '1' AND p.Número = k.Producto;
DECLARE @Cambiados int = @@ROWCOUNT;

DECLARE @CambiadosEmpresa3 int = 0;
IF @TambienEmpresa3 = 1
BEGIN
    UPDATE p
    SET SubGrupo = 'PRG', Usuario = SUSER_SNAME(), [Fecha Modificación] = GETDATE()
    FROM Productos p
    JOIN @Packs k ON p.Empresa = '3' AND p.Número = k.Producto
    WHERE p.Grupo = 'COS';
    SET @CambiadosEmpresa3 = @@ROWCOUNT;
END

COMMIT TRANSACTION;

PRINT CONCAT('Subgrupo PRG en ', @Cambiados, ' productos (empresa 3: ', @CambiadosEmpresa3, '); ',
             @Borradas, ' categorías secundarias borradas (esperadas 110).');

-- Comprobación: todos con subgrupo PRG, sin secundarias y encolados para la tienda.
SELECT RTRIM(k.Producto) AS Producto, LEFT(p.Nombre, 45) AS Nombre, k.SubgrupoAntes, p.SubGrupo AS SubgrupoAhora,
       (SELECT COUNT(*) FROM ProductosCategoriasSecundarias s WHERE s.Empresa = '1' AND s.Número = k.Producto) AS Secundarias,
       CASE WHEN EXISTS (SELECT 1 FROM Nesto_sync ns WHERE ns.Tabla = 'Productos' AND ns.ModificadoId = k.Producto
                         AND ns.Sincronizado IS NULL) THEN 'Sí' ELSE 'NO' END AS Encolado
FROM @Packs k
JOIN Productos p ON p.Empresa = '1' AND p.Número = k.Producto
ORDER BY k.Producto;
GO
