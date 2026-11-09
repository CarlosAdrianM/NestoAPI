/*
    NestoAPI#623 corte 4 (09/11/26): importar los leads de Meta (POST api/Clientes/Origenes/ImportarLeads).
    dbo.ClientesOrigen.LeadExternoId = el «id» del lead en Meta, para que reimportar el mismo fichero no cree nada dos veces.
    Índice único filtrado (solo las filas con lead).
    dbo.ClientesOrigen.AceptaComunicaciones = la casilla del formulario «deseo recibir por correo electrónico novedades…»
    (NULL = no se sabe: clientes con origen puesto a mano). Viaja a Odoo en el mensaje de nesto_sync (odoo-custom-addons#43).

    Lanzar como sa en NV. Idempotente. Sin esta columna la API funciona igual: reconoce los leads ya importados por el
    teléfono o el correo (los importados quedan con ese teléfono y ese correo en la ficha).
*/
SET NOCOUNT ON;
USE NV;
GO
-- La columna en su propio lote (el lote entero se compila antes de ejecutarse).
IF COL_LENGTH(N'dbo.ClientesOrigen', N'LeadExternoId') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.ClientesOrigen ADD LeadExternoId nvarchar(50) NULL');
    PRINT 'ClientesOrigen: añadida LeadExternoId.';
END
GO
IF COL_LENGTH(N'dbo.ClientesOrigen', N'AceptaComunicaciones') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.ClientesOrigen ADD AceptaComunicaciones bit NULL');
    PRINT 'ClientesOrigen: añadida AceptaComunicaciones.';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_ClientesOrigen_LeadExternoId' AND object_id = OBJECT_ID(N'dbo.ClientesOrigen'))
    EXEC(N'CREATE UNIQUE NONCLUSTERED INDEX UX_ClientesOrigen_LeadExternoId ON dbo.ClientesOrigen (LeadExternoId) WHERE LeadExternoId IS NOT NULL');
GO
-- Comprobación (sys.columns, no un LIKE)
SELECT c.name AS Columna, t.name AS Tipo, c.max_length, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.ClientesOrigen') AND c.name IN (N'LeadExternoId', N'AceptaComunicaciones');
SELECT name, is_unique, has_filter, filter_definition FROM sys.indexes
WHERE object_id = OBJECT_ID(N'dbo.ClientesOrigen') AND name = N'UX_ClientesOrigen_LeadExternoId';
