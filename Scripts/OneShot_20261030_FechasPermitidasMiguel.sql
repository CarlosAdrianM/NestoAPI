-- 30/10/26: Miguel (repartidor, usuario de Identity «Miguel» sin dominio) no tiene fila en FechasPermitidas.
-- Al entregar con cobro en Ariadna (POST api/RepartoPropio/Envios/250379/Entregar) el diario de reembolsos
-- se contabiliza a su nombre y prdContabilizar da «Fecha 30/10/2026 no permitida (anterior a la permitida)
-- para Miguel» (ELMAH 11:17, 11:43, 13:03 y 13:49).
-- Le copia las fechas de NUEVAVISION\Alfredo en las empresas 1 y 3 (las de su CajaDefecto).
-- La tarea mensual de FechasPermitidas le irá ampliando las fechas como a todos.
-- Parámetros de usuario: el cobro solo usa CajaDefecto, que Miguel ya tiene (57000032 en 1 y 3).
-- Lanzar como sa. Idempotente: si ya existe la fila, no la toca.

SET NOCOUNT ON;

INSERT INTO dbo.FechasPermitidas
    (Empresa, Usuario,
     FechaInicialCnt, FechaFinalCnt, FechaInicialVta, FechaFinalVta, FechaInicialCmp, FechaFinalCmp,
     FechaInicialRapports, FechaFinalRapports, FechaInicialExtrProd, FechaFinalExtrProd,
     FechaInicialBanco, FechaFinalBanco, FechaInicialPagare, FechaFinalPagare)
SELECT f.Empresa, N'Miguel',
       f.FechaInicialCnt, f.FechaFinalCnt, f.FechaInicialVta, f.FechaFinalVta, f.FechaInicialCmp, f.FechaFinalCmp,
       f.FechaInicialRapports, f.FechaFinalRapports, f.FechaInicialExtrProd, f.FechaFinalExtrProd,
       f.FechaInicialBanco, f.FechaFinalBanco, f.FechaInicialPagare, f.FechaFinalPagare
FROM dbo.FechasPermitidas f
WHERE RTRIM(f.Usuario) = N'NUEVAVISION\Alfredo'
  AND f.Empresa IN ('1  ', '3  ')
  AND NOT EXISTS (SELECT 1 FROM dbo.FechasPermitidas m WHERE m.Empresa = f.Empresa AND RTRIM(m.Usuario) = N'Miguel');

PRINT CONCAT('Filas insertadas: ', @@ROWCOUNT);

SELECT Empresa, Usuario, FechaInicialCnt, FechaFinalCnt, FechaInicialVta, FechaFinalVta
FROM dbo.FechasPermitidas
WHERE RTRIM(Usuario) IN (N'Miguel', N'NUEVAVISION\Alfredo') AND Empresa IN ('1  ', '3  ')
ORDER BY Usuario, Empresa;
