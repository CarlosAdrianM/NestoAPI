/*
    NestoAPI#405 - Reparar las ubicaciones duplicadas del picking 99327 (25/08/2026)

    QUE PASO
    --------
    El picking se ejecuto DOS VECES sobre los mismos pedidos, solapandose: la primera pasada
    todavia no habia commiteado cuando arranco la segunda, asi que esta volvio a leer las lineas
    con Picking NULL y las proceso otra vez.

    Prueba: el contador global esta en 99327 y NO existe ninguna linea con Picking = 99326. La
    primera pasada consumio ese numero y la segunda lo piso.

    El numero de picking de la linea se pisa (un UPDATE gana el ultimo), pero las ubicaciones NO:
    cada pasada reservo su propia ubicacion. Resultado: 14 lineas con DOS ubicaciones en estado 3
    (reservado picking), cada una con la cantidad completa. El SP prdInformePicking hace
    sum(Cantidad) por linea, asi que el packing imprime el DOBLE de lo pedido.

    Pedidos afectados: 924333, 924798, 924799. Ninguno albaraneado ni facturado.

    QUE HACE ESTE SCRIPT
    --------------------
    Por cada linea con mas de una ubicacion reservada, deja la MAS ANTIGUA (la de la primera
    pasada, que es la buena) y LIBERA las sobrantes: estado 0 (ubicado/libre) y sin linea de venta.

    NO borra filas. El picking no crea stock: mueve unidades de "libre" a "reservado". La
    ubicacion sobrante son unidades reales que siguen fisicamente en la estanteria; borrarlas las
    haria desaparecer del sistema. Liberandolas, el total por ubicacion no cambia:

        37156 en 008/003/001:  4 reservada + 4 duplicada + 6 libre  = 14
        despues:               4 reservada + 10 libre               = 14

    Acotado al picking 99327 a proposito: es el unico afectado (verificado el 25/08/2026; las dos
    lineas con dos ubicaciones del picking 99319 son legitimas — dos ubicaciones que SUMAN lo
    pedido, no dos veces lo pedido).

    Ejecutar en la BD NV. Revisar la primera SELECT antes de confirmar.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Ubicaciones sobrantes: por cada linea, todas menos la de menor NºOrden
IF OBJECT_ID('tempdb..#Sobrantes') IS NOT NULL DROP TABLE #Sobrantes;

SELECT u.[NºOrden], u.[NºOrdenVta], u.[Número] AS Producto, u.Cantidad,
       u.Pasillo, u.Fila, u.Columna, u.PedidoVta
INTO #Sobrantes
FROM Ubicaciones u
JOIN LinPedidoVta l ON l.[Nº Orden] = u.[NºOrdenVta]
WHERE u.Estado = 3
  AND l.Picking = 99327
  AND u.[NºOrden] > (SELECT MIN(u2.[NºOrden]) FROM Ubicaciones u2
                     WHERE u2.[NºOrdenVta] = u.[NºOrdenVta] AND u2.Estado = 3);

-- COMPROBACION 1: que lo que queda tras liberar cuadra EXACTAMENTE con lo pedido.
-- Si alguna fila sale aqui, PARAR y revisar a mano: no es el patron esperado.
SELECT l.[Número] AS Pedido, l.[Nº Orden] AS OrdenVta, l.Producto, l.Cantidad AS Pedida,
       (SELECT SUM(u.Cantidad) FROM Ubicaciones u
        WHERE u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3
          AND u.[NºOrden] NOT IN (SELECT [NºOrden] FROM #Sobrantes)) AS QuedaraUbicado
FROM LinPedidoVta l
WHERE l.Picking = 99327 AND l.TipoLinea = 1
  AND l.Cantidad <> (SELECT SUM(u.Cantidad) FROM Ubicaciones u
                     WHERE u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3
                       AND u.[NºOrden] NOT IN (SELECT [NºOrden] FROM #Sobrantes));

-- Lo que se va a liberar (deberian ser 14 filas)
SELECT * FROM #Sobrantes ORDER BY PedidoVta, [NºOrdenVta];
SELECT COUNT(*) AS UbicacionesALiberar, SUM(Cantidad) AS UnidadesQueVuelvenALibre FROM #Sobrantes;

/*  Revisada la salida de arriba, quitar el comentario y ejecutar:

BEGIN TRAN;

    -- OJO: Ubicaciones.FechaModificación es una columna timestamp (rowversion), la mantiene
    -- SQL Server sola. Incluirla en el SET da el error 272 "No se puede actualizar una columna
    -- de marca de tiempo". Y Usuario es varchar(30), asi que no cabe cualquier cosa.
    UPDATE u
    SET u.Estado = 0,           -- ubicado / libre
        u.[NºOrdenVta] = NULL,
        u.PedidoVta = NULL,
        u.Usuario = 'Fix NestoAPI#406'   -- ver nota de abajo
    FROM Ubicaciones u
    JOIN #Sobrantes s ON s.[NºOrden] = u.[NºOrden]
    WHERE u.Estado = 3;         -- guarda: no tocar nada que ya no este reservado

    -- COMPROBACION 2: tiene que devolver CERO filas. Si no, ROLLBACK.
    SELECT l.[Número] AS Pedido, l.Producto, l.Cantidad AS Pedida,
           ISNULL((SELECT SUM(u.Cantidad) FROM Ubicaciones u
                   WHERE u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3), 0) AS Ubicada
    FROM LinPedidoVta l
    WHERE l.Picking = 99327 AND l.TipoLinea = 1
      AND l.Cantidad <> ISNULL((SELECT SUM(u.Cantidad) FROM Ubicaciones u
                                WHERE u.[NºOrdenVta] = l.[Nº Orden] AND u.Estado = 3), 0);

-- COMMIT TRAN;   <-- solo si la comprobacion 2 sale vacia
-- ROLLBACK TRAN; <-- si sale cualquier fila

*/

-- DESPUES: volver a imprimir el packing del 99327 y contrastar con el pedido antes de servir.
--
-- NOTA sobre el 'Fix NestoAPI#406' del Usuario: las 14 filas se repararon en produccion antes de
-- crear la issue, cuando el numero que se preveia era el 406. La issue acabo siendo la #405, pero
-- el literal se deja como esta APLICADO para que quien busque esas filas en la BD las encuentre.
