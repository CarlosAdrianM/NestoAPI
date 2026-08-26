/*
    NestoSync 1.5.0 (cutover de precios) - RE-SYNC de precios hacia PrestaShop.
    SOLO fijados a mano y sentinel -1. Los de modo NULL (30 %) NO se encolan.

    QUE HACE
    --------
    Encola en Nesto_sync (Tabla = 'Productos') una fila por referencia viva con PVP cuyo modo de
    precio publico NO sea el derivado por defecto: precio fijo (PVP_IVA_Incluido > 0) o sentinel
    -1 (publico = profesional). El job de Hangfire 'sincronizar-productos' (cada 5 minutos, lotes
    de 50) las drena y publica el mensaje NORMAL de Productos, que desde el cutover del 26/08/2026
    lleva los dos precios absolutos:

        PrecioProfesional   = PVP de la ficha
        PrecioPublicoFinal  = calculado por NestoAPI segun el modo
                              (fijo -> tal cual; -1 -> PVP*(1+IVA))

    POR QUE NO SE ENCOLAN LOS DE MODO NULL (decision de Carlos, 26/08/2026)
    ----------------------------------------------------------------------
    Son ~4.700 referencias cuyo publico en la tienda YA es el derivado con el 30 %: republicarlas
    no cambia ningun precio y son horas de job (stocks + 2 HTTP por producto). Se iran refrescando
    solas segun se toquen en Nesto, y si algun dia hace falta el volcado entero, se quita el
    filtro del modo en este mismo script. Hoy el objetivo es dejar bien el grueso: los que la
    tienda NO puede calcular por su cuenta.

    ORDEN CON EL SENTINEL -1  ⚠️
    ---------------------------
    El script Nesto340_MarcarMismoPrecioPublicoProfesional.sql debe ejecutarse ANTES que este:
    aqui se seleccionan las filas CON el -1 ya puesto, y el PrecioPublicoFinal se calcula leyendo
    el modo en el momento de publicar. Hoy (26/08) hay 0 filas con -1: sin el sentinel ejecutado,
    este script solo encolaria los ~143 precios fijos.

    Y el borrado de las reglas igualadoras de PrestaShop se coordina con la llegada de estos
    precios: ventana corta, primero borrar la regla y justo despues publicar (se ve caro, nunca
    barato). Detalle en Scripts/NestoSync_PromptEquipoPrestashop.md.

    VOLUMEN Y DURACION
    ------------------
    ~143 fijos + ~981 sentinel = ~1.100 referencias. A 50 por lote / 5 s de pausa, con 3 stocks y
    2 llamadas HTTP (foto y enlace) por producto: del orden de MEDIA HORA / UNA HORA. Avisar al
    equipo de PrestaShop de la hora de arranque para que lo monitoricen, y decirles que el re-sync
    NO cubre los de modo NULL (su dry-run debe acotarse a fijos + igualados).

    CUANDO EJECUTARLO
    -----------------
    Con NestoAPI publicado con el cutover (calculo local del publico), el modulo 1.5.0 (el que
    escribe PrecioPublicoFinal en product.price) activo en produccion y la suscripcion push
    apuntando al webhook de PRODUCCION.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) COMPROBACIONES ANTES DE ENCOLAR NADA
-- =============================================================================
PRINT '--- A) Lo que se va a encolar, por modo ---';
SELECT CASE WHEN pp.PVP_IVA_Incluido = -1 THEN '-1 (mismo que profesional)'
            ELSE 'precio fijo' END AS Modo,
       COUNT(*) AS Referencias
FROM Productos p
JOIN PrestashopProductos pp ON pp.Empresa = p.Empresa AND pp.Número = p.Número
WHERE p.Empresa = '1' AND p.Estado >= 0 AND p.PVP > 0
  AND (pp.PVP_IVA_Incluido > 0 OR pp.PVP_IVA_Incluido = -1)
  AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                  WHERE ns.Tabla = 'Productos'
                    AND ns.ModificadoId = RTRIM(p.Número)
                    AND ns.Sincronizado IS NULL)
GROUP BY CASE WHEN pp.PVP_IVA_Incluido = -1 THEN '-1 (mismo que profesional)'
              ELSE 'precio fijo' END;
--  Si el -1 sale a 0 referencias, FALTA ejecutar el script del sentinel: parar aqui.

PRINT '--- B) Fijos o -1 que quedan fuera por la ficha: revisar que ninguno sorprenda ---';
--  OJO al leerla: EstadoFicha -1 significa DE BAJA (no confundir con el sentinel, que saldria en
--  ModoPrecio como -1,0000). El caso esperado son los ~173 de baja con precio fijo antiguo
--  (316 fijos totales - 143 vivos).
SELECT RTRIM(pp.Número) AS Referencia,
       CASE WHEN pp.PVP_IVA_Incluido = -1 THEN 'sentinel -1' ELSE 'fijo' END AS ModoPrecio,
       pp.PVP_IVA_Incluido AS ValorModo,
       p.Estado AS EstadoFicha,
       p.PVP AS PvpFicha,
       CASE WHEN p.Número IS NULL THEN 'sin ficha en Productos'
            WHEN p.Estado < 0 THEN 'de baja'
            ELSE 'sin PVP profesional' END AS MotivoExclusion
FROM PrestashopProductos pp
LEFT JOIN Productos p ON p.Empresa = pp.Empresa AND p.Número = pp.Número
WHERE pp.Empresa = '1' AND (pp.PVP_IVA_Incluido > 0 OR pp.PVP_IVA_Incluido = -1)
  AND (p.Número IS NULL OR p.Estado < 0 OR p.PVP IS NULL OR p.PVP <= 0);
--  Un producto sin PVP publicaria PrecioPublicoFinal = 0 y dejaria el articulo a 0 EUR en la
--  tienda: es el caso de la referencia 42262 (de baja Y con PVP 0,00) que detecto el proveedor.

PRINT '--- C) Cola actual (deberia estar practicamente a cero antes de empezar) ---';
SELECT Tabla, COUNT(*) AS Pendientes FROM Nesto_sync WHERE Sincronizado IS NULL GROUP BY Tabla;

/*  Revisadas las tres salidas, quitar el comentario y ejecutar:

BEGIN TRAN;

    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
    SELECT 'Productos', RTRIM(p.Número), 'Resync NestoSync', GETDATE()
    FROM Productos p
    JOIN PrestashopProductos pp ON pp.Empresa = p.Empresa AND pp.Número = p.Número
    WHERE p.Empresa = '1' AND p.Estado >= 0 AND p.PVP > 0
      AND (pp.PVP_IVA_Incluido > 0 OR pp.PVP_IVA_Incluido = -1)
      -- Si ya hay una encolada sin sincronizar, esa misma publicara el estado actual.
      AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                      WHERE ns.Tabla = 'Productos'
                        AND ns.ModificadoId = RTRIM(p.Número)
                        AND ns.Sincronizado IS NULL);

    SELECT @@ROWCOUNT AS Encoladas;   -- debe coincidir con el total de la salida A

-- COMMIT TRAN;    <-- si Encoladas cuadra con A
-- ROLLBACK TRAN;  <-- si no

*/

-- =============================================================================
-- 2) SEGUIMIENTO (mientras el job va drenando, cada 5 minutos)
-- =============================================================================
/*
    SELECT SUM(CASE WHEN Sincronizado IS NULL THEN 1 ELSE 0 END) AS Pendientes,
           SUM(CASE WHEN Sincronizado IS NOT NULL THEN 1 ELSE 0 END) AS YaPublicadas
    FROM Nesto_sync
    WHERE Tabla = 'Productos' AND Usuario = 'Resync NestoSync';

    -- Terminado cuando Pendientes = 0. Si se queda atascado en un numero fijo, mirar el dashboard
    -- de Hangfire (/hangfire): el job deja el error de cada referencia que falla y la reintenta.
*/

-- =============================================================================
-- CORTAR A MEDIA FAENA
-- =============================================================================
/*
    Lo ya publicado no vuelve, pero se puede parar lo que aun no ha salido:

    UPDATE Nesto_sync SET Sincronizado = GETDATE()
    WHERE Tabla = 'Productos' AND Usuario = 'Resync NestoSync' AND Sincronizado IS NULL;
*/
