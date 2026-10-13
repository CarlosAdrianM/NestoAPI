/*
    Sugerencia 575 (Alfredo, almacén; aprobada por Carlos el 13/10/26): dejar rastro de QUIÉN y CUÁNDO cambia la ruta de
    un pedido.

    Ejecutar en SSMS contra NV como sa (crea un trigger: el login nuevavision no tiene ALTER). Idempotente: se puede lanzar
    dos veces. Da igual lanzarlo antes o después de publicar la API: mientras no exista, el cambio que se hace desde el aviso
    del picking deja la fila él mismo; en cuanto existe, la deja el trigger (sin duplicar).

    Por qué un trigger: hoy la ruta de un pedido ya hecho se cambia desde el Nesto viejo (VB6), que escribe directamente en
    CabPedidoVta sin pasar por la API, así que la tabla Modificaciones (solo los PUT de la API) no se entera. Ejemplo:
    pedido 928241 (empresa 3, cliente 5825), FW -> 16 el 12/10/26 19:19 por NUEVAVISION\Alfredo: ninguna fila en
    Modificaciones.

    Qué hace trgCabPedidoVtaRuta: por cada pedido al que le cambia la ruta (de verdad: i.Ruta <> d.Ruta), una fila en
    LogModificaciones, la misma tabla en la que trgCabPedidoVtaUpd ya deja ServirJunto, MantenerJunto, CCC y PlazosPago:
      Tabla 'CabPedidoVta', Procedimiento 'Actualizar', Numero = pedido, Columna 'Ruta',
      ValorAntiguo = 'FW -> 16' (antes y después, para leerlo sin cruzar con nada), Fecha = ahora,
      Usuario: 1) el que manda la API por SESSION_CONTEXT('UsuarioNesto') (el del JWT, en el aviso del picking);
               2) si escribe la cuenta de máquina de la API (termina en $, p. ej. un PUT del pedido): CabPedidoVta.Usuario,
                  que es lo que la API graba como autor de la modificación;
               3) si no, SUSER_SNAME(): el usuario de Windows del Nesto viejo o de SSMS.
      Formulario: SESSION_CONTEXT('FormularioNesto') ('Picking: sugerencia de ruta propia (575)') o, si no, APP_NAME()
                  ('EntityFramework' = la API; el Nesto viejo y SSMS dicen el suyo).

    Para consultarlo:
      SELECT Fecha, Numero, ValorAntiguo, Usuario, Formulario
        FROM LogModificaciones WITH (NOLOCK) WHERE Tabla = 'CabPedidoVta' AND Columna = 'Ruta' ORDER BY Fecha DESC;
*/

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE NV;
GO

IF OBJECT_ID('dbo.trgCabPedidoVtaRuta', 'TR') IS NOT NULL
    DROP TRIGGER dbo.trgCabPedidoVtaRuta;
GO

CREATE TRIGGER dbo.trgCabPedidoVtaRuta ON dbo.CabPedidoVta
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT UPDATE(Ruta)
        RETURN;

    DECLARE @usuarioApi nvarchar(30) = CAST(SESSION_CONTEXT(N'UsuarioNesto') AS nvarchar(30));
    DECLARE @formularioApi nvarchar(50) = CAST(SESSION_CONTEXT(N'FormularioNesto') AS nvarchar(50));
    DECLARE @login nvarchar(128) = SUSER_SNAME();

    INSERT INTO dbo.LogModificaciones (Empresa, Formulario, Procedimiento, Tabla, Numero, Columna, Fecha, ValorAntiguo, Usuario)
    SELECT i.Empresa,
           LEFT(COALESCE(@formularioApi, APP_NAME(), ''), 50),
           'Actualizar',
           'CabPedidoVta',
           CAST(i.Número AS varchar(50)),
           'Ruta',
           GETDATE(),
           LEFT(ISNULL(RTRIM(d.Ruta), '(ninguna)') + ' -> ' + ISNULL(RTRIM(i.Ruta), '(ninguna)'), 50),
           LEFT(COALESCE(@usuarioApi,
                         CASE WHEN @login LIKE '%$' AND NULLIF(RTRIM(i.Usuario), '') IS NOT NULL THEN RTRIM(i.Usuario) END,
                         @login), 30)
    FROM inserted i
        INNER JOIN deleted d ON d.Empresa = i.Empresa AND d.Número = i.Número
    WHERE ISNULL(i.Ruta, '') <> ISNULL(d.Ruta, '');
END
GO

-- La API (cuenta de máquina) escribe la fila ella misma mientras el trigger no exista. public ya tiene SELECT e INSERT en
-- LogModificaciones; se concede también expresamente a la API (regla de los scripts de NestoConnection).
GRANT SELECT, INSERT ON dbo.LogModificaciones TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT name, is_disabled, create_date FROM sys.triggers WHERE name = 'trgCabPedidoVtaRuta';
GO
