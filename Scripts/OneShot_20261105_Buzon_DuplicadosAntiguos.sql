-- OneShot 05/11/26 (NestoAPI#822, decisión de Carlos): con la campana por PERSONA (Nesto + Ariadna + NestoApp comparten buzón), los
-- avisos que antes se grababan DOS veces (una fila para Nesto y otra para Ariadna o NestoApp) saldrían duplicados en la campana hasta
-- leerlos o borrarlos. Este script marca como eliminada la copia de Ariadna/NestoApp cuando existe la misma de Nesto para la misma
-- persona (mismo título, cuerpo y datos, creadas con menos de 2 minutos de diferencia) y ninguna de las dos está eliminada.
-- Ejecutar como sa en NV DESPUÉS de publicar la API y DESPUÉS de Issue823_UsuariosAriadnaADominio.sql (que pasa el Usuario de las filas
-- de Ariadna/NestoApp a su gemelo de dominio: así la persona se compara por Usuario directamente; por si acaso, también se casa el nombre
-- sin dominio con el de dominio). Idempotente. No borra filas: pone FechaEliminada.
USE NV;
GO
SET NOCOUNT ON;
BEGIN TRAN;

SELECT 'ANTES' Momento, Aplicacion, COUNT(*) Vivas, SUM(CASE WHEN FechaLeida IS NULL THEN 1 ELSE 0 END) SinLeer
FROM dbo.NotificacionesBuzon WHERE FechaEliminada IS NULL GROUP BY Aplicacion;

;WITH Duplicadas AS (
    SELECT a.Id
    FROM dbo.NotificacionesBuzon a
    JOIN dbo.NotificacionesBuzon n
      ON n.Aplicacion = 'Nesto'
     AND n.FechaEliminada IS NULL
     AND n.Titulo = a.Titulo
     AND ISNULL(n.Cuerpo, '') = ISNULL(a.Cuerpo, '')
     AND ISNULL(n.Datos, '') = ISNULL(a.Datos, '')
     AND ABS(DATEDIFF(SECOND, n.FechaCreacion, a.FechaCreacion)) <= 120
     AND (n.Usuario = a.Usuario
          OR n.Usuario = 'NUEVAVISION\' + a.Usuario
          OR (CHARINDEX('\', n.Usuario) > 0 AND SUBSTRING(n.Usuario, CHARINDEX('\', n.Usuario) + 1, 100) = a.Usuario))
    WHERE a.Aplicacion IN ('Ariadna', 'NestoApp')
      AND a.FechaEliminada IS NULL
)
UPDATE b SET FechaEliminada = GETDATE()
FROM dbo.NotificacionesBuzon b JOIN Duplicadas d ON d.Id = b.Id;
PRINT CONCAT('Copias de Ariadna/NestoApp marcadas como eliminadas: ', @@ROWCOUNT);

SELECT 'DESPUES' Momento, Aplicacion, COUNT(*) Vivas, SUM(CASE WHEN FechaLeida IS NULL THEN 1 ELSE 0 END) SinLeer
FROM dbo.NotificacionesBuzon WHERE FechaEliminada IS NULL GROUP BY Aplicacion;

COMMIT;
GO
