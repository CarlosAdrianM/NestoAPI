/*
    Promesa en riesgo (NestoAPI#693): la causa original no se pierde al recalcular (Carlos, 18/11/26).

    Pedido 929751: se detectó en riesgo el 05/11 porque faltaba stock; el stock llegó el 17/11 y el texto, rehecho con la
    situación de ese momento, acabó diciendo «todo está listo en Algete, pero no salió el jueves 05/11/2026», que era falso.

    CausaOriginal: lo que faltaba al detectarlo, más lo que se fue sumando al empeorar o actualizar, en JSON corto
    ([{"Producto":"45949","Nombre":"JABON CARBON ACTIVO","Unidades":9}, …], 30 como mucho). Lo escribe la API; cuando llega lo
    que faltaba, la fila se resuelve con «Ha llegado lo que faltaba» y el texto pasa a ser la buena noticia
    («Faltaba 45949 JABON CARBON ACTIVO; llegó el martes 17/11. Sale el martes 17/11.»).

    TodoListoDesde y EnObservacion: el pedido que va tarde con todo en Algete se observa sin avisar; si pasa un día laborable completo
    de Algete y sigue sin salir, se avisa al vendedor (caso raro). La PDA y el picking no cambian.

    Opcional y fuera del EDMX: la API usa las tres columnas solo si están las tres. Sin ellas funciona igual (textos nuevos), pero la
    buena noticia sale sin el producto («Ya está todo en Algete: sale en el próximo picking.»), no se avisa de los pedidos con todo en
    Algete que no salen y se apunta en ELMAH una vez. Se puede lanzar antes o después del deploy.

    Ejecutar en SSMS contra NV como sa. Idempotente: se puede lanzar dos veces. Sin GRANT nuevo: la tabla ya tiene SELECT, INSERT y
    UPDATE para la API (Issue693_PedidosPromesaRiesgo.sql).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') IS NULL
    RAISERROR('Falta la tabla dbo.PedidosPromesaRiesgo: lance antes Scripts/Issue693_PedidosPromesaRiesgo.sql.', 16, 1);
GO

-- Columna nueva en su propio lote
IF OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') IS NOT NULL AND COL_LENGTH('dbo.PedidosPromesaRiesgo', 'CausaOriginal') IS NULL
    ALTER TABLE dbo.PedidosPromesaRiesgo ADD CausaOriginal nvarchar(max) NULL;
GO

-- Desde cuándo lo vemos todo en Algete (o en picking) yendo tarde (Carlos, 18/11/26). En su propio lote.
IF OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') IS NOT NULL AND COL_LENGTH('dbo.PedidosPromesaRiesgo', 'TodoListoDesde') IS NULL
    ALTER TABLE dbo.PedidosPromesaRiesgo ADD TodoListoDesde datetime NULL;
GO

-- Fila «en observación»: todo en Algete y va tarde, pero aún no ha pasado un día laborable completo de Algete. No avisa ni sale
-- en el listado de Nesto ni en el correo resumen; si pasa el día laborable y sigue sin salir, la API la convierte en aviso
-- («Está todo en Algete desde el lunes 16/11 y sigue sin salir…», EnObservacion = 0). En su propio lote.
IF OBJECT_ID('dbo.PedidosPromesaRiesgo', 'U') IS NOT NULL AND COL_LENGTH('dbo.PedidosPromesaRiesgo', 'EnObservacion') IS NULL
    ALTER TABLE dbo.PedidosPromesaRiesgo ADD EnObservacion bit NOT NULL CONSTRAINT DF_PedidosPromesaRiesgo_EnObservacion DEFAULT (0);
GO

-- Comprobación: las tres con valor (CausaOriginal -1 = nvarchar(max)). La API solo las usa si están las tres.
SELECT COL_LENGTH('dbo.PedidosPromesaRiesgo', 'CausaOriginal') AS CausaOriginal,
       COL_LENGTH('dbo.PedidosPromesaRiesgo', 'TodoListoDesde') AS TodoListoDesde,
       COL_LENGTH('dbo.PedidosPromesaRiesgo', 'EnObservacion') AS EnObservacion;
GO

-- Cómo va:
-- SELECT Pedido, FechaPrometida, FechaNueva, Detectado, Resuelto, MotivoResolucion, Motivo, CausaOriginal, TodoListoDesde, EnObservacion
-- FROM dbo.PedidosPromesaRiesgo ORDER BY Actualizado DESC;
