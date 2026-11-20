/*
    NestoAPI#857h (20/11/26, Carlos): la fecha de recepción, línea a línea, por los productos que dice el aviso de salida del proveedor.

    Añade a dbo.ComprasCorreosSeguimiento (la de Scripts/Issue857_ComprasCorreosSeguimiento.sql):
      - LineasAviso nvarchar(max) NULL: los productos y unidades que dice el aviso de salida del proveedor (hoy, el «Resumen del pedido» de
        Weelko: [F806.N] … 2,00) y las líneas nuestras en el aire que casaron al analizarlo (JSON). NULL = el aviso no los dice o correo de antes.
      - LineasAplicadas nvarchar(max) NULL: las líneas del pedido de compra (nº de orden, producto, unidades del envío y si es parcial) a las
        que el correo puso la fecha (JSON). NULL = a todas las líneas en el aire del pedido, como hasta ahora.

    ORDEN: después de Issue857 y Issue857d (si no existe la tabla, no hace nada y lo dice). Puede ir ANTES o DESPUÉS de publicar la API: sin las
    dos columnas la API pone la fecha a todo el pedido, como hasta ahora (el pedido sí se reconoce por las líneas del aviso); lo vuelve a
    comprobar cada 10 minutos, sin reiniciar. Tras lanzarlo, POST api/Compras/AgenciasEntrada/Correos/Reanalizar?dias=7 guarda las líneas de
    los avisos de estos días que sigan sin aplicar (no aplica, ni reenvía, ni avisa).

    Lanzar como sa en NV. Idempotente: se puede relanzar. Cada columna nueva en su propio lote. Los permisos ya los tiene la tabla
    (GRANT SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$] en Issue857): no hace falta ninguno nuevo. ProveedoresProducto ya se lee desde la API.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'LineasAviso') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD LineasAviso nvarchar(max) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'LineasAplicadas') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD LineasAplicadas nvarchar(max) NULL;
GO
-- Comprobación (la misma que hace la API)
SELECT CASE WHEN COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'LineasAviso') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'LineasAplicadas') IS NOT NULL
            THEN N'OK: columnas LineasAviso y LineasAplicadas en dbo.ComprasCorreosSeguimiento'
            ELSE N'FALTA: no están las dos columnas (¿existe la tabla?)' END AS Resultado;
GO
