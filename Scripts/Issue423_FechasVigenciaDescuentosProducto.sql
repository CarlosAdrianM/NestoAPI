/*
    NestoAPI#423 (Slice 1) - fechas de vigencia en DescuentosProducto.

    POR QUE
    -------
    Hoy las campanas comerciales viven en las reglas de catalogo de PrestaShop, que caducan solas.
    Para que Nesto pueda ser la fuente de la verdad de los descuentos sin ser un paso atras, sus
    filas necesitan poder caducar igual: si no, cada campana hay que apagarla a mano (el 31/08/2026
    hubo que borrar 2.017 filas de las rebajas de verano a pelo).

    SEMANTICA
    ---------
    NULL = sin limite por ese lado. Una fila vigente hoy es:

        (FechaDesde IS NULL OR FechaDesde <= hoy) AND (FechaHasta IS NULL OR FechaHasta >= hoy)

    Por tanto NULL/NULL = SIEMPRE VIGENTE, que es el comportamiento actual: las 48.870 filas que
    ya existen no cambian de comportamiento al aplicar el script. La vigencia es "opt-in".

    Las dos fechas son INCLUSIVAS y se comparan contra el DIA (DateTime.Today en C#): una campana
    con FechaHasta = '31/08/2026' vale todo el dia 31 y deja de valer el 1 de septiembre. Por eso
    el tipo es `date` y no `datetime`: no hay hora que confunda, ni campanas que caduquen a
    medianoche a mitad de un pedido.

    A QUE FILAS APLICA
    ------------------
    A TODAS, sea cual sea su nivel: producto, familia, familia+grupo, grupo+cliente, cliente,
    contacto y proveedor (compras). La vigencia es una propiedad de la FILA, no del nivel. Esa es
    la decision de Carlos del 31/08/2026: si solo la respetaran unos niveles, el motor de precios
    quedaria incoherente consigo mismo.

    QUIEN LA RESPETA (todos, desde el mismo commit)
    -----------------------------------------------
      - GestorPrecios: los 12 niveles de calculo, mas comprobarCondiciones.
      - ProductoDTO.CargarDescuentosPorAudiencia: lo que viaja por el bus a la tienda.
      - ServicioPrecios: la consulta de descuentos de un producto.
      - PedidosCompraController: los descuentos de compra al crear lineas de pedido a proveedor.

    OJO CON EL DISPARO (Slice 2, todavia NO hecho)
    ----------------------------------------------
    Una campana que caduca por fecha NO modifica ninguna fila, asi que el job nocturno de #410
    -que detecta cambios por [Fecha Modificacion]- no encolaria nada y la tienda se quedaria con
    la oferta puesta. Hasta que exista el job de disparo del Slice 2, cualquier campana con fechas
    hay que republicarla a mano encolando sus productos en Nesto_sync.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) ANTES: foto de la que partimos
-- =============================================================================
PRINT '--- A) Filas actuales por ambito (todas deben quedar NULL/NULL) ---';
SELECT Ambito = CASE WHEN [Nº Cliente] IS NOT NULL THEN 'cliente'
                     WHEN NºProveedor IS NOT NULL THEN 'proveedor (compras)'
                     WHEN [Nº Producto] IS NOT NULL THEN 'tarifa / producto'
                     WHEN Familia IS NOT NULL THEN 'tarifa / familia'
                     WHEN GrupoProducto IS NOT NULL THEN 'tarifa / grupo'
                     ELSE 'otro' END,
       COUNT(*) AS Filas
FROM DescuentosProducto
GROUP BY CASE WHEN [Nº Cliente] IS NOT NULL THEN 'cliente'
              WHEN NºProveedor IS NOT NULL THEN 'proveedor (compras)'
              WHEN [Nº Producto] IS NOT NULL THEN 'tarifa / producto'
              WHEN Familia IS NOT NULL THEN 'tarifa / familia'
              WHEN GrupoProducto IS NOT NULL THEN 'tarifa / grupo'
              ELSE 'otro' END
ORDER BY Filas DESC;

-- =============================================================================
-- 2) LAS DOS COLUMNAS
-- =============================================================================
-- Son NULL y sin DEFAULT a proposito: anadir una columna nullable sin default es
-- metadata-only en SQL Server (no reescribe las 48.870 filas), asi que el ALTER es
-- instantaneo y no bloquea la tabla mas que un instante.
IF COL_LENGTH('dbo.DescuentosProducto', 'FechaDesde') IS NULL
BEGIN
    ALTER TABLE dbo.DescuentosProducto ADD FechaDesde date NULL;
    PRINT 'Columna FechaDesde creada.';
END
ELSE
    PRINT 'Columna FechaDesde ya existia: no se toca.';
GO

IF COL_LENGTH('dbo.DescuentosProducto', 'FechaHasta') IS NULL
BEGIN
    ALTER TABLE dbo.DescuentosProducto ADD FechaHasta date NULL;
    PRINT 'Columna FechaHasta creada.';
END
ELSE
    PRINT 'Columna FechaHasta ya existia: no se toca.';
GO

-- Una fila con el rango al reves no caduca: no vale NUNCA, y eso no lo quiere nadie.
-- Se impide en la BD porque a esta tabla se le mete mano por SQL a menudo, no solo desde Nesto.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DescuentosProducto_Vigencia')
BEGIN
    ALTER TABLE dbo.DescuentosProducto WITH CHECK
        ADD CONSTRAINT CK_DescuentosProducto_Vigencia
        CHECK (FechaDesde IS NULL OR FechaHasta IS NULL OR FechaDesde <= FechaHasta);
    PRINT 'Restriccion CK_DescuentosProducto_Vigencia creada.';
END
ELSE
    PRINT 'Restriccion CK_DescuentosProducto_Vigencia ya existia: no se toca.';
GO

-- =============================================================================
-- 3) DESPUES: comprobaciones
-- =============================================================================
PRINT '--- B) Las dos columnas existen y son nullable ---';
SELECT c.name, t.name AS Tipo, c.is_nullable
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.DescuentosProducto')
  AND c.name IN ('FechaDesde', 'FechaHasta');

PRINT '--- C) NINGUNA fila debe tener fechas todavia (0 y 0) ---';
SELECT COUNT(*) AS ConFechaDesde FROM DescuentosProducto WHERE FechaDesde IS NOT NULL;
SELECT COUNT(*) AS ConFechaHasta FROM DescuentosProducto WHERE FechaHasta IS NOT NULL;

/*
    GRANT: no hace falta ninguno. Son columnas de una tabla que ya tiene los permisos dados
    (ver [[scripts-sql-grants-por-bd]]); el SELECT/UPDATE de la tabla los arrastra.

    MARCHA ATRAS (solo si hubiera que revertir el despliegue entero):

        ALTER TABLE dbo.DescuentosProducto DROP CONSTRAINT CK_DescuentosProducto_Vigencia;
        ALTER TABLE dbo.DescuentosProducto DROP COLUMN FechaDesde;
        ALTER TABLE dbo.DescuentosProducto DROP COLUMN FechaHasta;

    Es seguro mientras no haya campanas con fechas metidas: con NULL/NULL en todo, el codigo
    nuevo y el viejo se comportan igual.
*/
