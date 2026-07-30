-- =============================================================================
-- NestoAPI#372: GRANTs que faltan en las tablas de PlanesVentajas.
-- ListarPlanesAsync devolvía 500 con "Se denegó el permiso SELECT en el objeto
-- 'PlanVentajasCliente'" (ELMAH 28/07/26, 3x).
--
-- Verificado en prod (sys.database_permissions, 30/07/26):
--   - EstadosPlanVentajas: RDS2016$ ya tiene SELECT (el patrón bueno).
--   - PlanesVentajas y PlanVentajasCliente: SIN ningún GRANT para RDS2016$.
--
-- Permisos según lo que usa PlanesVentajasService:
--   - PlanesVentajas: SELECT (listar/obtener), INSERT (CrearPlanAsync),
--     UPDATE (ActualizarPlanAsync).
--   - PlanVentajasCliente: SELECT (ObtenerClientesAsync), INSERT (alta de
--     clientes del plan), DELETE (quitar clientes en ActualizarPlanAsync).
--
-- BD: NestoConnection (NV).  GRANT a [NUEVAVISION\RDS2016$] (cuenta de máquina del servidor).
-- =============================================================================

GRANT SELECT, INSERT, UPDATE ON dbo.PlanesVentajas TO [NUEVAVISION\RDS2016$];
GO

GRANT SELECT, INSERT, DELETE ON dbo.PlanVentajasCliente TO [NUEVAVISION\RDS2016$];
GO
