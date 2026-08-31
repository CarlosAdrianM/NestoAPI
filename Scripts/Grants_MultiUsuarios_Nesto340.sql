/*
    GRANT sobre dbo.MultiUsuarios.  31/08/2026.

    EJECUTAR ANTES de publicar el slice de Nesto#340 que migre CargarMultiusuario. Todavia NO
    hace falta: hoy esa tabla la lee Nesto con Entity Framework, con las credenciales Windows del
    usuario, no la cuenta de la API.

    -------------------------------------------------------------------------------------
    POR QUE SE ESCRIBE ANTES DE NECESITARLO
    -------------------------------------------------------------------------------------
    Comprobado el 31/08/2026 contra produccion:

      EnviosHistoria ... SELECT, INSERT, UPDATE, DELETE  -> ya concedidos
      MultiUsuarios .... NADA

    Y la cuenta de la API **no pertenece a ningun rol de base de datos** ni tiene permiso a nivel
    de esquema sobre dbo (solo sobre HangFire). O sea que los permisos van tabla a tabla y lo que
    no este concedido explicitamente, no esta.

    Es el mismo patron que fallo esta manana con Familias y que estuvo a punto de fallar con
    DescuentosProducto: un endpoint nuevo toca una tabla que la API no habia tocado ANTES CON ESE
    VERBO, y el permiso se descubre en produccion. En ELMAH hay 19 incidentes distintos de "se
    denego el permiso" en 10 meses, todos iguales.

    Solo SELECT: CargarMultiusuario unicamente lee.

    -------------------------------------------------------------------------------------
    A QUIEN
    -------------------------------------------------------------------------------------
    [NUEVAVISION\RDS2016$] es la CUENTA MAQUINA del servidor, con la que la API se conecta por
    integrated security. Ejecutar en SSMS contra NV con una cuenta privilegiada: el login
    `nuevavision` de diagnostico lee pero no puede conceder permisos.
*/

SET NOCOUNT ON;
USE NV;
GO

GRANT SELECT ON dbo.MultiUsuarios TO [NUEVAVISION\RDS2016$];
PRINT 'MultiUsuarios: SELECT concedido.';
GO

-- =============================================================================
-- COMPROBACION
-- =============================================================================
SELECT Tabla = OBJECT_NAME(p.major_id), p.permission_name, p.state_desc
FROM sys.database_permissions p
INNER JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
WHERE u.name = 'NUEVAVISION\RDS2016$'
  AND OBJECT_NAME(p.major_id) IN ('MultiUsuarios', 'EnviosHistoria')
ORDER BY Tabla, p.permission_name;

/*
    QUE DEBE SALIR
    --------------
      EnviosHistoria ... SELECT, INSERT, UPDATE, DELETE  (ya estaban)
      MultiUsuarios .... SELECT
*/
