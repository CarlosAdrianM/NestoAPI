-- =============================================================================
-- Issue #390 — Slice 0 PARTE 2: contabilizar el 30/06 pendiente + extorno 01/07
-- =============================================================================
-- Contexto: la Parte 1 (Issue390_Slice0_ReclasificacionBalance3006.sql) insertó
-- los asientos del 30/06/26 en el diario 'Carlos' pero prdContabilizar falló
-- porque junio estaba cerrado. El extorno del 01/07 se insertaba DESPUÉS de esa
-- llamada, así que nunca llegó a crearse.
--
-- ANTES de ejecutar: abrir las fechas de junio. Al terminar se pueden volver a
-- cerrar (el extorno va con fecha 01/07).
--
-- Qué hace:
--   1) Recalcula los saldos igual que la Parte 1 y comprueba que casan con las
--      filas pendientes del diario (junio cerrado = no ha podido cambiar nada).
--   2) Contabiliza el 30/06 pendiente (si ya lo contabilizaste tú desde el
--      diario de Nesto, lo detecta y salta este paso).
--   3) Inserta el extorno del 01/07/26 (espejo exacto) con Liquidado apuntando
--      a la partida de cartera del 999, y lo contabiliza.
--   4) Verificaciones finales.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- -------------------------------------------------------------------------
-- Guardas
-- -------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Contabilidad WHERE [Nº Documento] = 'RECLAS3006' AND Fecha = '20260701')
    THROW 50000, 'El extorno del 01/07 ya está contabilizado: no hay nada que hacer.', 1;
IF EXISTS (SELECT 1 FROM PreContabilidad WHERE RTRIM(Diario) = 'Carlos' AND ISNULL([Nº Documento],'') <> 'RECLAS3006')
    THROW 50001, 'Hay filas ajenas en el diario Carlos: prdContabilizar las barrería. Vaciar antes.', 1;

-- -------------------------------------------------------------------------
-- Mapa y saldos (idénticos a la Parte 1)
-- -------------------------------------------------------------------------
DECLARE @map TABLE (Cuenta555 char(10), Cuenta440 char(10) NULL, Market varchar(20), Tipo char(1));
INSERT INTO @map (Cuenta555, Cuenta440, Market, Tipo) VALUES
('55500047', '44000047', 'Amazon.es',     'P'),
('55500045', '44000045', 'Amazon.fr',     'P'),
('55500048', '44000048', 'Amazon.it',     'P'),
('55500046', '44000046', 'Amazon.de',     'P'),
('55500049', '44000049', 'Amazon.co.uk',  'P'),
('55500050', '44000050', 'Amazon.nl',     'P'),
('55500072', '44000072', 'Amazon.se',     'P'),
('55500080', '44000080', 'Amazon.tr',     'P'),
('55500075', '44000075', 'Amazon.com.be', 'P'),
('55500039', '44000039', 'Amazon.pl',     'P'),
('55500082', '44000082', 'Amazon.ie',     'P'),
('55500084', '44000084', 'Amazon.ae',     'P'),
('55500087', '44000087', 'Amazon.sa',     'P'),
('55500062', NULL,       'Amazon.es',     'C'),
('55500064', NULL,       'Amazon.fr',     'C'),
('55500063', NULL,       'Amazon.it',     'C'),
('55500065', NULL,       'Amazon.de',     'C'),
('55500066', NULL,       'Amazon.co.uk',  'C'),
('55500069', NULL,       'Amazon.nl',     'C'),
('55500073', NULL,       'Amazon.se',     'C'),
('55500081', NULL,       'Amazon.tr',     'C'),
('55500076', NULL,       'Amazon.com.be', 'C'),
('55500038', NULL,       'Amazon.pl',     'C'),
('55500083', NULL,       'Amazon.ie',     'C'),
('55500085', NULL,       'Amazon.ae',     'C'),
('55500088', NULL,       'Amazon.sa',     'C');

-- El saldo a reclasificar NO debe incluir el propio RECLAS3006 (por si el 30/06
-- ya está contabilizado cuando se ejecuta esta parte).
DECLARE @saldos TABLE (Cuenta555 char(10), Cuenta440 char(10) NULL, Market varchar(20), Tipo char(1), Saldo money);
INSERT INTO @saldos (Cuenta555, Cuenta440, Market, Tipo, Saldo)
SELECT m.Cuenta555, m.Cuenta440, m.Market, m.Tipo, SUM(c.Debe - c.Haber)
FROM @map m
JOIN Contabilidad c ON c.Empresa = '1' AND c.[Nº Cuenta] = m.Cuenta555
WHERE c.Fecha >= '20260101' AND c.Fecha < '20260701'
  AND ISNULL(RTRIM(c.[Nº Documento]),'') <> 'RECLAS3006'
GROUP BY m.Cuenta555, m.Cuenta440, m.Market, m.Tipo
HAVING SUM(c.Debe - c.Haber) <> 0;

DECLARE @netoComisiones money = ISNULL((SELECT SUM(Saldo) FROM @saldos WHERE Tipo = 'C'), 0);

SELECT 'Saldos (deben coincidir con la Parte 1)' AS Paso, Tipo, RTRIM(Cuenta555) AS Cuenta555,
       RTRIM(ISNULL(Cuenta440,'-> prov 999')) AS Destino, Market, Saldo
FROM @saldos ORDER BY Tipo, Cuenta555;

-- -------------------------------------------------------------------------
-- Paso 1: contabilizar el 30/06 pendiente (o detectar que ya está hecho)
-- -------------------------------------------------------------------------
DECLARE @resultado int;
IF EXISTS (SELECT 1 FROM PreContabilidad WHERE [Nº Documento] = 'RECLAS3006' AND Fecha = '20260630')
BEGIN
    -- Sanity: en la pata 555 de cada cuenta, Haber - Debe debe ser exactamente el saldo
    IF EXISTS (
        SELECT s.Cuenta555
        FROM @saldos s
        JOIN PreContabilidad p ON p.[Nº Documento] = 'RECLAS3006' AND p.Fecha = '20260630' AND p.[Nº Cuenta] = s.Cuenta555
        GROUP BY s.Cuenta555, s.Saldo
        HAVING SUM(p.Haber - p.Debe) <> s.Saldo
    )
        THROW 50002, 'Las filas pendientes del 30/06 no casan con los saldos recalculados: revisar antes de contabilizar.', 1;

    EXEC @resultado = prdContabilizar '1', 'Carlos', 'Carlos';
    IF @resultado < 0
        THROW 50003, 'prdContabilizar devolvió error en el asiento del 30/06. ¿Está junio abierto?', 1;
    PRINT 'Contabilizado 30/06. Último asiento: ' + CAST(@resultado AS varchar(10));
END
ELSE IF EXISTS (SELECT 1 FROM Contabilidad WHERE [Nº Documento] = 'RECLAS3006' AND Fecha = '20260630')
    PRINT 'El 30/06 ya estaba contabilizado: se continúa con el extorno.';
ELSE
    THROW 50004, 'No hay asiento del 30/06 ni pendiente ni contabilizado: ejecutar antes la Parte 1.', 1;

-- -------------------------------------------------------------------------
-- Paso 2: extorno a 01/07/26 con Liquidado a la partida de cartera del 999
-- -------------------------------------------------------------------------
DECLARE @idCartera999 int =
    (SELECT TOP 1 NºOrden FROM ExtractoProveedor
     WHERE Empresa = '1' AND Número = '999' AND Contacto = '0' AND NºDocumento = 'RECLAS3006'
     ORDER BY NºOrden DESC);
IF @idCartera999 IS NULL AND @netoComisiones <> 0
    PRINT 'AVISO: no se encontró la partida RECLAS3006 en ExtractoProveedor; el extorno se contabilizará sin liquidar (compensar a mano en Nesto).';

BEGIN TRAN;

INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Concepto, Debe, Haber, Fecha, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
SELECT '1', '3', '1', s.Cuenta555, LEFT('Extorno reclas. 30/06 pagos ' + s.Market, 50),
       CASE WHEN s.Saldo > 0 THEN s.Saldo ELSE 0 END,
       CASE WHEN s.Saldo < 0 THEN -s.Saldo ELSE 0 END,
       '20260701', 'RECLAS3006', 1, 'Carlos', 0, 'ALG', 'VAR', 'Carlos'
FROM @saldos s WHERE s.Tipo = 'P';

INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Concepto, Debe, Haber, Fecha, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
SELECT '1', '3', '1', s.Cuenta440, LEFT('Extorno reclas. 30/06 pagos ' + s.Market, 50),
       CASE WHEN s.Saldo < 0 THEN -s.Saldo ELSE 0 END,
       CASE WHEN s.Saldo > 0 THEN s.Saldo ELSE 0 END,
       '20260701', 'RECLAS3006', 1, 'Carlos', 0, 'ALG', 'VAR', 'Carlos'
FROM @saldos s WHERE s.Tipo = 'P';

INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Concepto, Debe, Haber, Fecha, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
SELECT '1', '3', '1', s.Cuenta555, LEFT('Extorno reclas. 30/06 comisiones ' + s.Market, 50),
       CASE WHEN s.Saldo > 0 THEN s.Saldo ELSE 0 END,
       CASE WHEN s.Saldo < 0 THEN -s.Saldo ELSE 0 END,
       '20260701', 'RECLAS3006', 2, 'Carlos', 0, 'ALG', 'VAR', 'Carlos'
FROM @saldos s WHERE s.Tipo = 'C';

IF @netoComisiones <> 0
    INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Contacto, Concepto, Debe, Haber, Fecha, FechaVto, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Liquidado, Usuario)
    VALUES ('1', '3', '3', '999', '0', 'Extorno reclas. 30/06 comisiones Amazon (c/c)',
            CASE WHEN @netoComisiones < 0 THEN -@netoComisiones ELSE 0 END,
            CASE WHEN @netoComisiones > 0 THEN @netoComisiones ELSE 0 END,
            '20260701', '20260701', 'RECLAS3006', 2, 'Carlos', 0, 'ALG', 'VAR', @idCartera999, 'Carlos');

IF EXISTS (SELECT Asiento FROM PreContabilidad WHERE RTRIM(Diario) = 'Carlos' GROUP BY Asiento HAVING SUM(Debe) <> SUM(Haber))
BEGIN
    ROLLBACK;
    THROW 50005, 'El extorno no cuadra. OJO: el asiento del 30/06 YA está contabilizado; resolver a mano.', 1;
END;

COMMIT;

EXEC @resultado = prdContabilizar '1', 'Carlos', 'Carlos';
IF @resultado < 0
    THROW 50006, 'prdContabilizar devolvió error en el extorno. OJO: el 30/06 YA está contabilizado.', 1;
PRINT 'Extorno contabilizado. Último asiento: ' + CAST(@resultado AS varchar(10));

-- -------------------------------------------------------------------------
-- Verificaciones finales
-- -------------------------------------------------------------------------
-- 1) Las 555 de Amazon deben quedar a CERO a 30/06
SELECT 'Saldo 555 a 30/06 (debe ser 0)' AS Chequeo, RTRIM(c.[Nº Cuenta]) AS Cuenta, SUM(c.Debe - c.Haber) AS Saldo
FROM Contabilidad c JOIN @map m ON c.[Nº Cuenta] = m.Cuenta555
WHERE c.Empresa = '1' AND c.Fecha >= '20260101' AND c.Fecha < '20260701'
GROUP BY c.[Nº Cuenta] HAVING SUM(c.Debe - c.Haber) <> 0;

-- 2) Las 440 a 30/06 deben tener el saldo que tenía su 555
SELECT 'Saldo 440 a 30/06' AS Chequeo, RTRIM(c.[Nº Cuenta]) AS Cuenta, SUM(c.Debe - c.Haber) AS Saldo
FROM Contabilidad c JOIN @map m ON c.[Nº Cuenta] = m.Cuenta440
WHERE c.Empresa = '1' AND c.Fecha >= '20260101' AND c.Fecha < '20260701'
GROUP BY c.[Nº Cuenta];

-- 3) Todo el documento RECLAS3006 debe netear a cero en total
SELECT 'Neto RECLAS3006 (debe ser 0)' AS Chequeo, SUM(Debe) AS Debe, SUM(Haber) AS Haber
FROM Contabilidad WHERE [Nº Documento] = 'RECLAS3006';

-- 4) Cartera del 999: las dos partidas RECLAS3006 y su ImportePdte
--    (si Liquidado funcionó, ambas a 0; si no, compensarlas en Nesto)
SELECT 'Cartera 999 RECLAS3006' AS Chequeo, NºOrden, Fecha, RTRIM(Concepto) AS Concepto, Importe, ImportePdte
FROM ExtractoProveedor
WHERE Empresa = '1' AND Número = '999' AND NºDocumento = 'RECLAS3006';
