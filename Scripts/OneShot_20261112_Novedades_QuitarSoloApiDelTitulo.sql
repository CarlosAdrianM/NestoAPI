/*
    12/11/26: tres novedades de la 1.10.61.0 salieron con la nota interna «(solo API)» en el título (era del borrador; fallo de Claude).
    Se quita del título. No hay endpoint para editar una novedad que no es sugerencia (PUT api/Novedades/Sugerencias/{id} da 404).
    Lanzar como sa en NV. Idempotente. Novedades_1_10_61_0.sql ya está corregido (relanzarlo no duplica).
*/
SET NOCOUNT ON;
USE NV;
UPDATE dbo.Novedades
SET Titulo = RTRIM(REPLACE(Titulo, N' (solo API)', N''))
WHERE Id IN (1240, 1242, 1243) AND Version = '1.10.61.0' AND Titulo LIKE N'% (solo API)';
SELECT @@ROWCOUNT AS TitulosCorregidos;
SELECT Id, Titulo FROM dbo.Novedades WHERE Id IN (1240, 1242, 1243);
