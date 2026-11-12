/*
    NestoAPI#857 (fase 1, EN SOMBRA, 12/11/26), tercera parte: la entrega estimada que anuncia el propio correo y los envíos nuestros
    de salida que llegan en copia al buzón de Compras.

    Añade a dbo.ComprasCorreosSeguimiento (la de Scripts/Issue857_ComprasCorreosSeguimiento.sql):
      - EntregaEstimadaDia date NULL: el día que anuncia el correo («Entrega estimada: Lunes 16/11/2026», «Te lo entregaremos, como
        máximo, el 13/11/2026»). NULL = el correo no lo dice (o se guardó antes de este script).
      - EntregaEstimadaDesde time(0) NULL y EntregaEstimadaHasta time(0) NULL: el tramo horario («entre 10:45 - 14:45»); «antes de
        las 14:00» deja solo Hasta.
      - DisponibleEstimado date NULL: desde cuándo se podría servir (siempre el final del tramo, pesimista: antes de la hora de corte
        del picking en un laborable de Algete, ese día; si no, el siguiente laborable de Algete). NULL en las salidas nuestras.
      - EsSalidaNuestra bit NULL: 1 = es un envío NUESTRO de salida en copia (el aviso de GLS de un pedido de venta); se guarda para
        medir, pero sin proveedor propuesto, pedidos candidatos ni disponible. NULL = sin apuntar.

    ORDEN: después de Issue857_ComprasCorreosSeguimiento.sql (si no existe la tabla, no hace nada y lo dice); da igual antes o después
    de Issue857b. Puede ir ANTES o DESPUÉS de publicar la API: sin las cinco columnas, la API guarda como hasta ahora y GET …/Correos
    las devuelve a null; la API lo vuelve a comprobar cada 10 minutos, así que no hace falta reiniciarla. Conviene lanzarlo ANTES del
    histórico para que los correos antiguos lleven la entrega estimada.

    Lanzar como sa en NV. Idempotente: se puede relanzar. Cada columna nueva en su propio lote. Los permisos ya los tiene la tabla
    (GRANT SELECT, INSERT, UPDATE a [NUEVAVISION\RDS2016$] en Issue857): no hace falta ninguno nuevo.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NULL
    RAISERROR(N'No existe dbo.ComprasCorreosSeguimiento: lance antes Scripts/Issue857_ComprasCorreosSeguimiento.sql. No se ha cambiado nada.', 16, 1);
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaEstimadaDia') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EntregaEstimadaDia date NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaEstimadaDesde') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EntregaEstimadaDesde time(0) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaEstimadaHasta') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EntregaEstimadaHasta time(0) NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'DisponibleEstimado') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD DisponibleEstimado date NULL;
GO
IF OBJECT_ID(N'dbo.ComprasCorreosSeguimiento', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EsSalidaNuestra') IS NULL
    ALTER TABLE dbo.ComprasCorreosSeguimiento ADD EsSalidaNuestra bit NULL;
GO
-- Comprobación
SELECT CASE WHEN COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaEstimadaDia') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaEstimadaDesde') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EntregaEstimadaHasta') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'DisponibleEstimado') IS NOT NULL
             AND COL_LENGTH(N'dbo.ComprasCorreosSeguimiento', N'EsSalidaNuestra') IS NOT NULL
            THEN N'OK: columnas EntregaEstimadaDia, EntregaEstimadaDesde, EntregaEstimadaHasta, DisponibleEstimado y EsSalidaNuestra en dbo.ComprasCorreosSeguimiento'
            ELSE N'FALTA: no están las cinco columnas (¿existe la tabla?)' END AS Resultado;
GO
