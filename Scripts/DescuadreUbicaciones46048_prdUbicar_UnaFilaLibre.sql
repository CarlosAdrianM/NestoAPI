-- 30/10/26: descuadre del 46048 en Algete (Stock 44, Ubicaciones 53).
-- prdUbicar (David, 31/05/04) suma lo que se ubica con
--     update ubicaciones set cantidad=cantidad+@cantidad where empresa=@empresa and almacen=@almacen and numero=@producto
--            and pasillo=@pasillo and fila=@fila and columna=@columna and estado=0
-- es decir, a TODAS las filas libres (estado 0) del producto en ese hueco. Si hay dos, lo ubicado cuenta dos veces.
-- El 28/10/26 se ubicó el albarán de compra 125747 (+9 del 46048) en 004/004/012, donde había dos filas libres
-- (320751201 y 322574101): +9 a cada una → 9 de más.
--
-- Cambio: se suma solo a UNA fila libre (la más antigua). El resto del procedimiento no se toca.
-- No se reescribe el procedimiento a mano: se toma el cuerpo vigente en producción y se sustituye esa única línea, así
-- que no hay riesgo de pisar otro cambio. Si la línea no está exactamente una vez, el script no hace nada.
--
-- La API (Ariadna nueva, RepositorioUbicacionesAlmacen.SQL_UBICAR) ya junta las filas libres del hueco antes de llamar a
-- prdUbicar; este cambio cubre a Ariadna Vieja, que llama al procedimiento directamente.
--
-- Lanzar como sa en NV. @Aplicar = 0: solo enseña lo que haría. El procedimiento está creado con ANSI_NULLS ON y
-- QUOTED_IDENTIFIER OFF (sys.sql_modules, 30/10/26): se mantienen. Los permisos (EXECUTE a public) no cambian con ALTER.
USE NV;
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER OFF;
GO
SET NOCOUNT ON;

DECLARE @Aplicar bit = 0;   -- 0 = solo mirar; 1 = cambiar el procedimiento

DECLARE @cuerpo nvarchar(max) = OBJECT_DEFINITION(OBJECT_ID(N'dbo.prdUbicar'));
DECLARE @viejo nvarchar(400) = N'update ubicaciones set cantidad=cantidad+@cantidad where empresa=@empresa and almacen=@almacen and numero=@producto and pasillo=@pasillo and fila=@fila and columna=@columna and estado=0';
DECLARE @nuevo nvarchar(800) = N'-- 30/10/26 (descuadre del 46048): solo a UNA fila libre, la más antigua. Antes se sumaba a todas las del hueco.
	update ubicaciones set cantidad=cantidad+@cantidad where [NºOrden]=(select min([NºOrden]) from ubicaciones where empresa=@empresa and almacen=@almacen and numero=@producto and pasillo=@pasillo and fila=@fila and columna=@columna and estado=0)';

DECLARE @veces int = (LEN(@cuerpo) - LEN(REPLACE(@cuerpo, @viejo, N''))) / LEN(@viejo);
DECLARE @inicio int = CHARINDEX(N'CREATE PROCEDURE', @cuerpo);

IF @cuerpo IS NULL OR @veces <> 1 OR @inicio = 0
BEGIN
    RAISERROR(N'prdUbicar no tiene la línea esperada exactamente una vez (veces = %d): no se toca. Revisar a mano.', 16, 1, @veces);
    RETURN;
END;

IF CHARINDEX(N'30/10/26 (descuadre del 46048)', @cuerpo) > 0
BEGIN
    PRINT N'prdUbicar ya tiene el cambio: nada que hacer.';
    RETURN;
END;

DECLARE @alter nvarchar(max) = STUFF(REPLACE(@cuerpo, @viejo, @nuevo), @inicio, LEN(N'CREATE'), N'ALTER');

IF @Aplicar = 0
BEGIN
    PRINT N'SOLO MIRAR. La línea se encuentra 1 vez y quedaría así:';
    PRINT @nuevo;
    PRINT N'Para aplicarlo: @Aplicar = 1.';
    RETURN;
END;

EXEC sp_executesql @alter;
PRINT N'prdUbicar cambiado.';

-- Comprobación
SELECT CASE WHEN CHARINDEX(N'30/10/26 (descuadre del 46048)', OBJECT_DEFINITION(OBJECT_ID(N'dbo.prdUbicar'))) > 0
            THEN N'OK: suma a una sola fila libre' ELSE N'NO SE HA CAMBIADO' END AS prdUbicar,
       m.uses_ansi_nulls, m.uses_quoted_identifier
FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(N'dbo.prdUbicar');
GO
