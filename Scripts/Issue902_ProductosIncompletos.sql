/*
    NestoAPI#902 (Carlos 24/11/26): productos incompletos. «Sacar una pieza de un producto» sin que sea un inventario
    (POST api/Almacen/ProductosIncompletos/SacarPieza), completarlo cuando llegue el recambio (…/{id}/Completar) o darlo de baja
    (…/{id}/Baja), y el listado (GET api/Almacen/ProductosIncompletos).

    Lo que hace (para sa, idempotente; ANTES de que nadie saque una pieza: sin la tabla los endpoints dan 503 y sin el parámetro o
    el almacén, también):
      1. dbo.ProductosIncompletos (fuera del EDMX), en su propio lote. Estado con CHECK Incompleto / Completado / Baja.
      2. Los dos diarios de sistema (DiariosProducto empresa '1', Sistema = 1, sin recálculo de precio medio, como «_Ficticio» de #894):
         «_SacaPieza» (sacar y completar) y «_BajaIncom» (la baja: merma con causa conocida). Nunca «_RegInvent»: no es un inventario.
         PreExtrProducto.Diario tiene clave ajena a DiariosProducto: sin ellos, la primera operación falla.
      3. El almacén ficticio de incompletos «INC» («Incompletos», decisión de Carlos 26/11/26), COPIANDO la fila de otro ficticio
         (PRV) y fijando las columnas que usa el código (ver el paso). Tiene que ser activo, NO ser ALG/REI/ALC (son lo único que
         cuentan el stock disponible, el motor de fechas y la tienda) y SIN control de ubicaciones. La API lo comprueba y, si no, 503.
      4. ParametrosUsuario empresa '1', fila «(defecto)», clave «AlmacenIncompletos». Se crea VACÍA si no existe y, si INC existe
         y sigue vacía, se pone 'INC' (sin valor, 503 «falta el almacén de incompletos»).

    GRANT: la tabla es nueva → SELECT, INSERT, UPDATE (y DELETE por si hay que limpiar) a la cuenta de la API. El resto ya lo tiene
    (la API ya lee Almacenes, ParametrosUsuario, Productos, ExtractoProducto y Ubicaciones y ya escribe en PreExtrProducto,
    Ubicaciones, ContadoresGlobales y Modificaciones, y llama a prdExtrProducto y sp_getapplock: #894).
    Vuelta atrás: DROP TABLE dbo.ProductosIncompletos y borrar las filas de los pasos 2 y 4 cuando no haya nada en PreExtrProducto
    ni en ExtractoProducto con esos diarios (si ya se ha usado, NO: el extracto los referencia).
*/

SET NOCOUNT ON;
GO

-- 1. La tabla (en su propio lote)
IF OBJECT_ID(N'dbo.ProductosIncompletos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductosIncompletos
    (
        Id                 int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_ProductosIncompletos PRIMARY KEY,
        Empresa            char(3)        NOT NULL,
        -- De donde salió la unidad (y adonde vuelve al completarla); también donde entró la pieza
        Almacen            char(3)        NOT NULL,
        -- El almacén ficticio de incompletos donde está mientras tanto
        AlmacenIncompletos char(3)        NOT NULL,
        ProductoDonante    char(15)       NOT NULL,
        NumeroSerie        nvarchar(50)   NULL,
        Pieza              char(15)       NOT NULL,
        Cantidad           int            NOT NULL CONSTRAINT CK_ProductosIncompletos_Cantidad CHECK (Cantidad > 0),
        Pedido             int            NULL,
        Comentario         nvarchar(500)  NULL,
        -- Algete: el hueco de donde salió la unidad y dónde se ha dejado («002/002/004»)
        HuecoOrigen        varchar(11)    NULL,
        HuecoDestino       varchar(11)    NULL,
        Estado             varchar(12)    NOT NULL CONSTRAINT DF_ProductosIncompletos_Estado DEFAULT ('Incompleto')
                                          CONSTRAINT CK_ProductosIncompletos_Estado CHECK (Estado IN ('Incompleto', 'Completado', 'Baja')),
        Usuario            varchar(50)    NOT NULL,
        Fecha              datetime       NOT NULL CONSTRAINT DF_ProductosIncompletos_Fecha DEFAULT (GETDATE()),
        -- NºTraspaso de los apuntes de ExtractoProducto al sacar y al cerrar
        Traspaso           int            NULL,
        FechaCierre        datetime       NULL,
        UsuarioCierre      varchar(50)    NULL,
        MotivoCierre       nvarchar(500)  NULL,
        TraspasoCierre     int            NULL,
        -- Idempotencia de la PDA: con la misma clave se devuelve la misma ficha
        Clave              uniqueidentifier NULL
    );

    CREATE INDEX IX_ProductosIncompletos_Donante ON dbo.ProductosIncompletos (Empresa, ProductoDonante, Estado);
    CREATE INDEX IX_ProductosIncompletos_Estado ON dbo.ProductosIncompletos (Empresa, Estado, Almacen);
    CREATE UNIQUE INDEX UX_ProductosIncompletos_Clave ON dbo.ProductosIncompletos (Clave) WHERE Clave IS NOT NULL;
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ProductosIncompletos TO [NUEVAVISION\RDS2016$];
GO

-- 2. Los diarios de sistema
IF NOT EXISTS (SELECT 1 FROM DiariosProducto WHERE Empresa = '1' AND [Número] = '_SacaPieza')
    INSERT INTO DiariosProducto (Empresa, [Número], [Descripción], Sistema, RecalculaPrecioMedio, Usuario, [Fecha Modificación])
    VALUES ('1', '_SacaPieza', 'Piezas sacadas de productos incompletos (API)', 1, 0, 'Issue902', GETDATE());
GO

IF NOT EXISTS (SELECT 1 FROM DiariosProducto WHERE Empresa = '1' AND [Número] = '_BajaIncom')
    INSERT INTO DiariosProducto (Empresa, [Número], [Descripción], Sistema, RecalculaPrecioMedio, Usuario, [Fecha Modificación])
    VALUES ('1', '_BajaIncom', 'Baja de productos incompletos (API)', 1, 0, 'Issue902', GETDATE());
GO

-- 3. El almacén ficticio de incompletos «INC» (Carlos 26/11/26: «Incompletos», ficticio: sus unidades no cuentan como stock vendible).
--    Almacenes NO está en el EDMX y no se conocen todas sus columnas obligatorias, así que la fila se COPIA de otro almacén ficticio
--    activo (PRV si existe; si no, el primero ficticio activo sin control de ubicaciones) y se fijan las columnas que usa el código:
--    Número 'INC', Descripción 'Incompletos', AlmacénFicticio = 1, ControlUbicaciones = 0, PermitirNegativo = 0 y Estado = 0;
--    Usuario y [Fecha Modificación] si existen. Las DEMÁS columnas (DiarioEntradaRep, DiarioSalidaRep, FilaAMano, Delegación…)
--    quedan COMO EN LA PLANTILLA: revíselas con el SELECT de abajo (INC no se repone ni se vende: no debería llevar diarios de
--    reposición). Sin plantilla, no hace nada y lo dice. Idempotente: si INC ya existe, no lo toca.
--    ANTES de lanzarlo: SELECT * FROM Almacenes WHERE Empresa = '1' AND [Número] IN ('INC', 'PRV');
IF NOT EXISTS (SELECT 1 FROM Almacenes WHERE Empresa = '1' AND [Número] = 'INC')
BEGIN
    DECLARE @plantilla char(3) =
        ISNULL((SELECT [Número] FROM Almacenes WHERE Empresa = '1' AND [Número] = 'PRV' AND ISNULL(AlmacénFicticio, 0) = 1),
               (SELECT TOP 1 [Número] FROM Almacenes WHERE Empresa = '1' AND ISNULL(AlmacénFicticio, 0) = 1 AND ISNULL(Estado, 0) >= 0
                    AND ISNULL(ControlUbicaciones, 0) = 0 AND [Número] NOT IN ('ALG', 'REI', 'ALC') ORDER BY [Número]));
    IF @plantilla IS NULL
        PRINT 'No hay ningún almacén ficticio que copiar: dé de alta INC a mano (AlmacénFicticio 1, ControlUbicaciones 0, PermitirNegativo 0, Estado 0).';
    ELSE
    BEGIN
        DECLARE @columnas nvarchar(max) = N'', @valores nvarchar(max) = N'';
        SELECT @columnas = @columnas + N', ' + QUOTENAME(c.name),
               @valores = @valores + N', ' + CASE c.name
                    WHEN N'Número' THEN N'''INC'''
                    WHEN N'Descripción' THEN N'''Incompletos'''
                    WHEN N'AlmacénFicticio' THEN N'1'
                    WHEN N'ControlUbicaciones' THEN N'0'
                    WHEN N'PermitirNegativo' THEN N'0'
                    WHEN N'Estado' THEN N'0'
                    WHEN N'Usuario' THEN N'''Issue902'''
                    WHEN N'Fecha Modificación' THEN N'GETDATE()'
                    ELSE QUOTENAME(c.name) END
        FROM sys.columns c
        WHERE c.object_id = OBJECT_ID(N'dbo.Almacenes') AND c.is_identity = 0 AND c.is_computed = 0
          AND TYPE_NAME(c.user_type_id) NOT IN (N'timestamp', N'rowversion')
        ORDER BY c.column_id;
        DECLARE @sql nvarchar(max) = N'INSERT INTO dbo.Almacenes (' + STUFF(@columnas, 1, 2, N'') + N') SELECT ' + STUFF(@valores, 1, 2, N'')
            + N' FROM dbo.Almacenes WHERE Empresa = ''1'' AND [Número] = @plantilla;';
        PRINT 'Alta de INC copiando ' + @plantilla + ': ' + @sql;
        EXEC sp_executesql @sql, N'@plantilla char(3)', @plantilla = @plantilla;
    END
END
GO

-- Para revisar INC contra su plantilla (todas las columnas)
SELECT * FROM Almacenes WHERE Empresa = '1' AND [Número] IN ('INC', 'PRV');
GO

-- 4. El parámetro con el almacén de incompletos (vacío si no existe; se rellena con el UPDATE cuando exista el almacén)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AlmacenIncompletos' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AlmacenIncompletos', '(defecto)', '', 'Issue902', GETDATE());
GO

-- Con INC dado de alta, el parámetro apunta a él (solo si sigue vacío: si ya tiene otro valor, no se pisa)
IF EXISTS (SELECT 1 FROM Almacenes WHERE Empresa = '1' AND [Número] = 'INC')
    UPDATE ParametrosUsuario SET Valor = 'INC', Usuario2 = 'Issue902', [Fecha Modificación] = GETDATE()
    WHERE Empresa = '1' AND Clave = 'AlmacenIncompletos' AND Usuario = '(defecto)' AND LTRIM(RTRIM(ISNULL(Valor, ''))) = '';
GO

-- Comprobaciones (solo lectura)
SELECT name, create_date FROM sys.tables WHERE name = 'ProductosIncompletos';

SELECT RTRIM([Número]) AS Diario, RTRIM([Descripción]) AS Descripcion, Sistema, RecalculaPrecioMedio FROM DiariosProducto
WHERE Empresa = '1' AND [Número] IN ('_SacaPieza', '_BajaIncom', '_Ficticio');

SELECT RTRIM(p.Valor) AS AlmacenIncompletos, RTRIM(a.[Descripción]) AS Descripcion, a.AlmacénFicticio, a.ControlUbicaciones, a.Estado
FROM ParametrosUsuario p
LEFT JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = LTRIM(RTRIM(p.Valor))
WHERE p.Empresa = '1' AND p.Clave = 'AlmacenIncompletos' AND p.Usuario = '(defecto)';
GO
