/*
    NestoAPI#757 (30/10/26), corte 1 de la épica #755: la cabecera de las notas de entrega.

    NotasEntregaCabecera: el TIPO de la nota (Entrega / Recogida), sus BULTOS y, en las de recogida, la FACTURA ORIGINAL que se
    devuelve (opcional). PedidoAbono lo rellenará el corte 4 (el abono que se crea al escanear la recogida).

    - Sin fila = Entrega, 1 bulto: las notas de Nesto viejo siguen valiendo tal cual, no hay que migrar nada.
    - Bultos solo se usa en las de RECOGIDA (las de entrega pasan por picking y packing y sus bultos salen del packing,
      decisión de Carlos del 30/10/26); en las de entrega se guarda el valor por defecto.
    - La lee y la escribe la API por SQL (api/NotasEntrega/{empresa}/{pedido}, el GET del pedido y el POST que crea la nota),
      fuera del EDMX. Sin el script, el GET da los valores por defecto y el PUT da 400 «falta el script».

    Ejecutar en SSMS contra NV como sa (CREATE + GRANT). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.NotasEntregaCabecera', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotasEntregaCabecera (
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,                                   -- CabPedidoVta.Número (NotaEntrega = 1)
        Tipo varchar(10) NOT NULL CONSTRAINT DF_NotasEntregaCabecera_Tipo DEFAULT ('Entrega'),  -- Entrega / Recogida
        Bultos smallint NOT NULL CONSTRAINT DF_NotasEntregaCabecera_Bultos DEFAULT (1),
        FacturaOriginal varchar(10) NULL,                      -- la factura que se devuelve (recogida), opcional
        PedidoAbono int NULL,                                  -- corte 4: el pedido de abono creado al escanear la recogida
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_NotasEntregaCabecera_Fecha DEFAULT (GETDATE()),
        CONSTRAINT PK_NotasEntregaCabecera PRIMARY KEY CLUSTERED (Empresa, Pedido),
        CONSTRAINT CK_NotasEntregaCabecera_Tipo CHECK (Tipo IN ('Entrega', 'Recogida')),
        CONSTRAINT CK_NotasEntregaCabecera_Bultos CHECK (Bultos BETWEEN 0 AND 999)
    );
    PRINT 'NotasEntregaCabecera: creada.';
END
ELSE
    PRINT 'NotasEntregaCabecera: ya existía.';
GO

-- La API corre con la cuenta máquina de RDS2016 (integrated security). Tabla nueva: lee, inserta y actualiza (no borra).
GRANT SELECT, INSERT, UPDATE ON dbo.NotasEntregaCabecera TO [NUEVAVISION\RDS2016$];
GO
