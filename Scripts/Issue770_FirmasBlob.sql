/*
    NestoAPI#770: la firma de la prueba de entrega del reparto propio, SELLADA y guardada en el almacenamiento (contenedor
    privado «firmas» de la misma cuenta que las fotos de los bultos, Almacen:FotosBultosConexion).

    Ejecutar en SSMS contra NV como sa (ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    La tabla está fuera del EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy.
    Después de lanzarlo, reciclar el app pool (la API recuerda qué columnas existen).

    Sin el script: las firmas nuevas se guardan selladas en la columna Firma (como hasta ahora, pero con el sello) y
    POST api/RepartoPropio/PruebasEntrega/MigrarFirmas responde 400 «falta el script».

    1. EnviosAgenciaPruebasEntrega.RutaBlob: la ruta de la firma dentro del contenedor «firmas»
       ({empresa}/{envío}/{idPrueba}-{yyyyMMddHHmmss}.png). Con RutaBlob, Firma queda a NULL.

    Antes de migrar: crear el contenedor PRIVADO «firmas» en la cuenta de almacenamiento (Portal de Azure → la cuenta de las
    fotos de bultos → Contenedores → + Contenedor, nivel de acceso «Privado»), o con la CLI:
        az storage container create --name firmas --account-name <cuenta> --public-access off --auth-mode key
    Luego, con un usuario de Dirección o Informática:
        POST api/RepartoPropio/PruebasEntrega/MigrarFirmas?aplicar=false&maximo=100   (solo cuenta y lista)
        POST api/RepartoPropio/PruebasEntrega/MigrarFirmas?aplicar=true&maximo=100    (sube; repetir hasta Pendientes = 0)

    GRANT a la cuenta máquina con la que corre la API (NUEVAVISION\RDS2016$), como en Issue739_RepartoPropio.sql.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.EnviosAgenciaPruebasEntrega', 'RutaBlob') IS NULL
BEGIN
    ALTER TABLE dbo.EnviosAgenciaPruebasEntrega ADD RutaBlob varchar(300) NULL;
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EnviosAgenciaPruebasEntrega TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación: cuántas firmas quedan en la columna y cuántas están ya en el almacenamiento.
SELECT
    SUM(CASE WHEN Firma IS NOT NULL THEN 1 ELSE 0 END) AS FirmasEnColumna,
    SUM(CASE WHEN RutaBlob IS NOT NULL THEN 1 ELSE 0 END) AS FirmasEnBlob
FROM dbo.EnviosAgenciaPruebasEntrega;
GO
