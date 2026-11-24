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
      3. El almacén ficticio de incompletos: plantilla COMENTADA (no sé qué columnas exige Almacenes; la rellena Carlos). Tiene que
         ser activo, NO ser ALG/REI/ALC (son lo único que cuentan el stock disponible, el motor de fechas y la tienda) y SIN control
         de ubicaciones. La API lo comprueba y, si no, da 503 con el motivo.
      4. ParametrosUsuario empresa '1', fila «(defecto)», clave «AlmacenIncompletos» = el código del almacén del paso 3. Se crea
         VACÍA si no existe (sin valor, 503 «falta el almacén de incompletos»): se rellena con el UPDATE comentado.

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

-- 3. PLANTILLA (comentada a propósito): el almacén ficticio de incompletos. Antes, mirar cómo está dado de alta otro ficticio
--    (p. ej. PRV) y copiar sus columnas obligatorias; comprobar que el código no existe:
--
-- SELECT * FROM Almacenes WHERE Empresa = '1' AND [Número] IN ('PRV', 'INC');
--
-- INSERT INTO Almacenes (Empresa, [Número], [Descripción], AlmacénFicticio, ControlUbicaciones, PermitirNegativo, Estado /* , … */)
-- VALUES ('1', 'INC', 'Productos incompletos (piezas sacadas)', 1, 0, 0, 0 /* , … */);
-- GO

-- 4. El parámetro con el almacén de incompletos (vacío si no existe; se rellena con el UPDATE cuando exista el almacén)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'AlmacenIncompletos' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'AlmacenIncompletos', '(defecto)', '', 'Issue902', GETDATE());
GO

-- UPDATE ParametrosUsuario SET Valor = 'INC', Usuario2 = 'Issue902', [Fecha Modificación] = GETDATE()
-- WHERE Empresa = '1' AND Clave = 'AlmacenIncompletos' AND Usuario = '(defecto)';
-- GO

-- Comprobaciones (solo lectura)
SELECT name, create_date FROM sys.tables WHERE name = 'ProductosIncompletos';

SELECT RTRIM([Número]) AS Diario, RTRIM([Descripción]) AS Descripcion, Sistema, RecalculaPrecioMedio FROM DiariosProducto
WHERE Empresa = '1' AND [Número] IN ('_SacaPieza', '_BajaIncom', '_Ficticio');

SELECT RTRIM(p.Valor) AS AlmacenIncompletos, RTRIM(a.[Descripción]) AS Descripcion, a.AlmacénFicticio, a.ControlUbicaciones, a.Estado
FROM ParametrosUsuario p
LEFT JOIN Almacenes a ON a.Empresa = p.Empresa AND a.[Número] = LTRIM(RTRIM(p.Valor))
WHERE p.Empresa = '1' AND p.Clave = 'AlmacenIncompletos' AND p.Usuario = '(defecto)';
GO
