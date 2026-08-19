-- =============================================================================
-- Issue #390 — Slice 0: reclasificación para sacar el balance a 30/06/26
-- =============================================================================
-- Qué hace (todo en el diario 'Carlos', empresa 1, en UNA sola ejecución):
--   0) Alta idempotente de las 13 cuentas 440 (clonando atributos de su 555).
--   1) Asiento 30/06/26: saldo de cada 555 de PAGO -> su 440 espejo (detalle
--      por marketplace) y saldo de cada 555 de COMISIÓN -> proveedor 999
--      (Amazon EU), con una única pata neta de proveedor.
--   2) EXEC prdContabilizar.
--   3) Extorno 01/07/26 (espejo exacto), con Liquidado apuntando a la partida
--      de cartera del 999 creada en el paso 1 para que queden compensadas.
--   4) EXEC prdContabilizar.
-- Solo presentación: no toca resultado ni IVA. Los cuadres de cartera no se
-- ven afectados: la pata de proveedor crea ExtractoProveedor y contabilidad
-- a la vez, y asiento + extorno netean a cero (los SPs suman sin filtro fecha).
-- Idempotencia: aborta si ya existe el documento RECLAS3006 o si hay filas
-- pendientes en el diario Carlos.
-- =============================================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- -------------------------------------------------------------------------
-- Guardas
-- -------------------------------------------------------------------------
IF EXISTS (SELECT 1 FROM Contabilidad WHERE [Nº Documento] = 'RECLAS3006')
    THROW 50000, 'Ya existe RECLAS3006 en Contabilidad: el script ya se ejecutó.', 1;
IF EXISTS (SELECT 1 FROM PreContabilidad WHERE [Nº Documento] = 'RECLAS3006')
    THROW 50001, 'Ya existe RECLAS3006 en PreContabilidad: revisar antes de continuar.', 1;
IF EXISTS (SELECT 1 FROM PreContabilidad WHERE RTRIM(Diario) = 'Carlos')
    THROW 50002, 'Hay filas pendientes en el diario Carlos: prdContabilizar las barrería. Vaciar antes.', 1;

-- -------------------------------------------------------------------------
-- Mapa de cuentas (Tipo P = pago -> 440; Tipo C = comisión -> proveedor 999)
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

-- -------------------------------------------------------------------------
-- Saldos a 30/06/26 (solo cuentas con saldo distinto de cero)
-- -------------------------------------------------------------------------
DECLARE @saldos TABLE (Cuenta555 char(10), Cuenta440 char(10) NULL, Market varchar(20), Tipo char(1), Saldo money);
-- Desde el 01/01/26: el asiento de apertura (_ASIENTCIE, 01/01 00:00:01) arrastra
-- el saldo anterior, así que da lo mismo que sumar desde el principio pero sin
-- recorrer todo el histórico.
INSERT INTO @saldos (Cuenta555, Cuenta440, Market, Tipo, Saldo)
SELECT m.Cuenta555, m.Cuenta440, m.Market, m.Tipo, SUM(c.Debe - c.Haber)
FROM @map m
JOIN Contabilidad c ON c.Empresa = '1' AND c.[Nº Cuenta] = m.Cuenta555
WHERE c.Fecha >= '20260101' AND c.Fecha < '20260701'
GROUP BY m.Cuenta555, m.Cuenta440, m.Market, m.Tipo
HAVING SUM(c.Debe - c.Haber) <> 0;

SELECT 'Saldos a reclasificar (30/06/26)' AS Paso, Tipo, RTRIM(Cuenta555) AS Cuenta555,
       RTRIM(ISNULL(Cuenta440,'-> prov 999')) AS Destino, Market, Saldo
FROM @saldos ORDER BY Tipo, Cuenta555;

DECLARE @netoComisiones money = ISNULL((SELECT SUM(Saldo) FROM @saldos WHERE Tipo = 'C'), 0);

-- -------------------------------------------------------------------------
-- Paso 0: alta idempotente de las 440 (clonando atributos de su 555 espejo).
--         Las 555 no están en PlanGeneralContable, así que las 440 tampoco van.
-- -------------------------------------------------------------------------
-- Alta fila a fila: el trigger trgPlanCuentasUpd no es multi-fila (su bloque
-- "if update(estado)" hace un subquery sin TOP 1 y casca con error 512 si se
-- insertan varias cuentas en una sola instrucción).
DECLARE @c555 char(10), @c440 char(10), @market varchar(20), @altas int = 0;
DECLARE cAlta CURSOR LOCAL FAST_FORWARD FOR
    SELECT m.Cuenta555, m.Cuenta440, m.Market
    FROM @map m
    WHERE m.Tipo = 'P'
      AND NOT EXISTS (SELECT 1 FROM PlanCuentas x WHERE x.Empresa = '1' AND x.[Nº Cuenta] = m.Cuenta440);
OPEN cAlta;
FETCH NEXT FROM cAlta INTO @c555, @c440, @market;
WHILE @@FETCH_STATUS = 0
BEGIN
    INSERT INTO PlanCuentas (Empresa, [Nº Cuenta], Concepto, IVA, DebeHaber, SóloAuto, Bloqueada, Estado, Usuario, [Fecha Modificación])
    SELECT p.Empresa, @c440, LEFT(@market + ' liquidac. ptes.', 50), p.IVA, p.DebeHaber, p.SóloAuto, p.Bloqueada, p.Estado, 'Carlos', GETDATE()
    FROM PlanCuentas p
    WHERE p.Empresa = '1' AND p.[Nº Cuenta] = @c555;
    SET @altas = @altas + 1;
    FETCH NEXT FROM cAlta INTO @c555, @c440, @market;
END;
CLOSE cAlta;
DEALLOCATE cAlta;
PRINT 'Cuentas 440 dadas de alta: ' + CAST(@altas AS varchar(10));

-- -------------------------------------------------------------------------
-- Paso 1: asientos de reclasificación a 30/06/26
--   Asiento 1 = pagos (pares 555 -> 440), Asiento 2 = comisiones (555 -> 999)
-- -------------------------------------------------------------------------
BEGIN TRAN;

-- Asiento 1: patas 555 de pago (las dejan a cero)
INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Concepto, Debe, Haber, Fecha, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
SELECT '1', '3', '1', s.Cuenta555, LEFT('Reclas. 30/06 pagos ' + s.Market, 50),
       CASE WHEN s.Saldo < 0 THEN -s.Saldo ELSE 0 END,
       CASE WHEN s.Saldo > 0 THEN s.Saldo ELSE 0 END,
       '20260630', 'RECLAS3006', 1, 'Carlos', 0, 'ALG', 'VAR', 'Carlos'
FROM @saldos s WHERE s.Tipo = 'P';

-- Asiento 1: patas 440 espejo
INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Concepto, Debe, Haber, Fecha, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
SELECT '1', '3', '1', s.Cuenta440, LEFT('Reclas. 30/06 pagos ' + s.Market, 50),
       CASE WHEN s.Saldo > 0 THEN s.Saldo ELSE 0 END,
       CASE WHEN s.Saldo < 0 THEN -s.Saldo ELSE 0 END,
       '20260630', 'RECLAS3006', 1, 'Carlos', 0, 'ALG', 'VAR', 'Carlos'
FROM @saldos s WHERE s.Tipo = 'P';

-- Asiento 2: patas 555 de comisión (las dejan a cero)
INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Concepto, Debe, Haber, Fecha, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
SELECT '1', '3', '1', s.Cuenta555, LEFT('Reclas. 30/06 comisiones ' + s.Market, 50),
       CASE WHEN s.Saldo < 0 THEN -s.Saldo ELSE 0 END,
       CASE WHEN s.Saldo > 0 THEN s.Saldo ELSE 0 END,
       '20260630', 'RECLAS3006', 2, 'Carlos', 0, 'ALG', 'VAR', 'Carlos'
FROM @saldos s WHERE s.Tipo = 'C';

-- Asiento 2: pata única de proveedor 999 por el neto (TipoCuenta 3 = proveedor)
IF @netoComisiones <> 0
    INSERT INTO PreContabilidad (Empresa, TipoApunte, TipoCuenta, [Nº Cuenta], Contacto, Concepto, Debe, Haber, Fecha, FechaVto, [Nº Documento], Asiento, Diario, [Asiento Automático], Delegación, FormaVenta, Usuario)
    VALUES ('1', '3', '3', '999', '0', 'Reclas. 30/06 comisiones Amazon (c/c)',
            CASE WHEN @netoComisiones > 0 THEN @netoComisiones ELSE 0 END,
            CASE WHEN @netoComisiones < 0 THEN -@netoComisiones ELSE 0 END,
            '20260630', '20260630', 'RECLAS3006', 2, 'Carlos', 0, 'ALG', 'VAR', 'Carlos');

-- Verificación: los asientos deben cuadrar
IF EXISTS (SELECT Asiento FROM PreContabilidad WHERE RTRIM(Diario) = 'Carlos' GROUP BY Asiento HAVING SUM(Debe) <> SUM(Haber))
BEGIN
    ROLLBACK;
    THROW 50003, 'El asiento de reclasificación no cuadra: revisar saldos. No se contabiliza nada.', 1;
END;

COMMIT;

SELECT 'PreContabilidad 30/06 (antes de contabilizar)' AS Paso, Asiento, RTRIM([Nº Cuenta]) AS Cuenta, RTRIM(Concepto) AS Concepto, Debe, Haber
FROM PreContabilidad WHERE RTRIM(Diario) = 'Carlos' ORDER BY Asiento, [Nº Orden];

-- -------------------------------------------------------------------------
-- Paso 2: contabilizar el 30/06
-- -------------------------------------------------------------------------
DECLARE @resultado int;
EXEC @resultado = prdContabilizar '1', 'Carlos', 'Carlos';
IF @resultado < 0
    THROW 50004, 'prdContabilizar devolvió error en el asiento del 30/06.', 1;
PRINT 'Contabilizado 30/06. Último asiento: ' + CAST(@resultado AS varchar(10));

-- -------------------------------------------------------------------------
-- Paso 3: extorno a 01/07/26 (espejo de @saldos) con Liquidado a la partida
--         de cartera del 999 creada en el paso anterior
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

-- -------------------------------------------------------------------------
-- Paso 4: contabilizar el extorno
-- -------------------------------------------------------------------------
EXEC @resultado = prdContabilizar '1', 'Carlos', 'Carlos';
IF @resultado < 0
    THROW 50006, 'prdContabilizar devolvió error en el extorno. OJO: el 30/06 YA está contabilizado.', 1;
PRINT 'Extorno contabilizado. Último asiento: ' + CAST(@resultado AS varchar(10));

-- -------------------------------------------------------------------------
-- Verificaciones finales
-- -------------------------------------------------------------------------
-- 1) Las 555 de Amazon deben quedar a CERO a 30/06 y con su saldo original hoy
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
