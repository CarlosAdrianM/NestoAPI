/*
    NestoAPI#742 (29/10/26): el reembolso de TODAS las agencias se contabiliza al pasar el envío a ENTREGADO, no al tramitar.

    EnviosAgenciaReembolsosAlEntregar: una fila por envío cuyo reembolso se ha DIFERIDO a la entrega.
      - La crea la API al tramitar (dentro de la transacción de tramitar), solo con el interruptor Agencias:ReembolsoAlEntregar
        encendido, reembolso > 0 y agencia con seguimiento (GLS 1, Innovatrans 12, CTT 13). La 14 (reparto propio) no: tiene
        lo suyo (al entregar, contra la caja del repartidor, en EnviosAgenciaPruebasEntrega).
      - Asiento NULL = pendiente de contabilizar. El seguimiento de envíos (cada 30 min en horario de reparto) contabiliza los
        pendientes que ya están ENTREGADOS contra la CuentaReembolsos de la agencia y apunta aquí el asiento. La reserva de la
        fila (Asiento = 0) y el asiento van en la misma transacción: nunca dos veces.
      - Un envío SIN fila es un envío tramitado antes del cambio (o con el interruptor apagado): su reembolso ya se contabilizó
        al tramitar y la API no lo vuelve a contabilizar nunca.
      - Incidentados, devueltos y cerrados a mano que no llegan a entregarse se quedan pendientes para siempre (decisión de
        Carlos: el cliente no ha pagado y la factura sigue pendiente). No salen en la pestaña Reembolsos.

    Fuera del EDMX: la API la lee y escribe por SQL y comprueba que existe (OBJECT_ID). SIN EL SCRIPT, la API contabiliza al
    tramitar como siempre aunque el interruptor esté encendido. Se puede lanzar antes o después del deploy.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE a la cuenta con la que corre la API ([NUEVAVISION\RDS2016$], ver
    feedback_scripts_sql_grants_por_bd: [ABORRAR\sqlServerSvc] fue un error y da 15151). Sin DELETE: las filas son la constancia.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EnviosAgenciaReembolsosAlEntregar', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EnviosAgenciaReembolsosAlEntregar (
        EnvioId int NOT NULL,                               -- EnviosAgencia.Numero
        UsuarioTramitacion varchar(50) NOT NULL,            -- quien tramitó (y difirió) el reembolso
        FechaTramitacion datetime NOT NULL,
        Asiento int NULL,                                   -- NULL = pendiente; 0 = contabilizándose (transacción en curso)
        Importe money NULL,                                 -- lo contabilizado (EnviosAgencia.Reembolso al entregar)
        FechaContable datetime NULL,                        -- PreContabilidad.Fecha del apunte (para desliquidarlo si se modifica)
        UsuarioContabilizacion varchar(50) NULL,
        FechaContabilizacion datetime NULL,
        CONSTRAINT PK_EnviosAgenciaReembolsosAlEntregar PRIMARY KEY CLUSTERED (EnvioId),
        CONSTRAINT FK_EnviosAgenciaReembolsosAlEntregar_EnviosAgencia FOREIGN KEY (EnvioId) REFERENCES dbo.EnviosAgencia (Numero)
    );
    CREATE INDEX IX_EnviosAgenciaReembolsosAlEntregar_Asiento ON dbo.EnviosAgenciaReembolsosAlEntregar (Asiento);
    PRINT 'EnviosAgenciaReembolsosAlEntregar: creada.';
END
ELSE
    PRINT 'EnviosAgenciaReembolsosAlEntregar: ya existía.';
GO

GRANT SELECT, INSERT, UPDATE ON dbo.EnviosAgenciaReembolsosAlEntregar TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EnviosAgenciaReembolsosAlEntregar'
ORDER BY ORDINAL_POSITION;
GO

/*
    Consultas útiles:

    -- Pendientes (tramitados con el reembolso diferido que aún no se han entregado, o que no se entregarán):
    SELECT r.EnvioId, e.Agencia, e.Estado, e.Reembolso, e.Pedido, r.FechaTramitacion
    FROM dbo.EnviosAgenciaReembolsosAlEntregar r INNER JOIN dbo.EnviosAgencia e ON e.Numero = r.EnvioId
    WHERE r.Asiento IS NULL ORDER BY r.FechaTramitacion;

    -- Entregados que el seguimiento todavía no ha contabilizado (debería estar vacío salvo en los minutos entre pasadas;
    -- si no, mirar ELMAH «No se pudo contabilizar el reembolso del envío…»):
    SELECT r.EnvioId, e.Agencia, e.Reembolso, e.FechaEntrega
    FROM dbo.EnviosAgenciaReembolsosAlEntregar r INNER JOIN dbo.EnviosAgencia e ON e.Numero = r.EnvioId
    WHERE r.Asiento IS NULL AND e.Estado = 2 AND e.Reembolso > 0 AND e.FechaPagoReembolso IS NULL;
*/
