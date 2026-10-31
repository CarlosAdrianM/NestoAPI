-- 01/10/26: los packs nuevos de la campaña a la categoría «Pack Regalo» (COS/PRG) de la tienda.
--
-- «Pack Regalo» es una categoría SECUNDARIA (NestoAPI#414): la tienen 122 productos y ninguno como principal.
-- Así que se AÑADE como última categoría secundaria de cada producto; no se toca su grupo/subgrupo principal
-- (hoy COS/025 «Cremas mantenimiento») ni otras secundarias que tuviera. Hace lo mismo que guardar desde la
-- pantalla de Nesto (PUT api/ProductosCategoriasSecundarias): la fila en ProductosCategoriasSecundarias y el
-- producto encolado en Nesto_sync, para que la pasada de los 5 minutos lo republique en PrestaShop.
--
-- Idempotente: si un producto ya tiene Pack Regalo, no se toca; y no se reencola lo que ya está pendiente.
-- Ejecutar en NV como sa (o con un usuario con INSERT en las dos tablas).
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Usuario varchar(30) = SUSER_SNAME();
DECLARE @Packs TABLE (Producto varchar(15) PRIMARY KEY);
INSERT INTO @Packs (Producto) VALUES
    ('45932'), ('45930'), ('45929'), ('45931'), ('45914'), ('45913'), ('45912'), ('45911'), ('45910'),
    ('45909'), ('45908'), ('45907'), ('45856'), ('45699'), ('45698'), ('45697'), ('45696'), ('45695'),
    ('45694'), ('45693'), ('45692'), ('45691'), ('45918'), ('45917'), ('45916');

-- Comprobaciones previas: que existan todos y que la categoría exista.
IF NOT EXISTS (SELECT 1 FROM SubGruposProducto WHERE Empresa = '1' AND Grupo = 'COS' AND Número = 'PRG')
BEGIN
    RAISERROR('No existe el subgrupo COS/PRG (Pack Regalo). No se cambia nada.', 16, 1);
    RETURN;
END

DECLARE @NoExisten int = (SELECT COUNT(*) FROM @Packs k
                          WHERE NOT EXISTS (SELECT 1 FROM Productos p WHERE p.Empresa = '1' AND p.Número = k.Producto));
IF @NoExisten > 0
BEGIN
    SELECT k.Producto AS NoExiste FROM @Packs k
    WHERE NOT EXISTS (SELECT 1 FROM Productos p WHERE p.Empresa = '1' AND p.Número = k.Producto);
    RAISERROR('Hay %d productos de la lista que no existen. No se cambia nada.', 16, 1, @NoExisten);
    RETURN;
END

BEGIN TRANSACTION;

-- 1. Pack Regalo como última categoría secundaria de cada producto que aún no la tenga.
INSERT INTO ProductosCategoriasSecundarias (Empresa, Número, Orden, Grupo, SubGrupo, Usuario, [Fecha Modificación])
SELECT '1', k.Producto,
       ISNULL((SELECT MAX(pc.Orden) FROM ProductosCategoriasSecundarias pc WHERE pc.Empresa = '1' AND pc.Número = k.Producto), 0) + 1,
       'COS', 'PRG', @Usuario, GETDATE()
FROM @Packs k
WHERE NOT EXISTS (SELECT 1 FROM ProductosCategoriasSecundarias pc
                  WHERE pc.Empresa = '1' AND pc.Número = k.Producto AND pc.Grupo = 'COS' AND pc.SubGrupo = 'PRG');
DECLARE @Anadidos int = @@ROWCOUNT;

-- 2. Encolar en Nesto_sync para que se republiquen en la tienda (misma guarda que la API).
INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
SELECT 'Productos', p.Número, @Usuario, GETDATE()   -- p.Número va con el relleno, como el resto de filas
FROM @Packs k
JOIN Productos p ON p.Empresa = '1' AND p.Número = k.Producto
WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                  WHERE ns.Tabla = 'Productos' AND ns.ModificadoId = k.Producto AND ns.Sincronizado IS NULL);
DECLARE @Encolados int = @@ROWCOUNT;

COMMIT TRANSACTION;

PRINT CONCAT('Pack Regalo añadido a ', @Anadidos, ' productos; ', @Encolados, ' encolados para la tienda.');

-- Comprobación: los 25 deben salir con Pack Regalo.
SELECT RTRIM(p.Número) AS Producto, LEFT(p.Nombre, 45) AS Nombre, p.SubGrupo AS SubgrupoPrincipal,
       pc.Orden AS OrdenPackRegalo
FROM @Packs k
JOIN Productos p ON p.Empresa = '1' AND p.Número = k.Producto
LEFT JOIN ProductosCategoriasSecundarias pc ON pc.Empresa = '1' AND pc.Número = k.Producto
     AND pc.Grupo = 'COS' AND pc.SubGrupo = 'PRG'
ORDER BY p.Número;
GO
