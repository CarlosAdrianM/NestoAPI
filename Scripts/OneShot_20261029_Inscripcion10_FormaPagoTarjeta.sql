-- 29/10/26: la inscripción 10 (cliente 22709, Masterclass Rosácea) se pagó con enlace de tarjeta (PagoTPV 970, apunte 3061003 TAR)
-- y quedó con FormaPago «Transferencia» porque entró por el camino de la señal. Solo la etiqueta: factura, liquidación y cupón están bien.
-- Lanzar como sa en NV (comprobar antes cómo se guarda FormaPago: texto del enum).
SELECT Id, FormaPago FROM EventosInscripciones WHERE Id = 10;
UPDATE EventosInscripciones SET FormaPago = 'Tarjeta' WHERE Id = 10 AND FormaPago = 'Transferencia';
SELECT Id, FormaPago FROM EventosInscripciones WHERE Id = 10;
