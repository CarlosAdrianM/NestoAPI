/*
    NestoAPI#590 + #784 (Carlos 09/11/26): desde Nesto viejo ya NO se cuenta, ni se contabiliza, ni se deshace inventario.
    LANZAR HOY (antes del inventario general del sábado 21/11/26). Requiere antes Issue590_fnEsNestoViejo.sql.
    Como sa en NV. Se puede lanzar entero y repetir (idempotente).

    INTERRUPTOR (una línea; deja hacer a Nesto viejo y solo lo apunta en dbo.NestoViejoSombra):
        UPDATE ParametrosUsuario SET Valor = 'Sombra', [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_Inventario_Modo';
    (Volver a bloquear: Valor = 'Bloqueo'.) Apagado total: DISABLE TRIGGER dbo.trgInventariosNestoViejo ON dbo.Inventarios;
    Sin excepciones por usuario (decisión de Carlos): ni Carlos ni Alfredo pueden desde Nesto viejo.

    Dónde se cuenta y se contabiliza desde hoy:
      - Nesto (el nuevo), módulo Inventario: GET/POST/PUT api/Inventarios, Comprobaciones y Contabilizar (#788-#790).
      - Ariadna (la nueva), pantalla de inventario por ubicación (#795).
      La API entra como NUEVAVISION\RDS2016$ con APP_NAME «EntityFramework» / «NestoAPI-Hangfire»: no es Nesto viejo y no le afecta.

    Cómo escribe Nesto viejo en Inventarios (comprobado en producción el 09/11/26):
      - Directamente en la tabla (tiene INSERT/UPDATE/DELETE por los grupos Almacén, Tiendas, Administración, BUILTIN\Usuarios…):
        las filas salen con Aplicacion = 'Nesto' (el DEFAULT de la columna) y Usuario = suser_sname(). En octubre-noviembre lo han
        usado a diario Alfredo, Paloma, Reina y Santiago (el 09/11 a las 10:35, Alfredo, 2 ubicaciones de ALG a 0).
      - Procedimientos: prdContabilizarInventario (el mismo que usa la API, #787/#784 con la marca CONTEXT_INFO del 06/11) y
        prdDeshacerInventario (ejecutado el 09/11 a las 10:53; la API no lo usa). Ningún otro objeto escribe en Inventarios
        (sys.sql_modules: prdInformeInventarioStockDistinto y prdInformePendienteRecuentoFisico solo leen; prdUbicacionesParaInventario
        y prdRellenarReposicionStock solo leen la tabla real; trdUpdUbicaciones, trgExtractoProductoUpd/Dlt y trgUbicacionesDtl la leen).
      - Inventarios NO tenía ningún desencadenador (los del 06/11 con CONTEXT_INFO están en Ubicaciones y ExtractoProducto y no se tocan).

    Qué hace este script:
      1) Filas «(defecto)»: NestoViejo_Inventario_Modo = 'Bloqueo'.
      2) dbo.trgInventariosNestoViejo (AFTER INSERT, UPDATE, DELETE en Inventarios). Primera línea: si no es Nesto viejo, sale.
         Si es Nesto viejo, en modo Bloqueo: mensaje «Esto ya solo se puede hacer desde Nesto: …», ROLLBACK (deshace también lo que
         hubiera hecho antes el mismo procedimiento o la misma transacción: p. ej. los apuntes de ExtractoProducto de
         prdContabilizarInventario) y lo apunta en NestoViejoSombra como 'Bloqueado'. En modo Sombra solo lo apunta.
         Esto cubre las escrituras directas y prdContabilizarInventario llamado desde Nesto viejo, sin tocar ese procedimiento (es el
         único call site de la API y está recién arreglado para el 21/11; no se toca).
         Excepción de seguridad: Ariadna VIEJA marca sus filas con Aplicacion = 'Ariadna' (5.000-8.000 filas en cada inventario de
         ALG desde 2004; este año está previsto volver a usarla el 21/11). No se sabe con qué APP_NAME se conecta (la sesión «DataProj»
         de Andre podría ser ella, o podría entrar como «Nesto»): por si acaso, las filas 'Ariadna' PENDIENTES (Estado 1, sin
         NºTraspaso) se pueden seguir creando, cambiando y borrando. Contabilizarlas (Estado 2 / NºTraspaso) desde Nesto viejo, no.
      3) Guarda al principio de prdDeshacerInventario (misma técnica que en el picking: se inserta tras la línea de cabecera de la
         definición viva y se comprueba que el resto queda igual). Hace falta además del desencadenador porque si la fila de
         Inventarios ya no estuviera en Estado 2, el UPDATE final no tocaría ninguna fila y el procedimiento habría deshecho
         ExtractoProducto y Ubicaciones sin que el desencadenador saltara. Sin @@NESTLEVEL: nadie más lo llama (09/11/26).

    PRUEBA (tras lanzarlo): conexión de SSMS con «Opciones > Parámetros de conexión adicionales»: Application Name=Nesto
        BEGIN TRAN; UPDATE TOP (1) dbo.Inventarios SET StockReal = StockReal WHERE Estado = 2; IF @@TRANCOUNT > 0 ROLLBACK;
        -> tiene que salir el mensaje (y la fila 'Bloqueado' en NestoViejoSombra).
        EXEC dbo.prdDeshacerInventario '1', -1, 'NOEXISTE';   -> el mensaje, sin tocar nada.
    Y desde una conexión normal de SSMS el mismo UPDATE NO debe dar error (después, ROLLBACK).
    Antes del 21/11: probar con Andre una lectura de inventario en Ariadna VIEJA (si se va a usar) y, mientras, consultar
        SELECT program_name, login_name, host_name FROM sys.dm_exec_sessions WHERE login_name = 'NUEVAVISION\Andre';
    para saber con qué nombre entra (si es «Nesto», la excepción de las filas 'Ariadna' es la que la deja pasar).
*/
USE NV;
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- ANTES
SELECT 'ANTES' Momento, t.name Desencadenador, t.is_disabled,
       (SELECT STRING_AGG(e.type_desc, ',') FROM sys.trigger_events e WHERE e.object_id = t.object_id) Eventos
FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.Inventarios');
SELECT 'ANTES' Momento, OBJECT_NAME(object_id) Objeto, uses_quoted_identifier,
       CASE WHEN CHARINDEX(N'GUARDA_NESTO_VIEJO_590', definition) > 0 THEN 'YA TIENE LA GUARDA' ELSE 'sin guarda' END Guarda
FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerInventario');
GO

IF OBJECT_ID(N'dbo.fnEsNestoViejo') IS NULL OR OBJECT_ID(N'dbo.fnNestoViejoModo') IS NULL OR OBJECT_ID(N'dbo.NestoViejoSombra') IS NULL
BEGIN
    RAISERROR(N'Falta Issue590_fnEsNestoViejo.sql: lánzalo antes y vuelve a lanzar este. No se ha tocado nada.', 16, 1);
    SET NOEXEC ON;   -- el resto del script ya no se ejecuta
END
GO

-- 1) Modo Bloqueo
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_Inventario_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_Inventario_Modo', '(defecto)', 'Bloqueo', 'NestoAPI#590', GETDATE());
GO

-- 2) El desencadenador
CREATE OR ALTER TRIGGER dbo.trgInventariosNestoViejo ON dbo.Inventarios
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    -- NestoAPI#590 09/11/26: el inventario solo se cuenta y se contabiliza desde Nesto (api/Inventarios) y Ariadna.
    IF dbo.fnEsNestoViejo() = 0 RETURN;     -- API, SSMS, jobs: nada más (primera línea a propósito)
    SET NOCOUNT ON;

    IF NOT EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted) RETURN;   -- la sentencia no tocó ninguna fila

    -- Ariadna vieja contando: filas suyas y pendientes (ver cabecera del script)
    IF NOT EXISTS (SELECT 1 FROM inserted WHERE ISNULL(Aplicacion, '') <> 'Ariadna' OR ISNULL(Estado, 1) <> 1 OR NºTraspaso IS NOT NULL)
       AND NOT EXISTS (SELECT 1 FROM deleted WHERE ISNULL(Aplicacion, '') <> 'Ariadna' OR ISNULL(Estado, 1) <> 1 OR NºTraspaso IS NOT NULL)
        RETURN;

    DECLARE @modo varchar(10) = dbo.fnNestoViejoModo('Inventario');
    DECLARE @accion varchar(10) = CASE WHEN NOT EXISTS (SELECT 1 FROM deleted) THEN 'INSERT'
                                       WHEN NOT EXISTS (SELECT 1 FROM inserted) THEN 'DELETE' ELSE 'UPDATE' END;
    -- Lo que se apunta se prepara ANTES del ROLLBACK (las variables de tabla no se deshacen)
    DECLARE @rastro TABLE (Clave nvarchar(100), Detalle nvarchar(2000));
    DECLARE @filas int, @min int, @max int, @almacenes nvarchar(200), @productos nvarchar(1000);
    SELECT @filas = COUNT(*), @min = MIN(NºOrden), @max = MAX(NºOrden)
    FROM (SELECT NºOrden FROM inserted UNION SELECT NºOrden FROM deleted) f;
    SELECT @almacenes = STRING_AGG(RTRIM(Almacén), ',')
    FROM (SELECT Almacén FROM inserted UNION SELECT Almacén FROM deleted) a;
    SELECT @productos = STRING_AGG(RTRIM(Número), ',')
    FROM (SELECT TOP (20) Número FROM (SELECT Número FROM inserted UNION SELECT Número FROM deleted) x ORDER BY Número) p;
    INSERT @rastro (Clave, Detalle)
    VALUES (LEFT(N'NºOrden ' + CAST(@min AS nvarchar(12)) + N'-' + CAST(@max AS nvarchar(12)), 100),
            LEFT(@accion + N' de ' + CAST(@filas AS nvarchar(12)) + N' fila(s); almacén(es) ' + ISNULL(@almacenes, N'')
                 + N'; productos ' + ISNULL(@productos, N''), 2000));

    IF @modo = 'Bloqueo'
    BEGIN
        RAISERROR (N'Esto ya solo se puede hacer desde Nesto: el inventario se cuenta y se contabiliza en Nesto (pestaña Productos, grupo «Inventarios») o en Ariadna. No se ha guardado nada.', 16, 1);
        ROLLBACK TRANSACTION;
        -- Tras el ROLLBACK del desencadenador, lo que se escribe aquí ya no se deshace: queda el rastro del intento.
        INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
        SELECT 'Inventario', 'Bloqueado', 'Inventarios', Clave, Detalle FROM @rastro;
        RETURN;
    END

    INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
    SELECT 'Inventario', 'Sombra', 'Inventarios', Clave, Detalle FROM @rastro;
END
GO

-- 3) Guarda en prdDeshacerInventario (creado con QUOTED_IDENTIFIER OFF: se respeta)
SET QUOTED_IDENTIFIER OFF;
GO
DECLARE @antes nvarchar(max) = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerInventario'));
IF @antes IS NULL BEGIN RAISERROR(N'No existe dbo.prdDeshacerInventario.', 16, 1); RETURN; END
IF CHARINDEX(N'GUARDA_NESTO_VIEJO_590', @antes) > 0 BEGIN PRINT 'prdDeshacerInventario ya tiene la guarda: nada que hacer.'; RETURN; END

-- Cabecera (una sola línea el 09/11/26): «CREATE PROCEDURE prdDeshacerInventario @Empresa as char(3), @Traspaso as int, @Número as char(15) AS»
DECLARE @pNombre int = CHARINDEX(N'PROCEDURE prdDeshacerInventario', @antes);
DECLARE @pFinLinea int = CHARINDEX(NCHAR(10), @antes, @pNombre);
DECLARE @cabecera nvarchar(max) = RTRIM(REPLACE(SUBSTRING(@antes, @pNombre, @pFinLinea - @pNombre), NCHAR(13), N''));
IF @pNombre = 0 OR @pFinLinea = 0 OR RIGHT(@cabecera, 3) <> N' AS' OR CHARINDEX(N'@Traspaso as int', @cabecera) = 0
BEGIN
    RAISERROR(N'La cabecera de prdDeshacerInventario no es la esperada. Revisar a mano; no se ha tocado el procedimiento.', 16, 1);
    RETURN;
END
DECLARE @pVerbo int = CASE WHEN SUBSTRING(@antes, @pNombre - 7, 7) = N'CREATE ' THEN @pNombre - 7
                           WHEN SUBSTRING(@antes, @pNombre - 6, 6) = N'ALTER '  THEN @pNombre - 6 ELSE 0 END;
IF @pVerbo = 0 BEGIN RAISERROR(N'No encuentro CREATE/ALTER delante de PROCEDURE. No se ha tocado el procedimiento.', 16, 1); RETURN; END

DECLARE @guarda nvarchar(max) =
N'-- GUARDA_NESTO_VIEJO_590 (NestoAPI#590, 09/11/26): Nesto viejo ya no deshace inventarios (el inventario se lleva en Nesto).
-- Interruptor: ParametrosUsuario «(defecto)» NestoViejo_Inventario_Modo = Sombra (deja hacer y lo apunta en NestoViejoSombra).
IF dbo.fnEsNestoViejo() = 1
BEGIN
	DECLARE @ModoNestoViejo590 varchar(10) = dbo.fnNestoViejoModo(''Inventario'');
	INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
	VALUES (''Inventario'', CASE WHEN @ModoNestoViejo590 = ''Bloqueo'' THEN ''Bloqueado'' ELSE ''Sombra'' END, ''prdDeshacerInventario'',
	        ''Empresa '' + RTRIM(@Empresa) + '' / Traspaso '' + CAST(@Traspaso AS varchar(12)) + '' / '' + RTRIM(@Número), NULL);
	IF @ModoNestoViejo590 = ''Bloqueo''
	BEGIN
		RAISERROR (N''El inventario ya no se puede deshacer desde esta versión antigua de Nesto: el inventario se lleva desde Nesto (pestaña Productos, grupo «Inventarios»). Si hay que deshacer uno, avise a Informática. No se ha tocado nada.'', 16, 1);
		RETURN -1;
	END
END
-- FIN_GUARDA_NESTO_VIEJO_590
';

DECLARE @nueva nvarchar(max) = STUFF(@antes, @pFinLinea + 1, 0, @guarda);
SET @nueva = STUFF(@nueva, @pVerbo, @pNombre - @pVerbo, N'ALTER ');
EXEC sys.sp_executesql @nueva;
PRINT 'prdDeshacerInventario: guarda añadida.';

-- Comprobación: sin la guarda y con el verbo de antes, tiene que ser byte a byte lo de antes
DECLARE @sinGuarda nvarchar(max) = REPLACE((SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerInventario')), @guarda, N'');
DECLARE @pV2 int = CHARINDEX(N'PROCEDURE prdDeshacerInventario', @sinGuarda);
DECLARE @largoVerboAhora int = CASE WHEN SUBSTRING(@sinGuarda, @pV2 - 7, 7) = N'CREATE ' THEN 7 ELSE 6 END;
SET @sinGuarda = STUFF(@sinGuarda, @pV2 - @largoVerboAhora, @largoVerboAhora, SUBSTRING(@antes, @pVerbo, @pNombre - @pVerbo));
IF CAST(@sinGuarda AS varbinary(max)) = CAST(@antes AS varbinary(max))
    PRINT 'OK: el resto de prdDeshacerInventario es byte a byte el de antes.';
ELSE
    RAISERROR(N'OJO: prdDeshacerInventario guardado no coincide con el de antes más la guarda. Revisar a mano.', 16, 1);
GO
SET QUOTED_IDENTIFIER ON;
GO

-- DESPUÉS
SELECT 'DESPUES' Momento, t.name Desencadenador, t.is_disabled,
       (SELECT STRING_AGG(e.type_desc, ',') FROM sys.trigger_events e WHERE e.object_id = t.object_id) Eventos
FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.Inventarios');                 -- trgInventariosNestoViejo, INSERT,UPDATE,DELETE
SELECT 'DESPUES' Momento, OBJECT_NAME(object_id) Objeto, uses_quoted_identifier,          -- 0, como antes
       CASE WHEN CHARINDEX(N'GUARDA_NESTO_VIEJO_590', definition) > 0 THEN 'OK' ELSE 'FALTA' END Guarda
FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerInventario');
-- Los del 06/11 siguen con su marca CONTEXT_INFO (no se han tocado): los cinco deben decir OK
SELECT OBJECT_NAME(object_id) Objeto, CASE WHEN definition LIKE '%context_info()%' THEN 'OK' ELSE 'FALTA' END MarcaInventario
FROM sys.sql_modules
WHERE object_id IN (OBJECT_ID('dbo.prdContabilizarInventario'), OBJECT_ID('dbo.trdUpdUbicaciones'),
                    OBJECT_ID('dbo.trgExtractoProductoUpd'), OBJECT_ID('dbo.trgExtractoProductoDlt'), OBJECT_ID('dbo.trgUbicacionesDtl'));
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_Inventario_Modo';
GO

-- Seguimiento: SELECT * FROM dbo.NestoViejoSombra WHERE Operacion = 'Inventario' ORDER BY Id DESC;

/* VUELTA ATRÁS completa (el interruptor de arriba suele bastar). Como sa:
DROP TRIGGER IF EXISTS dbo.trgInventariosNestoViejo;
GO
SET QUOTED_IDENTIFIER OFF;
GO
DECLARE @d nvarchar(max) = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerInventario'));
DECLARE @i int = CHARINDEX(N'-- GUARDA_NESTO_VIEJO_590', @d);
DECLARE @f int = CHARINDEX(NCHAR(10), @d, CHARINDEX(N'-- FIN_GUARDA_NESTO_VIEJO_590', @d)) + 1;
IF @i > 0 AND @f > @i
BEGIN
    SET @d = STUFF(@d, @i, @f - @i, N'');
    DECLARE @p int = CHARINDEX(N'PROCEDURE prdDeshacerInventario', @d);
    IF SUBSTRING(@d, @p - 7, 7) = N'CREATE ' SET @d = STUFF(@d, @p - 7, 7, N'ALTER ');
    EXEC sys.sp_executesql @d;
    PRINT 'Guarda quitada.';
END
GO
SET QUOTED_IDENTIFIER ON;
*/

SET NOEXEC OFF;
GO
