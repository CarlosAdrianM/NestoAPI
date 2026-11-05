/*
    NestoAPI#818 (Carlos 05/11/26): el stock de la tienda online al momento.

    EL SÍNTOMA
    ----------
    04/11/26: entran 115 unidades de la 42294 (tenía stock 0) y la web sigue sin stock todo el día. Los movimientos de
    stock no encolaban el producto en Nesto_sync: trgProductosIns/trgProductosUpd solo saltan al cambiar la FICHA, y lo
    que toca el extracto (recepciones de Ariadna y de Nesto, inventario, regularizaciones, albaranes, kits…) solo lo
    recogía el job «sincronizar-stocks-nocturno» (NestoAPI#410) a las 02:00, con ventana de 48 h.

    LA REGLA (Carlos 05/11/26)
    --------------------------
    «Más que vigilar compra e inventario, lo que hay que vigilar es el extracto del producto. Todo lo que toca
    ExtractoProducto toca el stock, salvo cuando en el mismo momento se hace un +1 y un -1 (algo que sume cero): eso se
    puede ignorar» (producto en carpeta: se da de baja y de alta a la vez; un cambio de ubicación en un inventario).

    Por eso la captura es un disparador en la BD y no código en la API: en ExtractoProducto escriben prdExtrProducto
    (el único camino de NestoAPI, y el de casi todo Nesto), pero también prdCrearAlbaránVta/Cmp, prdCrearFacturaVta/Cmp,
    prdDeshacerAlbaránVta/Cmp, prdDeshacerInventario, prdDeshacerExtractoProducto… Un disparador los ve a todos.

    QUÉ HACE trgExtractoProducto_SyncTienda
    ---------------------------------------
    AFTER INSERT, UPDATE, DELETE (no solo INSERT: deshacer un albarán o un inventario BORRA apuntes y eso también cambia
    el stock). Por cada sentencia (que es el «lote»: prdExtrProducto pasa todo un diario en un único INSERT…SELECT):
      1. Neto por producto y almacén = SUM(inserted.Cantidad) - SUM(deleted.Cantidad), solo empresas 1 y 3 (titular y
         espejo) y almacenes ALG, REI, ALC: exactamente lo que suma ProductoService.CalcularStockBase para los Stocks
         que viajan en el mensaje. Por producto Y ALMACÉN, no solo por producto: un traspaso ALG→REI suma cero en el
         producto pero cambia dos de los tres stocks que se publican; el +1/-1 de la carpeta o del cambio de
         ubicación va en el mismo almacén y suma cero ahí, que es lo que se ignora. Un UPDATE que no cambia
         cantidad, almacén, producto ni empresa neta cero y no encola.
      2. Si el neto es distinto de cero, encola el producto y los KITS de los que es componente (su CantidadMontable
         depende del stock del componente y el kit no tiene apunte propio; misma regla que el nocturno, NestoAPI#412).
      3. Solo productos de la empresa 1 con Estado >= 0 (los mismos que encola el nocturno).
      4. Misma cola, mismos campos y misma clave que los demás encoladores: Nesto_sync (Tabla 'Productos',
         ModificadoId = RTRIM(Número)), y la misma guarda: no encola si el producto ya tiene una fila pendiente
         (Sincronizado IS NULL); la publicación manda siempre el estado ACTUAL, con una basta.
    La pasada de los 5 minutos (job «sincronizar-productos») publica el ProductoDTO con los Stocks de ALG, REI y ALC
    calculados en ese momento: lo recibido se ve en la web en ≤ 5 minutos.

    NUNCA FALLA EL APUNTE ORIGINAL
    ------------------------------
    Perder un encolado es aceptable (el nocturno lo recoge); perder un apunte de stock no. Todo va en TRY/CATCH y el
    CATCH no hace ROLLBACK ni relanza. SET XACT_ABORT OFF dentro del disparador: en un disparador XACT_ABORT está ON por
    defecto y cualquier error condenaría la transacción del llamante aunque se capture. Límite que no se puede quitar:
    un error que condene la transacción por sí mismo (un interbloqueo, por ejemplo) la condena igual que sin disparador.
    Va el ÚLTIMO (sp_settriggerorder) para correr después de las comprobaciones de inventario de trgExtractoProductoUpd,
    que sí pueden hacer ROLLBACK.

    EFECTOS SECUNDARIOS COMPROBADOS
    -------------------------------
    - @@IDENTITY: tras encolar, @@IDENTITY es el Id de Nesto_sync y no el Nº Orden del apunte. prdExtrProducto devuelve
      @@IDENTITY, pero todos sus llamantes solo miran el signo (< 0 o <= 0: prdCrearAlbaránVta/Cmp,
      prdContabilizarInventario, prdPasarProductoAInmovilizado, prdCuadrarCuenta4009 y ServicioExtractoProducto de la
      API, comprobado el 05/11/26) y el Id de Nesto_sync es positivo. SCOPE_IDENTITY() no cambia.
    - Permisos: el disparador es de dbo, como Nesto_sync, Productos y Kits: la cadena de propiedad basta y no hace falta
      ningún GRANT a los usuarios que escriben en el extracto.
    - Volumen (28/10-04/11/26): 600-950 apuntes al día de 400-600 productos, en 80-145 lotes: unas pocas centenas de
      filas al día en Nesto_sync, una publicación por producto y pasada (GestorSincronizacion agrupa, NestoAPI#408).
    - No se excluye a NUEVAVISION\RDS2016$ (la API) como hacen los disparadores de Productos: aquellos lo excluyen porque
      la API publica ella misma la ficha que cambia; un movimiento de stock de la API (recibir en Ariadna) no lo publica
      nadie más, y es justo el caso de la 42294.

    LO QUE SIGUE HACIENDO EL NOCTURNO (sincronizar-stocks-nocturno, 02:00, 48 h)
    -------------------------------------------------------------------------
    Lo que no pasa por ExtractoProducto: pedidos pendientes grabados por fuera de la API (LinPedidoVta, #705) y
    descuentos de tarifa (DescuentosProducto, #413); y de red para el encolado que se pierda (un CATCH, o la ventana de
    milisegundos en que la pasada ya ha calculado el stock y aún no ha marcado la fila como sincronizada).

    COMO SE EJECUTA
    ---------------
    Como sa en NV, desde SSMS. Idempotente (CREATE OR ALTER + orden). Se puede lanzar antes o después de publicar la
    API: la API no cambia por esto. Vuelta atrás: DROP TRIGGER dbo.trgExtractoProducto_SyncTienda;
    Al final hay una prueba comentada que no deja nada (transacción con ROLLBACK).
*/
SET NOCOUNT ON;
USE NV;
GO

CREATE OR ALTER TRIGGER dbo.trgExtractoProducto_SyncTienda ON dbo.ExtractoProducto
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    -- NestoAPI#818: sin esto, cualquier error de aquí dentro condenaría la transacción del apunte aunque se capture.
    SET XACT_ABORT OFF;

    -- Un UPDATE que no toca nada que cambie el stock no hace falta ni mirarlo. (En un INSERT UPDATE() es cierto para
    -- todas las columnas; en un DELETE no hay inserted.)
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND NOT (UPDATE(Cantidad) OR UPDATE(Almacén) OR UPDATE(Número) OR UPDATE(Empresa))
        RETURN;

    BEGIN TRY
        ;WITH movimientos AS (
            SELECT i.Número, i.Almacén, CAST(i.Cantidad AS int) AS Cantidad
            FROM inserted i
            WHERE i.Empresa IN ('1', '3') AND i.Almacén IN ('ALG', 'REI', 'ALC')
            UNION ALL
            SELECT d.Número, d.Almacén, -CAST(d.Cantidad AS int)
            FROM deleted d
            WHERE d.Empresa IN ('1', '3') AND d.Almacén IN ('ALG', 'REI', 'ALC')
        ),
        -- La regla de Carlos: lo que en esta sentencia suma cero (por producto y almacén) no cambia ningún stock.
        cambiados AS (
            SELECT m.Número
            FROM movimientos m
            GROUP BY m.Número, m.Almacén
            HAVING SUM(m.Cantidad) <> 0
        ),
        -- El producto y los kits de los que es componente (NestoAPI#412). UNION ya quita los repetidos.
        aEncolar AS (
            SELECT c.Número FROM cambiados c
            UNION
            SELECT k.Número
            FROM dbo.Kits k
            INNER JOIN cambiados c ON c.Número = k.NúmeroAsociado
            WHERE k.Empresa = '1'
        )
        INSERT INTO dbo.Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
        SELECT N'Productos', RTRIM(a.Número), N'Movimiento de stock', GETDATE()
        FROM aEncolar a
        WHERE EXISTS (SELECT 1 FROM dbo.Productos p
                      WHERE p.Empresa = '1' AND p.Número = a.Número AND p.Estado >= 0)
          AND NOT EXISTS (SELECT 1 FROM dbo.Nesto_sync ns
                          WHERE ns.Tabla = N'Productos'
                            AND ns.ModificadoId = RTRIM(a.Número)
                            AND ns.Sincronizado IS NULL);
    END TRY
    BEGIN CATCH
        -- NestoAPI#818: ni ROLLBACK ni THROW. Perder este encolado lo arregla el nocturno; perder el apunte, nadie.
    END CATCH
END
GO

-- Después de trgExtractoProductoUpd (comprobaciones de inventario, que pueden deshacer el apunte). CREATE OR ALTER
-- quita el orden al modificar el disparador, así que se vuelve a poner cada vez.
EXEC sp_settriggerorder @triggername = N'dbo.trgExtractoProducto_SyncTienda', @order = N'Last', @stmttype = N'INSERT';
EXEC sp_settriggerorder @triggername = N'dbo.trgExtractoProducto_SyncTienda', @order = N'Last', @stmttype = N'UPDATE';
EXEC sp_settriggerorder @triggername = N'dbo.trgExtractoProducto_SyncTienda', @order = N'Last', @stmttype = N'DELETE';
GO

-- Comprobación: debe salir el disparador, activo.
SELECT t.name, t.is_disabled, OBJECT_NAME(t.parent_id) AS tabla
FROM sys.triggers t
WHERE t.name = N'trgExtractoProducto_SyncTienda';
GO

/*
    PRUEBA (opcional, no deja nada: todo dentro de una transacción con ROLLBACK; los mismos casos se probaron el 05/11/26
    en una copia local de las tablas con nivel de compatibilidad 100). Cambiar @producto por una referencia viva de la
    empresa 1 con algún apunte en ALG, SIN fila pendiente en Nesto_sync y SIN inventario en curso. Los apuntes de prueba
    copian Grupo, Delegación y Forma Venta de su último apunte (son obligatorios).

    DECLARE @producto char(15) = '42294';
    BEGIN TRANSACTION;

    -- 1) +1 y -1 en la misma sentencia y el mismo almacén (la carpeta): suma cero, NO encola.
    INSERT INTO ExtractoProducto (Empresa, Número, Fecha, Almacén, Grupo, Cantidad, Delegación, [Forma Venta], Texto, Diario)
    SELECT '1', u.Número, GETDATE(), 'ALG', u.Grupo, v.Cantidad, u.Delegación, u.[Forma Venta], 'Prueba NestoAPI#818', 'Prueba818'
    FROM (SELECT TOP 1 * FROM ExtractoProducto WHERE Empresa = '1' AND Número = @producto AND Almacén = 'ALG'
          ORDER BY [Nº Orden] DESC) u
    CROSS JOIN (VALUES (1), (-1)) v(Cantidad);
    SELECT 'suma cero (esperado 0)' AS caso, COUNT(*) AS encolados FROM Nesto_sync
    WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;

    -- 2) Una entrada de 5: encola UNA fila (y las de los kits de los que sea componente, que no cuentan aquí).
    INSERT INTO ExtractoProducto (Empresa, Número, Fecha, Almacén, Grupo, Cantidad, Delegación, [Forma Venta], Texto, Diario)
    SELECT TOP 1 '1', Número, GETDATE(), 'ALG', Grupo, 5, Delegación, [Forma Venta], 'Prueba NestoAPI#818', 'Prueba818'
    FROM ExtractoProducto WHERE Empresa = '1' AND Número = @producto AND Almacén = 'ALG' ORDER BY [Nº Orden] DESC;
    SELECT 'entrada (esperado 1)' AS caso, COUNT(*) AS encolados FROM Nesto_sync
    WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;

    -- 3) Otra entrada con la fila aún pendiente: no se duplica.
    INSERT INTO ExtractoProducto (Empresa, Número, Fecha, Almacén, Grupo, Cantidad, Delegación, [Forma Venta], Texto, Diario)
    SELECT TOP 1 '1', Número, GETDATE(), 'ALG', Grupo, 2, Delegación, [Forma Venta], 'Prueba NestoAPI#818', 'Prueba818'
    FROM ExtractoProducto WHERE Empresa = '1' AND Número = @producto AND Almacén = 'ALG' ORDER BY [Nº Orden] DESC;
    SELECT 'segunda entrada (esperado 1)' AS caso, COUNT(*) AS encolados FROM Nesto_sync
    WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;

    ROLLBACK TRANSACTION;
*/
