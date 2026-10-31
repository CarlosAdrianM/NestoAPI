-- 28/09/26: clientes dados de alta desde NestoApp con la calle y el número sin coma (la app los juntaba con un
-- espacio; desde la API del 28/09 se corrige al guardar). Ejecutar como sa en NV. Solo toca la fila si la
-- dirección sigue exactamente igual que el 28/09.
USE NV;
GO
UPDATE Clientes SET Dirección = 'PºCHOPERA, 204'          WHERE Empresa = '1' AND [Nº Cliente] = '41966' AND Contacto = '0' AND RTRIM(Dirección) = 'PºCHOPERA 204';
UPDATE Clientes SET Dirección = 'C/ AZUCENA, 7'           WHERE Empresa = '1' AND [Nº Cliente] = '41919' AND Contacto = '0' AND RTRIM(Dirección) = 'C/ AZUCENA 7';
UPDATE Clientes SET Dirección = 'C/ ALBARICOQUE, 13'      WHERE Empresa = '1' AND [Nº Cliente] = '41879' AND Contacto = '0' AND RTRIM(Dirección) = 'C/ ALBARICOQUE 13';
UPDATE Clientes SET Dirección = 'VIA DANTE ALIGHIERI, 147' WHERE Empresa = '1' AND [Nº Cliente] = '41866' AND Contacto = '0' AND RTRIM(Dirección) = 'VIA DANTE ALIGHIERI 147';
GO
SELECT RTRIM([Nº Cliente]) Cliente, RTRIM(Dirección) Direccion FROM Clientes WHERE Empresa = '1' AND Contacto = '0' AND [Nº Cliente] IN ('41966','41919','41879','41866');
