-- ONE-SHOT 26/08/2026 (cutover NestoSync): encolar el resto del catalogo vivo que no se ha
-- publicado hoy (~1.475 refs la ultima vez que se conto). Programado en el Task Scheduler de
-- esta maquina para las 23:00; despues de esa noche la tarea se borra sola (/z) y este script
-- queda solo como registro.
--
-- Guardas: PVP > 0 (61 fichas a medio dar de alta quedan fuera: publicarian precio 0),
-- no encolar lo ya publicado o encolado el 26/08, y no duplicar pendientes.
SET NOCOUNT ON;
USE NV;

INSERT INTO Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
SELECT 'Productos', RTRIM(p.Número), 'Resync resto catalogo', GETDATE()
FROM Productos p
WHERE p.Empresa = '1' AND p.Estado >= 0 AND p.PVP > 0
  AND NOT EXISTS (SELECT 1 FROM Nesto_sync ns
                  WHERE ns.Tabla = 'Productos'
                    AND ns.ModificadoId = RTRIM(p.Número)
                    AND ns.FechaModificacion >= '20260826');

SELECT CONVERT(varchar(19), GETDATE(), 120) AS Momento, @@ROWCOUNT AS Encoladas;
