/*
    NestoAPI#421 / prestashop-nestosync#19 — sacar los productos "exclusivo profesional" de las
    categorías EP* y EXP para que la tienda online los MUESTRE, sin precio ni botón de compra para
    quien no sea profesional (eso lo hace ya la marca ExclusivoProfesional).

    Esas categorías están OCULTAS en PrestaShop (confirmado por Carlos el 28/08/2026): mientras el
    producto esté dentro, la tienda no lo enseña. Por eso hay que sacarlo de ellas. Y por eso el
    borrado es además coherente con el diseño de #421: esas categorías eran "exclusivo profesional"
    codificado como taxonomía, que es justo lo que se acaba de sustituir por un booleano.

    Estado comprobado en producción el 28/08/2026:
      - 220 productos con secundaria EP* (EPR, EPX, EPC, EPA, EPM, EPF, EPS, EPT, EPL), los 220 ya
        marcados a mano. Los 9 subgrupos son exclusivo profesional de verdad: ningún EPILACION ni
        falso positivo del comodín.
      - 11 productos con secundaria EXP (APA/EXP, PEL/EXP), NINGUNO marcado: 'ep%' no los caza.
        De esos 11, solo 4 están vivos (38175, 38176, 41851, 42459); los otros 7 están anulados.
      - 0 productos marcados fuera de esas categorías (no hay marcados de más).
      - 0 productos con EP* y EXP como categoría PRINCIPAL, así que borrar las secundarias los saca
        del todo. Si alguno la tuviera como principal, seguiría dentro y esto no serviría.
      - 206 de los 220 se quedan sin ninguna secundaria; siguen navegables por su principal
        (Tratamientos faciales, Peeling Exfoliantes, Cremas profesional...).

    ############################################################################################
    #  BLOQUEADO EL 28/08/2026 — NO EJECUTAR TODAVÍA.                                          #
    #  El piloto (producto 38171) demostró que el módulo de PrestaShop NO retira las           #
    #  categorías: prestashop-nestosync#12 está abierta, o sea que el mapeo de secundarias      #
    #  aún no está implementado. Borrar aquí las 242 asignaciones dejaría los 231 productos     #
    #  exactamente igual de invisibles (403) y encima sin el dato en Nesto.                     #
    #  Seguimiento: prestashop-nestosync#22. Reanudar cuando el módulo retire de verdad.        #
    ############################################################################################

    ANTES DE EJECUTAR:
      1. El API desplegado tiene que ser el que publica ExclusivoProfesional. Si no, la marca no
         viaja: PrestaShop recibiría el producto sin el campo (= "no toques la marca"), se quedaría
         sin marcar y, ya fuera de la categoría oculta, quedaría VISIBLE Y COMPRABLE por cualquiera.
      2. APA/EXP y PEL/EXP: dar por hecho que también están ocultas en la tienda. Si alguna fuera
         navegable de verdad, sacar los productos de ahí les quita esa vía de navegación (no es
         grave —siguen con su principal— pero conviene saberlo).
*/

SET NOCOUNT ON;
USE NV;

-- =====================================================================================
-- PASO 0 — Copia de seguridad. Estas asignaciones vienen de la carga inicial de #414
-- (Issue414_CargaInicial.sql, 1196 líneas). Cuesta nada y no hay vuelta atrás sin esto.
-- =====================================================================================
IF OBJECT_ID('dbo.ProductosCategoriasSecundarias_Backup_20260828') IS NULL
BEGIN
    SELECT * INTO dbo.ProductosCategoriasSecundarias_Backup_20260828
    FROM dbo.ProductosCategoriasSecundarias;
END
SELECT COUNT(*) AS FilasEnLaCopia FROM dbo.ProductosCategoriasSecundarias_Backup_20260828;
GO


-- =====================================================================================
-- PASO 1 — Marcar los 11 de EXP, que el 'ep%' de la marcada a mano no cazó.
-- Se marcan también los anulados: la marca es del producto y, si se revive, ya está bien.
-- =====================================================================================
UPDATE p
SET p.ExclusivoProfesional = 1,
    p.Usuario = SYSTEM_USER,
    p.[Fecha Modificación] = GETDATE()
FROM dbo.Productos p
WHERE p.Empresa = '1'
  AND p.ExclusivoProfesional = 0
  AND EXISTS (SELECT 1 FROM dbo.ProductosCategoriasSecundarias pcs
              WHERE pcs.Empresa = p.Empresa AND pcs.Número = p.Número AND pcs.SubGrupo = 'EXP');

SELECT @@ROWCOUNT AS MarcadosAhora;   -- esperado: 11
GO


-- =====================================================================================
-- PASO 2 — El piloto de un solo producto vive en su propio script:
--   OneShot_20260828_PilotoUnProductoCategoriasEP.sql   (producto 38171)
-- Ejecutarlo ANTES que este y comprobar en la tienda. Prueba que el módulo de PrestaShop
-- retira las categorías por el bus, que es de lo que depende todo este lote.
-- =====================================================================================


-- =====================================================================================
-- PASO 3 — El lote. Con candado: si quedara UN producto sin marcar en esas categorías, no
-- se borra nada. Sacarlo de la categoría oculta sin la marca es dejarlo a la venta pública.
-- =====================================================================================
BEGIN TRANSACTION;

DECLARE @SinMarcar int;
SELECT @SinMarcar = COUNT(DISTINCT p.Número)
FROM dbo.Productos p
WHERE p.Empresa = '1' AND p.ExclusivoProfesional = 0
  AND EXISTS (SELECT 1 FROM dbo.ProductosCategoriasSecundarias pcs
              WHERE pcs.Empresa = p.Empresa AND pcs.Número = p.Número
                AND (pcs.SubGrupo LIKE 'ep%' OR pcs.SubGrupo = 'EXP'));

IF @SinMarcar > 0
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('ABORTADO: hay %d productos en categorias EP* y EXP sin ExclusivoProfesional=1. Sacarlos de la categoria oculta sin la marca los dejaria a la venta publica.', 16, 1, @SinMarcar);
END
ELSE
BEGIN
    -- Los que hay que republicar: solo los VIVOS. Un anulado no se vende, y si se revive el
    -- trigger lo encola solo (el cambio de Estado sí lo vigila) y viajará ya correcto.
    DECLARE @Afectados TABLE (Numero varchar(15) NOT NULL PRIMARY KEY);

    INSERT INTO @Afectados (Numero)
    SELECT DISTINCT RTRIM(pcs.Número)
    FROM dbo.ProductosCategoriasSecundarias pcs
    INNER JOIN dbo.Productos p ON p.Empresa = pcs.Empresa AND p.Número = pcs.Número
    WHERE pcs.Empresa = '1' AND (pcs.SubGrupo LIKE 'ep%' OR pcs.SubGrupo = 'EXP')
      AND p.Estado >= 0;

    DELETE FROM dbo.ProductosCategoriasSecundarias
    WHERE Empresa = '1' AND (SubGrupo LIKE 'ep%' OR SubGrupo = 'EXP');

    SELECT @@ROWCOUNT AS AsignacionesBorradas;   -- esperado: 242 (231 de EP* y 11 de EXP), una menos si ya se hizo el piloto

    -- Hay que encolar a mano: el borrado es en OTRA tabla, así que ningún trigger de Productos
    -- se entera. Y el UPDATE de ExclusivoProfesional tampoco encola: el bloque de sincronización
    -- de trgProductosUpd no mira esa columna (por eso los 220 marcados a mano siguen sin viajar).
    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
    SELECT 'Productos', a.Numero, 'Salida categorias EP', GETDATE()
    FROM @Afectados a
    WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                      WHERE ns.Tabla = 'Productos' AND ns.ModificadoId = a.Numero
                        AND ns.Sincronizado IS NULL);

    SELECT @@ROWCOUNT AS Encolados;

    COMMIT TRANSACTION;
END
GO


-- =====================================================================================
-- COMPROBACIONES
-- =====================================================================================
SELECT COUNT(*) AS AsignacionesEPQueQuedan
FROM dbo.ProductosCategoriasSecundarias WHERE SubGrupo LIKE 'ep%' OR SubGrupo = 'EXP';   -- 0

SELECT COUNT(*) AS MarcadosExclusivoProfesional
FROM dbo.Productos WHERE Empresa = '1' AND ExclusivoProfesional = 1;                     -- 231

SELECT COUNT(*) AS PendientesDeSincronizar
FROM Nesto_sync WHERE Tabla = 'Productos' AND Sincronizado IS NULL;

-- A los 5-10 minutos, que la cola se haya vaciado:
-- SELECT COUNT(*) FROM Nesto_sync WHERE Tabla='Productos' AND Sincronizado IS NULL;


-- =====================================================================================
-- MARCHA ATRÁS, si PrestaShop no retirase las categorías por el bus
-- =====================================================================================
/*
INSERT INTO dbo.ProductosCategoriasSecundarias (Empresa, Número, Orden, Grupo, SubGrupo, Usuario, [Fecha Modificación])
SELECT b.Empresa, b.Número, b.Orden, b.Grupo, b.SubGrupo, b.Usuario, b.[Fecha Modificación]
FROM dbo.ProductosCategoriasSecundarias_Backup_20260828 b
WHERE (b.SubGrupo LIKE 'ep%' OR b.SubGrupo = 'EXP')
  AND NOT EXISTS (SELECT 1 FROM dbo.ProductosCategoriasSecundarias a
                  WHERE a.Empresa = b.Empresa AND a.Número = b.Número AND a.Orden = b.Orden);
*/
