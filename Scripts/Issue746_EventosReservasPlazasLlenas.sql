/*
    NestoAPI#746 (política de Pilar aprobada por Carlos, 30/10/26): «se reserva sin pagar MIENTRAS HAYA PLAZAS».

    Con el evento lleno (las inscripciones que ocupan plaza llegan a Eventos.Plazas), cada reserva sin pagar recibe UN aviso
    («pague antes de <fecha y hora> o perderá su plaza»); si vence sin pagar y el evento sigue lleno, pasa a Caducada.

    1. Dos columnas en EventosInscripciones (FUERA del EDMX: la API las lee y escribe por SQL y comprueba que existen con
       COL_LENGTH). Sin este script la API funciona como antes: no avisa ni anula nada (solo cambia el texto del rechazo).
       - AvisoPlazasLlenas: cuándo salió el aviso de «evento lleno» (una sola vez por reserva).
       - PlazoPagoPlazasLlenas: hasta cuándo puede pagar (aviso + EventosPlazasLlenasHorasPago, nunca después de su
         FechaLimitePago ni del inicio del evento). Si vence con plazas libres, la API las vuelve a poner a NULL.
    2. Los parámetros en ParametrosUsuario, empresa 1, usuario «(defecto)» (se cambian sin publicar):
       - EventosPlazasLlenasActivo = 1   («0» apaga los avisos y las anulaciones; sin fila, encendido)
       - EventosPlazasLlenasHorasPago = 48

    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces. Se puede lanzar antes o después del
    deploy. EventosInscripciones ya tenía SELECT/INSERT/UPDATE/DELETE (#611): las columnas nuevas no necesitan GRANT nuevo.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Las columnas
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.EventosInscripciones', 'AvisoPlazasLlenas') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD AvisoPlazasLlenas datetime NULL;
GO

IF COL_LENGTH('dbo.EventosInscripciones', 'PlazoPagoPlazasLlenas') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD PlazoPagoPlazasLlenas datetime NULL;
GO

-- Por si se lanza en una BD sin el GRANT de #611 (no hace daño repetirlo)
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- 2. Los parámetros («(defecto)», empresa 1)
------------------------------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'EventosPlazasLlenasActivo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'EventosPlazasLlenasActivo', '(defecto)', '1', 'Issue746', GETDATE());

IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'EventosPlazasLlenasHorasPago' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'EventosPlazasLlenasHorasPago', '(defecto)', '48', 'Issue746', GETDATE());
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT COL_LENGTH('dbo.EventosInscripciones', 'AvisoPlazasLlenas') AS AvisoPlazasLlenas,
       COL_LENGTH('dbo.EventosInscripciones', 'PlazoPagoPlazasLlenas') AS PlazoPagoPlazasLlenas;

SELECT Clave, Usuario, Valor FROM ParametrosUsuario
WHERE Empresa = '1' AND Clave IN ('EventosPlazasLlenasActivo', 'EventosPlazasLlenasHorasPago');

-- Eventos con plazas que hoy están llenos y tienen reservas sin pagar (los que recibirán el aviso en la primera pasada)
SELECT e.Id, e.Titulo, e.Fecha, e.Plazas,
       SUM(CASE WHEN i.Estado IN ('Reservada','Pagada','Facturada','Inscrita','Asistio','NoAsistio')
                 AND (i.Estado IN ('Asistio','NoAsistio') OR ISNULL(i.AsistenciaRespuesta, '') <> 'NoPodraIr') THEN 1 ELSE 0 END) AS Ocupadas,
       SUM(CASE WHEN i.Estado = 'Reservada' AND ISNULL(i.AsistenciaRespuesta, '') <> 'NoPodraIr' THEN 1 ELSE 0 END) AS SinPagar
FROM Eventos e
LEFT JOIN EventosInscripciones i ON i.EventoId = e.Id
WHERE e.Activo = 1 AND e.Fecha >= CAST(GETDATE() AS date) AND e.Plazas IS NOT NULL
GROUP BY e.Id, e.Titulo, e.Fecha, e.Plazas
ORDER BY e.Fecha;
GO
