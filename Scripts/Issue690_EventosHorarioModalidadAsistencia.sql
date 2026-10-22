/*
    ##################################################################################################################
    ###  LANZAR ANTES DEL DEPLOY DE LA API. Las columnas nuevas de Eventos y EventosInscripciones están en el EDMX: ###
    ###  sin ellas, api/Eventos falla.                                                                            ###
    ##################################################################################################################

    NestoAPI#690 (eventos, corte 1b de #610): horario, ponente, imagen, dos señales (reembolsable / no reembolsable),
    evento gratuito, confirmación de asistencia con recordatorio.

    Ejecutar en SSMS contra NV como sa (ALTER y CREATE: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces.

    1. Eventos: columnas nuevas.
       - HoraInicio / HoraFin: el horario (time, sin segundos). NULL = sin horario (los eventos de antes).
       - Ponente: texto libre.
       - ImporteSenalNoReembolsable: la señal NO reembolsable (factura exenta del curso, sin cupón). 0 = el evento no la
         ofrece (los de antes). Con las dos señales a 0, el evento es gratuito. La API pone 100 / 60 al crear si no vienen.
    2. EventosInscripciones: columnas nuevas y el estado Inscrita.
       - Modalidad: Reembolsable, NoReembolsable o Gratuita. NULL = Reembolsable (las de antes).
       - AsistenciaRespuesta / AsistenciaFecha / AsistenciaUsuario / AsistenciaOrigen: «Confirmo asistencia» (Confirma)
         o «No podré ir» (NoPodraIr), cuándo, quién y si contestó el cliente (Cliente) o lo apuntó la tienda (Tienda).
         Independiente del estado del pago.
       - RecordatorioEnviado: cuándo salió el recordatorio (dos días antes, una sola vez).
       - ClaveAsistencia: la clave del enlace del correo para contestar sin usuario (no se puede adivinar).
       - Estado Inscrita: la inscripción gratuita (no caduca, no factura, va directa a la lista de asistencia).
    3. EventosImagenes: la imagen del evento, fuera del EDMX, como NovedadesAdjuntos (#616). Una por evento.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Eventos
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Eventos', 'HoraInicio') IS NULL
    ALTER TABLE dbo.Eventos ADD HoraInicio time(0) NULL;
GO
IF COL_LENGTH('dbo.Eventos', 'HoraFin') IS NULL
    ALTER TABLE dbo.Eventos ADD HoraFin time(0) NULL;
GO
IF COL_LENGTH('dbo.Eventos', 'Ponente') IS NULL
    ALTER TABLE dbo.Eventos ADD Ponente nvarchar(100) NULL;
GO
IF COL_LENGTH('dbo.Eventos', 'ImporteSenalNoReembolsable') IS NULL
    ALTER TABLE dbo.Eventos ADD ImporteSenalNoReembolsable decimal(18, 2) NOT NULL
        CONSTRAINT DF_Eventos_ImporteSenalNoReembolsable DEFAULT (0);
GO
IF OBJECT_ID('dbo.CK_Eventos_ImporteSenalNoReembolsable') IS NULL
    ALTER TABLE dbo.Eventos ADD CONSTRAINT CK_Eventos_ImporteSenalNoReembolsable CHECK (ImporteSenalNoReembolsable >= 0);
GO
IF OBJECT_ID('dbo.CK_Eventos_Horario') IS NULL
    ALTER TABLE dbo.Eventos ADD CONSTRAINT CK_Eventos_Horario CHECK (HoraInicio IS NULL OR HoraFin IS NULL OR HoraFin > HoraInicio);
GO

------------------------------------------------------------------------------------------------
-- 2. EventosInscripciones
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.EventosInscripciones', 'Modalidad') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD Modalidad varchar(20) NULL;            -- NULL = Reembolsable
GO
IF COL_LENGTH('dbo.EventosInscripciones', 'AsistenciaRespuesta') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD AsistenciaRespuesta varchar(20) NULL;  -- NULL = sin contestar
GO
IF COL_LENGTH('dbo.EventosInscripciones', 'AsistenciaFecha') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD AsistenciaFecha datetime NULL;
GO
IF COL_LENGTH('dbo.EventosInscripciones', 'AsistenciaUsuario') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD AsistenciaUsuario varchar(30) NULL;
GO
IF COL_LENGTH('dbo.EventosInscripciones', 'AsistenciaOrigen') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD AsistenciaOrigen varchar(20) NULL;
GO
IF COL_LENGTH('dbo.EventosInscripciones', 'RecordatorioEnviado') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD RecordatorioEnviado datetime NULL;
GO
IF COL_LENGTH('dbo.EventosInscripciones', 'ClaveAsistencia') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD ClaveAsistencia uniqueidentifier NULL;
GO
IF OBJECT_ID('dbo.CK_EventosInscripciones_Modalidad') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD CONSTRAINT CK_EventosInscripciones_Modalidad
        CHECK (Modalidad IS NULL OR Modalidad IN ('Reembolsable', 'NoReembolsable', 'Gratuita'));
GO
IF OBJECT_ID('dbo.CK_EventosInscripciones_AsistenciaRespuesta') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD CONSTRAINT CK_EventosInscripciones_AsistenciaRespuesta
        CHECK (AsistenciaRespuesta IS NULL OR AsistenciaRespuesta IN ('Confirma', 'NoPodraIr'));
GO
IF OBJECT_ID('dbo.CK_EventosInscripciones_AsistenciaOrigen') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD CONSTRAINT CK_EventosInscripciones_AsistenciaOrigen
        CHECK (AsistenciaOrigen IS NULL OR AsistenciaOrigen IN ('Cliente', 'Tienda'));
GO

-- El estado nuevo Inscrita (inscripción gratuita): se rehace el CHECK de #611 si todavía no lo admite.
IF OBJECT_ID('dbo.CK_EventosInscripciones_Estado') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.check_constraints
                   WHERE name = 'CK_EventosInscripciones_Estado' AND definition LIKE '%Inscrita%')
    ALTER TABLE dbo.EventosInscripciones DROP CONSTRAINT CK_EventosInscripciones_Estado;
GO
IF OBJECT_ID('dbo.CK_EventosInscripciones_Estado') IS NULL
    ALTER TABLE dbo.EventosInscripciones ADD CONSTRAINT CK_EventosInscripciones_Estado
        CHECK (Estado IN ('Reservada', 'Pagada', 'Facturada', 'Inscrita', 'Asistio', 'NoAsistio', 'Caducada', 'Anulada'));
GO

-- El enlace del correo busca la inscripción por su clave
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_EventosInscripciones_ClaveAsistencia'
               AND object_id = OBJECT_ID('dbo.EventosInscripciones'))
    CREATE INDEX IX_EventosInscripciones_ClaveAsistencia ON dbo.EventosInscripciones (ClaveAsistencia);
GO

------------------------------------------------------------------------------------------------
-- 3. EventosImagenes (fuera del EDMX, como NovedadesAdjuntos de #616)
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EventosImagenes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.EventosImagenes (
        EventoId int NOT NULL CONSTRAINT PK_EventosImagenes PRIMARY KEY
            CONSTRAINT FK_EventosImagenes_Eventos REFERENCES dbo.Eventos (Id),
        Nombre nvarchar(200) NOT NULL,
        Tipo varchar(100) NOT NULL,
        Tamano int NOT NULL,
        Contenido varbinary(max) NOT NULL,
        Usuario nvarchar(50) NULL,
        Fecha datetime NOT NULL CONSTRAINT DF_EventosImagenes_Fecha DEFAULT GETDATE()
    );
END
GO

-- Eventos y EventosInscripciones ya tenían SELECT/INSERT/UPDATE/DELETE (#591, #611): las columnas nuevas no necesitan GRANT.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosImagenes TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: 4 columnas en Eventos, 7 en EventosInscripciones, el CHECK con Inscrita y la tabla nueva
------------------------------------------------------------------------------------------------
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE (TABLE_NAME = 'Eventos' AND COLUMN_NAME IN ('HoraInicio', 'HoraFin', 'Ponente', 'ImporteSenalNoReembolsable'))
   OR (TABLE_NAME = 'EventosInscripciones' AND COLUMN_NAME IN ('Modalidad', 'AsistenciaRespuesta', 'AsistenciaFecha',
       'AsistenciaUsuario', 'AsistenciaOrigen', 'RecordatorioEnviado', 'ClaveAsistencia'))
ORDER BY TABLE_NAME, COLUMN_NAME;

SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_EventosInscripciones_Estado';
SELECT 'EventosImagenes' AS Tabla, COUNT(*) AS Filas FROM dbo.EventosImagenes;
GO
