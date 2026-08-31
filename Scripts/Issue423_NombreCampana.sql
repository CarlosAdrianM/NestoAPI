/*
    NestoAPI#423 (Slice 6) - nombre de campana en DescuentosProducto.

    POR QUE
    -------
    El 31/08/2026, para saber que filas eran las rebajas de verano, hubo que recurrir a esto:

        WHERE usuario = 'sa'
          AND [Fecha Modificacion] > '25/06/26 10:35:00'
          AND [Fecha Modificacion] < '25/06/26 10:40:00'

    Una ventana de CINCO MINUTOS en el reloj de hace dos meses. Funciono de milagro: el lote se
    metio de una tacada y nadie toco nada mas en esos cinco minutos. Si alguien hubiera metido un
    descuento suelto a las 10:37, se habria borrado con las rebajas sin que nadie se enterara.

    Con el nombre, ese WHERE pasa a ser `WHERE Campana = 'Rebajas verano 2026'`, la pantalla puede
    filtrar y operar por campana, y dentro de un ano se sabra que fue Black Friday y que es un
    descuento de siempre. Hoy eso se pierde en cuanto pasa el tiempo.

    Precedente: las ofertas combinadas ya llevan Nombre.

    QUE ES Y QUE NO ES
    ------------------
    Es una ETIQUETA, no una entidad: no hay tabla de campanas ni claves foraneas. Una campana es
    "todas las filas que comparten este texto". Se ha elegido asi a proposito — montar una tabla
    de campanas obligaria a decidir que pasa con las filas huerfanas, con los renombrados y con
    las campanas vacias, y el valor esta en agrupar y poder operar en bloque, que con un texto ya
    se consigue.

    NULL = fila que no pertenece a ninguna campana (los ~235 descuentos de siempre).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) LA COLUMNA
-- =============================================================================
-- Nullable y sin DEFAULT: metadata-only, instantanea, y las filas que ya existen no cambian.
IF COL_LENGTH('dbo.DescuentosProducto', 'Campana') IS NULL
BEGIN
    ALTER TABLE dbo.DescuentosProducto ADD Campana nvarchar(50) NULL;
    PRINT 'Columna Campana creada.';
END
ELSE
    PRINT 'Columna Campana ya existia: no se toca.';
GO

-- =============================================================================
-- 2) BACKFILL DE LAS REBAJAS DE VERANO 2026  (UN SOLO USO)
-- =============================================================================
/*
    Este bloque es de UN SOLO USO y usa el criterio forense del 31/08/2026, que es el ULTIMO dia
    en que sirve: mientras esas 2.017 filas sigan sin tocar, la ventana de cinco minutos las
    identifica. En cuanto alguien edite una, deja de valer — y esa es exactamente la fragilidad
    que la columna viene a eliminar.

    Se acota ademas a filas de TARIFA (sin cliente ni proveedor) para no etiquetar por accidente
    un descuento pactado con un cliente que se hubiera metido en el mismo minuto.
*/
PRINT '--- A) Lo que se va a etiquetar (esperado: 2.017 filas) ---';
DECLARE @d1 datetime = '20260625 10:35:00', @d2 datetime = '20260625 10:40:00';

SELECT COUNT(*) AS FilasAEtiquetar,
       COUNT(DISTINCT [Nº Producto]) AS ProductosDistintos,
       MIN(Descuento) AS DtoMinimo,
       MAX(Descuento) AS DtoMaximo
FROM DescuentosProducto
WHERE Usuario = 'sa'
  AND [Fecha Modificación] > @d1 AND [Fecha Modificación] < @d2
  AND [Nº Cliente] IS NULL AND NºProveedor IS NULL
  AND Campana IS NULL;

PRINT '--- B) Nadie mas deberia haber tocado la tabla en esa ventana: revisar que sale vacio ---';
SELECT RTRIM(Usuario) AS Usuario, COUNT(*) AS Filas
FROM DescuentosProducto
WHERE [Fecha Modificación] > @d1 AND [Fecha Modificación] < @d2
  AND Usuario <> 'sa'
GROUP BY RTRIM(Usuario);

/*  Revisadas las dos salidas, quitar el comentario y ejecutar:

BEGIN TRAN;

    UPDATE DescuentosProducto
    SET Campana = N'Rebajas verano 2026'
    WHERE Usuario = 'sa'
      AND [Fecha Modificación] > '20260625 10:35:00'
      AND [Fecha Modificación] < '20260625 10:40:00'
      AND [Nº Cliente] IS NULL AND NºProveedor IS NULL
      AND Campana IS NULL;

    SELECT @@ROWCOUNT AS Etiquetadas;   -- debe coincidir con la salida A (2.017)

-- COMMIT TRAN;    <-- si cuadra
-- ROLLBACK TRAN;  <-- si no

    OJO: el UPDATE NO toca [Fecha Modificacion] a proposito. Es una etiqueta administrativa, no
    un cambio del descuento: si se refrescara la fecha, el job nocturno de stocks (#410) creeria
    que 2.017 productos han cambiado y los encolaria para republicar — horas de trabajo para no
    cambiar ni un precio. Ademas se perderia el rastro de cuando se metieron de verdad.
*/

-- =============================================================================
-- 3) COMPROBACIONES
-- =============================================================================
PRINT '--- C) Reparto por campana (tras el UPDATE deberian salir 2.017 en Rebajas verano 2026) ---';
SELECT ISNULL(Campana, N'(sin campana)') AS Campana, COUNT(*) AS Filas
FROM DescuentosProducto
WHERE [Nº Cliente] IS NULL AND NºProveedor IS NULL
GROUP BY Campana
ORDER BY Filas DESC;

/*
    MARCHA ATRAS:

        UPDATE DescuentosProducto SET Campana = NULL WHERE Campana = N'Rebajas verano 2026';
        ALTER TABLE dbo.DescuentosProducto DROP COLUMN Campana;
*/
