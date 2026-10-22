/*
    NestoAPI#615 (cupones, corte 5 de #610): proponer la señal del evento al contabilizar la transferencia.

    Ejecutar en SSMS contra NV como sa (CREATE TABLE: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. Se puede lanzar ANTES o DESPUÉS de publicar la API: la tabla NO está en el EDMX (SQL directo, y todo comprueba
    antes que la tabla existe). Lo que se engancha a lo vivo va detrás del interruptor Eventos:ProponerSenales del
    Web.config, que nace APAGADO; hay que lanzar este script ANTES de encenderlo.

    EventosSenalesPropuestas: un cobro a cuenta del extracto del cliente (ExtractoCliente.[Nº Orden]) que puede ser la
    señal de un evento. Una fila por apunte y evento candidato (si hay varios eventos posibles, varias filas: Administración
    elige). El emparejamiento solo PROPONE; confirmar es marcar la señal como hasta ahora (EventosSenales).
      - PorInscripcion: el cliente/contacto tiene una inscripción reservada en el evento (InscripcionId).
      - PorConcepto: el concepto del cobro (el del extracto o el de la transferencia en el banco) menciona el evento.
      - Estado: Propuesta, Confirmada (se marcó la señal de ese evento) o Descartada (a mano, o porque se confirmó otro
        evento para el mismo apunte). Una descartada no se vuelve a proponer.
      - Origen: Contabilizar, Conciliar (punteo con el banco) o Barrido (job senales-eventos de las 7:50 o «Buscar»).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.EventosSenalesPropuestas') IS NULL
BEGIN
    CREATE TABLE dbo.EventosSenalesPropuestas (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        NumOrdenExtracto int NOT NULL,                  -- ExtractoCliente.[Nº Orden] del cobro a cuenta
        EventoId int NOT NULL,
        InscripcionId int NULL,                         -- la inscripción reservada que lo respalda (si la hay)
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        Importe decimal(18, 2) NOT NULL,                -- lo que tenía a favor al proponerla
        PorInscripcion bit NOT NULL CONSTRAINT DF_EventosSenalesPropuestas_PorInscripcion DEFAULT (0),
        PorConcepto bit NOT NULL CONSTRAINT DF_EventosSenalesPropuestas_PorConcepto DEFAULT (0),
        Detalle nvarchar(300) NULL,                     -- por qué se propone (para Administración)
        ConceptoBanco nvarchar(500) NULL,               -- el concepto de la transferencia, si ya está punteada
        Estado varchar(15) NOT NULL CONSTRAINT DF_EventosSenalesPropuestas_Estado DEFAULT ('Propuesta'),
        Origen varchar(15) NOT NULL,
        Usuario varchar(50) NOT NULL CONSTRAINT DF_EventosSenalesPropuestas_Usuario DEFAULT (SUSER_SNAME()),
        FechaCreacion datetime NOT NULL CONSTRAINT DF_EventosSenalesPropuestas_FechaCreacion DEFAULT (GETDATE()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_EventosSenalesPropuestas_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_EventosSenalesPropuestas PRIMARY KEY (Id),
        CONSTRAINT FK_EventosSenalesPropuestas_Eventos FOREIGN KEY (EventoId) REFERENCES dbo.Eventos (Id),
        CONSTRAINT FK_EventosSenalesPropuestas_Inscripciones FOREIGN KEY (InscripcionId) REFERENCES dbo.EventosInscripciones (Id),
        CONSTRAINT UQ_EventosSenalesPropuestas_ApunteEvento UNIQUE (Empresa, NumOrdenExtracto, EventoId),
        CONSTRAINT CK_EventosSenalesPropuestas_Estado CHECK (Estado IN ('Propuesta', 'Confirmada', 'Descartada')),
        CONSTRAINT CK_EventosSenalesPropuestas_Origen CHECK (Origen IN ('Contabilizar', 'Conciliar', 'Barrido')),
        CONSTRAINT CK_EventosSenalesPropuestas_Importe CHECK (Importe > 0)
    );
    CREATE INDEX IX_EventosSenalesPropuestas_Estado ON dbo.EventosSenalesPropuestas (Empresa, Estado);
END
GO

-- BD de negocio: la API corre con la cuenta de máquina de RDS2016
GRANT SELECT, INSERT, UPDATE ON dbo.EventosSenalesPropuestas TO [NUEVAVISION\RDS2016$];
GO

SELECT 'EventosSenalesPropuestas' AS Tabla, COUNT(*) AS Filas FROM dbo.EventosSenalesPropuestas;
GO
