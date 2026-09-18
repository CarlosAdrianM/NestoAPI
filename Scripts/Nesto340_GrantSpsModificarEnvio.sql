-- Nesto#340 (Agencias, slice A4.4): POST api/EnviosAgencias/{id}/ModificarDatos lanza por SQL los dos
-- SPs del extracto que la modificacion de un envio tramitado necesita y que NO estan en el EDMX del
-- servidor: prdDesliquidar (al cambiar el reembolso, si el pago anterior estaba liquidado) y
-- prdModificarEfectoCliente (al rehusar: el efecto de la factura pasa a RHS).
--
-- Comprobado el 18/09/2026 en sys.database_permissions: ninguno de los dos tiene EXECUTE para la cuenta
-- con la que corre NestoAPI (prdContabilizar si: public). Sin este GRANT la llamada falla con Msg 229
-- (permiso EXECUTE denegado) y la transaccion del servidor hace rollback: no se pierde nada, pero el
-- usuario ve el error. Ejecutar como sa ANTES de encender ModificarEnvioPorApi a nadie.
--
-- GRANT a la cuenta de NestoConnection (feedback_scripts_sql_grants_por_bd): [NUEVAVISION\RDS2016$].

USE NV;
GO

GRANT EXECUTE ON dbo.prdDesliquidar TO [NUEVAVISION\RDS2016$];
GRANT EXECUTE ON dbo.prdModificarEfectoCliente TO [NUEVAVISION\RDS2016$];
GO

-- Verificacion
SELECT o.name AS sp, dp.name AS principal, pe.permission_name
FROM sys.database_permissions pe
JOIN sys.objects o ON o.object_id = pe.major_id
JOIN sys.database_principals dp ON dp.principal_id = pe.grantee_principal_id
WHERE o.name IN ('prdDesliquidar', 'prdModificarEfectoCliente')
ORDER BY o.name, dp.name;
