-- OneShot 05/11/26 (URGENTE, prueba del inventario): la cuenta con la que escribe la API (NUEVAVISION\RDS2016$) no tiene
-- EXECUTE en varios procedimientos que la API llama. Hoy ha fallado el inventario desde Ariadna (Alfredo y Andre, ELMAH 11:07-11:13:
-- «Se denegó el permiso EXECUTE en el objeto 'prdUbicacionesParaInventario'» y sugerencia 1055: «…'prdContabilizarInventario'»).
-- Auditoría (05/11/26): de los 52 SPs que referencia el código de la API, estos 10 no tienen EXECUTE ni para RDS2016$ ni para public.
-- Los demás ya lo tienen. Ejecutar como sa en NV. Idempotente.
USE NV;
GO
SET NOCOUNT ON;

GRANT EXECUTE ON dbo.prdUbicacionesParaInventario TO [NUEVAVISION\RDS2016$];          -- Ariadna: inventario por ubicación (#784/#795)
GRANT EXECUTE ON dbo.prdContabilizarInventario TO [NUEVAVISION\RDS2016$];             -- Contabilizar inventario (#790)
GRANT EXECUTE ON dbo.prdDeshacerInventario TO [NUEVAVISION\RDS2016$];                 -- Deshacer inventario
GRANT EXECUTE ON dbo.prdAgruparAlbaranesVta TO [NUEVAVISION\RDS2016$];                -- Fin de mes por la API (1.10.51.0)
GRANT EXECUTE ON dbo.prdInsertarLineaCmp TO [NUEVAVISION\RDS2016$];                   -- Pedidos de compra
GRANT EXECUTE ON dbo.prdComprobarCamposNecesarios TO [NUEVAVISION\RDS2016$];
GRANT EXECUTE ON dbo.prdActualizarClasificacionProductos TO [NUEVAVISION\RDS2016$];
GRANT EXECUTE ON dbo.prdActualizarPrecioMedioProductoNuevo TO [NUEVAVISION\RDS2016$];
GRANT EXECUTE ON dbo.prdActualizarPreciosMediosDeTodosLosProductos TO [NUEVAVISION\RDS2016$];
GRANT EXECUTE ON dbo.prdLLamarActualizarPrecioMedioProducto TO [NUEVAVISION\RDS2016$];

-- Comprobación: deben salir los 10 con 'SI'
DECLARE @sps TABLE (n sysname);
INSERT @sps VALUES ('prdUbicacionesParaInventario'),('prdContabilizarInventario'),('prdDeshacerInventario'),('prdAgruparAlbaranesVta'),
    ('prdInsertarLineaCmp'),('prdComprobarCamposNecesarios'),('prdActualizarClasificacionProductos'),('prdActualizarPrecioMedioProductoNuevo'),
    ('prdActualizarPreciosMediosDeTodosLosProductos'),('prdLLamarActualizarPrecioMedioProducto');
SELECT s.n SP,
       CASE WHEN EXISTS (SELECT 1 FROM sys.database_permissions pe
                         JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
                         WHERE pe.major_id = OBJECT_ID('dbo.' + s.n) AND pe.permission_name = 'EXECUTE' AND pe.state = 'G'
                           AND pr.name = 'NUEVAVISION\RDS2016$') THEN 'SI' ELSE 'NO' END ApiPuede
FROM @sps s ORDER BY s.n;
GO
