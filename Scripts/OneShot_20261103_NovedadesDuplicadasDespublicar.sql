/*
    03/11/26: al marcar como implementadas (con su versión) 12 sugerencias de usuarios, quedaron duplicadas con las
    novedades que metió el script de la 1.10.53.0 (Nesto) y de Ariadna 2.26. Regla: la sugerencia implementada ES la
    novedad (conserva autor e hilo), así que se despublican las filas del script. Decisión de Carlos 03/11/26.
    Pares (script → sugerencia): 912→903, 925→866/867, 926→909, 929→902/907, 933→899, 934→900, 935→904, 936→908,
    939→906, 940→901, 941→910. Lanzar como sa en NV.
*/
USE NV;
SELECT Id, Version, Ambito, Publicada, LEFT(Titulo, 70) Titulo FROM dbo.Novedades
WHERE Id IN (912, 925, 926, 929, 933, 934, 935, 936, 939, 940, 941) ORDER BY Id;
UPDATE dbo.Novedades SET Publicada = 0
WHERE Id IN (912, 925, 926, 929, 933, 934, 935, 936, 939, 940, 941) AND Publicada = 1;
PRINT CONCAT('Novedades despublicadas: ', @@ROWCOUNT);
