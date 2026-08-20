-- Verifactu #39 (20/08/26): el flujo de rectificativas en serie RV/RC LEE la tabla
-- LinFacturaVtaRectificacion desde la API (cuenta de máquina) en tres puntos: al copiar
-- (guardar vinculaciones comprueba duplicados), al facturar a mano (LIFO de #38) y al
-- declarar a Verifactu (CargarFacturasRectificadas). La tabla solo tenía INSERT para la
-- cuenta del API y ni siquiera SELECT para public → "Se denegó el permiso SELECT en el
-- objeto 'LinFacturaVtaRectificacion'" (ELMAH 20/08 10:55 y 11:15; la RV2600001 se quedó
-- sin vinculaciones y sin declarar).
-- EJECUTAR EN LA BD NV (NestoConnection → la cuenta del app pool es la de máquina).
GRANT SELECT ON dbo.LinFacturaVtaRectificacion TO [NUEVAVISION\RDS2016$];
