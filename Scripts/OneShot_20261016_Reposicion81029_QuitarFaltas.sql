-- 16/10/26: reposición 81029 (ALG → ALC). Andre marcó por error «No está» en 25539 (1 ud, 10:37) y 44714 (1 ud,
-- hueco 001/005/001, 09:44), pero las dos unidades van en la caja. Esto hace lo mismo que Deshacer: una falta en
-- negativo por cada una. Después: volver a abrir la 81029 en la PDA, leer 1 de cada y pulsar Terminar.
USE NV;
SET XACT_ABORT ON;
BEGIN TRAN;

INSERT INTO PreparacionEscaneos (IdCliente, Empresa, NumeroOrigen, Pedido, LineaPedido, Producto, Fase, Cantidad, Metodo,
                                 Bulto, Motivo, Usuario, Dispositivo, FechaEscaneo, FechaRegistro, TipoOrigen)
SELECT NEWID(), Empresa, NumeroOrigen, NULL, NULL, Producto, Fase, -Cantidad, Metodo,
       NULL, Motivo, 'Carlos', 'Corrección manual', GETDATE(), GETDATE(), TipoOrigen
FROM PreparacionEscaneos
WHERE Id IN (700, 837)            -- las dos faltas de Andre (44714 y 25539)
  AND NumeroOrigen = '81029' AND RTRIM(Metodo) = 'FALTA' AND Cantidad = 1
  AND NOT EXISTS (SELECT 1 FROM PreparacionEscaneos x WHERE x.NumeroOrigen = '81029' AND x.Dispositivo = 'Corrección manual');

SELECT @@ROWCOUNT AS FilasInsertadas;   -- debe dar 2
COMMIT;

-- Comprobación: falta 0 en las dos
SELECT RTRIM(Producto) AS Producto,
       SUM(CASE WHEN RTRIM(Metodo) = 'FALTA' THEN Cantidad ELSE 0 END) AS Falta,
       SUM(CASE WHEN RTRIM(Metodo) <> 'FALTA' THEN Cantidad ELSE 0 END) AS Cogido
FROM PreparacionEscaneos
WHERE NumeroOrigen = '81029' AND Producto IN ('44714', '25539')
GROUP BY Producto;
