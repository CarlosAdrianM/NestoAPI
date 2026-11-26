/*
    Etiquetas de inmovilizado (25/11/26): la API solo LEE dbo.Inmovilizados, pero su cuenta no tenía permiso de lectura
    (ELMAH 26/11/26 9:07: «Se denegó el permiso SELECT en el objeto 'Inmovilizados'»). Sin él, GET api/Inmovilizados y
    POST api/Inmovilizados/Etiquetas/Imprimir dan 500 y la ventana de Nesto no carga nada.

    Lanzar como sa en la BD NV. Se puede lanzar más de una vez. Solo lectura: la API no escribe en esa tabla.
*/
USE NV;
GO
GRANT SELECT ON dbo.Inmovilizados TO [NUEVAVISION\RDS2016$];
GO
SELECT pr.name AS Principal, pe.permission_name AS Permiso
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.Inmovilizados') AND pr.name = 'NUEVAVISION\RDS2016$';
GO
