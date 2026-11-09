/*
    NestoAPI#590, slice «Quitar picking» (Carlos 09/11/26). Sustituye al REVOKE de Issue590_QuitarPicking_RevokeNestoViejo.sql
    (borrado): en vez de quitar el permiso (error 229 incomprensible para el usuario), una GUARDA al principio de
    prdDeshacerUbicacionPicking que, si la llama Nesto viejo DIRECTAMENTE, para con un mensaje claro y no toca nada.

    CUÁNDO: DESPUÉS de comprobar en producción el botón nuevo de Nesto (Picking › «Quitar picking del pedido»,
    QuitarPickingController / QuitarPickingPedido de la API). Requiere antes Issue590_fnEsNestoViejo.sql.
    Como sa en NV. Se puede lanzar entero y repetir (si la guarda ya está, no hace nada).

    INTERRUPTOR (una línea; deja hacer a Nesto viejo y solo lo apunta en dbo.NestoViejoSombra):
        UPDATE ParametrosUsuario SET Valor = 'Sombra', [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_QuitarPicking_Modo';
    (Para volver a bloquear: Valor = 'Bloqueo'.) Excepción para un usuario: fila 'NestoViejo_QuitarPicking' = '1' con su usuario
    (ver Issue590_fnEsNestoViejo.sql). Quitar la guarda del todo: bloque «VUELTA ATRÁS» al final.

    Qué hace la guarda:
        IF @@NESTLEVEL = 1 AND dbo.fnEsNestoViejo() = 1 AND dbo.fnNestoViejoPermitido('QuitarPicking') = 0
           -> modo Bloqueo: apunta 'Bloqueado' en NestoViejoSombra, RAISERROR «Esto ya solo se puede hacer desde Nesto: …» y
              RETURN -1 (antes del BEGIN TRANSACTION del procedimiento: no se ha tocado nada; la línea sigue con su picking).
           -> modo Sombra: lo apunta ('Sombra') y sigue como siempre.
      - @@NESTLEVEL = 1: solo cuando se llama directamente. prdCrearAlbaránVta (el único otro objeto que lo llama, 09/11/26) lo hace
        con @@NESTLEVEL = 2 y sigue funcionando también desde Nesto viejo (albaranes de la empresa 3, etc.).
        ⚠️ Si Nesto viejo lo llamara a través de sp_executesql, dentro del procedimiento @@NESTLEVEL sería 2 y la guarda NO saltaría.
        En la caché de planes del 09/11 no hay ninguna llamada «Prepared/Adhoc» a este procedimiento (todo apunta a RPC directo), pero
        hay que comprobarlo con la PRUEBA de abajo; la sombra de Issue590_SombraEscriturasNestoViejo_XE.sql también lo dirá.
      - La API (EntityFramework, QuitarPickingPedido.cs: EXEC prdDeshacerUbicacionPicking @p0, 0, @p1, 1) no es Nesto viejo: no cambia.

    ⚠️ EFECTO AÑADIDO (aceptado): Nesto viejo llama a este procedimiento también al BORRAR UNA LÍNEA o UN PEDIDO que tiene picking.
       Desde hoy, desde Nesto viejo, eso tampoco se puede (sale el mismo mensaje): primero hay que quitar el picking en Nesto.

    Cómo se ha hecho (09/11/26): la definición viva (sys.sql_modules) se lee AL EJECUTAR el script y se le inserta la guarda justo
    después de la línea de cabecera «CREATE PROCEDURE prdDeshacerUbicacionPicking … @Reubicar as bit  AS». El resto del
    procedimiento queda byte a byte igual (el script lo comprueba al final). El procedimiento se creó con QUOTED_IDENTIFIER OFF y
    ANSI_NULLS ON (uses_quoted_identifier = 0): se respeta. Los permisos (EXECUTE a public) no cambian con un ALTER.

    PRUEBA (tras lanzarlo): nueva conexión de SSMS como sa con «Opciones > Parámetros de conexión adicionales»: Application Name=Nesto
        EXEC dbo.prdDeshacerUbicacionPicking '1', 0, 0, 0;     -- pedido 0 y orden 0: tiene que salir el mensaje, sin tocar nada
    y desde Nesto viejo (Carlos), «quitar picking» de un pedido de prueba: tiene que salir el mensaje y seguir con su picking.
*/
USE NV;
GO
SET NOCOUNT ON;
GO
IF OBJECT_ID(N'dbo.fnEsNestoViejo') IS NULL OR OBJECT_ID(N'dbo.fnNestoViejoModo') IS NULL OR OBJECT_ID(N'dbo.NestoViejoSombra') IS NULL
BEGIN
    RAISERROR(N'Falta Issue590_fnEsNestoViejo.sql: lánzalo antes y vuelve a lanzar este. No se ha tocado nada.', 16, 1);
    SET NOEXEC ON;   -- el resto del script ya no se ejecuta
END
GO

-- ANTES
SELECT 'ANTES' Momento, OBJECT_NAME(object_id) Objeto, uses_ansi_nulls, uses_quoted_identifier, LEN(definition) Longitud,
       CASE WHEN CHARINDEX(N'GUARDA_NESTO_VIEJO_590', definition) > 0 THEN 'YA TIENE LA GUARDA' ELSE 'sin guarda' END Guarda
FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerUbicacionPicking');

-- Filas «(defecto)»: modo Bloqueo y sin excepciones
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_QuitarPicking_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_QuitarPicking_Modo', '(defecto)', 'Bloqueo', 'NestoAPI#590', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_QuitarPicking' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_QuitarPicking', '(defecto)', '0', 'NestoAPI#590', GETDATE());
GO

-- El procedimiento se creó con QUOTED_IDENTIFIER OFF: el ALTER se lanza igual (en este lote no hay comillas dobles).
SET QUOTED_IDENTIFIER OFF;
SET ANSI_NULLS ON;
GO
IF OBJECT_ID(N'dbo.fnEsNestoViejo') IS NULL OR OBJECT_ID(N'dbo.fnNestoViejoModo') IS NULL OR OBJECT_ID(N'dbo.NestoViejoSombra') IS NULL
BEGIN
    RAISERROR(N'Falta Issue590_fnEsNestoViejo.sql: lánzalo antes. No se ha tocado nada.', 16, 1);
    RETURN;
END

DECLARE @antes nvarchar(max) = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerUbicacionPicking'));
IF @antes IS NULL BEGIN RAISERROR(N'No existe dbo.prdDeshacerUbicacionPicking.', 16, 1); RETURN; END
IF CHARINDEX(N'GUARDA_NESTO_VIEJO_590', @antes) > 0
BEGIN
    PRINT 'prdDeshacerUbicacionPicking ya tiene la guarda: nada que hacer.';
    RETURN;
END

-- Línea de cabecera: «CREATE PROCEDURE prdDeshacerUbicacionPicking … AS» (una sola línea el 09/11/26)
DECLARE @pNombre int = CHARINDEX(N'PROCEDURE prdDeshacerUbicacionPicking', @antes);
DECLARE @pFinLinea int = CHARINDEX(NCHAR(10), @antes, @pNombre);
DECLARE @cabecera nvarchar(max) = RTRIM(REPLACE(SUBSTRING(@antes, @pNombre, @pFinLinea - @pNombre), NCHAR(13), N''));
IF @pNombre = 0 OR @pFinLinea = 0 OR RIGHT(@cabecera, 3) <> N' AS' OR CHARINDEX(N'@Reubicar as bit', @cabecera) = 0
BEGIN
    RAISERROR(N'La cabecera de prdDeshacerUbicacionPicking no es la esperada (… @Reubicar as bit  AS en una línea). Revisar a mano; no se ha tocado nada.', 16, 1);
    RETURN;
END
-- La palabra de delante de PROCEDURE (CREATE, o ALTER si alguien lo guardó así)
DECLARE @pVerbo int = CASE WHEN SUBSTRING(@antes, @pNombre - 7, 7) = N'CREATE ' THEN @pNombre - 7
                           WHEN SUBSTRING(@antes, @pNombre - 6, 6) = N'ALTER '  THEN @pNombre - 6 ELSE 0 END;
IF @pVerbo = 0 BEGIN RAISERROR(N'No encuentro CREATE/ALTER delante de PROCEDURE. No se ha tocado nada.', 16, 1); RETURN; END

DECLARE @guarda nvarchar(max) =
N'-- GUARDA_NESTO_VIEJO_590 (NestoAPI#590, 09/11/26): el picking se quita desde Nesto (ventana Picking, «Quitar picking del pedido»).
-- Solo si la llama Nesto viejo directamente (@@NESTLEVEL = 1); prdCrearAlbaránVta (nivel 2) y la API no se ven afectados.
-- Interruptor: ParametrosUsuario «(defecto)» NestoViejo_QuitarPicking_Modo = Sombra (deja hacer y lo apunta en NestoViejoSombra).
IF @@NESTLEVEL = 1 AND dbo.fnEsNestoViejo() = 1 AND dbo.fnNestoViejoPermitido(''QuitarPicking'') = 0
BEGIN
	DECLARE @ModoNestoViejo590 varchar(10) = dbo.fnNestoViejoModo(''QuitarPicking'');
	INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
	VALUES (''QuitarPicking'', CASE WHEN @ModoNestoViejo590 = ''Bloqueo'' THEN ''Bloqueado'' ELSE ''Sombra'' END, ''prdDeshacerUbicacionPicking'',
	        ''Empresa '' + RTRIM(@Empresa) + '' / Pedido '' + CAST(@Pedido AS varchar(12)) + '' / NºOrden '' + CAST(@NºOrden AS varchar(12)),
	        ''@Reubicar = '' + CAST(@Reubicar AS varchar(1)));
	IF @ModoNestoViejo590 = ''Bloqueo''
	BEGIN
		RAISERROR (N''Esto ya solo se puede hacer desde Nesto: el picking se quita en la ventana Picking, con «Quitar picking del pedido». Para borrar una línea o un pedido con picking, quite antes el picking en Nesto.'', 16, 1);
		RETURN -1;
	END
END
-- FIN_GUARDA_NESTO_VIEJO_590
';

DECLARE @nueva nvarchar(max) = STUFF(@antes, @pFinLinea + 1, 0, @guarda);              -- guarda tras la línea de cabecera
SET @nueva = STUFF(@nueva, @pVerbo, @pNombre - @pVerbo, N'ALTER ');                       -- CREATE -> ALTER
EXEC sys.sp_executesql @nueva;
PRINT 'prdDeshacerUbicacionPicking: guarda añadida.';

-- Comprobación: lo guardado, quitando la guarda y con el verbo original, tiene que ser IGUAL a lo de antes.
DECLARE @despues nvarchar(max) = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerUbicacionPicking'));
DECLARE @sinGuarda nvarchar(max) = REPLACE(@despues, @guarda, N'');
DECLARE @pV2 int = CHARINDEX(N'PROCEDURE prdDeshacerUbicacionPicking', @sinGuarda);
DECLARE @largoVerboAhora int = CASE WHEN SUBSTRING(@sinGuarda, @pV2 - 7, 7) = N'CREATE ' THEN 7 ELSE 6 END;   -- 'CREATE ' o 'ALTER '
SET @sinGuarda = STUFF(@sinGuarda, @pV2 - @largoVerboAhora, @largoVerboAhora, SUBSTRING(@antes, @pVerbo, @pNombre - @pVerbo));
IF CAST(@sinGuarda AS varbinary(max)) = CAST(@antes AS varbinary(max))
    PRINT 'OK: el resto del procedimiento es byte a byte el de antes.';
ELSE
    RAISERROR(N'OJO: el procedimiento guardado no coincide con el de antes más la guarda. Revisar a mano (VUELTA ATRÁS abajo).', 16, 1);
GO
SET QUOTED_IDENTIFIER ON;
GO

-- DESPUÉS: Guarda = 'OK', uses_quoted_identifier = 0, y los permisos de siempre (EXECUTE a public)
SELECT 'DESPUES' Momento, OBJECT_NAME(object_id) Objeto, uses_ansi_nulls, uses_quoted_identifier, LEN(definition) Longitud,
       CASE WHEN CHARINDEX(N'GUARDA_NESTO_VIEJO_590', definition) > 0 THEN 'OK' ELSE 'FALTA' END Guarda
FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerUbicacionPicking');
SELECT pr.name Principal, pe.permission_name Permiso, pe.state_desc Estado
FROM sys.database_permissions pe JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID(N'dbo.prdDeshacerUbicacionPicking');
SELECT Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]QuitarPicking%';
GO

SET NOEXEC OFF;
GO

-- Seguimiento: lo que Nesto viejo ha intentado
-- SELECT * FROM dbo.NestoViejoSombra WHERE Operacion = 'QuitarPicking' ORDER BY Id DESC;

/* VUELTA ATRÁS (quitar la guarda del todo; el interruptor de arriba suele bastar). Seleccionar y ejecutar como sa:
SET QUOTED_IDENTIFIER OFF;
GO
DECLARE @d nvarchar(max) = (SELECT definition FROM sys.sql_modules WHERE object_id = OBJECT_ID(N'dbo.prdDeshacerUbicacionPicking'));
DECLARE @i int = CHARINDEX(N'-- GUARDA_NESTO_VIEJO_590', @d);
DECLARE @f int = CHARINDEX(NCHAR(10), @d, CHARINDEX(N'-- FIN_GUARDA_NESTO_VIEJO_590', @d)) + 1;   -- hasta el final de la línea FIN_
IF @i > 0 AND @f > @i
BEGIN
    SET @d = STUFF(@d, @i, @f - @i, N'');
    DECLARE @p int = CHARINDEX(N'PROCEDURE prdDeshacerUbicacionPicking', @d);
    IF SUBSTRING(@d, @p - 7, 7) = N'CREATE ' SET @d = STUFF(@d, @p - 7, 7, N'ALTER ');
    EXEC sys.sp_executesql @d;
    PRINT 'Guarda quitada.';
END
GO
SET QUOTED_IDENTIFIER ON;
*/
