-- OneShot 28/10/26 (sugerencia 829, Reina): el cobro de la factura GB2601662 (Global, empresa 3) del cliente 11276 (11,90 €) se metió
-- en la caja de la empresa 1 (diario LauraC) y prdContabilizar no puede liquidar el extracto 3058956 (está en la empresa 3).
-- Se quitan los dos apuntes de PreContabilidad para que Reina vuelva a cobrarlo en Global. Lanzar como sa en NV.
SET NOCOUNT ON;
SELECT [Nº Orden], Empresa, Diario, TipoCuenta, [Nº Cuenta], [Nº Documento], Debe, Haber, Liquidado FROM dbo.PreContabilidad WHERE [Nº Orden] IN (690821800, 690821900);
DELETE FROM dbo.PreContabilidad WHERE [Nº Orden] IN (690821800, 690821900) AND [Nº Documento] = 'GB2601662' AND Diario = 'LauraC';
PRINT CAST(@@ROWCOUNT AS varchar) + ' apuntes borrados (tienen que ser 2).';
