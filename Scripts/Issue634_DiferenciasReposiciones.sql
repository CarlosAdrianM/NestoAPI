/*
    NestoAPI#634 (corte 1): tabla DiferenciasReposiciones.

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. No está en el EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy:
    sin ella, la recepción de reposiciones sigue como hoy (entra lo leído y se avisa), sin dejar constancia ni ajustar.

    Una fila por traspaso y producto en que lo recibido no coincide con lo enviado (caso 38631 / 80915: enviado 1, leído 0).
    La escribe la API al terminar la recepción (api/Almacen/Recepciones/REPO/{traspaso}/Terminar), dentro de la misma
    transacción que la entrada. Después, y solo con el interruptor Reposiciones:AjustarStockDiferencias encendido, la API
    ajusta el stock del ORIGEN por prdExtrProducto en el diario «_ErrRepo» (Cuadre de errores de reposición): falta → entrada,
    sobra → salida; el número del apunte queda en NumOrdenAjuste.

    Estado:      Pendiente (por comprobar en el origen) / Resuelta.
    Resolucion:  Confirmada (el origen la ha comprobado y cuadra), NoEstaba (falta y no está: baja), Sobraba (sobra y sí está:
                 alta), ErrorRecepcion (la tienda se equivocó al recibir: se deshace el ajuste del origen y se corrige el destino).
    Ajuste:      NULL = por hacer (lo hace la API al terminar), Hecho, Apagado (interruptor apagado: no se tocó el stock),
                 Fallido (AjusteMensaje dice por qué; queda para que Almacén lo corrija a mano).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
    GRANT: SELECT, INSERT y UPDATE. Sin DELETE: las filas no se borran (son la constancia).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.DiferenciasReposiciones') IS NULL
BEGIN
    CREATE TABLE dbo.DiferenciasReposiciones (
        Id int IDENTITY(1, 1) NOT NULL,
        Empresa char(3) NOT NULL,
        NumTraspaso int NOT NULL,
        Origen char(3) NULL,                            -- de dónde salió (Delegación de las filas de entrada); NULL si no se sabe
        Destino char(3) NOT NULL,                       -- dónde se recibió
        Producto varchar(15) NOT NULL,
        Enviado int NOT NULL,
        Leido int NOT NULL,
        Diferencia int NOT NULL,                        -- Leido - Enviado: negativo falta, positivo sobra
        FechaRecepcion datetime NOT NULL CONSTRAINT DF_DiferenciasReposiciones_FechaRecepcion DEFAULT (GETDATE()),
        UsuarioRecepcion varchar(50) NOT NULL,
        Preparador varchar(50) NULL,                    -- quien creó/preparó el traspaso (se le avisa)
        IdRecepcion uniqueidentifier NULL,

        -- Ajuste automático del origen al recibir
        Ajuste varchar(10) NULL,
        AjusteMensaje nvarchar(500) NULL,
        NumOrdenAjuste int NULL,                        -- ExtractoProducto.[Nº Orden] del ajuste en el origen

        -- Comprobación en el origen
        Estado varchar(10) NOT NULL CONSTRAINT DF_DiferenciasReposiciones_Estado DEFAULT ('Pendiente'),
        Resolucion varchar(20) NULL,
        UsuarioResolucion varchar(50) NULL,
        FechaResolucion datetime NULL,
        Comentario nvarchar(500) NULL,
        NumOrdenResolucion int NULL,                    -- ExtractoProducto.[Nº Orden] del movimiento en el origen al resolver
        NumOrdenResolucionDestino int NULL,             -- ídem en el destino (solo ErrorRecepcion)

        CONSTRAINT PK_DiferenciasReposiciones PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_DiferenciasReposiciones_Traspaso_Producto UNIQUE (Empresa, NumTraspaso, Producto),
        CONSTRAINT CK_DiferenciasReposiciones_Estado CHECK (Estado IN ('Pendiente', 'Resuelta')),
        CONSTRAINT CK_DiferenciasReposiciones_Resolucion CHECK (Resolucion IS NULL OR Resolucion IN ('Confirmada', 'NoEstaba', 'Sobraba', 'ErrorRecepcion')),
        CONSTRAINT CK_DiferenciasReposiciones_Ajuste CHECK (Ajuste IS NULL OR Ajuste IN ('Hecho', 'Apagado', 'Fallido')),
        CONSTRAINT CK_DiferenciasReposiciones_Diferencia CHECK (Diferencia <> 0 AND Diferencia = Leido - Enviado)
    );
END
GO

-- Para la lista de pendientes de un almacén (Nesto#526 y Ariadna#25). Sin filtro: nada de índices filtrados.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DiferenciasReposiciones_Estado_Origen' AND object_id = OBJECT_ID('dbo.DiferenciasReposiciones'))
    CREATE INDEX IX_DiferenciasReposiciones_Estado_Origen ON dbo.DiferenciasReposiciones (Estado, Origen, FechaRecepcion);
GO

GRANT SELECT, INSERT, UPDATE ON dbo.DiferenciasReposiciones TO [NUEVAVISION\RDS2016$];
GO

-- El diario del ajuste ya existe en producción («_ErrRepo», Cuadre de errores de reposicion, Sistema = 1, sin uso desde 2012).
IF NOT EXISTS (SELECT 1 FROM dbo.DiariosProducto WHERE Empresa = '1' AND [Número] = '_ErrRepo')
    PRINT 'OJO: no existe el diario de producto _ErrRepo en la empresa 1: el ajuste automático fallará hasta crearlo.';
GO
