/*
    Nesto#340 / NestoSync v1.4.0 - marcar con el sentinel -1 los productos cuyo precio publico
    es IGUAL al profesional, para sustituir a las reglas de catalogo de PrestaShop.

    QUE HACE
    --------
    Pone PVP_IVA_Incluido = -1 en PrestashopProductos. Es un UPSERT que PRESERVA lo que ya haya:
      - Ficha existente -> se toca UNICAMENTE PVP_IVA_Incluido. Nombre, descripciones y visto
        bueno se quedan igual (hay 568 fichas con nombre o descripcion en estas familias:
        borrarlas y recrearlas perderia ese trabajo).
      - Sin ficha       -> se crea con SOLO el -1 y los campos obligatorios.

    A QUIEN MARCA
    -------------
    Se seleccionan desde NESTO, por FAMILIA, no desde la lista de PrestaShop. Es a proposito
    (decision de Carlos 25/08/2026): marca de mas, y eso es lo que se quiere. Son productos que
    nunca han tenido stock y por eso no estan creados en la tienda, pero cuando se creen ya
    naceran con el sentinel puesto y no habra que acordarse de marcarlos.

    Por eso los recuentos NO cuadran con los de PrestaShop, y no pasa nada:

        Familia Nesto        Vivos aqui   PrestaShop dice (activos)
        Silverfox (Weelko)      303              239
        UnionLaser              262               64
        DDUUEETT                188               25
        Staleks                 175               95
        Fama (Fama Fabre)        55               55

    Solo se marcan los productos VIVOS (Estado >= 0): un descatalogado no se va a crear nunca en
    la tienda. Con ese filtro no hay ni un solo conflicto: ninguno tiene precio publico fijo.

    ⚠️ CERAS DEPILATORIAS (categoria 57) NO ENTRA
    ---------------------------------------------
    Su regla es del 25 %, no del 30 %, asi que el publico NO queda igual al profesional:
        publico base = PVP / 0,7  ->  con 25 % de descuento = PVP / 0,7 * 0,75 = PVP * 1,0714
    Hoy esos 52 productos se venden un 7,14 % POR ENCIMA del profesional, y marcarlos con el
    sentinel les BAJARIA el precio. Si se quiere igualar de verdad es una decision de negocio; si
    se quiere conservar el precio de hoy, lo suyo es un valor explicito, no el sentinel.

    CUANDO EJECUTARLO
    -----------------
    SOLO cuando (1) NestoAPI este publicado con el soporte del -1 y (2) el modulo NestoSync 1.4.0
    este instalado y ACTIVO en PRODUCCION. Antes de eso, un -1 aqui se sirve a Nesto, NestoApp y
    TiendasNuevaVision, que comparten esta misma base de datos.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) A QUIEN SE MARCA
-- =============================================================================
IF OBJECT_ID('tempdb..#Marcar') IS NOT NULL DROP TABLE #Marcar;
CREATE TABLE #Marcar (Referencia char(15) NOT NULL PRIMARY KEY, Motivo varchar(40) NOT NULL);

-- Reglas por FABRICANTE de PrestaShop -> familia de Nesto
INSERT INTO #Marcar (Referencia, Motivo)
SELECT p.Número, RTRIM(p.Familia)
FROM Productos p
WHERE p.Empresa = '1'
  AND p.Estado >= 0
  AND RTRIM(p.Familia) IN ('Silverfox', 'UniónLáser', 'Fama', 'Staleks', 'DDUUEETT');
--  Silverfox = Weelko (id_manufacturer 268), UnionLaser = 72, Fama = Fama Fabre 1,
--  Staleks = 357, DDUUEETT = 348.

/*  Reglas por CATEGORIA (Webinars 40281 y Formacion para esteticistas 40288).
    DESCOMENTAR SOLO SI SE CONFIRMA que TODA la formacion va a mismo precio publico y
    profesional: en Nesto no hay forma de distinguir esas dos categorias de PrestaShop, asi que
    esto marca los 82 cursos vivos del grupo CUR, no solo los 35 de las dos categorias.

INSERT INTO #Marcar (Referencia, Motivo)
SELECT p.Número, 'Formacion'
FROM Productos p
WHERE p.Empresa = '1' AND p.Estado >= 0 AND p.Grupo = 'CUR'
  AND NOT EXISTS (SELECT 1 FROM #Marcar m WHERE m.Referencia = p.Número);
*/

-- =============================================================================
-- 2) COMPROBACIONES ANTES DE TOCAR NADA
-- =============================================================================
PRINT '--- A) Resumen de lo que se va a hacer ---';
SELECT m.Motivo,
       COUNT(*) AS Referencias,
       SUM(CASE WHEN pp.Número IS NULL THEN 1 ELSE 0 END) AS SeCrearan,
       SUM(CASE WHEN pp.Número IS NOT NULL AND ISNULL(pp.PVP_IVA_Incluido, 0) <> -1 THEN 1 ELSE 0 END) AS SeActualizaran,
       SUM(CASE WHEN pp.PVP_IVA_Incluido = -1 THEN 1 ELSE 0 END) AS YaEstaban
FROM #Marcar m
LEFT JOIN PrestashopProductos pp ON pp.Empresa = '1' AND pp.Número = m.Referencia
GROUP BY m.Motivo
ORDER BY m.Motivo;

PRINT '--- B) Los que YA tienen precio publico fijo: el -1 lo BORRARIA. Deberia salir vacio ---';
SELECT m.Referencia, m.Motivo, RTRIM(p.Nombre) AS Nombre, p.PVP, pp.PVP_IVA_Incluido AS PrecioFijoActual
FROM #Marcar m
JOIN PrestashopProductos pp ON pp.Empresa = '1' AND pp.Número = m.Referencia
JOIN Productos p ON p.Empresa = '1' AND p.Número = m.Referencia
WHERE pp.PVP_IVA_Incluido > 0;

PRINT '--- C) Fichas con nombre o descripcion: se CONSERVAN, esto es solo para saber cuantas son ---';
SELECT COUNT(*) AS FichasConDatosQueSeConservan
FROM #Marcar m
JOIN PrestashopProductos pp ON pp.Empresa = '1' AND pp.Número = m.Referencia
WHERE pp.Nombre IS NOT NULL OR pp.Descripción IS NOT NULL OR pp.DescripciónBreve IS NOT NULL;

/*  Revisadas las tres salidas (y con B vacia), quitar el comentario y ejecutar:

BEGIN TRAN;

    -- BACKUP para poder revertir: el valor anterior de las fichas que ya existian
    IF OBJECT_ID('dbo.PrestashopProductos_BackupSentinel') IS NULL
        SELECT pp.Empresa, pp.Número, pp.PVP_IVA_Incluido, GETDATE() AS FechaBackup
        INTO dbo.PrestashopProductos_BackupSentinel
        FROM PrestashopProductos pp
        JOIN #Marcar m ON m.Referencia = pp.Número
        WHERE pp.Empresa = '1';

    -- 1. Fichas existentes: SOLO el precio. Nombre, descripciones y visto bueno intactos.
    UPDATE pp
    SET pp.PVP_IVA_Incluido = -1,
        pp.Usuario = 'Sentinel NestoSync',          -- Usuario es varchar(30)
        pp.[Fecha Modificación] = GETDATE()
    FROM PrestashopProductos pp
    JOIN #Marcar m ON m.Referencia = pp.Número
    WHERE pp.Empresa = '1'
      AND ISNULL(pp.PVP_IVA_Incluido, 0) <> -1;

    -- 2. Las que no tienen ficha: se crea con lo minimo imprescindible.
    INSERT INTO PrestashopProductos (Empresa, Número, PVP_IVA_Incluido, Usuario, [Fecha Modificación])
    SELECT '1', m.Referencia, -1, 'Sentinel NestoSync', GETDATE()
    FROM #Marcar m
    WHERE NOT EXISTS (SELECT 1 FROM PrestashopProductos pp
                      WHERE pp.Empresa = '1' AND pp.Número = m.Referencia);

    -- COMPROBACION FINAL: debe dar 0
    SELECT COUNT(*) AS SinMarcarQueDeberianEstarlo
    FROM #Marcar m
    LEFT JOIN PrestashopProductos pp ON pp.Empresa = '1' AND pp.Número = m.Referencia
    WHERE ISNULL(pp.PVP_IVA_Incluido, 0) <> -1;

-- COMMIT TRAN;    <-- solo si la comprobacion final da 0
-- ROLLBACK TRAN;  <-- si da cualquier otra cosa

*/

-- =============================================================================
-- REVERSION (si hubiera que deshacerlo)
-- =============================================================================
/*
    -- Devuelve su valor anterior a las fichas que ya existian...
    UPDATE pp SET pp.PVP_IVA_Incluido = b.PVP_IVA_Incluido
    FROM PrestashopProductos pp
    JOIN dbo.PrestashopProductos_BackupSentinel b
      ON b.Empresa = pp.Empresa AND b.Número = pp.Número;

    -- ...y borra las que creo este script, solo si no tienen ningun otro dato.
    DELETE pp
    FROM PrestashopProductos pp
    WHERE pp.Empresa = '1'
      AND pp.Usuario = 'Sentinel NestoSync'
      AND pp.Nombre IS NULL AND pp.Descripción IS NULL AND pp.DescripciónBreve IS NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.PrestashopProductos_BackupSentinel b
                      WHERE b.Empresa = pp.Empresa AND b.Número = pp.Número);
*/
