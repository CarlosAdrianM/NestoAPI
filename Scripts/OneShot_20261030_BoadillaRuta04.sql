/*
    30/10/26: la lista de días de reparto de Alfredo pone Boadilla en JUEVES (04, N-VI); el Excel no la traía.
    Rehecho tras el error de concepto: 01-05 NO es la ruta del cliente ni del CP (siguen en su ruta, FW = agencia),
    solo el DÍA en que saldría por nuestra ruta. Por eso solo apunta el día en dbo.CodigosPostalesDiaReparto
    (la crea OneShot_20261030_DeshacerRutas01a05.sql: lanzar ese primero).
    CP 28660 BOADILLA DEL MONTE, 28668 MONTEPRINCIPE - BOADILLA, 28669 LAS LOMAS-BOADILLA → 04.
    Pendiente de confirmar con Manuel y Alfredo. Lanzar como sa. @Aplicar = 0 solo enseña.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @Aplicar bit = 0;

SELECT * FROM dbo.CodigosPostalesDiaReparto WHERE Empresa = '1' AND CodigoPostal IN ('28660', '28668', '28669');

IF @Aplicar = 1
BEGIN
    MERGE dbo.CodigosPostalesDiaReparto AS d
    USING (VALUES ('1  ', '28660'), ('1  ', '28668'), ('1  ', '28669')) AS s (Empresa, CodigoPostal)
       ON d.Empresa = s.Empresa AND d.CodigoPostal = s.CodigoPostal
    WHEN MATCHED AND d.Ruta <> '04' THEN UPDATE SET Ruta = '04', FechaModificacion = GETDATE()
    WHEN NOT MATCHED THEN INSERT (Empresa, CodigoPostal, Ruta) VALUES (s.Empresa, s.CodigoPostal, '04');
    PRINT CONCAT('Filas: ', @@ROWCOUNT);
END
ELSE
    PRINT 'Solo lectura (@Aplicar = 0). Nada modificado.';
