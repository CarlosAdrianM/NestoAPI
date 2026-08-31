/*
    GRANTs que le faltan a la API.  31/08/2026.

    EJECUTAR ANTES (o justo despues) DE PUBLICAR. Sin esto:
      - El mantenimiento de familias sigue fallando (ya fallo).
      - La pestana de Campanas, que se publica hoy, MUERE en el primer guardado.

    -------------------------------------------------------------------------------------
    1) dbo.Familias  -->  UPDATE
    -------------------------------------------------------------------------------------
    Error real en ELMAH, 31/08/2026 13:25, usuario NUEVAVISION\Laura:

        Se denego el permiso UPDATE en el objeto 'Familias', base de datos 'NV', esquema 'dbo'.
        NestoAPI.Controllers.FamiliasController.PutFamilia, linea 91

    Es el mantenimiento de familias de #406 (marcar "publico = profesional"). La API venia
    LEYENDO Familias desde siempre, pero nunca la habia ESCRITO: cuando #406 anadio el endpoint,
    el permiso no se penso porque la tabla ya existia.

    -------------------------------------------------------------------------------------
    2) dbo.DescuentosProducto  -->  INSERT, UPDATE, DELETE
    -------------------------------------------------------------------------------------
    ESTE NO HA FALLADO TODAVIA, y por eso importa: CampanasController es el UNICO sitio del
    repositorio que escribe en DescuentosProducto, y es de hoy. La API llevaba anos leyendo esa
    tabla para calcular precios y no la habia escrito nunca.

    Sin este GRANT, la pestana de Campanas que se publica hoy falla en cuanto alguien pulse
    Guardar, Cerrar campana o Borrar campana — exactamente el mismo error que el de Familias.

    -------------------------------------------------------------------------------------
    3) dbo.OfertasPermitidas  -->  INSERT, UPDATE, DELETE   (probablemente ya estan)
    -------------------------------------------------------------------------------------
    La pestana nueva de "Ofertas de Producto" escribe aqui, pero no estrena nada: el controller
    de ofertas por familia ya lo hacia y funciona, asi que el permiso deberia estar. Se incluye
    igualmente porque GRANT es idempotente y sale mas barato que descubrirlo en caliente.

    -------------------------------------------------------------------------------------
    A QUIEN SE LE DA
    -------------------------------------------------------------------------------------
    [NUEVAVISION\RDS2016$] es la CUENTA MAQUINA del servidor con la que la API se conecta por
    integrated security. NO es [ABORRAR\sqlServerSvc] (eso fue un error del pasado y falla con
    "Mens. 15151, no se puede buscar el usuario").

    Ejecutar en SSMS contra NV con una cuenta privilegiada: el login `nuevavision` de diagnostico
    puede leer pero NO puede aplicar GRANTs.
*/

SET NOCOUNT ON;
USE NV;
GO

GRANT UPDATE ON dbo.Familias TO [NUEVAVISION\RDS2016$];
PRINT 'Familias: UPDATE concedido.';

GRANT INSERT, UPDATE, DELETE ON dbo.DescuentosProducto TO [NUEVAVISION\RDS2016$];
PRINT 'DescuentosProducto: INSERT, UPDATE, DELETE concedidos.';

GRANT INSERT, UPDATE, DELETE ON dbo.OfertasPermitidas TO [NUEVAVISION\RDS2016$];
PRINT 'OfertasPermitidas: INSERT, UPDATE, DELETE concedidos.';
GO

-- =============================================================================
-- COMPROBACION
-- =============================================================================
PRINT '--- Permisos de la cuenta de la API sobre las tres tablas ---';
SELECT Tabla = OBJECT_NAME(p.major_id),
       p.permission_name,
       p.state_desc
FROM sys.database_permissions p
INNER JOIN sys.database_principals u ON u.principal_id = p.grantee_principal_id
WHERE u.name = 'NUEVAVISION\RDS2016$'
  AND OBJECT_NAME(p.major_id) IN ('Familias', 'DescuentosProducto', 'OfertasPermitidas')
ORDER BY Tabla, p.permission_name;

/*
    QUE DEBE SALIR
    --------------
      Familias ............. al menos SELECT y UPDATE
      DescuentosProducto ... al menos SELECT, INSERT, UPDATE, DELETE
      OfertasPermitidas .... al menos SELECT, INSERT, UPDATE, DELETE

    Si la consulta sale VACIA no significa necesariamente que no haya permisos: pueden venir
    heredados de un rol de base de datos en vez de concedidos a la cuenta. La prueba que vale es
    la de verdad: marcar una familia desde Herramientas y guardar una campana desde la pestana
    nueva.

    POR QUE PASA ESTO UNA Y OTRA VEZ
    --------------------------------
    En ELMAH hay 19 incidentes distintos de "se denego el permiso" en los ultimos 10 meses
    (SyncMessageRetries, CabAlquileres, CodigosPostales, VideosProductos, PrestashopProductos,
    EnviosAgencia, varios prd*...). Siempre el mismo patron: un endpoint nuevo toca una tabla o un
    SP que la API no habia tocado ANTES CON ESE VERBO, y el permiso se descubre en produccion.

    La regla que teniamos escrita ("GRANT solo para objetos NUEVOS") es la que deja el hueco: el
    problema no es que la TABLA sea nueva, es que el VERBO lo sea. Una tabla de toda la vida que
    la API solo leia necesita GRANT el dia que se le escribe por primera vez.
*/
