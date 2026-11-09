/*
    NestoAPI#623 corte 5c (09/11/26): leads de Meta por la API Graph (job meta-leads cada 15 min y
    POST api/Clientes/Origenes/ImportarLeadsMeta).

    dbo.OrigenesCliente.MetaFormId           = el id del formulario de clientes potenciales de Meta de ese origen
                                               (GET api/Clientes/Origenes/MetaFormularios los lista). NULL = sin formulario.
    dbo.OrigenesCliente.MetaLeadsLeidosHasta = UTC: hasta dónde leyó la última pasada buena (la escribe la API; NULL = nunca:
                                               la primera pasada lee desde el inicio de la campaña, como mucho 30 días atrás).

    Lanzar como sa en NV. Idempotente. Se puede lanzar ANTES o DESPUÉS del deploy de la API: sin las columnas la API no lee
    ningún formulario (y el endpoint dice «ningún origen tiene MetaFormId»); con ellas y la API vieja no pasa nada.
    No hacen falta GRANTs nuevos: RDS2016$ ya tiene SELECT/UPDATE en dbo.OrigenesCliente (Issue623_OrigenesCliente.sql).
*/
SET NOCOUNT ON;
USE NV;
GO
-- Cada columna en su propio lote (el lote entero se compila antes de ejecutarse).
IF COL_LENGTH(N'dbo.OrigenesCliente', N'MetaFormId') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.OrigenesCliente ADD MetaFormId varchar(30) NULL');
    PRINT 'OrigenesCliente: añadida MetaFormId.';
END
GO
IF COL_LENGTH(N'dbo.OrigenesCliente', N'MetaLeadsLeidosHasta') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.OrigenesCliente ADD MetaLeadsLeidosHasta datetime NULL');
    PRINT 'OrigenesCliente: añadida MetaLeadsLeidosHasta.';
END
GO
-- Formulario «HARMONIA | Solicitud Demo | OCT26» de la página Nueva Visión (351494490222), comprobado el 09/11/26.
EXEC(N'UPDATE dbo.OrigenesCliente SET MetaFormId = ''1301604492043919''
      WHERE Codigo = ''META-HARMONIA-0926'' AND ISNULL(MetaFormId, '''') <> ''1301604492043919''');
GO
-- Plantilla para otra campaña (el id sale de GET api/Clientes/Origenes/MetaFormularios). Si no se quiere que la primera
-- pasada lea hacia atrás, poner también MetaLeadsLeidosHasta (UTC) a partir de cuándo leer:
-- UPDATE dbo.OrigenesCliente SET MetaFormId = '<id del formulario>', MetaLeadsLeidosHasta = '20261109 00:00'
-- WHERE Codigo = '<CODIGO-DEL-ORIGEN>';
GO
-- Comprobación (sys.columns, no un LIKE)
SELECT c.name AS Columna, t.name AS Tipo, c.max_length, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID(N'dbo.OrigenesCliente') AND c.name IN (N'MetaFormId', N'MetaLeadsLeidosHasta');
EXEC(N'SELECT Codigo, Activo, EsLead, MetaFormId, MetaLeadsLeidosHasta FROM dbo.OrigenesCliente WHERE MetaFormId IS NOT NULL');
GO
