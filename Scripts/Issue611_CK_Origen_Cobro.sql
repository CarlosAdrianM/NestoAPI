/*
    ##################################################################################################################
    ###  LANZAR ANTES DEL DEPLOY DE LA API. Sin esto, la señal que crea una inscripción (origen «Cobro») falla con  ###
    ###  «Instrucción INSERT en conflicto con la restricción CHECK CK_EventosInscripciones_Origen».                   ###
    ##################################################################################################################

    NestoAPI#611 (29/10/26): la inscripción creada desde una señal (cobro sin inscripción previa) ya no se etiqueta
    «Transferencia» (podía ser un enlace de tarjeta o caja), sino «Cobro». CK_EventosInscripciones_Origen (Issue611)
    solo admitía Nesto, NestoApp, TNV, PrestaShop, Telefono y Transferencia. «Transferencia» se queda para las antiguas.

    Ejecutar en SSMS contra NV como sa. Idempotente.
*/
SET NOCOUNT ON;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_EventosInscripciones_Origen' AND definition NOT LIKE '%''Cobro''%')
BEGIN
    ALTER TABLE dbo.EventosInscripciones DROP CONSTRAINT CK_EventosInscripciones_Origen;
    ALTER TABLE dbo.EventosInscripciones WITH CHECK
        ADD CONSTRAINT CK_EventosInscripciones_Origen
        CHECK (Origen IN ('Nesto', 'NestoApp', 'TNV', 'PrestaShop', 'Telefono', 'Transferencia', 'Cobro'));
    PRINT 'CK_EventosInscripciones_Origen: ahora admite Cobro.';
END
ELSE
    PRINT 'CK_EventosInscripciones_Origen ya admite Cobro (o no existe).';

SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_EventosInscripciones_Origen';
