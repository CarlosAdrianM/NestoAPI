/*
    NestoAPI#594 (corte 2): UbicacionesMovimientos, una fila por cada cambio que hace la puerta única de Ubicaciones
    (IPuertaUbicaciones → Registrar). Es el testigo que nos faltó el 14/10/26: tres productos (23101, 37182, 38282) tenían
    en Ubicaciones lo que se había devuelto al proveedor el 13/10 (prdCrearAlbaránCmp dejaba fuera de #ubi las líneas de
    grupos sin ProductoBonificacion) y no había forma de ver qué movimiento lo había dejado así.

    Quién escribe: SOLO NestoAPI (RegistroMovimientosUbicacionSql), con SQL ad hoc dentro de la MISMA transacción que la
    operación (si la operación se deshace, el movimiento también). Hoy pasan por la puerta las operaciones de la reposición
    desde Algete (reservar, liberar, salida y anular la salida); el resto se irá añadiendo en el corte 3. Lo que hacen los
    procedimientos (albaranes, compras, inventario…) y Nesto viejo NO queda aquí.

    Interruptor: Ubicaciones:RegistrarMovimientos en el Web.config (nace a false). Sin la tabla, la API no falla: el INSERT
    va detrás de OBJECT_ID y no apunta nada. Orden: primero este script, luego encender el interruptor en el repo y publicar.

    Columnas = las de MovimientoUbicacion (Infraestructure/Ubicaciones/PuertaUbicaciones.cs):
      Tipo           TipoMovimientoUbicacion (ReservarParaReposicion, SalidaDeReposicion…)
      NumOrdenUbicacion  Ubicaciones.NºOrden de la fila tocada (la nueva si se inserta). Sin FK: la fila puede borrarse luego.
      Pasillo/Fila/Columna de origen y de destino (NULL = sin hueco, «pendiente de ubicar»)
      Cantidad       unidades movidas, siempre en positivo
      EstadoAnterior / EstadoNuevo  Ubicaciones.Estado antes y después
      TipoDocumento / NumDocumento  Picking, PedidoVenta, Albaran, PedidoCompra, AlbaranCompra, Traspaso, LineaReposicion,
                     Inventario, Kit (NULL si no hay)
      Usuario        el usuario de auditoría (mismo ancho que Ubicaciones.Usuario)
      Momento        cuándo (hora del servidor de la API)

    Índices: por producto y almacén en el tiempo (la pregunta del 14/10: «¿qué le ha pasado a este producto?») y por
    documento («¿qué tocó este traspaso?»).

    GRANT: SELECT e INSERT a la cuenta máquina con la que corre la API. Es un registro: la API nunca lo cambia ni lo borra.

    Ejecutar en SSMS contra NV como sa (crea una tabla: el login nuevavision no tiene ALTER). Idempotente.
*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
USE NV;
GO

IF OBJECT_ID('dbo.UbicacionesMovimientos') IS NULL
BEGIN
    CREATE TABLE dbo.UbicacionesMovimientos (
        Id bigint IDENTITY(1, 1) NOT NULL,
        Tipo varchar(30) NOT NULL,                      -- TipoMovimientoUbicacion
        Empresa char(3) NOT NULL,
        Almacen char(3) NOT NULL,
        Producto char(15) NOT NULL,
        NumOrdenUbicacion int NULL,                     -- Ubicaciones.NºOrden
        PasilloOrigen char(3) NULL,
        FilaOrigen char(3) NULL,
        ColumnaOrigen char(3) NULL,
        PasilloDestino char(3) NULL,
        FilaDestino char(3) NULL,
        ColumnaDestino char(3) NULL,
        Cantidad int NOT NULL,                          -- siempre en positivo
        EstadoAnterior int NULL,
        EstadoNuevo int NULL,
        TipoDocumento varchar(20) NULL,                 -- TipoDocumentoUbicacion
        NumDocumento int NULL,
        Usuario varchar(30) NOT NULL,                   -- mismo ancho que Ubicaciones.Usuario
        Momento datetime NOT NULL CONSTRAINT DF_UbicacionesMovimientos_Momento DEFAULT (GETDATE()),
        CONSTRAINT PK_UbicacionesMovimientos PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_UbicacionesMovimientos_Cantidad CHECK (Cantidad >= 0)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.UbicacionesMovimientos') AND name = 'IX_UbicacionesMovimientos_Producto')
    CREATE INDEX IX_UbicacionesMovimientos_Producto ON dbo.UbicacionesMovimientos (Producto, Almacen, Momento)
        INCLUDE (Tipo, Cantidad, EstadoAnterior, EstadoNuevo);
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.UbicacionesMovimientos') AND name = 'IX_UbicacionesMovimientos_Documento')
    CREATE INDEX IX_UbicacionesMovimientos_Documento ON dbo.UbicacionesMovimientos (TipoDocumento, NumDocumento);
GO

GRANT SELECT, INSERT ON dbo.UbicacionesMovimientos TO [NUEVAVISION\RDS2016$];
GO
