/*
    NestoAPI#890: las novedades de cada versión se cargan, cambian y borran por la API (POST/GET api/Novedades/Version,
    PUT/DELETE api/Novedades/{id}) en vez de con un Scripts/Novedades_X_Y_Z_R.sql lanzado como sa.

    La cuenta de la API ya tenía SELECT, INSERT y UPDATE en dbo.Novedades (sugerencias, perfiles); le faltaba DELETE para
    DELETE api/Novedades/{id}. Sin este script todo funciona salvo el borrado (500 con «permission denied» en ELMAH).
    Repetir un GRANT no hace nada: se puede lanzar las veces que haga falta.

    Ejecutar contra NV como sa, antes o después de publicar la API.
*/
USE NV;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Novedades TO [NUEVAVISION\RDS2016$];
GO
GRANT INSERT ON dbo.Modificaciones TO [NUEVAVISION\RDS2016$];
GO
