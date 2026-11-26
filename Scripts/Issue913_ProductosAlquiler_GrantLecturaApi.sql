/*
    NestoAPI#913 (26/11/26): las pestañas «Historia» y «Descuadres» de Alquileres (publicadas hoy) LEEN dbo.ProductosAlquiler, pero
    la cuenta de la API no tenía permiso de lectura sobre esa tabla (ELMAH 26/11/26, 18 veces: «Se denegó el permiso SELECT en el
    objeto 'ProductosAlquiler'»). Las demás tablas que usa (CabAlquileres, NumerosSerie, Inmovilizados, LinPedidoCmp, Proveedores,
    ExtractoProducto…) ya las puede leer (comprobado el 26/11/26: por permiso propio o por «public»).

    Lanzar como sa en la BD NV. Se puede lanzar más de una vez. Solo lectura: la API no escribe en esa tabla.
*/
USE NV;
GO
GRANT SELECT ON dbo.ProductosAlquiler TO [NUEVAVISION\RDS2016$];
GO
SELECT pr.name AS Principal, pe.permission_name AS Permiso
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id = OBJECT_ID('dbo.ProductosAlquiler') AND pr.name = 'NUEVAVISION\RDS2016$';
GO
