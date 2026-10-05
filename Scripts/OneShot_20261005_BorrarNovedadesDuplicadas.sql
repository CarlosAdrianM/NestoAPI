/*
    05/10/2026: el script Novedades_1_10_36_3.sql metió dos novedades que ya eran sugerencias de usuarios, ahora
    implementadas en la 1.10.36.3 (se les puso la versión con PUT api/Novedades/Sugerencias): salían repetidas.

      - 487 «Clientes con dirección de fuera de España…»  = sugerencia 482 de Laura (Corregido, 1.10.36.3)
      - 484 «Mandar facturas por correo desde la ficha…»  = sugerencia 483 de Manuel (Nuevo, 1.10.36.3)

    Se borran las dos filas del script (484 y 487), con sus votos y comentarios si los tuvieran, y se dejan las
    sugerencias, que son las que llevan el hilo con el usuario. Ejecutar como sa en NV.
*/

SET NOCOUNT ON;
USE NV;

-- Comprobación previa: 482 y 483 ya tienen versión, y 484/487 son las del script.
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Id IN (482, 483, 484, 487) ORDER BY Id;

BEGIN TRANSACTION;

DELETE FROM NovedadesVotos WHERE NovedadId IN (484, 487);
DELETE FROM NovedadesComentarios WHERE NovedadId IN (484, 487);
DELETE FROM Novedades
WHERE Id IN (484, 487)
  AND Version = '1.10.36.3'
  AND Titulo IN (N'Mandar facturas por correo desde la ficha del cliente',
                 N'Clientes con dirección de fuera de España aunque Google no tenga su código postal');

DECLARE @borradas int = @@ROWCOUNT;
SELECT @borradas AS NovedadesBorradas;   -- debe salir 2

IF @borradas <> 2
BEGIN
    ROLLBACK TRANSACTION;
    PRINT 'No se ha borrado nada: las filas no son las esperadas.';
END
ELSE
    COMMIT TRANSACTION;

-- Resultado: las de la 1.10.36.3 (las dos sugerencias incluidas, sin repetidos).
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.36.3' ORDER BY Id;
