/*
    30/10/26: cuadra Ubicaciones con el stock (ExtractoProducto) de un almacén. Genérico: cualquier producto descuadrado.

    Misma definición que el cuadre de la API (Infraestructure/Ubicaciones/CuadreUbicaciones.cs, NestoAPI#594):
      Stock      = SUM(ExtractoProducto.Cantidad) de la empresa y su espejo (1 y 3) en el almacén
      Ubicación  = SUM(Ubicaciones.Cantidad) con Estado >= 0 (0 libre, 2 pendiente de ubicar, 3/4/5 reservado) de 1 y 3
      Diferencia = Ubicación − Stock. Los productos con Productos.Ubicar = 0 y sin filas en Ubicaciones cuadran siempre.

    Qué hace, por pasos:
      1. JUNTA las filas libres repetidas (dos o más filas en estado 0 del mismo producto en el mismo hueco) en la más
         antigua. No cambia ninguna suma; evita que prdUbicar vuelva a sumar lo ubicado a cada una (causa del 46048).
         Se hace en todos los productos del alcance, cuadren o no.
      2. SOBRA en ubicaciones (Diferencia > 0): quita la diferencia de las filas LIBRES (estado 0) del producto, empezando
         por el hueco con más unidades. Si una fila se queda en 0, se borra (como hace la API). Lo que no se pueda quitar
         de filas libres (la sobra está en pendiente de ubicar o en reservas) NO se toca: sale como «revisar a mano».
      3. FALTA en ubicaciones (Diferencia < 0) y @CuadrarFaltas = 1: mete la diferencia como «pendiente de ubicar»
         (estado 2, sin hueco), para que aparezca en Ariadna y el mozo la ubique donde esté de verdad.

    Caso que lo motiva: 46048 KERAPLANT NEW ABSOLUTE 7/07 AVELLANA, ALG, 004/004/012: stock 44, ubicaciones 53 (filas
    libres 320751201 = 31 y 322574101 = 22). Paso 1: 320751201 = 53 y se borra 322574101. Paso 2: 320751201 = 44.
    Después, si no cuadra con lo físico (el mozo contó 45), inventario.

    Cómo lanzarlo (como sa, en NV):
      a) @Aplicar = 0 (por defecto): SOLO MIRA. Enseña los descuadres, lo que se juntaría y lo que se cambiaría.
      b) Revisado, @Aplicar = 1: lo hace en una transacción. Cada UPDATE/DELETE comprueba el valor que se leyó: si
         alguien ha tocado la fila entre medias, se deshace TODO y avisa (repetir).
    Con @Producto = NULL recorre el almacén entero: es la consulta PESADA del cuadre (17/09/26 bloqueó a los usuarios),
    así que fuera de horario. Con un producto, milisegundos.
    Los triggers de Ubicaciones (trdUpdUbicaciones / trgUbicacionesDtl) deshacen todo si hay un inventario en curso de
    alguno de esos productos.
*/
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Aplicar bit = 0;                       -- 0 = solo mirar; 1 = cuadrar
DECLARE @CuadrarFaltas bit = 1;                 -- 1 = lo que falta en Ubicaciones se mete como pendiente de ubicar
DECLARE @Producto char(15) = NULL;              -- NULL = todo el almacén (PESADA: fuera de horario); p. ej. '46048'
DECLARE @Almacen char(3) = 'ALG';
DECLARE @Empresa char(3) = '1';
DECLARE @Espejo char(3) = '3';
DECLARE @Usuario varchar(30) = 'Cuadre stock 30/10/26';   -- Ubicaciones.Usuario es varchar(30)

IF (SELECT ControlUbicaciones FROM Almacenes WHERE Empresa = @Empresa AND [Número] = @Almacen) <> 1
BEGIN
    RAISERROR(N'El almacén %s no lleva control de ubicaciones.', 16, 1, @Almacen);
    RETURN;
END;

-- ------------------------------------------------------------------------------------------------ Foto (sin bloquear)
IF OBJECT_ID('tempdb..#Stock') IS NOT NULL DROP TABLE #Stock;
IF OBJECT_ID('tempdb..#Ubic') IS NOT NULL DROP TABLE #Ubic;
IF OBJECT_ID('tempdb..#Cuadre') IS NOT NULL DROP TABLE #Cuadre;
IF OBJECT_ID('tempdb..#Libres') IS NOT NULL DROP TABLE #Libres;
IF OBJECT_ID('tempdb..#Juntar') IS NOT NULL DROP TABLE #Juntar;
IF OBJECT_ID('tempdb..#Quitar') IS NOT NULL DROP TABLE #Quitar;

SELECT e.[Número] AS Producto, SUM(CAST(e.Cantidad AS int)) AS Stock
INTO #Stock
FROM ExtractoProducto e WITH (NOLOCK)
WHERE e.Empresa IN (@Empresa, @Espejo) AND e.[Almacén] = @Almacen AND (@Producto IS NULL OR e.[Número] = @Producto)
GROUP BY e.[Número]
OPTION (RECOMPILE);

SELECT u.[Número] AS Producto, SUM(u.Cantidad) AS Ubicacion, COUNT(*) AS Filas,
       SUM(CASE WHEN ISNULL(u.Estado, 0) = 0 THEN u.Cantidad ELSE 0 END) AS Libre,
       SUM(CASE WHEN u.Estado = 2 THEN u.Cantidad ELSE 0 END) AS PendienteUbicar,
       SUM(CASE WHEN u.Estado > 2 THEN u.Cantidad ELSE 0 END) AS Reservado
INTO #Ubic
FROM Ubicaciones u WITH (NOLOCK)
WHERE u.Empresa IN (@Empresa, @Espejo) AND u.[Almacén] = @Almacen AND ISNULL(u.Estado, 0) >= 0
  AND (@Producto IS NULL OR u.[Número] = @Producto)
GROUP BY u.[Número]
OPTION (RECOMPILE);

SELECT COALESCE(s.Producto, u.Producto) AS Producto, RTRIM(p.Nombre) AS Nombre, ISNULL(CAST(p.Ubicar AS bit), 1) AS Ubicar,
       ISNULL(s.Stock, 0) AS Stock, ISNULL(u.Ubicacion, 0) AS Ubicacion,
       ISNULL(u.Libre, 0) AS Libre, ISNULL(u.PendienteUbicar, 0) AS PendienteUbicar, ISNULL(u.Reservado, 0) AS Reservado,
       ISNULL(u.Ubicacion, 0) - ISNULL(s.Stock, 0) AS Diferencia
INTO #Cuadre
FROM #Stock s
     FULL OUTER JOIN #Ubic u ON u.Producto = s.Producto
     LEFT JOIN Productos p WITH (NOLOCK) ON p.Empresa = @Empresa AND p.[Número] = COALESCE(s.Producto, u.Producto)
WHERE ISNULL(u.Ubicacion, 0) <> ISNULL(s.Stock, 0)
  AND NOT (ISNULL(CAST(p.Ubicar AS bit), 1) = 0 AND u.Producto IS NULL);   -- no se ubica y no tiene filas: cuadra

-- Filas libres por hueco (lo que quedaría tras juntar): la más antigua se queda con la suma
SELECT u.Empresa, u.[Número] AS Producto, u.Pasillo, u.Fila, u.Columna,
       MIN(u.[NºOrden]) AS Queda, SUM(u.Cantidad) AS Cantidad, COUNT(*) AS Filas
INTO #Libres
FROM Ubicaciones u WITH (NOLOCK)
WHERE u.Empresa IN (@Empresa, @Espejo) AND u.[Almacén] = @Almacen AND u.Estado = 0
  AND (@Producto IS NULL OR u.[Número] = @Producto)
GROUP BY u.Empresa, u.[Número], u.Pasillo, u.Fila, u.Columna
OPTION (RECOMPILE);

-- Paso 1: cada fila de los huecos con más de una libre, con lo que tiene ahora (para comprobarlo al aplicar)
SELECT l.Queda, u.[NºOrden], u.Cantidad AS CantidadAhora, l.Cantidad AS CantidadQueda, RTRIM(u.[Número]) AS Producto,
       RTRIM(u.Pasillo) + '/' + RTRIM(u.Fila) + '/' + RTRIM(u.Columna) AS Hueco
INTO #Juntar
FROM #Libres l
     INNER JOIN Ubicaciones u WITH (NOLOCK) ON u.Empresa = l.Empresa AND u.[Almacén] = @Almacen AND u.[Número] = l.Producto
          AND u.Pasillo = l.Pasillo AND u.Fila = l.Fila AND u.Columna = l.Columna AND u.Estado = 0
WHERE l.Filas > 1;

-- Paso 2: lo que sobra, quitado de los huecos libres (tras juntar) empezando por el que más tiene
;WITH Orden AS (
    SELECT l.Queda, l.Producto, l.Cantidad, c.Diferencia,
           RTRIM(l.Pasillo) + '/' + RTRIM(l.Fila) + '/' + RTRIM(l.Columna) AS Hueco,
           ISNULL(SUM(l.Cantidad) OVER (PARTITION BY l.Producto ORDER BY l.Cantidad DESC, l.Queda DESC
                                        ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING), 0) AS Antes
    FROM #Libres l INNER JOIN #Cuadre c ON c.Producto = l.Producto
    WHERE c.Diferencia > 0 AND l.Cantidad > 0
)
SELECT Queda, Producto, Hueco, Cantidad AS CantidadTrasJuntar,
       CASE WHEN Antes >= Diferencia THEN 0
            WHEN Antes + Cantidad <= Diferencia THEN Cantidad
            ELSE Diferencia - Antes END AS Quitar
INTO #Quitar
FROM Orden;
DELETE FROM #Quitar WHERE Quitar = 0;

-- ------------------------------------------------------------------------------------------------ Lo que hay y lo que se haría
SELECT RTRIM(c.Producto) AS Producto, c.Nombre, c.Ubicar, c.Stock, c.Ubicacion, c.Libre, c.PendienteUbicar, c.Reservado,
       c.Diferencia,
       CASE WHEN c.Diferencia > 0 THEN N'Sobra en ubicaciones' ELSE N'Falta en ubicaciones' END AS Situacion,
       CASE WHEN c.Diferencia > 0 THEN N'Quitar ' + CAST(ISNULL(q.Quitar, 0) AS nvarchar(10)) + N' de huecos libres'
                 + CASE WHEN ISNULL(q.Quitar, 0) < c.Diferencia
                        THEN N'; REVISAR A MANO ' + CAST(c.Diferencia - ISNULL(q.Quitar, 0) AS nvarchar(10)) + N' (no están en huecos libres)'
                        ELSE N'' END
            WHEN @CuadrarFaltas = 1 THEN N'Meter ' + CAST(-c.Diferencia AS nvarchar(10)) + N' como pendiente de ubicar'
            ELSE N'Nada (@CuadrarFaltas = 0)' END AS QueSeHaria
FROM #Cuadre c
     LEFT JOIN (SELECT Producto, SUM(Quitar) AS Quitar FROM #Quitar GROUP BY Producto) q ON q.Producto = c.Producto
ORDER BY ABS(c.Diferencia) DESC, c.Producto;

SELECT COUNT(*) AS ProductosDescuadrados,
       SUM(CASE WHEN Diferencia > 0 THEN 1 ELSE 0 END) AS ConSobra,
       SUM(CASE WHEN Diferencia < 0 THEN 1 ELSE 0 END) AS ConFalta,
       SUM(Diferencia) AS DiferenciaNeta
FROM #Cuadre;

SELECT N'Paso 1: juntar filas libres' AS Paso, Producto, Hueco, Queda AS FilaQueQueda, [NºOrden],
       CantidadAhora, CASE WHEN [NºOrden] = Queda THEN N'Queda con ' + CAST(CantidadQueda AS nvarchar(10)) ELSE N'Se borra' END AS Accion
FROM #Juntar ORDER BY Producto, Hueco, [NºOrden];

SELECT N'Paso 2: quitar lo que sobra' AS Paso, RTRIM(Producto) AS Producto, Hueco, Queda AS Fila, CantidadTrasJuntar, Quitar,
       CantidadTrasJuntar - Quitar AS CantidadFinal, CASE WHEN CantidadTrasJuntar = Quitar THEN N'Se borra' ELSE N'' END AS Accion
FROM #Quitar ORDER BY Producto, Hueco;

IF @Aplicar = 0
BEGIN
    PRINT N'SOLO MIRAR: no se ha cambiado nada. Revisado lo de arriba, @Aplicar = 1.';
    RETURN;
END;

-- ------------------------------------------------------------------------------------------------ Aplicar
DECLARE @esperadas int, @hechas int;
BEGIN TRANSACTION;

    -- Paso 1a: la que queda, con la suma (solo si ninguna de las del hueco ha cambiado)
    SET @esperadas = (SELECT COUNT(*) FROM #Juntar);
    SET @hechas = (SELECT COUNT(*) FROM #Juntar j
                   INNER JOIN Ubicaciones u WITH (UPDLOCK, HOLDLOCK) ON u.[NºOrden] = j.[NºOrden]
                   WHERE u.Cantidad = j.CantidadAhora AND u.Estado = 0);
    IF @hechas <> @esperadas
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'Paso 1: alguna fila libre ha cambiado desde la foto (%d de %d iguales). No se ha hecho nada: repetir.', 16, 1, @hechas, @esperadas);
        RETURN;
    END;

    UPDATE u SET u.Cantidad = j.CantidadQueda, u.Usuario = @Usuario
    FROM Ubicaciones u INNER JOIN #Juntar j ON j.[NºOrden] = u.[NºOrden]
    WHERE j.[NºOrden] = j.Queda AND u.Estado = 0;

    DELETE u
    FROM Ubicaciones u INNER JOIN #Juntar j ON j.[NºOrden] = u.[NºOrden]
    WHERE j.[NºOrden] <> j.Queda AND u.Estado = 0;
    SET @hechas = @@ROWCOUNT;
    PRINT CONCAT(N'Paso 1: ', @hechas, N' filas libres sobrantes borradas (juntadas en la más antigua de su hueco).');

    -- Paso 2: quitar lo que sobra (cada fila tiene que seguir con la cantidad tras juntar)
    UPDATE u SET u.Cantidad = u.Cantidad - q.Quitar, u.Usuario = @Usuario
    FROM Ubicaciones u INNER JOIN #Quitar q ON q.Queda = u.[NºOrden]
    WHERE u.Estado = 0 AND u.Cantidad = q.CantidadTrasJuntar;
    SET @hechas = @@ROWCOUNT;
    SET @esperadas = (SELECT COUNT(*) FROM #Quitar);
    IF @hechas <> @esperadas
    BEGIN
        ROLLBACK TRANSACTION;
        RAISERROR(N'Paso 2: alguna fila ha cambiado desde la foto (%d de %d). No se ha hecho nada: repetir.', 16, 1, @hechas, @esperadas);
        RETURN;
    END;
    DELETE u FROM Ubicaciones u INNER JOIN #Quitar q ON q.Queda = u.[NºOrden] WHERE u.Estado = 0 AND u.Cantidad = 0;
    PRINT CONCAT(N'Paso 2: ', @hechas, N' filas libres rebajadas.');

    -- Paso 3: lo que falta, como pendiente de ubicar
    IF @CuadrarFaltas = 1
    BEGIN
        INSERT INTO Ubicaciones (Empresa, [Almacén], [Número], Cantidad, Estado, Usuario)
        SELECT @Empresa, @Almacen, c.Producto, -c.Diferencia, 2, @Usuario
        FROM #Cuadre c WHERE c.Diferencia < 0;
        PRINT CONCAT(N'Paso 3: ', @@ROWCOUNT, N' productos con lo que faltaba como pendiente de ubicar.');
    END;

COMMIT TRANSACTION;

-- ------------------------------------------------------------------------------------------------ Comprobación
SELECT RTRIM(c.Producto) AS Producto, c.Nombre,
       (SELECT SUM(CAST(e.Cantidad AS int)) FROM ExtractoProducto e WITH (NOLOCK)
         WHERE e.Empresa IN (@Empresa, @Espejo) AND e.[Almacén] = @Almacen AND e.[Número] = c.Producto) AS Stock,
       (SELECT SUM(u.Cantidad) FROM Ubicaciones u WITH (NOLOCK)
         WHERE u.Empresa IN (@Empresa, @Espejo) AND u.[Almacén] = @Almacen AND u.[Número] = c.Producto AND ISNULL(u.Estado, 0) >= 0) AS Ubicacion
FROM #Cuadre c
ORDER BY c.Producto;
GO
