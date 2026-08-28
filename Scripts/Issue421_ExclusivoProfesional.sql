-- NestoAPI#421: "exclusivo profesional" como campo propio de la ficha de producto.
-- El producto se ve en la tienda, pero sin precio ni botón de compra para quien no es
-- profesional. Antes se pretendía deducir de las categorías secundarias (patrones */EP*,
-- APA/EXP, PEL/EXP) y era falso: esos subgrupos son categorías navegables normales cuyos
-- productos SÍ se venden al público. Ver prestashop-nestosync#19.
--
-- PENDIENTE DE EJECUTAR EN PRODUCCIÓN.

-- Arranca en 0 para todos: nadie está restringido hasta que se marque a mano en la ficha.
-- NOT NULL a propósito: el mensaje de sincronización manda siempre valor explícito, y el
-- null del contrato ("no toques la marca") es una salvaguarda del consumidor, no un estado
-- que queramos poder guardar aquí.
ALTER TABLE dbo.Productos
    ADD ExclusivoProfesional bit NOT NULL
        CONSTRAINT DF_Productos_ExclusivoProfesional DEFAULT (0);
GO

-- No hay GRANT que añadir: el API ya tiene permisos sobre dbo.Productos.

-- OJO, dos cosas del trigger trgProductosUpd que conviene tener presentes y que NO se tocan:
--   1. Su bloque de sincronización solo mira Nombre, CodBarras, Grupo, Subgrupo, Familia,
--      UnidadMedida, PVP, Estado, Tamaño, Ficticio y RoturaStockProveedor. ExclusivoProfesional
--      no está, así que un UPDATE suelto por SSMS NO encola el producto para la web. El
--      endpoint PUT api/Productos/ExclusivoProfesional llama a EncolarProductoSync
--      explícitamente, que es como lo hace el mantenimiento de familias de #406.
--   2. El trigger pone Revisado = 0 al modificar cualquier campo que no sea Revisado,
--      PrecioMedio ni RoturaStockProveedor. Marcar la casilla desmarca "revisado" del
--      producto, igual que pasa hoy al tocar cualquier otro dato de la ficha.

-- Comprobación
SELECT COUNT(*) AS Productos, SUM(CAST(ExclusivoProfesional AS int)) AS Marcados
FROM dbo.Productos WHERE Empresa = '1';
