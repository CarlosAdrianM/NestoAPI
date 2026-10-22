/*
    NestoAPI#532 (bajas por tipo): un cliente puede darse de baja de UN tipo de correo automático sin dejar de recibir
    los demás. Hasta ahora una fila de CorreosPostCompraBajas le quitaba de todo.

    Qué hace (idempotente: se puede lanzar dos veces):
      1. Columna dbo.CorreosPostCompraBajas.Tipo varchar(40) NULL.
           NULL                     = baja de TODOS los correos (así se quedan las bajas que ya hay).
           'RecordatorioReposicion' = solo el recordatorio de reposición de los jueves.
           'VideosCompras'          = solo el correo posventa de los sábados con los vídeos de lo comprado.
         Los códigos los valida la API (TiposCorreoCliente); no hay CHECK para no tener que tocar la BD con cada tipo nuevo.
      2. Índice por (Empresa, Cliente) para las preferencias de la app.
      3. Permisos: la API (cuenta máquina [NUEVAVISION\RDS2016$]) ahora también da de alta y de baja desde la app
         (GET/PUT api/Clientes/PreferenciasCorreo): INSERT y DELETE además del SELECT que ya tenía.

    ORDEN: 1) Issue532_RecordatorioReposicionActivo.sql (crea la tabla; si ya está lanzado, nada); 2) ESTE script;
    3) publicar el API. Si se publica el API sin este script: el recordatorio en Activo no escribe a nadie (no puede leer
    las bajas y lo dice en el resumen), el correo de los sábados sale como siempre (sin mirar bajas, lo loguea a ELMAH)
    y las preferencias de la app dan error.

    Es DDL: desde SSMS con `sa`.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.CorreosPostCompraBajas') IS NULL
BEGIN
    RAISERROR(N'Falta dbo.CorreosPostCompraBajas: lanzar antes Scripts/Issue532_RecordatorioReposicionActivo.sql', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH('dbo.CorreosPostCompraBajas', 'Tipo') IS NULL
    ALTER TABLE dbo.CorreosPostCompraBajas ADD Tipo varchar(40) NULL;   -- NULL = todos los correos
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_CorreosPostCompraBajas_Cliente' AND object_id = OBJECT_ID('dbo.CorreosPostCompraBajas'))
    CREATE INDEX IX_CorreosPostCompraBajas_Cliente ON dbo.CorreosPostCompraBajas (Empresa, Cliente) INCLUDE (Email, Tipo);
GO

GRANT SELECT, INSERT, DELETE ON dbo.CorreosPostCompraBajas TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación: la columna y cómo quedan las bajas (las de antes, con Tipo NULL = de todo)
SELECT COL_LENGTH('dbo.CorreosPostCompraBajas', 'Tipo') AS LongitudTipo;
SELECT ISNULL(Tipo, '(todos)') AS Tipo, COUNT(*) AS Bajas FROM dbo.CorreosPostCompraBajas GROUP BY Tipo;
GO

SET NOEXEC OFF;
GO

-- DAR DE BAJA A MANO a quien conteste pidiéndolo, SOLO del tipo de correo al que contesta:
-- INSERT INTO dbo.CorreosPostCompraBajas (Empresa, Cliente, Email, Tipo, Motivo, Usuario)
-- VALUES ('1', '15191', NULL, 'RecordatorioReposicion', N'Contestó al recordatorio de reposición pidiendo la baja', 'Carlos');
-- Si pide que no le escribamos nada de nada, Tipo = NULL.
