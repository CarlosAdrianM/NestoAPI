-- OneShot 18/11/26: republicar hacia Odoo los 3 clientes que ya tienen Dirección 2 (42146, 42147, 42148).
--
-- Para qué: desde la API de hoy (NestoAPI#874, corte 3) el mensaje de cada contacto lleva Direccion2 y TipoLugar, y Odoo
-- (nesto_sync 2.12.0, desplegado hoy a las 13:25) ya los lee. Estos tres clientes se dieron de alta antes y Odoo no los tiene.
--
-- Qué hace: encola los 3 clientes en Nesto_sync, como si se hubieran modificado. El job de sincronización (cada 5 minutos) los
-- publica como mensaje normal. No toca ninguna ficha. Como sa en NV. Si se relanza, no duplica lo que siga pendiente.
USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @clientes TABLE (Cliente char(10) PRIMARY KEY);
INSERT INTO @clientes VALUES ('42146'), ('42147'), ('42148');

BEGIN TRAN;
INSERT INTO Nesto_sync (Tabla, ModificadoId)
SELECT 'Clientes', c.Cliente
FROM @clientes c
WHERE NOT EXISTS (SELECT 1 FROM Nesto_sync s WHERE s.Tabla = 'Clientes' AND s.Sincronizado IS NULL AND LTRIM(RTRIM(s.ModificadoId)) = LTRIM(RTRIM(c.Cliente)));
PRINT N'Clientes encolados para republicar: ' + CAST(@@ROWCOUNT AS nvarchar(10)) + N' (se publican en la siguiente pasada, en 5 minutos como mucho).';
COMMIT;

SELECT s.Id, RTRIM(s.ModificadoId) Cliente, s.Sincronizado, s.Usuario, s.FechaModificacion
FROM Nesto_sync s JOIN @clientes c ON LTRIM(RTRIM(s.ModificadoId)) = LTRIM(RTRIM(c.Cliente))
WHERE s.Tabla = 'Clientes' AND s.FechaModificacion >= CAST(GETDATE() AS date) ORDER BY s.Id;
GO
