-- NestoAPI#547: apagar el trabajo del SQL Agent «Precios Medios» (domingos 00:30, prdActualizarPreciosMediosDeTodosLosProductos),
-- que bloqueaba LinPedidoVta unas 5 h 20 min cada domingo. Decisión de Carlos (21/10/26): apagarlo del todo.
-- Lo sustituye el incremental diario de Hangfire (precios-medios-incremental, 02:30), y la sombra semanal
-- (precios-medios-sombra, domingos 06:30) sigue comparando y hace de red: 0 diferencias el 04/10, 11/10 y 18/10.
-- Lanzar como sa. Cerrar #547 tras 2-3 sombras limpias con el trabajo apagado.

EXEC msdb.dbo.sp_update_job @job_name = N'Precios Medios', @enabled = 0;
GO

-- Comprobación: enabled debe valer 0.
SELECT name, enabled, date_modified FROM msdb.dbo.sysjobs WHERE name = N'Precios Medios';
GO

-- VUELTA ATRÁS (si hiciera falta):
-- EXEC msdb.dbo.sp_update_job @job_name = N'Precios Medios', @enabled = 1;
