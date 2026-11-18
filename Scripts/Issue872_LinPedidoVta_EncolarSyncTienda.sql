/*
    NestoAPI#872 (Carlos 18/11/26): el «disponible» de la tienda online al momento también cuando las líneas de pedido
    se tocan por SQL directo.

    EL SÍNTOMA
    ----------
    Un pedido pendiente compromete stock sin apunte en ExtractoProducto. La API ya encola el producto en Nesto_sync
    cuando graba líneas (ProductosLineasVentaSync, desde NVEntities.SaveChanges, NestoAPI#705), pero lo que cambia
    LinPedidoVta POR FUERA de EF no pasa por ahí:
      - las muestras que se quitan del pedido al terminar el picking (TransaccionSalidaSql.QuitarDelPedido, SQL directo);
      - Nesto (cliente de escritorio) y los procedimientos almacenados que insertan, borran o cambian líneas.
    La tienda se queda con el disponible descuadrado hasta que lo recoge el nocturno (sincronizar-stocks-nocturno).

    LA REGLA (la misma que ProductosLineasVentaSync.ComprometeStock)
    -----------------------------------------------------------------
    Una línea COMPROMETE stock si es de producto (TipoLinea = 1), está pendiente (-1) o en curso (1), su almacén es ALG,
    REI o ALC y su empresa la 1 o la 3 (titular y espejo). Lo comprometido es su Cantidad.

    QUÉ HACE trgLinPedidoVta_SyncTienda
    -----------------------------------
    AFTER INSERT, UPDATE, DELETE. Por cada sentencia:
      0. Sale en el acto si la sentencia no tocó filas, o si es un UPDATE cuyo SET no lleva ninguna columna que
         cambie lo comprometido (Cantidad, Producto, Almacén, Estado, TipoLinea, Empresa). UPDATE(col) dice que la
         columna está en el SET, no que cambie su valor: por eso el paso 1 compara valores igualmente. Asignar picking,
         cambiar la fecha de entrega, el precio o el texto NO pasan de aquí (son los UPDATE masivos de la tabla).
      1. Comprometido por producto y almacén = SUM(inserted) - SUM(deleted) de las líneas que comprometen. Si da cero,
         el disponible no ha cambiado y NO encola: un UPDATE que reescribe los mismos valores (Nesto manda la fila
         entera), pasar de pendiente a en curso, o mover la línea de un pedido a otro del mismo almacén. Pasar a
         presupuesto, a albarán o borrar la línea libera stock y sí encola; cambiar el producto encola los dos.
      2. Encola el producto y los KITS de los que es componente (su CantidadMontable depende del disponible del
         componente; misma regla que el disparador de ExtractoProducto, NestoAPI#818, y el nocturno, NestoAPI#412).
      3. Solo fichas de la empresa 1 con Estado >= 0, en Nesto_sync (Tabla 'Productos', ModificadoId = RTRIM(Producto)),
         sin repetir lo que ya está pendiente (Sincronizado IS NULL): la publicación manda el estado ACTUAL, con una basta.
    El encolado de la API (ProductosLineasVentaSync) se queda: sobra, pero no estorba (la guarda evita duplicados).

    NUNCA FALLA LA ESCRITURA DE LA LÍNEA
    ------------------------------------
    Como en #818: TRY/CATCH sin ROLLBACK ni THROW y SET XACT_ABORT OFF (en un disparador está ON por defecto y cualquier
    error condenaría la transacción del llamante aunque se capture). Perder un encolado lo arregla el nocturno.

    @@IDENTITY (DIFERENCIA CON #818)
    --------------------------------
    Insertar en Nesto_sync cambia @@IDENTITY de la sesión: tras un INSERT en LinPedidoVta, @@IDENTITY dejaría de ser el
    Nº Orden de la línea y pasaría a ser el Id de Nesto_sync. EF usa SCOPE_IDENTITY() (no le afecta), pero no se ha
    podido comprobar todo lo que inserta líneas (ADO de Nesto viejo, procedimientos): por eso, si el disparador ha
    encolado algo, deja @@IDENTITY como estaba (inserción explícita en una tabla temporal con IDENTITY_INSERT, la
    técnica habitual). SCOPE_IDENTITY() no cambia nunca.

    RENDIMIENTO
    -----------
    LinPedidoVta es una tabla caliente. El coste fijo es el IF del paso 0 (sin leer nada); el resto lee solo
    inserted/deleted y, si algo cambia, Kits, Productos y Nesto_sync por clave.

    COMO SE EJECUTA
    ---------------
    Lo lanza Carlos como sa en NV, desde SSMS. Idempotente (CREATE OR ALTER; requiere SQL Server 2016 SP1 o posterior,
    como el de #818). Se puede lanzar antes o después de publicar la API: la API no cambia por esto.
    Va el ÚLTIMO (sp_settriggerorder) solo si no hay ya otro disparador marcado como último para esa operación.
    Al final: comprobación (SELECT), cómo quitarlo y una prueba comentada que no deja nada (ROLLBACK).
*/
SET NOCOUNT ON;
USE NV;
GO
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER TRIGGER dbo.trgLinPedidoVta_SyncTienda ON dbo.LinPedidoVta
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    -- Lo primero: @@ROWCOUNT es el de la sentencia que disparó (cualquier SET lo pondría a 0).
    IF @@ROWCOUNT = 0
        RETURN;
    SET NOCOUNT ON;
    -- NestoAPI#872 (como #818): sin esto, cualquier error de aquí dentro condenaría la transacción del llamante.
    SET XACT_ABORT OFF;

    -- Un UPDATE cuyo SET no lleva nada que cambie lo comprometido no hace falta ni mirarlo. (En un INSERT UPDATE() es
    -- cierto para todas las columnas; en un DELETE no hay inserted.)
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND NOT (UPDATE(Cantidad) OR UPDATE(Producto) OR UPDATE(Almacén) OR UPDATE(Estado) OR UPDATE(TipoLinea) OR UPDATE(Empresa))
        RETURN;

    DECLARE @identidadAnterior numeric(38, 0) = @@IDENTITY;
    DECLARE @encolados int = 0;

    BEGIN TRY
        ;WITH comprometido AS (
            -- La regla de ProductosLineasVentaSync.ComprometeStock
            SELECT i.Producto, i.Almacén, CAST(ISNULL(i.Cantidad, 0) AS int) AS Cantidad
            FROM inserted i
            WHERE i.TipoLinea = 1 AND i.Estado IN (-1, 1)
              AND i.Empresa IN ('1', '3') AND i.Almacén IN ('ALG', 'REI', 'ALC')
              AND i.Producto IS NOT NULL AND RTRIM(i.Producto) <> ''
            UNION ALL
            SELECT d.Producto, d.Almacén, -CAST(ISNULL(d.Cantidad, 0) AS int)
            FROM deleted d
            WHERE d.TipoLinea = 1 AND d.Estado IN (-1, 1)
              AND d.Empresa IN ('1', '3') AND d.Almacén IN ('ALG', 'REI', 'ALC')
              AND d.Producto IS NOT NULL AND RTRIM(d.Producto) <> ''
        ),
        -- Lo que en esta sentencia suma cero (por producto y almacén) no cambia el disponible.
        cambiados AS (
            SELECT c.Producto
            FROM comprometido c
            GROUP BY c.Producto, c.Almacén
            HAVING SUM(c.Cantidad) <> 0
        ),
        -- El producto y los kits de los que es componente (NestoAPI#412). UNION ya quita los repetidos.
        aEncolar AS (
            SELECT c.Producto AS Número FROM cambiados c
            UNION
            SELECT k.Número
            FROM dbo.Kits k
            INNER JOIN cambiados c ON c.Producto = k.NúmeroAsociado
            WHERE k.Empresa = '1'
        )
        INSERT INTO dbo.Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
        SELECT N'Productos', RTRIM(a.Número), N'Pedido de venta (BD)', GETDATE()
        FROM aEncolar a
        WHERE EXISTS (SELECT 1 FROM dbo.Productos p
                      WHERE p.Empresa = '1' AND p.Número = a.Número AND p.Estado >= 0)
          AND NOT EXISTS (SELECT 1 FROM dbo.Nesto_sync ns
                          WHERE ns.Tabla = N'Productos'
                            AND ns.ModificadoId = RTRIM(a.Número)
                            AND ns.Sincronizado IS NULL);
        SET @encolados = @@ROWCOUNT;
    END TRY
    BEGIN CATCH
        -- NestoAPI#872: ni ROLLBACK ni THROW. Perder este encolado lo arregla el nocturno; perder la línea, nadie.
        SET @encolados = 0;
    END CATCH

    -- Dejar @@IDENTITY como estaba (ver cabecera). Solo si se ha insertado algo en Nesto_sync.
    IF @encolados > 0 AND @identidadAnterior IS NOT NULL
    BEGIN
        BEGIN TRY
            CREATE TABLE #trgLinPedidoVtaSync_Identidad (Id numeric(38, 0) IDENTITY(1, 1) NOT NULL);
            SET IDENTITY_INSERT #trgLinPedidoVtaSync_Identidad ON;
            INSERT INTO #trgLinPedidoVtaSync_Identidad (Id) VALUES (@identidadAnterior);
            SET IDENTITY_INSERT #trgLinPedidoVtaSync_Identidad OFF;
            DROP TABLE #trgLinPedidoVtaSync_Identidad;
        END TRY
        BEGIN CATCH
            -- Si no se puede (p. ej. el llamante tiene IDENTITY_INSERT activo en otra tabla), se deja como esté.
        END CATCH
    END
END
GO

-- El último, solo si no hay ya otro marcado como último para esa operación (sp_settriggerorder daría error).
-- CREATE OR ALTER quita el orden al modificar el disparador, así que se vuelve a poner cada vez.
DECLARE @operacion nvarchar(10);
DECLARE operaciones CURSOR LOCAL FAST_FORWARD FOR SELECT v.op FROM (VALUES (N'INSERT'), (N'UPDATE'), (N'DELETE')) v(op);
OPEN operaciones;
FETCH NEXT FROM operaciones INTO @operacion;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF NOT EXISTS (SELECT 1
                   FROM sys.triggers t
                   INNER JOIN sys.trigger_events e ON e.object_id = t.object_id
                   WHERE t.parent_id = OBJECT_ID(N'dbo.LinPedidoVta')
                     AND t.name <> N'trgLinPedidoVta_SyncTienda'
                     AND e.is_last = 1
                     AND e.type_desc = @operacion)
        EXEC sp_settriggerorder @triggername = N'dbo.trgLinPedidoVta_SyncTienda', @order = N'Last', @stmttype = @operacion;
    ELSE
        PRINT N'Ya hay otro disparador último para ' + @operacion + N' en LinPedidoVta: se deja el orden por defecto (no afecta al resultado).';
    FETCH NEXT FROM operaciones INTO @operacion;
END
CLOSE operaciones;
DEALLOCATE operaciones;
GO

-- Comprobación: debe salir el disparador, activo, y los demás disparadores de LinPedidoVta con su orden.
SELECT t.name, t.is_disabled, e.type_desc AS operacion, e.is_first, e.is_last
FROM sys.triggers t
INNER JOIN sys.trigger_events e ON e.object_id = t.object_id
WHERE t.parent_id = OBJECT_ID(N'dbo.LinPedidoVta')
ORDER BY t.name, e.type_desc;
GO

/*
    CÓMO QUITARLO (vuelta atrás; la API sigue encolando lo suyo como antes):

    DROP TRIGGER IF EXISTS dbo.trgLinPedidoVta_SyncTienda;
*/

/*
    PRUEBA (opcional, no deja nada: todo dentro de una transacción con ROLLBACK). Cambiar @pedido y @orden por una línea
    de producto PENDIENTE (Estado -1) de la empresa 1 en ALG, con Cantidad > 0, cuyo producto NO tenga fila pendiente en
    Nesto_sync.

    DECLARE @empresa char(3) = '1', @pedido int = 0, @orden int = 0;
    DECLARE @producto char(15) = (SELECT Producto FROM LinPedidoVta WHERE Empresa = @empresa AND Número = @pedido AND [Nº Orden] = @orden);
    BEGIN TRANSACTION;

    -- 1) Cambiar la fecha de entrega y reescribir la misma cantidad: NO encola.
    UPDATE LinPedidoVta SET [Fecha Entrega] = [Fecha Entrega], Cantidad = Cantidad
    WHERE Empresa = @empresa AND Número = @pedido AND [Nº Orden] = @orden;
    SELECT 'sin cambios (esperado 0)' AS caso, COUNT(*) AS encolados FROM Nesto_sync
    WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;

    -- 2) Pasar la línea a presupuesto (libera stock): encola UNA fila.
    UPDATE LinPedidoVta SET Estado = -3
    WHERE Empresa = @empresa AND Número = @pedido AND [Nº Orden] = @orden;
    SELECT 'a presupuesto (esperado 1)' AS caso, COUNT(*) AS encolados FROM Nesto_sync
    WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;

    -- 3) Volverla a pendiente con la fila aún pendiente: no se duplica.
    UPDATE LinPedidoVta SET Estado = -1
    WHERE Empresa = @empresa AND Número = @pedido AND [Nº Orden] = @orden;
    SELECT 'otra vez (esperado 1)' AS caso, COUNT(*) AS encolados FROM Nesto_sync
    WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;

    -- 4) @@IDENTITY tras un DELETE que encola: debe seguir siendo el de antes (aquí, el de esta SELECT no cambia nada;
    --    para probarlo con un INSERT, insertar una copia de la línea y comparar @@IDENTITY con SCOPE_IDENTITY()).
    DELETE FROM Nesto_sync WHERE Tabla = N'Productos' AND ModificadoId = RTRIM(@producto) AND Sincronizado IS NULL;
    DECLARE @antes numeric(38, 0) = @@IDENTITY;
    UPDATE LinPedidoVta SET Estado = -3 WHERE Empresa = @empresa AND Número = @pedido AND [Nº Orden] = @orden;
    SELECT 'identidad intacta (esperado 1)' AS caso, CASE WHEN @@IDENTITY = @antes OR (@@IDENTITY IS NULL AND @antes IS NULL) THEN 1 ELSE 0 END AS ok;

    ROLLBACK TRANSACTION;
*/
