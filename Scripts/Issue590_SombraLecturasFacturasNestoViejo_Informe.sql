/*
    NestoAPI#590: informe de las lecturas de facturas de Nesto viejo (sesión de Issue590_SombraLecturasFacturasNestoViejo_XE.sql).
    Como sa, fuera de horario (leer los .xel cuesta CPU). Solo lee; no cambia nada.

    Para decidir cómo dejar de imprimir facturas desde Nesto viejo (salvo empresa 3 / serie GB, que solo existe en la 3):
      1) Por categoría: impresión de Crystal (la consulta que cruza LinPedidoVta, CabFacturaVta, Vendedores y CabPedidoVta, #567),
         cada vista, cada procedimiento que lee CabFacturaVta, y consultas directas. Si casi todo es Crystal, un RLS en CabFacturaVta
         haría poco daño; si hay mucho libro de IVA, comisiones o pantallas de facturas, mejor por el lado del informe (RutaInformes).
      2) Por categoría y usuario: a quién habría que avisar.
      3) Impresiones de Crystal por usuario y día, con un ejemplo del texto (para ver la empresa o la serie que pide).
*/
SET NOCOUNT ON;
DECLARE @Desde datetime2(0) = '20261109';    -- <-- desde cuándo (hora UTC de los eventos)

IF OBJECT_ID('tempdb..#Lecturas') IS NOT NULL DROP TABLE #Lecturas;

;WITH xe AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue590_SombraLecturasFacturasNestoViejo*.xel', NULL, NULL, NULL) f
    WHERE f.timestamp_utc >= @Desde
)
SELECT x.value('(event/@timestamp)[1]', 'datetime2(0)')                                   AS MomentoUtc,
       x.value('(event/action[@name="server_principal_name"]/value)[1]', 'nvarchar(128)') AS Usuario,
       x.value('(event/data[@name="object_name"]/value)[1]', 'nvarchar(256)')             AS Objeto,
       x.value('(event/data[@name="statement"]/value)[1]', 'nvarchar(max)')               AS Texto,
       CAST(NULL AS nvarchar(300))                                                         AS Categoria
INTO #Lecturas
FROM xe;

-- Procedimientos que leen CabFacturaVta: los demás prd% no interesan aquí
DELETE l FROM #Lecturas l
WHERE l.Objeto LIKE N'prd%'
  AND l.Texto NOT LIKE N'%CabFacturaVta%'
  AND NOT EXISTS (SELECT 1 FROM sys.sql_expression_dependencies d
                  WHERE d.referencing_id = OBJECT_ID(N'dbo.' + l.Objeto) AND d.referenced_entity_name = N'CabFacturaVta');

UPDATE #Lecturas SET Categoria =
    CASE
        WHEN Texto LIKE N'%LinPedidoVta%' AND Texto LIKE N'%CabFacturaVta%' AND Texto LIKE N'%Vendedores%' AND Texto LIKE N'%CabPedidoVta%'
             THEN N'Impresión Crystal (facturas.rpt)'
        WHEN Texto LIKE N'%vstLibroIVA%'                 THEN N'Vista vstLibroIVA'
        WHEN Texto LIKE N'%vstRepetirRuta%'              THEN N'Vista vstRepetirRuta'
        WHEN Texto LIKE N'%vstComisionesEspeciales%'     THEN N'Vista vstComisionesEspeciales'
        WHEN Texto LIKE N'%vstInformeComisionesResumen%' THEN N'Vista vstInformeComisionesResumen'
        WHEN Objeto LIKE N'prd%'                         THEN N'Procedimiento ' + Objeto
        ELSE N'Consulta directa a CabFacturaVta'
    END;

-- 1) Por categoría
SELECT Categoria, COUNT(*) Veces, COUNT(DISTINCT Usuario) Usuarios, MIN(MomentoUtc) Primera, MAX(MomentoUtc) Ultima,
       LEFT(MAX(Texto), 300) Ejemplo
FROM #Lecturas GROUP BY Categoria ORDER BY Veces DESC;

-- 2) Por categoría y usuario
SELECT Categoria, Usuario, COUNT(*) Veces, MAX(MomentoUtc) Ultima
FROM #Lecturas GROUP BY Categoria, Usuario ORDER BY Categoria, Veces DESC;

-- 3) Impresiones de Crystal por usuario y día
SELECT CAST(MomentoUtc AS date) Dia, Usuario, COUNT(*) Impresiones, LEFT(MAX(Texto), 400) Ejemplo
FROM #Lecturas WHERE Categoria = N'Impresión Crystal (facturas.rpt)'
GROUP BY CAST(MomentoUtc AS date), Usuario ORDER BY Dia, Impresiones DESC;
