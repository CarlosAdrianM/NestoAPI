/*
    NestoAPI#776 (03/11/26): la limpieza de códigos de barras no válidos (POST api/Productos/CodigosBarras/LimpiarNoValidos)
    borra las filas no válidas de ProductosCodigosBarras y la cuenta con la que corre la API no tenía DELETE
    («Se denegó el permiso DELETE en el objeto 'ProductosCodigosBarras'»; 0 de 2.760 limpiados, todo revertido).
    Lanzar como sa en NV y repetir la limpieza.
*/
USE NV;
GRANT DELETE ON dbo.ProductosCodigosBarras TO [NUEVAVISION\RDS2016$];
SELECT p.permission_name, p.state_desc, pr.name
FROM sys.database_permissions p JOIN sys.database_principals pr ON pr.principal_id = p.grantee_principal_id
WHERE p.major_id = OBJECT_ID('dbo.ProductosCodigosBarras') AND pr.name = 'NUEVAVISION\RDS2016$';
