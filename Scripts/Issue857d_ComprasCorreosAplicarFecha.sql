/*
    NestoAPI#857 (FASE 3, 12/11/26): el correo de seguimiento PONE la fecha de recepción del pedido de compra (ya no es solo sombra).

    Añade a dbo.ComprasCorreosSeguimiento (la de Scripts/Issue857_ComprasCorreosSeguimiento.sql):
      - RemitentePaquete nvarchar(100) NULL: quién expide el paquete según el aviso («De MBE STORE C/O MULTIBOXES S.L» de UPS,
        «Remitente:\nWeelko Barcelona S. L.» de MBE), normalizado (sin tildes, en minúsculas, un solo espacio).
      - EmpresaAplicada char(3) NULL, PedidoAplicado int NULL, FechaRecepcionAplicada date NULL, AplicadoEl datetime NULL,
        AplicadoPor nvarchar(50) NULL: a qué pedido se ha puesto la fecha de este correo, cuál, cuándo y quién («(automático)» = el job).
      - Descartado bit NULL: Compras ha dicho que no es de ninguna compra.
      - AvisadoPorAsignar datetime NULL: cuándo se avisó a Compras (campana) de que su envío quedaba por asignar (uno por envío).
      - EligioSugerido bit NULL: al asignar a mano, si se eligió el pedido sugerido (para medir la sugerencia).
    Crea dbo.ComprasRemitentesPaquete: la memoria «quien expide el paquete → proveedor» (única por remitente), que alimenta la asignación.
    Crea en ParametrosUsuario (empresa '1', usuario '(defecto)') el interruptor 'Compras.CorreosSeguimientoAplicar' a '1' solo si no existe.
      '1' = el job escribe LinPedidoCmp.FechaRecepción de lo inequívoco; sin fila o cualquier otro valor = no escribe nada en compras
      (todo lo demás sigue: guardar, enlazar por mismo envío, avisar, y la asignación a mano). Se lee en cada pasada, sin caché.
      Apagado de emergencia:
        UPDATE ParametrosUsuario SET Valor = '0', Usuario2 = 'Compras857', [Fecha Modificación] = GETDATE()
        WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoAplicar' AND Usuario = '(defecto)';

    «Mismo envío» (correos que comparten código de seguimiento) NO se guarda: se calcula cada vez de Codigo y Seguimientos.

    ORDEN: después de Issue857_ComprasCorreosSeguimiento.sql (si no existe la tabla, no hace nada y lo dice) y de Issue857c. Puede ir
    ANTES o DESPUÉS de publicar la API: sin TODO lo de este script (las nueve columnas y la tabla), la API sigue exactamente como antes
    (no escribe en compras, no avisa, y los endpoints nuevos dan 503); lo vuelve a comprobar cada 10 minutos, sin reiniciar.
    Tras lanzarlo, POST api/Compras/AgenciasEntrada/Correos/Reanalizar?dias=7 para que lo ya guardado lleve remitente y enlaces.

    Lanzar como sa en NV. Idempotente: se puede relanzar. Cada columna nueva en su propio lote. Permisos: la tabla nueva necesita
    GRANT a [NUEVAVISION\RDS2016$]; ComprasCorreosSeguimiento ya los tiene (Issue857) y LinPedidoCmp y Modificaciones ya las escribe la API.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'RemitentePaquete') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD RemitentePaquete nvarchar(100) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EmpresaAplicada') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EmpresaAplicada char(3) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'PedidoAplicado') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD PedidoAplicado int NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'FechaRecepcionAplicada') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD FechaRecepcionAplicada date NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AplicadoEl') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD AplicadoEl datetime NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AplicadoPor') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD AplicadoPor nvarchar(50) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'Descartado') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD Descartado bit NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AvisadoPorAsignar') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD AvisadoPorAsignar datetime NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EligioSugerido') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EligioSugerido bit NULL;
GO
IF OBJECT_ID(N'dbo.ComprasRemitentesPaquete', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ComprasRemitentesPaquete (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ComprasRemitentesPaquete PRIMARY KEY CLUSTERED,
        RemitentePaquete nvarchar(100) NOT NULL,   -- normalizado: sin tildes, en minúsculas, un solo espacio
        Empresa char(3) NOT NULL,
        Proveedor nvarchar(10) NOT NULL,
        Usuario nvarchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ComprasRemitentesPaquete_FechaModificacion DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX UX_ComprasRemitentesPaquete_RemitentePaquete ON dbo.ComprasRemitentesPaquete (RemitentePaquete);
END
GO
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ComprasRemitentesPaquete TO [NUEVAVISION\RDS2016$];
GO
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoAplicar' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'Compras.CorreosSeguimientoAplicar', '(defecto)', '1', 'Compras857', GETDATE());
GO
-- Comprobación
SELECT CASE WHEN OBJECT_ID(N'dbo.ComprasRemitentesPaquete', N'U') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'RemitentePaquete') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EmpresaAplicada') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'PedidoAplicado') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'FechaRecepcionAplicada') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AplicadoEl') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AplicadoPor') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'Descartado') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'AvisadoPorAsignar') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EligioSugerido') IS NOT NULL
            THEN N'OK: fase 3 de NestoAPI#857 (nueve columnas en ComprasCorreosSeguimiento y la tabla ComprasRemitentesPaquete)'
            ELSE N'FALTA algo (¿existe dbo.ComprasCorreosSeguimiento?)' END AS Resultado;
SELECT RTRIM(Clave) AS Clave, RTRIM(Usuario) AS Usuario, RTRIM(Valor) AS Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave = 'Compras.CorreosSeguimientoAplicar';
GO
