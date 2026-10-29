-- 29/10/26: la inscripción 10 (cliente 22709, Masterclass Rosácea) la creó la señal de un cobro con enlace de tarjeta
-- (PagoTPV 970, apunte 3061003 TAR) y quedó con Origen «Transferencia». Desde NestoAPI#611 (29/10/26) ese origen es «Cobro».
-- La FormaPago ya se corrigió con OneShot_20261029_Inscripcion10_FormaPagoTarjeta.sql.
-- Lanzar como sa en NV DESPUÉS de Issue611_CK_Origen_Cobro.sql (sin él, la restricción CHECK rechaza 'Cobro').
-- Origen es varchar(20) con el texto del enum (Issue611_EventosInscripciones.sql): 'Nesto', 'NestoApp', ..., 'Transferencia'.
SET NOCOUNT ON;
SELECT definition FROM sys.check_constraints WHERE name = 'CK_EventosInscripciones_Origen';
SELECT Id, Cliente, Origen, FormaPago, Estado FROM dbo.EventosInscripciones WHERE Id = 10;
UPDATE dbo.EventosInscripciones SET Origen = 'Cobro' WHERE Id = 10 AND Origen = 'Transferencia';
SELECT Id, Cliente, Origen, FormaPago, Estado FROM dbo.EventosInscripciones WHERE Id = 10;
