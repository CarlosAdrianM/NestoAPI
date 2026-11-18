/*
    Incidencia 1328 de Novedades (18/11/26): qué picking(s) lleva cada envío.

    EnviosAgencia solo guarda el número de pedido. El pedido 930358 se sirvió en dos veces: el envío 250999 (ENTREGADO el 17/11)
    llevaba las 2 líneas del picking 100095; las 6 que quedaban (picking 100114) seguían en el almacén, y al aplazarlas el PUT
    contestó PEDIDO_YA_EN_AGENCIA porque contaba CUALQUIER envío tramitado del pedido.

    EnviosAgenciaPickings: una fila por envío y picking que lleva.
      - Se apunta al crear el envío (POST api/EnviosAgencias en curso, DesdePedido, packing de Ariadna) y al registrar una etiqueta
        pendiente (PUT de un pendiente o en curso): los pickings de las líneas del pedido en curso (1) o en albarán (2). Unión: nunca
        se quita uno.
      - El reenvío (#699) copia las del envío original. El retorno (recogida en casa del cliente) lleva Picking = 0: no lleva nada.
      - Sin filas = sin dato (envíos de antes de este script): la API contesta como antes, salvo que un envío ENTREGADO o cerrado a mano
        ya no cuenta como «la agencia ya tiene» unas líneas que siguen en el almacén.

    Fuera del EDMX: la API la lee y escribe por SQL y comprueba que existe (OBJECT_ID). SIN EL SCRIPT, todo como antes (ELMAH una vez).
    Se puede lanzar antes o después del deploy (con la API arrancada la ve en la siguiente llamada).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    Sin FK a EnviosAgencia a propósito (incidencia 1191: no bloquear el borrado de un envío por una tabla fuera del EDMX).
    GRANT: SELECT e INSERT a la cuenta con la que corre la API ([NUEVAVISION\RDS2016$], ver feedback_scripts_sql_grants_por_bd).
    Sin UPDATE ni DELETE: las filas son la constancia.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EnviosAgenciaPickings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnviosAgenciaPickings (
        Envio int NOT NULL,                                 -- EnviosAgencia.Numero
        Picking int NOT NULL,                               -- LinPedidoVta.Picking (0 = no lleva nada del almacén)
        FechaAlta datetime NOT NULL CONSTRAINT DF_EnviosAgenciaPickings_FechaAlta DEFAULT (GETDATE()),
        Usuario varchar(50) NOT NULL,
        CONSTRAINT PK_EnviosAgenciaPickings PRIMARY KEY CLUSTERED (Envio, Picking)
    );
    PRINT 'EnviosAgenciaPickings: creada.';
END
ELSE
    PRINT 'EnviosAgenciaPickings: ya existía.';
GO

-- Para «qué envío lleva este picking» (consultas de diagnóstico).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EnviosAgenciaPickings_Picking' AND object_id = OBJECT_ID('dbo.EnviosAgenciaPickings'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_EnviosAgenciaPickings_Picking ON dbo.EnviosAgenciaPickings (Picking) INCLUDE (Envio);
    PRINT 'IX_EnviosAgenciaPickings_Picking: creado.';
END
GO

GRANT SELECT, INSERT ON dbo.EnviosAgenciaPickings TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EnviosAgenciaPickings'
ORDER BY ORDINAL_POSITION;
GO

/*
    Consultas útiles:

    -- Los envíos de un pedido y lo que lleva cada uno (NULL = sin dato):
    SELECT e.Numero, e.Agencia, e.Estado, e.Fecha, p.Picking, p.FechaAlta, p.Usuario
    FROM dbo.EnviosAgencia e
    LEFT JOIN dbo.EnviosAgenciaPickings p ON p.Envio = e.Numero
    WHERE e.Pedido = 930358
    ORDER BY e.Numero, p.Picking;

    -- Los envíos de los últimos 7 días que se han quedado sin dato (algún camino que no apunta):
    SELECT e.Numero, e.Agencia, e.Estado, e.Pedido, e.Usuario, e.Fecha
    FROM dbo.EnviosAgencia e
    WHERE e.Fecha >= DATEADD(DAY, -7, CAST(GETDATE() AS date)) AND e.Estado >= 0
      AND NOT EXISTS (SELECT 1 FROM dbo.EnviosAgenciaPickings p WHERE p.Envio = e.Numero)
    ORDER BY e.Numero DESC;
*/
