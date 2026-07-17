-- NestoAPI#318: reparación de albaranes "partidos" entre empresas.
--
-- CAUSA (corregida en código el 17/07/26): TraspasarPedidoAEmpresa movía las líneas
-- albaranadas (con su [Nº Albarán]) a la empresa espejo pero dejaba la cabecera
-- CabAlbaránVta en la empresa origen. Resultado: cabecera en la 1, líneas en la 3.
-- A fecha 17/07/26: 763 albaranes huérfanos (543 solo de 2026, desde que la
-- facturación de rutas albaranea antes de traspasar; el más antiguo es de 2010).
--
-- ESTE SCRIPT mueve a la empresa 3 las cabeceras de albarán de la empresa 1 cuyas
-- líneas viven en la 3. Es CONSERVADOR:
--   - No toca cabeceras que alguna línea de la empresa 1 siga referenciando.
--   - No toca números que ya existan como albarán propio de la empresa 3 (302
--     duplicados legacy entre empresas; esos requieren revisión manual).
-- El contador de albaranes es GLOBAL (ContadoresGlobales.Albaranes), así que mover
-- la cabecera no puede chocar con números futuros.
--
-- USO: ejecutar en SSMS con login admin. Primero el PREVIEW; si cuadra (~763 menos
-- los conflictos), ejecutar el bloque de UPDATE. Transaccional.

-- ============ PREVIEW (solo lectura) ============
SELECT c.Número AS Albaran, c.Fecha, c.[Nº Cliente] AS Cliente,
       (SELECT COUNT(*) FROM LinPedidoVta l WHERE l.Empresa = '3' AND l.[Nº Albarán] = c.Número) AS LineasEn3
FROM CabAlbaránVta c
WHERE c.Empresa = '1'
  AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = '3' AND l.[Nº Albarán] = c.Número)
  AND NOT EXISTS (SELECT 1 FROM LinPedidoVta lo WHERE lo.Empresa = '1' AND lo.[Nº Albarán] = c.Número)
  AND NOT EXISTS (SELECT 1 FROM CabAlbaránVta d WHERE d.Empresa = '3' AND d.Número = c.Número)
ORDER BY c.Fecha DESC;

-- Conflictos que NO se van a mover (revisión manual): el número ya existe en la 3
SELECT c.Número AS AlbaranConflicto, c.Fecha
FROM CabAlbaránVta c
WHERE c.Empresa = '1'
  AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = '3' AND l.[Nº Albarán] = c.Número)
  AND NOT EXISTS (SELECT 1 FROM LinPedidoVta lo WHERE lo.Empresa = '1' AND lo.[Nº Albarán] = c.Número)
  AND EXISTS (SELECT 1 FROM CabAlbaránVta d WHERE d.Empresa = '3' AND d.Número = c.Número)
ORDER BY c.Fecha DESC;

-- ============ REPARACIÓN (descomentar para ejecutar) ============
/*
BEGIN TRANSACTION;

UPDATE c
SET c.Empresa = '3'
FROM CabAlbaránVta c
WHERE c.Empresa = '1'
  AND EXISTS (SELECT 1 FROM LinPedidoVta l WHERE l.Empresa = '3' AND l.[Nº Albarán] = c.Número)
  AND NOT EXISTS (SELECT 1 FROM LinPedidoVta lo WHERE lo.Empresa = '1' AND lo.[Nº Albarán] = c.Número)
  AND NOT EXISTS (SELECT 1 FROM CabAlbaránVta d WHERE d.Empresa = '3' AND d.Número = c.Número);

-- Verificación: debe quedar solo el residuo de conflictos (los del segundo SELECT del preview)
SELECT COUNT(DISTINCT l.[Nº Albarán]) AS HuerfanosRestantes
FROM LinPedidoVta l
WHERE l.Empresa = '3' AND l.[Nº Albarán] IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM CabAlbaránVta c WHERE c.Empresa = '3' AND c.Número = l.[Nº Albarán]);

COMMIT TRANSACTION;
-- Si algo no cuadra: ROLLBACK TRANSACTION;
*/
