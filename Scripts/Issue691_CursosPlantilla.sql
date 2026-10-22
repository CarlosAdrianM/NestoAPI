/*
    NestoAPI#691 (cupones y eventos, #610): cursos vendidos desde la plantilla de venta.

    Ejecutar en SSMS contra NV como sa (ALTER TABLE). Idempotente: se puede lanzar dos veces. Hay que lanzarlo ANTES de
    publicar la API: sin él, el pedido del curso que crea la plantilla no se puede ligar a su inscripción (el CHECK rechaza
    el estado nuevo) y la API devuelve el error sin crear el pedido del curso.

    EventosInscripcionesCurso.Estado admite un valor nuevo, «PendienteEvento»: el pedido del curso (serie CV, prepago) ya
    existe porque lo creó la plantilla junto con la inscripción Reservada; espera al pago y al día del evento para
    facturarse (al entrar el pago si el evento ya ha llegado; si no, el job diario inscripciones-eventos el día del
    evento, detrás de Eventos:FacturarCursosPlantilla).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EventosInscripcionesCurso') IS NULL
BEGIN
    RAISERROR('Falta dbo.EventosInscripcionesCurso: lance antes Scripts/Issue612_FacturacionCursosEventos.sql.', 16, 1);
    RETURN;
END
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints
           WHERE name = 'CK_EventosInscripcionesCurso_Estado'
             AND parent_object_id = OBJECT_ID('dbo.EventosInscripcionesCurso')
             AND definition NOT LIKE '%PendienteEvento%')
BEGIN
    ALTER TABLE dbo.EventosInscripcionesCurso DROP CONSTRAINT CK_EventosInscripcionesCurso_Estado;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE name = 'CK_EventosInscripcionesCurso_Estado'
                 AND parent_object_id = OBJECT_ID('dbo.EventosInscripcionesCurso'))
BEGIN
    ALTER TABLE dbo.EventosInscripcionesCurso WITH CHECK ADD CONSTRAINT CK_EventosInscripcionesCurso_Estado
        CHECK (Estado IN ('EnCurso', 'Facturada', 'Error', 'NoAutomatizable', 'PendienteEvento'));
END
GO

-- La API corre con la cuenta de máquina de RDS2016 (el de #612 ya daba SELECT, INSERT, UPDATE; se repite por si acaso)
GRANT SELECT, INSERT, UPDATE ON dbo.EventosInscripcionesCurso TO [NUEVAVISION\RDS2016$];
GO

SELECT definition AS CheckEstado FROM sys.check_constraints WHERE name = 'CK_EventosInscripcionesCurso_Estado';
GO
