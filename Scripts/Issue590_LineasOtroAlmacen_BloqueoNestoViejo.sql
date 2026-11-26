/*
    NestoAPI#590 (Carlos 26/11/26): desde Nesto viejo, quien tiene como almacén REI o ALC (su parámetro «AlmacénPedidoVta») no
    puede cambiar ni borrar líneas de pedido de venta que NO sean de su almacén. En especial el precio y el descuento de las de
    Algete. Tampoco vale el rodeo de pasar la línea a su almacén, cambiarla y devolverla a ALG: el primer paso (cambiar el
    almacén de una línea que era de otro) ya se bloquea.
    Requiere antes Issue590_fnEsNestoViejo.sql. Como sa en NV. Se puede lanzar entero y repetir (idempotente).

    EMPIEZA EN MODO BLOQUEO: desde Nesto viejo no se guarda el cambio y sale un mensaje.
    Cada intento queda en dbo.NestoViejoSombra (Operacion 'LineasOtroAlmacen').
    INTERRUPTOR (pasa a solo apuntar, una línea):
        UPDATE ParametrosUsuario SET Valor = 'Sombra',  [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_LineasOtroAlmacen_Modo';
    VOLVER A BLOQUEAR:
        UPDATE ParametrosUsuario SET Valor = 'Bloqueo', [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_LineasOtroAlmacen_Modo';
    Apagado total: DISABLE TRIGGER dbo.trgLinPedidoVtaNestoViejoOtroAlmacen ON dbo.LinPedidoVta;
    Excepción para un usuario (sigue pudiendo en modo Bloqueo, y queda apuntado como 'Permitido'):
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'NestoViejo_LineasOtroAlmacen', 'Javier', '1', 'NestoAPI#590', GETDATE());

    A QUIÉN: al usuario de la conexión (ORIGINAL_LOGIN, sin dominio) cuyo parámetro propio «AlmacénPedidoVta» de la empresa 1 es
    REI o ALC. El 26/11/26 son 11: 8 con ALC y 3 con REI. OJO: entre los de ALC están Javier y CARLOS, que no son personal de
    tienda; si tienen que seguir pudiendo, la excepción de arriba. Quien no tiene el parámetro, o lo tiene en ALG u otro, no se toca.

    QUÉ BLOQUEA, en una línea cuyo almacén (el que TENÍA) no es el del usuario:
      - un UPDATE que cambie de verdad Precio, Descuento, DescuentoProducto, DescuentoCliente, DescuentoPP, [Aplicar Dto],
        Cantidad, Producto, IVA o Almacén;
      - un DELETE.
    QUÉ NO BLOQUEA:
      - las líneas de su propio almacén;
      - dar de alta líneas nuevas (el INSERT), ni terminar de rellenar una línea que ese mismo usuario acaba de crear (menos de
        15 minutos): Nesto viejo puede insertar y luego completar. Eso no abre ningún rodeo: una línea nueva ya nace con el precio
        que se le ponga;
      - los cambios que no tocan esas columnas (estado, albarán, factura, picking, fecha de entrega, texto…);
      - la API, Nesto nuevo, SSMS, sa y los jobs (primera línea: fnEsNestoViejo() = 0 -> RETURN), ni lo que cambie otro
        desencadenador en cadena (TRIGGER_NESTLEVEL() > 1).

    RENDIMIENTO: para todo lo que no es Nesto viejo, el coste es una función que solo mira APP_NAME().
    ORDEN: en Bloqueo hace ROLLBACK de toda la sentencia.

    PRUEBA (tras lanzarlo): como un usuario de tienda, o en SSMS con «Parámetros de conexión adicionales»: Application Name=Nesto
    y un login cuyo AlmacénPedidoVta sea REI o ALC:
        BEGIN TRAN;
        UPDATE TOP (1) dbo.LinPedidoVta SET Descuento = 0.5 WHERE Empresa = '1' AND Almacén = 'ALG' AND Estado = -1 AND FechaCreacion < DATEADD(day, -1, GETDATE());
        -- tiene que dar el error «Esto ya solo se puede hacer desde Nesto…» y no cambiar nada
        IF @@TRANCOUNT > 0 ROLLBACK;
        SELECT TOP 5 * FROM dbo.NestoViejoSombra WHERE Operacion = 'LineasOtroAlmacen' ORDER BY Id DESC;   -- 'Bloqueado'
*/
USE NV;
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.fnEsNestoViejo') IS NULL OR OBJECT_ID(N'dbo.fnNestoViejoModo') IS NULL
   OR OBJECT_ID(N'dbo.fnNestoViejoPermitido') IS NULL OR OBJECT_ID(N'dbo.NestoViejoSombra') IS NULL
BEGIN
    RAISERROR(N'Falta Issue590_fnEsNestoViejo.sql: lánzalo antes y vuelve a lanzar este. No se ha tocado nada.', 16, 1);
    SET NOEXEC ON;   -- el resto del script ya no se ejecuta
END
GO

-- ANTES
SELECT 'ANTES' Momento, t.name, t.is_disabled FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.LinPedidoVta');
SELECT 'ANTES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]LineasOtroAlmacen%';
GO

-- 1) Filas «(defecto)»: modo Bloqueo y sin excepciones (si ya existen, NO se tocan)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_LineasOtroAlmacen_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_LineasOtroAlmacen_Modo', '(defecto)', 'Bloqueo', 'NestoAPI#590', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_LineasOtroAlmacen' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_LineasOtroAlmacen', '(defecto)', '0', 'NestoAPI#590', GETDATE());
GO

-- 2) El desencadenador
CREATE OR ALTER TRIGGER dbo.trgLinPedidoVtaNestoViejoOtroAlmacen ON dbo.LinPedidoVta
AFTER UPDATE, DELETE
AS
BEGIN
    -- NestoAPI#590 26/11/26: desde Nesto viejo, las tiendas (REI, ALC) solo cambian las líneas de su almacén.
    IF dbo.fnEsNestoViejo() = 0 RETURN;     -- API, Nesto nuevo, SSMS, sa, jobs: nada más (primera línea a propósito)
    IF TRIGGER_NESTLEVEL() > 1 RETURN;      -- lo cambia otro desencadenador, no el usuario
    SET NOCOUNT ON;

    DECLARE @login nvarchar(128) = ORIGINAL_LOGIN();
    DECLARE @usuario varchar(30) = LEFT(SUBSTRING(@login, CHARINDEX(N'\', @login) + 1, 128), 30);
    DECLARE @suAlmacen varchar(10);
    SELECT @suAlmacen = UPPER(RTRIM(LTRIM(Valor)))
    FROM dbo.ParametrosUsuario
    WHERE Empresa = '1' AND Clave = N'AlmacénPedidoVta' AND Usuario = @usuario;   -- el suyo propio, sin «(defecto)»

    IF @suAlmacen IS NULL OR @suAlmacen NOT IN ('REI', 'ALC') RETURN;            -- no es de una tienda

    DECLARE @haceUnRato datetime = DATEADD(minute, -15, GETDATE());
    DECLARE @cambios TABLE (Clave nvarchar(100), Detalle nvarchar(2000));

    -- Cambios de una línea que era de otro almacén
    INSERT @cambios (Clave, Detalle)
    SELECT TOP (200)
           LEFT(RTRIM(d.Empresa) + N'/' + CONVERT(nvarchar(12), d.Número) + N'/' + CONVERT(nvarchar(12), d.[Nº Orden]), 100),
           N'CAMBIO (' + @suAlmacen + N' toca ' + RTRIM(d.Almacén) + N') ' + ISNULL(RTRIM(d.Producto), N'')
               + CASE WHEN ISNULL(i.Precio, 0) <> ISNULL(d.Precio, 0)
                      THEN N' precio ' + CONVERT(nvarchar(30), d.Precio) + N'->' + CONVERT(nvarchar(30), i.Precio) ELSE N'' END
               + CASE WHEN ISNULL(i.Descuento, 0) <> ISNULL(d.Descuento, 0)
                      THEN N' dto ' + CONVERT(nvarchar(30), d.Descuento) + N'->' + CONVERT(nvarchar(30), i.Descuento) ELSE N'' END
               + CASE WHEN ISNULL(i.DescuentoProducto, 0) <> ISNULL(d.DescuentoProducto, 0)
                      THEN N' dtoProducto ' + CONVERT(nvarchar(30), d.DescuentoProducto) + N'->' + CONVERT(nvarchar(30), i.DescuentoProducto) ELSE N'' END
               + CASE WHEN ISNULL(i.DescuentoCliente, 0) <> ISNULL(d.DescuentoCliente, 0)
                      THEN N' dtoCliente ' + CONVERT(nvarchar(30), d.DescuentoCliente) + N'->' + CONVERT(nvarchar(30), i.DescuentoCliente) ELSE N'' END
               + CASE WHEN ISNULL(i.DescuentoPP, 0) <> ISNULL(d.DescuentoPP, 0)
                      THEN N' dtoPP ' + CONVERT(nvarchar(30), d.DescuentoPP) + N'->' + CONVERT(nvarchar(30), i.DescuentoPP) ELSE N'' END
               + CASE WHEN ISNULL(i.[Aplicar Dto], 0) <> ISNULL(d.[Aplicar Dto], 0) THEN N' aplicarDto' ELSE N'' END
               + CASE WHEN ISNULL(i.Cantidad, 0) <> ISNULL(d.Cantidad, 0)
                      THEN N' cantidad ' + CONVERT(nvarchar(30), d.Cantidad) + N'->' + CONVERT(nvarchar(30), i.Cantidad) ELSE N'' END
               + CASE WHEN ISNULL(RTRIM(i.Producto), '') <> ISNULL(RTRIM(d.Producto), '')
                      THEN N' producto ->' + ISNULL(RTRIM(i.Producto), N'') ELSE N'' END
               + CASE WHEN ISNULL(RTRIM(i.IVA), '') <> ISNULL(RTRIM(d.IVA), '')
                      THEN N' IVA ' + ISNULL(RTRIM(d.IVA), N'') + N'->' + ISNULL(RTRIM(i.IVA), N'') ELSE N'' END
               + CASE WHEN ISNULL(RTRIM(i.Almacén), '') <> ISNULL(RTRIM(d.Almacén), '')
                      THEN N' almacén ' + ISNULL(RTRIM(d.Almacén), N'') + N'->' + ISNULL(RTRIM(i.Almacén), N'') ELSE N'' END
    FROM deleted d
    INNER JOIN inserted i ON i.Empresa = d.Empresa AND i.Número = d.Número AND i.[Nº Orden] = d.[Nº Orden]
    WHERE ISNULL(RTRIM(d.Almacén), '') <> '' AND UPPER(RTRIM(d.Almacén)) <> @suAlmacen
      -- una línea que ese mismo usuario acaba de crear se puede terminar de rellenar
      AND NOT (d.FechaCreacion >= @haceUnRato AND d.Usuario = @login)
      AND (   ISNULL(i.Precio, 0) <> ISNULL(d.Precio, 0)
           OR ISNULL(i.Descuento, 0) <> ISNULL(d.Descuento, 0)
           OR ISNULL(i.DescuentoProducto, 0) <> ISNULL(d.DescuentoProducto, 0)
           OR ISNULL(i.DescuentoCliente, 0) <> ISNULL(d.DescuentoCliente, 0)
           OR ISNULL(i.DescuentoPP, 0) <> ISNULL(d.DescuentoPP, 0)
           OR ISNULL(i.[Aplicar Dto], 0) <> ISNULL(d.[Aplicar Dto], 0)
           OR ISNULL(i.Cantidad, 0) <> ISNULL(d.Cantidad, 0)
           OR ISNULL(RTRIM(i.Producto), '') <> ISNULL(RTRIM(d.Producto), '')
           OR ISNULL(RTRIM(i.IVA), '') <> ISNULL(RTRIM(d.IVA), '')
           OR ISNULL(RTRIM(i.Almacén), '') <> ISNULL(RTRIM(d.Almacén), ''));

    -- Borrados de una línea que era de otro almacén (en un DELETE no hay filas en inserted)
    IF NOT EXISTS (SELECT 1 FROM inserted)
        INSERT @cambios (Clave, Detalle)
        SELECT TOP (200)
               LEFT(RTRIM(d.Empresa) + N'/' + CONVERT(nvarchar(12), d.Número) + N'/' + CONVERT(nvarchar(12), d.[Nº Orden]), 100),
               N'BORRADO (' + @suAlmacen + N' borra ' + RTRIM(d.Almacén) + N') ' + ISNULL(RTRIM(d.Producto), N'')
                   + N' x' + CONVERT(nvarchar(30), d.Cantidad) + N' a ' + CONVERT(nvarchar(30), d.Precio)
        FROM deleted d
        WHERE ISNULL(RTRIM(d.Almacén), '') <> '' AND UPPER(RTRIM(d.Almacén)) <> @suAlmacen
          AND NOT (d.FechaCreacion >= @haceUnRato AND d.Usuario = @login);

    IF NOT EXISTS (SELECT 1 FROM @cambios) RETURN;

    DECLARE @modo varchar(10) = dbo.fnNestoViejoModo('LineasOtroAlmacen');
    DECLARE @permitido bit = dbo.fnNestoViejoPermitido('LineasOtroAlmacen');

    IF @modo = 'Bloqueo' AND @permitido = 0
    BEGIN
        RAISERROR (N'Esto ya solo se puede hacer desde Nesto: desde aquí solo puede cambiar las líneas del almacén de su tienda. Las de otro almacén (precio, descuento, cantidad, producto, almacén o borrarlas) se cambian en el pedido en Nesto. No se ha guardado nada.', 16, 1);
        ROLLBACK TRANSACTION;
        -- Tras el ROLLBACK del desencadenador, esto ya no se deshace: queda el rastro del intento.
        INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
        SELECT 'LineasOtroAlmacen', 'Bloqueado', 'LinPedidoVta', Clave, Detalle FROM @cambios;
        RETURN;
    END

    INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
    SELECT 'LineasOtroAlmacen', CASE WHEN @modo = 'Bloqueo' THEN 'Permitido' ELSE 'Sombra' END, 'LinPedidoVta', Clave, Detalle FROM @cambios;
END
GO

-- DESPUÉS: trgLinPedidoVtaNestoViejoOtroAlmacen creado y activo; los demás, sin cambios
SELECT 'DESPUES' Momento, t.name, t.is_disabled, t.modify_date FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.LinPedidoVta');
SELECT 'DESPUES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]LineasOtroAlmacen%';
-- A quién afecta hoy
SELECT RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Almacen FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = N'AlmacénPedidoVta' AND RTRIM(Valor) IN ('REI', 'ALC') ORDER BY 2, 1;
GO
SET NOEXEC OFF;
GO

-- SEGUIMIENTO:
-- SELECT TOP 100 * FROM dbo.NestoViejoSombra WHERE Operacion = 'LineasOtroAlmacen' ORDER BY Id DESC;

/* VUELTA ATRÁS completa (el interruptor de arriba suele bastar):
DROP TRIGGER IF EXISTS dbo.trgLinPedidoVtaNestoViejoOtroAlmacen;
*/
