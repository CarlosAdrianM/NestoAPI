/*
    NestoAPI#786 (04/11/26): parada de solo COBRO en el reparto propio (correo del 929609: el envío salió sin reembolso y hay que
    volver el jueves solo a cobrar 134,16 €).

    - NotasEntregaCabecera.Tipo admite «Cobro» (se ALTERA la CHECK CK_NotasEntregaCabecera_Tipo: sin ella el INSERT fallaría con la
      restricción, la lección del modo 5, #698).
    - NotasEntregaCabecera.ImporteCobro: lo que hay que cobrar (el Reembolso de su parada). NULL en las de entrega y recogida.
    - NotasEntregaCobroEfectos: los efectos (ExtractoCliente.[Nº Orden]) que liquida el cobro, en su orden. Sin filas = solo importe.
    - La nota de cobro es un pedido con NotaEntrega = 1 SIN LÍNEAS: no entra en picking, packing ni Facturar rutas. La crea
      POST api/NotasEntrega/Cobro junto con su parada (envío de la agencia 14 ya tramitado, Bultos 0, Reembolso = ImporteCobro).
    - Sin este script la API contesta 400 «falta lanzar el script» al crear una nota de cobro y todo lo demás sigue como antes.

    Requiere Scripts/Issue757_NotasEntregaCabecera.sql. Ejecutar en SSMS contra NV como sa (ALTER + CREATE + GRANT).
    Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.NotasEntregaCabecera', 'U') IS NULL
BEGIN
    RAISERROR('Falta NotasEntregaCabecera: lance antes Scripts/Issue757_NotasEntregaCabecera.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 1. La CHECK del tipo, con «Cobro» (se rehace si existe y no lo admite; se crea si no existe)
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NotasEntregaCabecera_Tipo'
           AND parent_object_id = OBJECT_ID('dbo.NotasEntregaCabecera') AND definition NOT LIKE '%Cobro%')
BEGIN
    ALTER TABLE dbo.NotasEntregaCabecera DROP CONSTRAINT CK_NotasEntregaCabecera_Tipo;
    PRINT 'CK_NotasEntregaCabecera_Tipo: quitada la antigua (sin Cobro).';
END
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_NotasEntregaCabecera_Tipo'
               AND parent_object_id = OBJECT_ID('dbo.NotasEntregaCabecera'))
BEGIN
    ALTER TABLE dbo.NotasEntregaCabecera WITH CHECK
        ADD CONSTRAINT CK_NotasEntregaCabecera_Tipo CHECK (Tipo IN ('Entrega', 'Recogida', 'Cobro'));
    PRINT 'CK_NotasEntregaCabecera_Tipo: Entrega, Recogida y Cobro.';
END
ELSE
    PRINT 'CK_NotasEntregaCabecera_Tipo: ya admitía Cobro.';
GO

-- 2. El importe a cobrar
IF COL_LENGTH('dbo.NotasEntregaCabecera', 'ImporteCobro') IS NULL
BEGIN
    ALTER TABLE dbo.NotasEntregaCabecera ADD ImporteCobro decimal(12, 2) NULL;
    PRINT 'NotasEntregaCabecera.ImporteCobro: creada.';
END
ELSE
    PRINT 'NotasEntregaCabecera.ImporteCobro: ya existía.';
GO

-- 3. Los efectos que liquida el cobro
IF OBJECT_ID('dbo.NotasEntregaCobroEfectos', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.NotasEntregaCobroEfectos (
        Empresa char(3) NOT NULL,
        Pedido int NOT NULL,                 -- NotasEntregaCabecera.Pedido (Tipo = 'Cobro')
        ExtractoId int NOT NULL,             -- ExtractoCliente.[Nº Orden] (de cualquier empresa)
        Orden smallint NOT NULL,             -- en qué orden se liquidan (1..n)
        CONSTRAINT PK_NotasEntregaCobroEfectos PRIMARY KEY CLUSTERED (Empresa, Pedido, ExtractoId),
        CONSTRAINT FK_NotasEntregaCobroEfectos_Cabecera FOREIGN KEY (Empresa, Pedido)
            REFERENCES dbo.NotasEntregaCabecera (Empresa, Pedido)
    );
    PRINT 'NotasEntregaCobroEfectos: creada.';
END
ELSE
    PRINT 'NotasEntregaCobroEfectos: ya existía.';
GO

-- La API corre con la cuenta máquina de RDS2016 (integrated security). Lee e inserta (no borra ni cambia).
GRANT SELECT, INSERT ON dbo.NotasEntregaCobroEfectos TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO
