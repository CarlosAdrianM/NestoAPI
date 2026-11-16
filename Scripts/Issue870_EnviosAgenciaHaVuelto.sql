/*
    NestoAPI#870 (novedad 1280, 16/11/26): de un reenvío solo sale lo que ha vuelto al almacén.

    EnviosAgenciaHaVuelto: la caja de un envío devuelto o incidentado HA VUELTO a Algete. Una fila por envío (Envio único):
      - Como = 'Lectura'    -> el mozo leyó su etiqueta en Ariadna («Ha vuelto un paquete», POST api/EnviosAgencias/HaVuelto).
      - Como = 'YaEstaAqui' -> botón «Ya está aquí» de Agencias › Devueltos en Nesto (POST api/EnviosAgencias/{id}/HaVuelto).
      - Como = 'AlImprimir' -> se forzó al imprimir o tramitar su reenvío («Ya está aquí, imprimir», ?yaEstaAqui=true).
      Codigo = lo leído en Ariadna (si se leyó). Cada marca deja además una línea en EnviosHistoria (Campo 'HaVuelto').
    Sin la fila = la caja no ha vuelto: el reenvío de ese envío (EnviosAgenciaReenvios.EnvioNuevo, de NestoAPI#699) no puede
    imprimir su etiqueta, pasar a en curso ni tramitarse (409 ESPERANDO_CAJA; «Tramitar todos» lo salta y lo dice).

    Fuera del EDMX: la API la lee y escribe por SQL y comprueba que existe (OBJECT_ID). SIN EL SCRIPT, marcar «ha vuelto» contesta
    503 «falta lanzar el script» y la guarda de los reenvíos NO actúa (todo como antes de #870). Se puede lanzar antes o después del
    deploy; mejor ANTES, para que la guarda funcione desde el primer momento (tras lanzarlo con la API arrancada, basta reciclar el
    app pool o esperar al siguiente deploy si ya se había preguntado).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    Sin FK a EnviosAgencia a propósito (incidencia 1191: no bloquear el borrado de un envío por una tabla fuera del EDMX).
    GRANT: SELECT e INSERT a la cuenta con la que corre la API ([NUEVAVISION\RDS2016$], ver feedback_scripts_sql_grants_por_bd).
    Sin UPDATE ni DELETE: las filas son la constancia.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EnviosAgenciaHaVuelto', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnviosAgenciaHaVuelto (
        Envio int NOT NULL,                                 -- EnviosAgencia.Numero del devuelto / incidentado
        Fecha datetime NOT NULL CONSTRAINT DF_EnviosAgenciaHaVuelto_Fecha DEFAULT (GETDATE()),
        Usuario varchar(50) NOT NULL,
        Como varchar(20) NOT NULL,                          -- 'Lectura' | 'YaEstaAqui' | 'AlImprimir'
        Codigo varchar(50) NULL,                            -- lo leído en Ariadna
        CONSTRAINT PK_EnviosAgenciaHaVuelto PRIMARY KEY CLUSTERED (Envio),
        CONSTRAINT CK_EnviosAgenciaHaVuelto_Como CHECK (Como IN ('Lectura', 'YaEstaAqui', 'AlImprimir'))
    );
    PRINT 'EnviosAgenciaHaVuelto: creada.';
END
ELSE
    PRINT 'EnviosAgenciaHaVuelto: ya existía.';
GO

GRANT SELECT, INSERT ON dbo.EnviosAgenciaHaVuelto TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EnviosAgenciaHaVuelto'
ORDER BY ORDINAL_POSITION;
GO

/*
    Consultas útiles:

    -- Reenvíos esperando la caja (lo que la API no deja salir):
    SELECT r.EnvioOrigen, o.Agencia, o.Estado AS EstadoOrigen, r.EnvioNuevo, n.Estado AS EstadoNuevo, r.Usuario, r.Fecha
    FROM dbo.EnviosAgenciaReenvios r
    JOIN dbo.EnviosAgencia o ON o.Numero = r.EnvioOrigen
    JOIN dbo.EnviosAgencia n ON n.Numero = r.EnvioNuevo
    LEFT JOIN dbo.EnviosAgenciaHaVuelto v ON v.Envio = r.EnvioOrigen
    WHERE r.Decision = 'Reenviar' AND n.Estado < 1 AND v.Envio IS NULL
    ORDER BY r.Fecha;

    -- Las marcas de los últimos 30 días:
    SELECT v.*, e.Agencia, e.Estado, e.Pedido, e.Cliente
    FROM dbo.EnviosAgenciaHaVuelto v JOIN dbo.EnviosAgencia e ON e.Numero = v.Envio
    WHERE v.Fecha >= DATEADD(DAY, -30, GETDATE())
    ORDER BY v.Fecha DESC;
*/
