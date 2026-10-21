/*
    NestoAPI#683: el modelo 1 de sugerencias aprende el estado del cliente (distribuidores 6/67, 0, 9…).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. Requiere Issue671a_SugerenciasProductosModelo.sql (ya lanzado). Se puede lanzar antes o después del deploy, pero
    ANTES DEL ENTRENAMIENTO DE LAS 23:30 (por el punto 2). Sin este script todo funciona como antes: el job nocturno no
    calcula la popularidad por estado y Calcular sigue con el último zip de la versión 1 de las features.

    1) SugerenciasProductosPopularidadEstado: la rellena el job «sugerencias-productos-pares» (05:00, L-S) en la misma pasada
       que la popularidad, con el estado de HOY de cada cliente (Clientes.Estado del contacto principal, agrupado como
       EstadosCliente.Grupo del núcleo: «6» = 6 y 67, «9» = 9 y 99, «0» = 0/21/31/88, «2» = 2 y 3, «11» = 11 y 22, -1, 7, 8, 93,
       95, «OTRO» y «NADA»; «*» = todos). Por grupo, clientes distintos que han comprado en 12 meses:
         Tipo 'P': cada producto sugerible (Clave = producto)
         Tipo 'F': cada familia/marca (Clave = familia)
         Tipo 'T': cualquier cosa sugerible (Clave = ''): el total del grupo
       Se vacía y se rellena entera en una transacción. Unas decenas de miles de filas. Hasta que el job la rellene (el total
       de '*' existe), Calcular no usa modelos de la versión 2.
    2) GRANT SELECT en SeguimientoCambiosEstadoCliente a la cuenta de la API: el entrenamiento (POST
       api/SugerenciasProductos/ReentrenarModelo, que corre DENTRO de la API) reconstruye con ella el estado que tenía cada
       cliente en cada instantánea (sin fuga). Si no puede leerla, entrena igual pero el modelo NO PASA la puerta.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.SugerenciasProductosPopularidadEstado') IS NULL
BEGIN
    CREATE TABLE dbo.SugerenciasProductosPopularidadEstado (
        Estado varchar(10) NOT NULL,                    -- grupo de estado del cliente, o '*' = todos
        Tipo char(1) NOT NULL,                          -- 'P' producto, 'F' familia, 'T' total del grupo
        Clave varchar(20) NOT NULL,                     -- producto, familia o '' (total)
        Clientes int NOT NULL,                          -- clientes distintos del grupo que lo han comprado en 12 meses
        FechaCalculo datetime NOT NULL,

        CONSTRAINT PK_SugerenciasProductosPopularidadEstado PRIMARY KEY CLUSTERED (Estado, Tipo, Clave),
        CONSTRAINT CK_SugerenciasProductosPopularidadEstado_Tipo CHECK (Tipo IN ('P', 'F', 'T'))
    );
END
GO

GRANT SELECT, INSERT, DELETE ON dbo.SugerenciasProductosPopularidadEstado TO [NUEVAVISION\RDS2016$];
GRANT SELECT ON dbo.SeguimientoCambiosEstadoCliente TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación (tras el job de las 05:00):
-- SELECT Estado, Tipo, COUNT(*) AS Filas, MAX(Clientes) AS MaxClientes, MAX(FechaCalculo) AS Calculo
-- FROM dbo.SugerenciasProductosPopularidadEstado GROUP BY Estado, Tipo ORDER BY Estado, Tipo;
-- SELECT * FROM dbo.SugerenciasProductosPopularidadEstado WHERE Tipo = 'T' ORDER BY Clientes DESC;
