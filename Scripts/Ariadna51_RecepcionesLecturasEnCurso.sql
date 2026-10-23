/*
    Ariadna#51 (corte 2): tabla RecepcionesLecturasEnCurso.

    Lo leído de una recepción en curso (REPO: una reposición; COMP: lo que llega de un proveedor) se guarda en el
    servidor: Nesto y Ariadna, en cualquier equipo, ven lo mismo y se puede seguir o terminar desde cualquiera. Por qué
    (23/10/26): la reposición 81119 se escaneó entera en Nesto, que lo tenía solo en memoria; el primer «Terminar» falló
    y se terminó desde Ariadna, donde no había nada leído (2 de 45): 43 diferencias y 113 unidades devueltas a Algete.

    Cada fila es un apunte (Cantidad + o −): con dos personas a la vez, se suma. IdCliente es único: un reenvío (la cola
    de Ariadna sin red) no duplica. IdRecepcion es el de la recepción en curso, el mismo para todos los equipos; es el
    que se manda al Terminar (que ya es idempotente por IdRecepcion + producto).

    La fila de apertura (la crea el GET cuando no hay nada en curso, y el DELETE y una recepción en parte al abrir otra):
    Producto y CodigoBulto NULL y Cantidad 0. Solo guarda el IdRecepcion.

    Estado:       EnCurso / Terminada (al terminar con ese IdRecepcion, en la misma transacción) / Descartada («Descartar
                  lo leído»: no se borra nada).
    FechaEstado, UsuarioEstado: cuándo y quién la terminó o la descartó.
    Fecha:        la de la lectura en el dispositivo (la cola sin red llega tarde); si no llega, la del servidor.

    No está en el EDMX (la API la lee y escribe con SQL a mano, como PedidosPromesaRiesgo #693): se puede lanzar antes o
    después del deploy. Sin ella las rutas …/Lecturas dan 404 y Nesto y Ariadna funcionan como hasta ahora.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE. Sin DELETE: las filas no se borran (son la constancia de lo leído).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.RecepcionesLecturasEnCurso', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RecepcionesLecturasEnCurso (
        Id bigint IDENTITY(1, 1) NOT NULL,
        IdCliente uniqueidentifier NOT NULL,            -- lo genera el dispositivo: un reenvío no duplica
        Empresa char(3) NOT NULL,
        TipoOrigen char(4) NOT NULL,                    -- REPO / COMP
        Documento varchar(20) NOT NULL,                 -- REPO: el traspaso; COMP: el proveedor
        Almacen char(3) NOT NULL,                       -- el que recibe
        IdRecepcion uniqueidentifier NOT NULL,          -- el compartido de la recepción en curso
        Producto char(15) NULL,                         -- o CodigoBulto (NULL los dos: la fila de apertura)
        CodigoBulto varchar(20) NULL,                   -- la etiqueta del bulto («R81119-1»)
        Cantidad int NOT NULL,                          -- + al leer, − al quitar uno
        Usuario varchar(50) NOT NULL,
        Dispositivo varchar(50) NULL,
        Fecha datetime NOT NULL,                        -- la de la lectura en el dispositivo
        Estado varchar(10) NOT NULL CONSTRAINT DF_RecepcionesLecturasEnCurso_Estado DEFAULT ('EnCurso'),
        FechaEstado datetime NULL,
        UsuarioEstado varchar(50) NULL,
        Creado datetime NOT NULL CONSTRAINT DF_RecepcionesLecturasEnCurso_Creado DEFAULT (GETDATE()),

        CONSTRAINT PK_RecepcionesLecturasEnCurso PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT CK_RecepcionesLecturasEnCurso_Estado CHECK (Estado IN ('EnCurso', 'Terminada', 'Descartada'))
    );
END
GO

-- Un reenvío no duplica (la API inserta con WHERE NOT EXISTS; esto lo garantiza con dos peticiones a la vez)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_RecepcionesLecturasEnCurso_IdCliente' AND object_id = OBJECT_ID('dbo.RecepcionesLecturasEnCurso'))
    CREATE UNIQUE INDEX UX_RecepcionesLecturasEnCurso_IdCliente ON dbo.RecepcionesLecturasEnCurso (IdCliente);
GO

-- Lo en curso de una recepción (GET, POST, DELETE y Terminar). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_RecepcionesLecturasEnCurso_Recepcion' AND object_id = OBJECT_ID('dbo.RecepcionesLecturasEnCurso'))
    CREATE INDEX IX_RecepcionesLecturasEnCurso_Recepcion ON dbo.RecepcionesLecturasEnCurso (Empresa, TipoOrigen, Documento, Almacen, Estado)
        INCLUDE (IdRecepcion);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.RecepcionesLecturasEnCurso TO [NUEVAVISION\RDS2016$];
GO

-- Comprobación
SELECT OBJECT_ID('dbo.RecepcionesLecturasEnCurso', 'U') AS Tabla,
       (SELECT COUNT(*) FROM dbo.RecepcionesLecturasEnCurso) AS Filas;
GO

-- Lo que hay en curso:
-- SELECT TipoOrigen, Documento, Almacen, IdRecepcion, Producto, CodigoBulto, SUM(Cantidad) AS Cantidad, COUNT(*) AS Apuntes
-- FROM dbo.RecepcionesLecturasEnCurso WHERE Estado = 'EnCurso'
-- GROUP BY TipoOrigen, Documento, Almacen, IdRecepcion, Producto, CodigoBulto ORDER BY TipoOrigen, Documento, Producto;
