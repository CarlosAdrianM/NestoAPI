/*
    NestoAPI#699 (corte 2, 04/11/26): «Volver a enviar» un envío DEVUELTO o INCIDENTADO.

    EnviosAgenciaReenvios: la decisión sobre un envío que la agencia devuelve (o deja con incidencia). Una fila por envío
    (EnvioOrigen único); sin fila = sin decidir.
      - Decision 'Reenviar': el cliente lo sigue queriendo. La API (POST api/EnviosAgencias/{id}/Reenviar) crea un envío NUEVO
        del mismo pedido (EnvioNuevo), clonado del devuelto, en curso y sin etiqueta. Sin abono, sin stock, sin picking ni
        factura. Direccion…Observaciones = lo que se grabó en el envío nuevo (la dirección corregida, si se corrigió).
        IncluirPortes = 1 si se añadió al pedido una línea de portes (por defecto no: los portes del reenvío nos los comemos).
      - Decision 'Abonar': el cliente no lo quiere (POST api/EnviosAgencias/{id}/Abonar). Solo queda la constancia: el abono lo
        hace el almacén con Copiar factura.
      - Decision 'EntregadoPorLaAgencia' (Carlos 04/11/26): se decidió reenviar, pero la agencia acabó entregando el envío viejo y
        el reenvío aún no tenía etiqueta (Estado < 1). El seguimiento borra el envío nuevo, pasa la fila a esta decisión con
        EnvioNuevo = NULL (por la FK) y EnvioAnulado = el número del reenvío borrado, deja constancia en EnviosHistoria del viejo
        y avisa al vendedor y a Logística. Si el reenvío ya estaba tramitado, la fila sigue en 'Reenviar' y solo se avisa.
      - Es también la estadística: cuántos devueltos, cuántos reenviados y cuántos abonados, por agencia y motivo.

    Fuera del EDMX: la API la lee y escribe por SQL y comprueba que existe (OBJECT_ID). SIN EL SCRIPT, los endpoints Reenviar,
    Abonar y Devueltos contestan 400 «falta lanzar el script» y nada más cambia (el aviso de devuelto al vendedor sale igual).
    Se puede lanzar antes o después del deploy.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE a la cuenta con la que corre la API ([NUEVAVISION\RDS2016$], ver feedback_scripts_sql_grants_por_bd).
    UPDATE solo para pasar un 'Reenviar' a 'EntregadoPorLaAgencia'. Sin DELETE: las filas son la constancia.
    Si la tabla ya existía con la versión anterior del script (Decision varchar(10), sin EnvioAnulado), el bloque de después la
    pone al día.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EnviosAgenciaReenvios', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnviosAgenciaReenvios (
        Id int IDENTITY(1,1) NOT NULL,
        EnvioOrigen int NOT NULL,                           -- EnviosAgencia.Numero del devuelto / incidentado
        Decision varchar(25) NOT NULL,                      -- 'Reenviar' | 'Abonar' | 'EntregadoPorLaAgencia'
        EnvioNuevo int NULL,                                -- EnviosAgencia.Numero del reenvío (solo con 'Reenviar')
        EnvioAnulado int NULL,                              -- El reenvío borrado (solo con 'EntregadoPorLaAgencia'; sin FK: ya no existe)
        Direccion varchar(100) NULL,
        CodPostal nvarchar(20) NULL,
        Poblacion varchar(100) NULL,
        Provincia varchar(100) NULL,
        Telefono varchar(50) NULL,
        Movil varchar(50) NULL,
        Observaciones varchar(255) NULL,
        IncluirPortes bit NOT NULL CONSTRAINT DF_EnviosAgenciaReenvios_IncluirPortes DEFAULT (0),
        Usuario varchar(50) NOT NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_EnviosAgenciaReenvios_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_EnviosAgenciaReenvios PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_EnviosAgenciaReenvios_EnvioOrigen UNIQUE (EnvioOrigen),
        CONSTRAINT CK_EnviosAgenciaReenvios_Decision CHECK (Decision IN ('Reenviar', 'Abonar', 'EntregadoPorLaAgencia')),
        CONSTRAINT CK_EnviosAgenciaReenvios_EnvioNuevo CHECK ((Decision = 'Reenviar' AND EnvioNuevo IS NOT NULL)
                                                           OR (Decision = 'Abonar' AND EnvioNuevo IS NULL)
                                                           OR (Decision = 'EntregadoPorLaAgencia' AND EnvioNuevo IS NULL AND EnvioAnulado IS NOT NULL)),
        CONSTRAINT FK_EnviosAgenciaReenvios_Origen FOREIGN KEY (EnvioOrigen) REFERENCES dbo.EnviosAgencia (Numero),
        CONSTRAINT FK_EnviosAgenciaReenvios_Nuevo FOREIGN KEY (EnvioNuevo) REFERENCES dbo.EnviosAgencia (Numero)
    );
    CREATE INDEX IX_EnviosAgenciaReenvios_EnvioNuevo ON dbo.EnviosAgenciaReenvios (EnvioNuevo);
    PRINT 'EnviosAgenciaReenvios: creada.';
END
ELSE
    PRINT 'EnviosAgenciaReenvios: ya existía.';
GO

-- Puesta al día de una tabla creada con la versión anterior del script (idempotente).
IF COL_LENGTH('dbo.EnviosAgenciaReenvios', 'EnvioAnulado') IS NULL
BEGIN
    ALTER TABLE dbo.EnviosAgenciaReenvios ADD EnvioAnulado int NULL;
    PRINT 'EnviosAgenciaReenvios: añadida EnvioAnulado.';
END
GO

IF COL_LENGTH('dbo.EnviosAgenciaReenvios', 'Decision') < 25
BEGIN
    ALTER TABLE dbo.EnviosAgenciaReenvios DROP CONSTRAINT CK_EnviosAgenciaReenvios_EnvioNuevo;
    ALTER TABLE dbo.EnviosAgenciaReenvios DROP CONSTRAINT CK_EnviosAgenciaReenvios_Decision;
    ALTER TABLE dbo.EnviosAgenciaReenvios ALTER COLUMN Decision varchar(25) NOT NULL;
    ALTER TABLE dbo.EnviosAgenciaReenvios ADD
        CONSTRAINT CK_EnviosAgenciaReenvios_Decision CHECK (Decision IN ('Reenviar', 'Abonar', 'EntregadoPorLaAgencia')),
        CONSTRAINT CK_EnviosAgenciaReenvios_EnvioNuevo CHECK ((Decision = 'Reenviar' AND EnvioNuevo IS NOT NULL)
                                                           OR (Decision = 'Abonar' AND EnvioNuevo IS NULL)
                                                           OR (Decision = 'EntregadoPorLaAgencia' AND EnvioNuevo IS NULL AND EnvioAnulado IS NOT NULL));
    PRINT 'EnviosAgenciaReenvios: Decision ampliada y CHECK con EntregadoPorLaAgencia.';
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.EnviosAgenciaReenvios TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EnviosAgenciaReenvios'
ORDER BY ORDINAL_POSITION;
GO

/*
    Consultas útiles:

    -- Devueltos e incidentados de los últimos 60 días con su decisión (lo que enseña la pestaña Devueltos de Agencias):
    SELECT e.Numero, e.Agencia, e.Estado, e.DetalleEstado, e.Pedido, e.Cliente, e.Fecha,
           ISNULL(r.Decision, 'SinDecidir') AS Decision, r.EnvioNuevo, r.EnvioAnulado, n.Estado AS EstadoNuevo, r.Usuario, r.Fecha AS FechaDecision
    FROM dbo.EnviosAgencia e
    LEFT JOIN dbo.EnviosAgenciaReenvios r ON r.EnvioOrigen = e.Numero
    LEFT JOIN dbo.EnviosAgencia n ON n.Numero = r.EnvioNuevo
    WHERE (e.Estado IN (3, 4) OR r.Decision = 'EntregadoPorLaAgencia') AND e.Fecha >= DATEADD(DAY, -60, CAST(GETDATE() AS date))
    ORDER BY CASE WHEN r.Id IS NULL THEN 0 ELSE 1 END, e.Fecha DESC;

    -- Estadística por agencia:
    SELECT e.Agencia, COUNT(*) AS Devueltos,
           SUM(CASE WHEN r.Decision = 'Reenviar' THEN 1 ELSE 0 END) AS Reenviados,
           SUM(CASE WHEN r.Decision = 'Abonar' THEN 1 ELSE 0 END) AS Abonados
    FROM dbo.EnviosAgencia e LEFT JOIN dbo.EnviosAgenciaReenvios r ON r.EnvioOrigen = e.Numero
    WHERE e.Estado = 4 GROUP BY e.Agencia;
*/
