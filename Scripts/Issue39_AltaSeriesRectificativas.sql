-- =============================================================================
-- Issue #39 — Alta de las series rectificativas RV y RC en la tabla Series
-- =============================================================================
-- EJECUTAR ANTES DE DESPLEGAR el cambio de GestorCopiaPedidos que asigna serie
-- RV/RC a las rectificativas por copia: sin estas filas, prdCrearFacturaVta no
-- tiene contador para numerar la primera factura RV/RC y la facturación falla.
-- Solo empresa 1 (las rectificativas de NV/CV/EV/UL; GB no tiene asociada).
-- Idempotente.
-- =============================================================================

SET NOCOUNT ON;

IF NOT EXISTS (SELECT 1 FROM Series WHERE Empresa = '1' AND Número = 'RV')
    INSERT INTO Series (Empresa, Número, Descripción, [Factura Inicial], Contador, NoComprobarFacturaPosterior)
    VALUES ('1', 'RV', 'Rectificativas de venta', '2600000', '2600000', 0);

IF NOT EXISTS (SELECT 1 FROM Series WHERE Empresa = '1' AND Número = 'RC')
    INSERT INTO Series (Empresa, Número, Descripción, [Factura Inicial], Contador, NoComprobarFacturaPosterior)
    VALUES ('1', 'RC', 'Rectificativas de cursos', '2600000', '2600000', 0);

SELECT RTRIM(Empresa) Emp, RTRIM(Número) Serie, RTRIM(Descripción) Descr, RTRIM([Factura Inicial]) FacturaInicial, RTRIM(Contador) Contador
FROM Series WHERE Empresa = '1' AND Número IN ('RV', 'RC');
