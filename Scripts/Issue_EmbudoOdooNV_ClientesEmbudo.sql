/*
    Embudo de Odoo con clientes del vendedor general NV (odoo-custom-addons#45, 10/11/26): REGISTRO PERMANENTE de los clientes
    que han entrado en el embudo de conversión de Odoo. Cada cliente, UNA SOLA VEZ: la fila no se borra nunca.

    - POST api/Clientes/Sync/Republicar mete aquí (Origen 'Nesto', FechaEnviado NULL) los clientes del bloque y los encola en
      Nesto_sync en la misma transacción. El job 'sincronizar-clientes', al publicar un cliente con FechaEnviado NULL, añade a
      cada contacto BloqueEmbudo, ResponsableEmbudoEmail y ResponsableEmbudoTipo, y rellena FechaEnviado cuando han salido
      todos sus contactos (si falla a medias, se reintenta con los campos).
    - POST api/Clientes/Sync/Republicar/Sembrar mete los que ya pasaron por el embudo por otras puertas (lista de Odoo:
      cliente, fecha y puerta en Origen), con Bloque 'SIEMBRA' y FechaEnviado = esa fecha (nunca se publican por esto).

    Fuera del EDMX (como ClientesOrigen): la API la lee y escribe por SQL. Sin la tabla, los dos endpoints contestan 400
    («falta lanzar el script…») y el job publica como siempre (sin campos del embudo).

    Lanzar como sa en NV ANTES O DESPUÉS de publicar la API (la API sin tabla sigue funcionando), pero ANTES de usar el endpoint.
    Idempotente. Para deshacerlo: DROP TABLE dbo.ClientesEmbudo (se pierde el registro de quién ha entrado ya en el embudo:
    no hacerlo una vez usado).
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ClientesEmbudo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesEmbudo (
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Bloque varchar(30) NOT NULL,             -- NV-2026-11-01… o SIEMBRA
        Fecha datetime NOT NULL CONSTRAINT DF_ClientesEmbudo_Fecha DEFAULT (GETDATE()),
        Usuario varchar(50) NOT NULL,
        Origen varchar(30) NOT NULL,             -- 'Nesto' (republicación) o la puerta de Odoo (siembra)
        FechaEnviado datetime NULL,              -- NULL = el bloque aún no ha llegado a Odoo
        CONSTRAINT PK_ClientesEmbudo PRIMARY KEY CLUSTERED (Empresa, Cliente)
    );
    PRINT 'Creada dbo.ClientesEmbudo.';
END
GO
-- Por si la tabla se creó antes sin la columna (en su propio lote y con EXEC: el lote se compila entero antes de ejecutarse)
IF COL_LENGTH(N'dbo.ClientesEmbudo', N'FechaEnviado') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.ClientesEmbudo ADD FechaEnviado datetime NULL');
    PRINT 'ClientesEmbudo: añadida FechaEnviado.';
END
GO
-- Nunca se borra: sin DELETE para la API
GRANT SELECT, INSERT, UPDATE ON dbo.ClientesEmbudo TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación
SELECT Bloque, Origen, COUNT(*) AS Clientes, SUM(CASE WHEN FechaEnviado IS NULL THEN 1 ELSE 0 END) AS PendientesDeEnviar,
    MIN(Fecha) AS Desde, MAX(Fecha) AS Hasta
FROM dbo.ClientesEmbudo
GROUP BY Bloque, Origen
ORDER BY MIN(Fecha);
