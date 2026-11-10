/*
    NestoAPI#828 (decisión de Carlos del 10/11/26): la carta del cheque regalo (#838) que sale con algunas facturas en el packing
    lleva su propia marca en dbo.DocumentosPapelPacking (Tipo 'CartaCheque', Documento = el número de la factura que generó el
    cheque). Así el reintento y F8 repetido imprimen solo lo que no salió: si no salió ninguna, la factura y la carta; si solo
    una, la otra.

    Este script amplía la CHECK de Tipo (CK_DocumentosPapelPacking_Tipo) con 'CartaCheque'. Lanzar como sa en NV, antes o después
    de publicar la API: sin él la API no intenta guardar la marca de la carta (la carta se comporta como antes: sale con la
    factura, pero el reintento no la vuelve a imprimir). Idempotente: si la CHECK ya admite 'CartaCheque', no hace nada.
    Requiere Scripts/Issue828_DocumentosPapelPacking.sql (la tabla).

    Deshacer (solo si no hay filas 'CartaCheque'):
        ALTER TABLE dbo.DocumentosPapelPacking DROP CONSTRAINT CK_DocumentosPapelPacking_Tipo;
        ALTER TABLE dbo.DocumentosPapelPacking WITH CHECK ADD CONSTRAINT CK_DocumentosPapelPacking_Tipo
            CHECK (Tipo IN ('Factura', 'Albaran', 'NotaEntrega'));
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.DocumentosPapelPacking', N'U') IS NULL
    PRINT N'No existe dbo.DocumentosPapelPacking: lance antes Scripts/Issue828_DocumentosPapelPacking.sql (ya la crea con CartaCheque).';
ELSE IF EXISTS (SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.DocumentosPapelPacking') AND name = N'CK_DocumentosPapelPacking_Tipo'
                  AND definition LIKE N'%''CartaCheque''%')
    PRINT N'CK_DocumentosPapelPacking_Tipo ya admite CartaCheque: nada que hacer.';
ELSE
BEGIN
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    IF EXISTS (SELECT 1 FROM sys.check_constraints
               WHERE parent_object_id = OBJECT_ID(N'dbo.DocumentosPapelPacking') AND name = N'CK_DocumentosPapelPacking_Tipo')
        ALTER TABLE dbo.DocumentosPapelPacking DROP CONSTRAINT CK_DocumentosPapelPacking_Tipo;
    ALTER TABLE dbo.DocumentosPapelPacking WITH CHECK ADD CONSTRAINT CK_DocumentosPapelPacking_Tipo
        CHECK (Tipo IN ('Factura', 'Albaran', 'NotaEntrega', 'CartaCheque'));
    COMMIT TRANSACTION;
    PRINT N'CK_DocumentosPapelPacking_Tipo ampliada con CartaCheque.';
END
GO
-- Comprobación: la definición de la CHECK (tiene que salir 'CartaCheque')
SELECT name, definition FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID(N'dbo.DocumentosPapelPacking') AND name = N'CK_DocumentosPapelPacking_Tipo';
