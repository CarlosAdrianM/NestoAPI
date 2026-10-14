/*
    NestoAPI#611 (cupones, corte 1 de #610): eventos con referencia de curso e inscripciones sin pedido.

    Ejecutar en SSMS contra NV como sa (ALTER y CREATE: el login nuevavision no puede). Idempotente: se puede lanzar
    dos veces. Lanzarlo ANTES de publicar la API: las columnas nuevas y la tabla están en el EDMX y, sin ellas,
    api/Eventos falla.

    1. Eventos: columnas nuevas, todas NULL en la BD (los eventos de #591 siguen valiendo tal cual).
       - Almacen: la tienda donde es el evento (REI o ALC; la API lo exige al crear o modificar). Lo mantiene esa tienda
         (grupo Tiendas con ese AlmacénPedidoVta) y Dirección (Carlos, 14/10/26).
       - ProductoCurso: la referencia de producto del curso (una por curso), con la que se facturará en el corte 2 (#612).
       - Plazas: si viene, la API no deja inscribir a más gente de la que cabe.
       - Lugar / Descripcion: para la ficha del evento en TNV.
       - FechaListaAsistencia: cuándo se pasó lista (a mano o, al día siguiente sin lista, el job que da a todos por asistidos).
    2. EventosInscripciones: la inscripción de un cliente a un evento. NO es un pedido. Al inscribir se genera un enlace
       de pago (PagosTPV) y la inscripción caduca a los 7 días sin pagar.
       Estado: Reservada -> Pagada -> Facturada -> Asistio / NoAsistio; o Caducada / Anulada.
       Origen: Nesto, NestoApp, TNV, PrestaShop, Telefono, Transferencia (señal sin inscripción previa).
       FormaPago: Tarjeta (enlace), Transferencia o Efectivo (en la tienda). Mientras está Reservada es la prevista; al
       pagarse, la real, con ImportePagado, UsuarioPago y FechaPago (Carlos, 14/10/26: el enlace es opcional).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas. Que un cliente no tenga dos
    inscripciones vivas en el mismo evento lo controla la API (un UNIQUE filtrado por estado es justo lo que no queremos).
*/

SET NOCOUNT ON;
USE NV;
GO

------------------------------------------------------------------------------------------------
-- 1. Eventos: columnas nuevas
------------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Eventos', 'Almacen') IS NULL
    ALTER TABLE dbo.Eventos ADD Almacen char(3) NULL;               -- Almacenes.Número: REI o ALC
GO
IF COL_LENGTH('dbo.Eventos', 'ProductoCurso') IS NULL
    ALTER TABLE dbo.Eventos ADD ProductoCurso char(15) NULL;        -- Productos.Número (misma empresa)
GO
IF COL_LENGTH('dbo.Eventos', 'Plazas') IS NULL
    ALTER TABLE dbo.Eventos ADD Plazas int NULL;                    -- NULL = sin límite
GO
IF COL_LENGTH('dbo.Eventos', 'Lugar') IS NULL
    ALTER TABLE dbo.Eventos ADD Lugar nvarchar(100) NULL;
GO
IF COL_LENGTH('dbo.Eventos', 'Descripcion') IS NULL
    ALTER TABLE dbo.Eventos ADD Descripcion nvarchar(2000) NULL;
GO
IF COL_LENGTH('dbo.Eventos', 'FechaListaAsistencia') IS NULL
    ALTER TABLE dbo.Eventos ADD FechaListaAsistencia datetime NULL; -- NULL = nadie ha pasado lista todavía
GO
IF OBJECT_ID('dbo.CK_Eventos_Plazas') IS NULL
    ALTER TABLE dbo.Eventos ADD CONSTRAINT CK_Eventos_Plazas CHECK (Plazas IS NULL OR Plazas > 0);
GO

------------------------------------------------------------------------------------------------
-- 2. EventosInscripciones
------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.EventosInscripciones') IS NULL
BEGIN
    CREATE TABLE dbo.EventosInscripciones (
        Id int IDENTITY(1, 1) NOT NULL,
        EventoId int NOT NULL,
        Empresa char(3) NOT NULL,
        Cliente char(10) NOT NULL,
        Contacto char(3) NOT NULL,
        Origen varchar(20) NOT NULL,
        Estado varchar(20) NOT NULL CONSTRAINT DF_EventosInscripciones_Estado DEFAULT ('Reservada'),
        FechaLimitePago date NULL,                      -- hasta ese día (incluido) se puede pagar; NULL si ya entró pagada
        PagoTPVId int NULL,                             -- el enlace de pago (el raíz: los reintentos llevan PagoOriginalId)
        EventoSenalId int NULL,                         -- la señal (#591) cuando el dinero entra por transferencia
        FormaPago varchar(20) NULL,
        ImportePagado decimal(18, 2) NULL,
        UsuarioPago varchar(30) NULL,                   -- quién la marcó pagada (o NestoAPI si fue el enlace)
        FechaPago datetime NULL,
        UsuarioCreacion varchar(30) NOT NULL CONSTRAINT DF_EventosInscripciones_UsuarioCreacion DEFAULT (SUSER_SNAME()),
        FechaCreacion datetime NOT NULL CONSTRAINT DF_EventosInscripciones_FechaCreacion DEFAULT (GETDATE()),
        Usuario varchar(30) NOT NULL CONSTRAINT DF_EventosInscripciones_Usuario DEFAULT (SUSER_SNAME()),
        FechaModificacion datetime NOT NULL CONSTRAINT DF_EventosInscripciones_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_EventosInscripciones PRIMARY KEY (Id),
        CONSTRAINT FK_EventosInscripciones_Eventos FOREIGN KEY (EventoId) REFERENCES dbo.Eventos (Id),
        CONSTRAINT FK_EventosInscripciones_PagosTPV FOREIGN KEY (PagoTPVId) REFERENCES dbo.PagosTPV (Id),
        CONSTRAINT FK_EventosInscripciones_EventosSenales FOREIGN KEY (EventoSenalId) REFERENCES dbo.EventosSenales (Id),
        CONSTRAINT CK_EventosInscripciones_Origen CHECK (Origen IN ('Nesto', 'NestoApp', 'TNV', 'PrestaShop', 'Telefono', 'Transferencia')),
        CONSTRAINT CK_EventosInscripciones_FormaPago CHECK (FormaPago IS NULL OR FormaPago IN ('Tarjeta', 'Transferencia', 'Efectivo')),
        CONSTRAINT CK_EventosInscripciones_ImportePagado CHECK (ImportePagado IS NULL OR ImportePagado >= 0),
        CONSTRAINT CK_EventosInscripciones_Estado CHECK (Estado IN ('Reservada', 'Pagada', 'Facturada', 'Asistio', 'NoAsistio', 'Caducada', 'Anulada'))
    );
    CREATE INDEX IX_EventosInscripciones_Evento ON dbo.EventosInscripciones (EventoId, Estado);
    CREATE INDEX IX_EventosInscripciones_Cliente ON dbo.EventosInscripciones (Empresa, Cliente, Contacto);
    CREATE INDEX IX_EventosInscripciones_Caducidad ON dbo.EventosInscripciones (Estado, FechaLimitePago);
    CREATE INDEX IX_EventosInscripciones_Pago ON dbo.EventosInscripciones (PagoTPVId);
END
GO

-- Eventos ya tenía SELECT/INSERT/UPDATE/DELETE (#591): las columnas nuevas no necesitan GRANT.
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.EventosInscripciones TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN: las seis columnas nuevas de Eventos y la tabla nueva (vacía la primera vez)
------------------------------------------------------------------------------------------------
SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Eventos' AND COLUMN_NAME IN ('Almacen', 'ProductoCurso', 'Plazas', 'Lugar', 'Descripcion', 'FechaListaAsistencia');

SELECT 'EventosInscripciones' AS Tabla, COUNT(*) AS Filas FROM dbo.EventosInscripciones;
GO
