/*
    Encolar en Nesto_sync los productos de CURSOS que se han vendido desde el 01/01/2025.

    POR QUE ESTA ACOTADO
    --------------------
    El grupo CUR se quedo fuera de la sincronizacion con PrestaShop, seguramente a proposito: son
    87 referencias y muchas son cursos antiguos que crearian productos basura en la tienda.
    Encolarlas todas seria justo el problema que se evito.

    El corte es "que se haya VENDIDO desde el 01/01/2025", que es lo que distingue un curso vivo
    de uno historico. Medido el 31/08/2026:

        Productos del grupo CUR ................................ 87
        de ellos, vivos (Estado >= 0) .......................... 82
        con linea de pedido desde el 01/01/2025 ................ 58
        (con movimiento en el extracto, para comparar) ......... 57

    Se usa la LINEA DE PEDIDO y no el extracto porque "vendido" es literalmente eso, y porque un
    curso es un servicio: puede facturarse sin generar movimiento de almacen. Los dos numeros
    salen casi iguales, asi que el criterio no cambia el resultado — pero si el significado.

    Se exige ademas Estado >= 0. Un curso vendido en 2025 pero hoy DE BAJA viajaria con su estado
    negativo, y el modulo de PrestaShop lo crearia para desactivarlo acto seguido
    (prestashop-nestosync#8): exactamente el producto basura que se quiere evitar.

    NO se filtra por PVP: si alguno no lo tiene, la propia pasada de sincronizacion lo descarta
    (SincronizacionJobsService.TieneDatosMinimosParaSincronizar) sin romper nada.

    CUANTO TARDA
    ------------
    Unas 58 referencias. El job 'sincronizar-productos' drena la cola entera en una pasada, en
    lotes de 50 con 5 s de pausa y 3 stocks + 2 llamadas HTTP por producto: cuestion de minutos.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) LO QUE SE VA A ENCOLAR: mirarlo ANTES
-- =============================================================================
PRINT '--- A) Recuento por subgrupo (esperado: unas 58 en total) ---';
SELECT RTRIM(p.SubGrupo) AS Subgrupo,
       RTRIM(sg.Descripción) AS Descripcion,
       COUNT(DISTINCT p.Número) AS Referencias
FROM Productos p
LEFT JOIN SubGruposProducto sg ON sg.Empresa = p.Empresa AND sg.Grupo = p.Grupo AND sg.Número = p.SubGrupo
WHERE p.Empresa = '1' AND RTRIM(p.Grupo) = 'CUR' AND p.Estado >= 0
  AND EXISTS (SELECT 1 FROM LinPedidoVta l
              INNER JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.Número = l.Número
              WHERE l.Empresa = p.Empresa AND l.Producto = p.Número AND c.Fecha >= '20250101')
GROUP BY RTRIM(p.SubGrupo), RTRIM(sg.Descripción)
ORDER BY Referencias DESC;

PRINT '--- B) Los que quedan FUERA, para revisar que ninguno sorprenda ---';
SELECT RTRIM(p.Número) AS Referencia, RTRIM(p.Nombre) AS Nombre, p.Estado, p.PVP,
       Motivo = CASE WHEN p.Estado < 0 THEN 'de baja'
                     ELSE 'sin venta desde el 01/01/2025' END
FROM Productos p
WHERE p.Empresa = '1' AND RTRIM(p.Grupo) = 'CUR'
  AND NOT (p.Estado >= 0
           AND EXISTS (SELECT 1 FROM LinPedidoVta l
                       INNER JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.Número = l.Número
                       WHERE l.Empresa = p.Empresa AND l.Producto = p.Número AND c.Fecha >= '20250101'))
ORDER BY p.Estado DESC, p.Número;

PRINT '--- C) La cola deberia estar practicamente vacia antes de empezar ---';
SELECT Tabla, COUNT(*) AS Pendientes FROM Nesto_sync WHERE Sincronizado IS NULL GROUP BY Tabla;

/*  Revisadas las tres salidas, quitar el comentario y ejecutar:

BEGIN TRAN;

    INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
    SELECT 'Productos', RTRIM(p.Número), 'Alta cursos vendidos', GETDATE()
    FROM Productos p
    WHERE p.Empresa = '1' AND RTRIM(p.Grupo) = 'CUR' AND p.Estado >= 0
      AND EXISTS (SELECT 1 FROM LinPedidoVta l
                  INNER JOIN CabPedidoVta c ON c.Empresa = l.Empresa AND c.Número = l.Número
                  WHERE l.Empresa = p.Empresa AND l.Producto = p.Número AND c.Fecha >= '20250101')
      -- Si ya hay una encolada sin sincronizar, esa misma publicara el estado actual.
      AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                      WHERE ns.Tabla = 'Productos'
                        AND ns.ModificadoId = RTRIM(p.Número)
                        AND ns.Sincronizado IS NULL);

    SELECT @@ROWCOUNT AS Encoladas;   -- debe coincidir con el total de la salida A

-- COMMIT TRAN;    <-- si cuadra
-- ROLLBACK TRAN;  <-- si no

*/

-- =============================================================================
-- 2) SEGUIMIENTO
-- =============================================================================
/*
    SELECT SUM(CASE WHEN Sincronizado IS NULL THEN 1 ELSE 0 END) AS Pendientes,
           SUM(CASE WHEN Sincronizado IS NOT NULL THEN 1 ELSE 0 END) AS YaPublicadas
    FROM Nesto_sync
    WHERE Tabla = 'Productos' AND Usuario = 'Alta cursos vendidos';

    CORTAR A MEDIA FAENA (lo ya publicado no vuelve, pero se para lo que falta):

    UPDATE Nesto_sync SET Sincronizado = GETDATE()
    WHERE Tabla = 'Productos' AND Usuario = 'Alta cursos vendidos' AND Sincronizado IS NULL;
*/
