/*
    NestoAPI#763 (Nesto#584): «horario preferible» de entrega por cliente y contacto.

    SOLO LO RESPETA NUESTRA RUTA PROPIA (agencia 14): el orden sugerido de las paradas de Ariadna avisa de las que llegarían
    fuera de su horario. Las agencias externas no tienen obligación de respetarlo.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    No está en el EDMX (la API lo lee y lo escribe con SQL a mano), así que se puede lanzar antes o después del deploy. Sin el
    script:
      - GET api/Clientes/{empresa}/{cliente}/{contacto}/HorarioPreferible devuelve el horario vacío (la ficha de Nesto sale igual);
      - el PUT y aceptar/descartar propuestas dan 400 «Falta lanzar el script…»;
      - las paradas del reparto salen sin horario.
    Después de lanzarlo, reciclar el app pool de la API (recuerda qué tablas existen).

    NO TOCA dbo.Clientes: el dato va en sus propias tablas (nunca en Comentarios/ComentarioRuta/Ruta ni en otra columna que ya
    signifique otra cosa).

    ClientesHorarioPreferible: una fila por franja (como mucho dos por día: mañana y tarde). DiaSemana 1 = lunes … 7 = domingo.
    ClientesHorarioPreferiblePropuestas: lo que se saca de los comentarios de la ficha (Scripts/Issue763_CargaPropuestasHorario.sql).
      No cuenta para nada hasta que una persona lo acepta en Nesto (Clientes > Horarios por revisar, o la pestaña de la ficha).
      Estado: PorRevisar | Aceptada | Descartada. La propuesta en sí la calculan las reglas de la API a partir de TextoOrigen.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT a la cuenta máquina con la que corre la API (NUEVAVISION\RDS2016$).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ClientesHorarioPreferible') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesHorarioPreferible (
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        DiaSemana tinyint NOT NULL,                     -- 1 = lunes … 7 = domingo
        Franja tinyint NOT NULL,                        -- 1 (mañana) o 2 (tarde), por orden de hora
        Desde time(0) NOT NULL,
        Hasta time(0) NOT NULL,
        Usuario nvarchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ClientesHorarioPreferible_Fecha DEFAULT (GETDATE()),

        CONSTRAINT PK_ClientesHorarioPreferible PRIMARY KEY CLUSTERED (Empresa, Cliente, Contacto, DiaSemana, Franja),
        CONSTRAINT CK_ClientesHorarioPreferible_Dia CHECK (DiaSemana BETWEEN 1 AND 7),
        CONSTRAINT CK_ClientesHorarioPreferible_Franja CHECK (Franja BETWEEN 1 AND 2),
        CONSTRAINT CK_ClientesHorarioPreferible_Horas CHECK (Desde < Hasta)
    );
END
GO

IF OBJECT_ID('dbo.ClientesHorarioPreferiblePropuestas') IS NULL
BEGIN
    CREATE TABLE dbo.ClientesHorarioPreferiblePropuestas (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        Origen varchar(20) NOT NULL CONSTRAINT DF_ClientesHorarioPreferiblePropuestas_Origen DEFAULT ('Comentarios'),
        TextoOrigen nvarchar(2000) NOT NULL,            -- Comentarios || ComentarioRuta, tal como estaban
        Estado varchar(12) NOT NULL CONSTRAINT DF_ClientesHorarioPreferiblePropuestas_Estado DEFAULT ('PorRevisar'),
        Fecha datetime NOT NULL CONSTRAINT DF_ClientesHorarioPreferiblePropuestas_Fecha DEFAULT (GETDATE()),
        UsuarioRevision nvarchar(50) NULL,
        FechaRevision datetime NULL,
        HorarioAceptado nvarchar(200) NULL,             -- el horario que se guardó al aceptarla (en texto)

        CONSTRAINT PK_ClientesHorarioPreferiblePropuestas PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ClientesHorarioPreferiblePropuestas UNIQUE (Empresa, Cliente, Contacto, Origen),
        CONSTRAINT CK_ClientesHorarioPreferiblePropuestas_Estado CHECK (Estado IN ('PorRevisar', 'Aceptada', 'Descartada'))
    );
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesHorarioPreferible TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ClientesHorarioPreferiblePropuestas TO [NUEVAVISION\RDS2016$];
GO

PRINT 'NestoAPI#763: ClientesHorarioPreferible y ClientesHorarioPreferiblePropuestas listas. Recicle el app pool de la API y lance Issue763_CargaPropuestasHorario.sql.';
GO
