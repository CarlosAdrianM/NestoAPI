/*
    NestoAPI#866 (corte 3, 16/11/26): datos para decidir si las ofertas del día y el motivo explicado venden más.

    Añade a dbo.SugerenciasContacto (la de Scripts/Issue603_SugerenciasContacto.sql; la tabla está en el EDMX, pero estas columnas
    NO: la API las escribe y las lee con SQL plano y, sin ellas, todo sigue como hoy):
      - Origen varchar(20) NULL: por qué entró en la lista ese día (Probabilidad, Cobertura, ChequeRegalo, Ofertas). Hasta ahora se
        deducía del principio del Motivo (OrigenesSugerenciaContacto.DeMotivo) y así se sigue leyendo; la columna es para analizar
        sin depender del texto. NULL = registrada antes de este script.
      - ProductosOferta nvarchar(400) NULL: los productos que el cliente compra habitualmente y que ese día estaban en oferta (los
        3 mejores de ReglasImpulsoOfertasContacto), en JSON corto:
          [{"p":"45929","d":20,"c":45,"u":64,"t":1,"n":0,"f":1}]
          p = producto, d = descuento de ese día en % (el equivalente en un N+M), c = su ciclo de recompra en días (mediana),
          u = días desde su última compra, t = 1 si ya le tocaba (u >= c), n = 1 si la oferta había entrado hacía 7 días o menos,
          f = 1 si caducaba en 7 días o menos.
        Se apunta en TODAS las sugerencias del cliente con alguno, no solo en las que subió el impulso (Origen = Ofertas): así hay
        grupo de control. NULL = ninguno en oferta ese día (o registrada antes de este script).
      - PedidoEn7Dias bit NULL: 1 si el cliente/contacto hizo un pedido (empresa 1, cualquier línea con Estado >= -1) entre el día de
        la sugerencia y 7 días después (incluidos); 0 si no.
      - PedidoConOferta bit NULL: solo con ProductosOferta: 1 si ese pedido llevaba alguno de esos productos; 0 si no.
      - ImportePedido7Dias decimal(12, 2) NULL: base imponible de esos pedidos.
      - ResultadoCalculadoEl datetime NULL: cuándo lo cerró el job «sugerencias-contacto-resultado» (todos los días a las 3:25, con
        las sugerencias de hace 8 a 60 días sin cerrar). NULL = todavía sin cerrar.

    Lanzar como sa en NV. Idempotente: se puede relanzar (cada columna en su propio lote y solo si falta). Puede ir ANTES o DESPUÉS
    de publicar la API: la API lo comprueba una vez por proceso y recuerda el «sí» (sin las columnas no escribe ni avisa; tras
    lanzarlo, basta reciclar el app pool o el siguiente despliegue). Los permisos ya los tiene la tabla (SELECT, INSERT, UPDATE a
    [NUEVAVISION\RDS2016$] en Issue603): no hace falta ninguno nuevo.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NULL
    RAISERROR(N'No existe dbo.SugerenciasContacto: lance antes Scripts/Issue603_SugerenciasContacto.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SugerenciasContacto', N'Origen') IS NULL
    ALTER TABLE dbo.SugerenciasContacto ADD Origen varchar(20) NULL;
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SugerenciasContacto', N'ProductosOferta') IS NULL
    ALTER TABLE dbo.SugerenciasContacto ADD ProductosOferta nvarchar(400) NULL;
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SugerenciasContacto', N'PedidoEn7Dias') IS NULL
    ALTER TABLE dbo.SugerenciasContacto ADD PedidoEn7Dias bit NULL;
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SugerenciasContacto', N'PedidoConOferta') IS NULL
    ALTER TABLE dbo.SugerenciasContacto ADD PedidoConOferta bit NULL;
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SugerenciasContacto', N'ImportePedido7Dias') IS NULL
    ALTER TABLE dbo.SugerenciasContacto ADD ImportePedido7Dias decimal(12, 2) NULL;
GO
IF OBJECT_ID(N'dbo.SugerenciasContacto', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.SugerenciasContacto', N'ResultadoCalculadoEl') IS NULL
    ALTER TABLE dbo.SugerenciasContacto ADD ResultadoCalculadoEl datetime NULL;
GO
-- Comprobación
SELECT CASE WHEN COL_LENGTH(N'dbo.SugerenciasContacto', N'Origen') IS NOT NULL
             AND COL_LENGTH(N'dbo.SugerenciasContacto', N'ProductosOferta') IS NOT NULL
             AND COL_LENGTH(N'dbo.SugerenciasContacto', N'PedidoEn7Dias') IS NOT NULL
             AND COL_LENGTH(N'dbo.SugerenciasContacto', N'PedidoConOferta') IS NOT NULL
             AND COL_LENGTH(N'dbo.SugerenciasContacto', N'ImportePedido7Dias') IS NOT NULL
             AND COL_LENGTH(N'dbo.SugerenciasContacto', N'ResultadoCalculadoEl') IS NOT NULL
            THEN N'OK: columnas Origen, ProductosOferta, PedidoEn7Dias, PedidoConOferta, ImportePedido7Dias y ResultadoCalculadoEl en dbo.SugerenciasContacto'
            ELSE N'FALTA: no están las seis columnas (¿existe la tabla?)' END AS Resultado;
GO
