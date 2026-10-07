-- NestoAPI#596, paso 4: limpieza de los códigos postales portugueses que ya hay en la BD.
--
-- Los pasos 1-3 ya están en la API: un solo normalizador (Infraestructure/Direcciones/CodigoPostal.cs),
-- que se aplica al grabar clientes y envíos, y CTT recibe siempre el CP con guion. Lo grabado antes sigue
-- mezclando «4480 670», «4480670» y «4480-670», y en CódigosPostales hay duplicados por formato. Este
-- script lo deja todo en el formato canónico, con EXACTAMENTE las reglas de CodigoPostal.Normalizar:
--   · 4 cifras (la primera de 1 a 9) + 3 cifras, con o sin separador (guion, espacios, o espacios
--     alrededor del guion: «4480 - 670») → «dddd-ddd».
--     Se aplica con país PT, ES o sin país (con ES o sin país, 7 cifras solo pueden ser Portugal).
--   · 4 cifras con país PT: se queda como está («dddd» es canónico).
--   · Lo demás NO se toca. En particular, un CP de 4 cifras sin país, que el normalizador daría por
--     español y rellenaría con el cero, queda fuera: este script es solo para Portugal. Los «raros» con
--     país PT (208, EXER, 440046...) salen en un listado para corregirlos a mano.
--
-- Qué hace (todo en UNA transacción):
--   1. CódigosPostales (país PT, ES o sin país; la PK es Empresa+Número): para cada CP que no está en
--      canónico, se asegura la fila canónica (si no existe, se crea copiando la «superviviente» del grupo:
--      la que más clientes tiene, luego la que tiene población) y se borra la vieja. Si dos filas pasan a
--      ser el mismo CP, se conserva la canónica que ya existiera (o la superviviente) y las otras se
--      borran, DESPUÉS de mover sus referencias. Referencias con FK a CódigosPostales (todas se mueven a
--      la canónica antes de borrar): Clientes, Proveedores, ExtractoRuta, FamiliasVendedor,
--      VendedoresCodigoPostalGrupoProducto y Empresas. Una fila vieja que siga referenciada no se borra
--      (se cuenta al final).
--   2. Clientes (activos y de baja; país PT, ES o vacío, o apuntando a una fila que se renombra): CodPostal
--      al canónico.
--      OJO con trgClientesUpd: al cambiar CodPostal BORRA las filas de VendedoresClienteGrupoProducto del
--      cliente si el CP nuevo no tiene VendedoresCodigoPostalGrupoProducto (ningún CP portugués lo tiene).
--      Por eso el script guarda antes esas filas y repone las que desaparezcan (mismo Id). El disparador
--      también apunta el cambio en LogModificaciones y encola los clientes de la empresa 1 en Nesto_sync
--      (se republican en Odoo con el CP bueno): eso está bien y se deja.
--   3. EnviosAgencia PENDIENTES (Estado < 0, la etiqueta aún no está creada: Constantes.Agencias.
--      ESTADO_PENDIENTE = -1) a Portugal (Pais 351/620), a España o sin país: CodPostal al canónico. Los
--      envíos en curso (0, etiqueta ya impresa con su CP) o tramitados (>= 1) NO se tocan, ni las facturas
--      ni los albaranes (CabFacturaVta, CabAlbaránVta), que son históricos.
--
-- El disparador trgCodigosPostalesUpd manda un correo a carlosadrian@ con las filas canónicas que se
-- crean (uno por INSERT). Es normal.
--
-- Recuento en producción del 07/10/26 (con lectura, sin tocar nada):
--   · CódigosPostales PT: 132 filas; 48 no están en canónico (41 de la empresa 1 y 7 de la 3). Solo un
--     duplicado por formato: «4430 999» (NV, 2 clientes) y «4430-999» (PA, 0 clientes), empresa 1. Se
--     queda «4430-999» y se le mueven los 2 clientes. El resto se renombra (se crea la canónica y se borra
--     la vieja). Ninguna fila PT tiene proveedores, ruta, familias, vendedores por CP ni empresas colgando.
--   · Clientes: 47 cambian (46 activos y 1 de baja); 1 con VendedoresClienteGrupoProducto (se repone).
--   · EnviosAgencia pendientes: 1 a Portugal y ya en canónico: 0 cambian.
--
-- Cómo lanzarlo: como sa en SSMS, en NV. Con @SoloSimular = 1 (por defecto) hace todo, enseña el antes
-- y el después y deshace (ROLLBACK). Con @SoloSimular = 0 confirma (COMMIT). Idempotente: una segunda
-- pasada no encuentra nada que cambiar.

USE NV;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @SoloSimular bit = 1;   -- 1 = simular (ROLLBACK); 0 = aplicar (COMMIT)

-------------------------------------------------------------------------------------------------
-- 0. Conteo por formato (el mismo SELECT antes y después). «Aspecto portugués»: país PT, o forma
--    dddd-ddd / dddd ddd / ddddddd con país ES o vacío.
-------------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Conteo') IS NOT NULL DROP TABLE #Conteo;
CREATE TABLE #Conteo (Momento varchar(10), Tabla varchar(60), Formato varchar(10), N int);

DECLARE @SqlConteo nvarchar(max) = N'
;WITH Fuente AS (
    SELECT ''CódigosPostales'' AS Tabla, cp.Número AS CP, cp.Pais AS PaisIso
    FROM dbo.CódigosPostales cp
    UNION ALL
    SELECT CASE WHEN k.Estado >= 0 THEN ''Clientes activos'' ELSE ''Clientes de baja'' END, k.CodPostal, k.Pais
    FROM dbo.Clientes k
    UNION ALL
    SELECT ''EnviosAgencia pendientes'', e.CodPostal,
           CASE WHEN e.Pais IN (351, 620) THEN ''PT'' WHEN e.Pais IN (34, 724) THEN ''ES'' WHEN ISNULL(e.Pais, 0) = 0 THEN NULL ELSE ''XX'' END
    FROM dbo.EnviosAgencia e
    WHERE e.Estado < 0
), Clasificado AS (
    SELECT f.Tabla, f.PaisIso,
           CASE WHEN a.X LIKE N''[1-9][0-9][0-9][0-9]-[0-9][0-9][0-9]'' THEN ''dddd-ddd''
                WHEN a.X LIKE N''[1-9][0-9][0-9][0-9] [0-9][0-9][0-9]'' THEN ''dddd ddd''
                WHEN a.X LIKE N''[1-9][0-9][0-9][0-9][0-9][0-9][0-9]'' THEN ''ddddddd''
                WHEN a.X LIKE N''[0-9][0-9][0-9][0-9]'' THEN ''dddd''
                ELSE ''otro'' END AS Formato
    FROM Fuente f
    CROSS APPLY (SELECT LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(CAST(f.CP AS nvarchar(30)), NCHAR(160), N'' ''), NCHAR(9), N'' ''), NCHAR(13), N'' ''), NCHAR(10), N'' ''))) AS X) a
)
INSERT #Conteo (Momento, Tabla, Formato, N)
SELECT @Momento, Tabla, Formato, COUNT(*)
FROM Clasificado
WHERE LTRIM(RTRIM(ISNULL(PaisIso, ''''))) = ''PT''
   OR (LTRIM(RTRIM(ISNULL(PaisIso, ''''))) IN ('''', ''ES'') AND Formato IN (''dddd-ddd'', ''dddd ddd'', ''ddddddd''))
GROUP BY Tabla, Formato;';

DECLARE @SqlVerConteo nvarchar(max) = N'
SELECT @Momento AS Momento, Tabla,
       SUM(CASE WHEN Formato = ''dddd-ddd'' THEN N ELSE 0 END) AS [dddd-ddd],
       SUM(CASE WHEN Formato = ''dddd ddd'' THEN N ELSE 0 END) AS [dddd ddd],
       SUM(CASE WHEN Formato = ''ddddddd''  THEN N ELSE 0 END) AS [ddddddd],
       SUM(CASE WHEN Formato = ''dddd''     THEN N ELSE 0 END) AS [dddd],
       SUM(CASE WHEN Formato = ''otro''     THEN N ELSE 0 END) AS [otro]
FROM #Conteo WHERE Momento = @Momento
GROUP BY Tabla ORDER BY Tabla;';

EXEC sp_executesql @SqlConteo, N'@Momento varchar(10)', @Momento = 'Antes';
EXEC sp_executesql @SqlVerConteo, N'@Momento varchar(10)', @Momento = 'Antes';

-- Los raros con país PT: no se tocan, para corregirlos a mano (ventana de CPs / ficha del cliente).
SELECT 'CódigosPostales' AS Tabla, RTRIM(cp.Empresa) AS Empresa, '[' + RTRIM(cp.Número) + ']' AS CP, RTRIM(cp.Descripción) AS Poblacion, NULL AS Cliente
FROM dbo.CódigosPostales cp
WHERE cp.Pais = 'PT'
  AND RTRIM(LTRIM(cp.Número)) NOT LIKE '[0-9][0-9][0-9][0-9]'
  AND RTRIM(LTRIM(cp.Número)) NOT LIKE '[1-9][0-9][0-9][0-9]%[0-9][0-9][0-9]'
UNION ALL
SELECT 'Clientes', RTRIM(k.Empresa), '[' + RTRIM(k.CodPostal) + ']', RTRIM(k.Población), RTRIM(k.[Nº Cliente]) + '/' + RTRIM(k.Contacto)
FROM dbo.Clientes k
WHERE k.Pais = 'PT'
  AND RTRIM(LTRIM(k.CodPostal)) NOT LIKE '[0-9][0-9][0-9][0-9]'
  AND RTRIM(LTRIM(k.CodPostal)) NOT LIKE '[1-9][0-9][0-9][0-9]%[0-9][0-9][0-9]'
ORDER BY 1, 2, 3;

BEGIN TRANSACTION;

-------------------------------------------------------------------------------------------------
-- 1. Mapa de CódigosPostales que cambian: (Empresa, Viejo) → Canon
-------------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#MapaCP') IS NOT NULL DROP TABLE #MapaCP;

SELECT cp.Empresa, cp.Número AS Viejo, b.Canon,
       cp.Descripción, cp.Provincia, cp.Ruta, cp.Vendedor, cp.Pais,
       (SELECT COUNT(*) FROM dbo.Clientes k WHERE k.Empresa = cp.Empresa AND k.CodPostal = cp.Número) AS Clientes
INTO #MapaCP
FROM dbo.CódigosPostales cp WITH (UPDLOCK, HOLDLOCK)
CROSS APPLY (SELECT LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(CAST(cp.Número AS nvarchar(30)), NCHAR(160), N' '), NCHAR(9), N' '), NCHAR(13), N' '), NCHAR(10), N' '))) AS X) a
CROSS APPLY (SELECT CASE
                 WHEN LEN(a.X) < 7 THEN NULL
                 WHEN a.X LIKE N'[1-9][0-9][0-9][0-9]%' AND RIGHT(a.X, 3) LIKE N'[0-9][0-9][0-9]'
                      AND REPLACE(REPLACE(SUBSTRING(a.X, 5, CASE WHEN LEN(a.X) > 7 THEN LEN(a.X) - 7 ELSE 0 END), N' ', N''), N'-', N'') = N''
                      AND LEN(REPLACE(SUBSTRING(a.X, 5, CASE WHEN LEN(a.X) > 7 THEN LEN(a.X) - 7 ELSE 0 END), N' ', N'')) <= 1
                 THEN CAST(LEFT(a.X, 4) + N'-' + RIGHT(a.X, 3) AS varchar(15))
             END AS Canon) b
WHERE (cp.Pais IS NULL OR LTRIM(RTRIM(cp.Pais)) IN ('', 'PT', 'ES'))
  AND b.Canon IS NOT NULL
  AND cp.Número COLLATE Latin1_General_BIN <> b.Canon COLLATE Latin1_General_BIN;   -- binario: « 4480-670» también cambia

CREATE UNIQUE CLUSTERED INDEX IX_MapaCP ON #MapaCP (Empresa, Viejo);

-- Grupos que se fusionan: la canónica ya existe, o hay más de un viejo con el mismo canónico.
SELECT 'Duplicados que se fusionan' AS Informe, RTRIM(m.Empresa) AS Empresa, m.Canon,
       '[' + RTRIM(m.Viejo) + ']' AS SeBorra, RTRIM(m.Descripción) AS Poblacion, RTRIM(m.Vendedor) AS Vendedor, m.Clientes,
       CASE WHEN EXISTS (SELECT 1 FROM dbo.CódigosPostales c WHERE c.Empresa = m.Empresa AND c.Número = m.Canon)
            THEN 'ya existe la canónica (vendedor ' + (SELECT RTRIM(c.Vendedor) FROM dbo.CódigosPostales c WHERE c.Empresa = m.Empresa AND c.Número = m.Canon) + ')'
            ELSE 'otro viejo del mismo CP' END AS SeQueda
FROM #MapaCP m
WHERE EXISTS (SELECT 1 FROM dbo.CódigosPostales c WHERE c.Empresa = m.Empresa AND c.Número = m.Canon)
   OR (SELECT COUNT(*) FROM #MapaCP m2 WHERE m2.Empresa = m.Empresa AND m2.Canon = m.Canon) > 1
ORDER BY m.Empresa, m.Canon, m.Viejo;

SELECT COUNT(*) AS CodigosPostalesQueCambian,
       COUNT(DISTINCT CAST(Empresa AS varchar(3)) + '|' + Canon) AS CanonicosDistintos
FROM #MapaCP;

-------------------------------------------------------------------------------------------------
-- 2. Fila canónica: si no existe, se crea copiando la superviviente del grupo
-------------------------------------------------------------------------------------------------
;WITH Superviviente AS (
    SELECT m.*, ROW_NUMBER() OVER (PARTITION BY m.Empresa, m.Canon
                                   ORDER BY m.Clientes DESC,
                                            CASE WHEN NULLIF(LTRIM(RTRIM(m.Descripción)), '') IS NULL THEN 1 ELSE 0 END,
                                            m.Viejo) AS Orden
    FROM #MapaCP m
    WHERE NOT EXISTS (SELECT 1 FROM dbo.CódigosPostales c WHERE c.Empresa = m.Empresa AND c.Número = m.Canon)
)
INSERT dbo.CódigosPostales (Empresa, Número, Descripción, Provincia, Ruta, Vendedor, Pais)
SELECT s.Empresa, s.Canon, s.Descripción, s.Provincia, s.Ruta, s.Vendedor, ISNULL(NULLIF(LTRIM(RTRIM(s.Pais)), ''), 'PT')
FROM Superviviente s
WHERE s.Orden = 1;

DECLARE @CanonicasCreadas int = @@ROWCOUNT;

-------------------------------------------------------------------------------------------------
-- 3. Clientes que cambian (los de país PT/ES/vacío con forma 4+3, y cualquiera que apunte a una
--    fila de CódigosPostales que se renombra)
-------------------------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Clientes') IS NOT NULL DROP TABLE #Clientes;

SELECT k.Empresa, k.[Nº Cliente] AS Cliente, k.Contacto, k.CodPostal AS Viejo,
       ISNULL(m.Canon, b.Canon) AS Canon, k.Población, k.Provincia, k.Pais
INTO #Clientes
FROM dbo.Clientes k WITH (UPDLOCK, HOLDLOCK)
CROSS APPLY (SELECT LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(CAST(k.CodPostal AS nvarchar(30)), NCHAR(160), N' '), NCHAR(9), N' '), NCHAR(13), N' '), NCHAR(10), N' '))) AS X) a
CROSS APPLY (SELECT CASE
                 WHEN NOT (k.Pais IS NULL OR LTRIM(RTRIM(k.Pais)) IN ('', 'PT', 'ES')) THEN NULL
                 WHEN LEN(a.X) < 7 THEN NULL
                 WHEN a.X LIKE N'[1-9][0-9][0-9][0-9]%' AND RIGHT(a.X, 3) LIKE N'[0-9][0-9][0-9]'
                      AND REPLACE(REPLACE(SUBSTRING(a.X, 5, CASE WHEN LEN(a.X) > 7 THEN LEN(a.X) - 7 ELSE 0 END), N' ', N''), N'-', N'') = N''
                      AND LEN(REPLACE(SUBSTRING(a.X, 5, CASE WHEN LEN(a.X) > 7 THEN LEN(a.X) - 7 ELSE 0 END), N' ', N'')) <= 1
                 THEN CAST(LEFT(a.X, 4) + N'-' + RIGHT(a.X, 3) AS varchar(15))
             END AS Canon) b
LEFT JOIN #MapaCP m ON m.Empresa = k.Empresa AND m.Viejo = k.CodPostal
WHERE ISNULL(m.Canon, b.Canon) IS NOT NULL
  AND k.CodPostal COLLATE Latin1_General_BIN <> ISNULL(m.Canon, b.Canon) COLLATE Latin1_General_BIN;

CREATE UNIQUE CLUSTERED INDEX IX_Clientes ON #Clientes (Empresa, Cliente, Contacto);

-- Un cliente cuyo CP no estaba en CódigosPostales (la FK no es de confianza): se crea la canónica con
-- los datos del cliente, como hace GestorClientes.AsegurarCodigoPostalExtranjero (ruta 00, vendedor NV).
INSERT dbo.CódigosPostales (Empresa, Número, Descripción, Provincia, Ruta, Vendedor, Pais)
SELECT x.Empresa, x.Canon, x.Población, x.Provincia, '00', 'NV', 'PT'
FROM (SELECT k.Empresa, k.Canon, UPPER(LTRIM(RTRIM(k.Población))) AS Población, ISNULL(UPPER(LTRIM(RTRIM(k.Provincia))), '') AS Provincia,
             ROW_NUMBER() OVER (PARTITION BY k.Empresa, k.Canon ORDER BY k.Cliente, k.Contacto) AS Orden
      FROM #Clientes k
      WHERE NOT EXISTS (SELECT 1 FROM dbo.CódigosPostales c WHERE c.Empresa = k.Empresa AND c.Número = k.Canon)) x
WHERE x.Orden = 1;

SET @CanonicasCreadas += @@ROWCOUNT;

SELECT 'Clientes que cambian' AS Informe, RTRIM(k.Empresa) AS Empresa, RTRIM(k.Cliente) AS Cliente, RTRIM(k.Contacto) AS Contacto,
       '[' + RTRIM(k.Viejo) + ']' AS Antes, k.Canon AS Despues, RTRIM(k.Población) AS Poblacion, k.Pais
FROM #Clientes k
ORDER BY k.Empresa, k.Cliente, k.Contacto;

-- Lo que trgClientesUpd borraría de VendedoresClienteGrupoProducto (se repone en el paso 5)
IF OBJECT_ID('tempdb..#VendedoresGrupoAntes') IS NOT NULL DROP TABLE #VendedoresGrupoAntes;
SELECT v.*
INTO #VendedoresGrupoAntes
FROM dbo.VendedoresClienteGrupoProducto v
WHERE EXISTS (SELECT 1 FROM #Clientes k WHERE k.Empresa = v.Empresa AND k.Cliente = v.Cliente AND k.Contacto = v.Contacto);

-------------------------------------------------------------------------------------------------
-- 4. Mover las referencias a la canónica
-------------------------------------------------------------------------------------------------
UPDATE k SET k.CodPostal = x.Canon
FROM dbo.Clientes k
INNER JOIN #Clientes x ON x.Empresa = k.Empresa AND x.Cliente = k.[Nº Cliente] AND x.Contacto = k.Contacto;
DECLARE @ClientesCambiados int = @@ROWCOUNT;

UPDATE p SET p.CodPostal = m.Canon
FROM dbo.Proveedores p INNER JOIN #MapaCP m ON m.Empresa = p.Empresa AND m.Viejo = p.CodPostal;
DECLARE @ProveedoresCambiados int = @@ROWCOUNT;

UPDATE r SET r.CodPostal = m.Canon
FROM dbo.ExtractoRuta r INNER JOIN #MapaCP m ON m.Empresa = r.Empresa AND m.Viejo = r.CodPostal;
DECLARE @ExtractoRutaCambiados int = @@ROWCOUNT;

UPDATE f SET f.CodigoPostal = m.Canon
FROM dbo.FamiliasVendedor f INNER JOIN #MapaCP m ON m.Empresa = f.Empresa AND m.Viejo = f.CodigoPostal;
DECLARE @FamiliasVendedorCambiados int = @@ROWCOUNT;

-- Si la canónica ya tenía el mismo grupo, sobra la del viejo (si no, chocaría al moverla).
DELETE v
FROM dbo.VendedoresCodigoPostalGrupoProducto v
INNER JOIN #MapaCP m ON m.Empresa = v.Empresa AND m.Viejo = v.CodigoPostal
WHERE EXISTS (SELECT 1 FROM dbo.VendedoresCodigoPostalGrupoProducto v2
              WHERE v2.Empresa = v.Empresa AND v2.CodigoPostal = m.Canon AND v2.GrupoProducto = v.GrupoProducto);

UPDATE v SET v.CodigoPostal = m.Canon
FROM dbo.VendedoresCodigoPostalGrupoProducto v INNER JOIN #MapaCP m ON m.Empresa = v.Empresa AND m.Viejo = v.CodigoPostal;
DECLARE @VendedoresCPCambiados int = @@ROWCOUNT;

UPDATE e SET e.CodPostal = m.Canon
FROM dbo.Empresas e INNER JOIN #MapaCP m ON m.Empresa = e.Número AND m.Viejo = e.CodPostal;
DECLARE @EmpresasCambiadas int = @@ROWCOUNT;

-------------------------------------------------------------------------------------------------
-- 5. Reponer lo que trgClientesUpd haya borrado de VendedoresClienteGrupoProducto (mismo Id)
-------------------------------------------------------------------------------------------------
SET IDENTITY_INSERT dbo.VendedoresClienteGrupoProducto ON;

INSERT dbo.VendedoresClienteGrupoProducto (Id, Empresa, Cliente, Contacto, GrupoProducto, Vendedor, Estado, Usuario, FechaModificacion)
SELECT a.Id, a.Empresa, a.Cliente, a.Contacto, a.GrupoProducto, a.Vendedor, a.Estado, a.Usuario, a.FechaModificacion
FROM #VendedoresGrupoAntes a
WHERE NOT EXISTS (SELECT 1 FROM dbo.VendedoresClienteGrupoProducto v WHERE v.Id = a.Id)
  AND NOT EXISTS (SELECT 1 FROM dbo.VendedoresClienteGrupoProducto v
                  WHERE v.Empresa = a.Empresa AND v.Cliente = a.Cliente AND v.Contacto = a.Contacto AND v.GrupoProducto = a.GrupoProducto);
DECLARE @VendedoresGrupoRepuestos int = @@ROWCOUNT;

SET IDENTITY_INSERT dbo.VendedoresClienteGrupoProducto OFF;

-- Comprobación: ningún cliente ha perdido su vendedor por grupo.
IF EXISTS (SELECT 1 FROM #VendedoresGrupoAntes a
           WHERE NOT EXISTS (SELECT 1 FROM dbo.VendedoresClienteGrupoProducto v
                             WHERE v.Empresa = a.Empresa AND v.Cliente = a.Cliente AND v.Contacto = a.Contacto
                               AND v.GrupoProducto = a.GrupoProducto AND v.Vendedor = a.Vendedor))
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR('NestoAPI#596: algún cliente ha perdido o cambiado su vendedor por grupo de producto. Deshecho, no se cambia nada.', 16, 1);
    RETURN;
END

-------------------------------------------------------------------------------------------------
-- 6. Borrar las filas viejas de CódigosPostales que ya no referencia nadie
-------------------------------------------------------------------------------------------------
DELETE c
FROM dbo.CódigosPostales c
INNER JOIN #MapaCP m ON m.Empresa = c.Empresa AND m.Viejo = c.Número
WHERE c.Número COLLATE Latin1_General_BIN <> m.Canon COLLATE Latin1_General_BIN
  AND EXISTS (SELECT 1 FROM dbo.CódigosPostales c2 WHERE c2.Empresa = m.Empresa AND c2.Número COLLATE Latin1_General_BIN = m.Canon COLLATE Latin1_General_BIN)
  AND NOT EXISTS (SELECT 1 FROM dbo.Clientes x WHERE x.Empresa = c.Empresa AND x.CodPostal = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.Proveedores x WHERE x.Empresa = c.Empresa AND x.CodPostal = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.ExtractoRuta x WHERE x.Empresa = c.Empresa AND x.CodPostal = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.FamiliasVendedor x WHERE x.Empresa = c.Empresa AND x.CodigoPostal = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.VendedoresCodigoPostalGrupoProducto x WHERE x.Empresa = c.Empresa AND x.CodigoPostal = c.Número)
  AND NOT EXISTS (SELECT 1 FROM dbo.Empresas x WHERE x.Número = c.Empresa AND x.CodPostal = c.Número);
DECLARE @ViejosBorrados int = @@ROWCOUNT;

-------------------------------------------------------------------------------------------------
-- 7. EnviosAgencia pendientes (Estado < 0) a Portugal, España o sin país
-------------------------------------------------------------------------------------------------
UPDATE e SET e.CodPostal = b.Canon
FROM dbo.EnviosAgencia e
CROSS APPLY (SELECT LTRIM(RTRIM(REPLACE(REPLACE(REPLACE(REPLACE(CAST(e.CodPostal AS nvarchar(30)), NCHAR(160), N' '), NCHAR(9), N' '), NCHAR(13), N' '), NCHAR(10), N' '))) AS X) a
CROSS APPLY (SELECT CASE
                 WHEN LEN(a.X) < 7 THEN NULL
                 WHEN a.X LIKE N'[1-9][0-9][0-9][0-9]%' AND RIGHT(a.X, 3) LIKE N'[0-9][0-9][0-9]'
                      AND REPLACE(REPLACE(SUBSTRING(a.X, 5, CASE WHEN LEN(a.X) > 7 THEN LEN(a.X) - 7 ELSE 0 END), N' ', N''), N'-', N'') = N''
                      AND LEN(REPLACE(SUBSTRING(a.X, 5, CASE WHEN LEN(a.X) > 7 THEN LEN(a.X) - 7 ELSE 0 END), N' ', N'')) <= 1
                 THEN LEFT(a.X, 4) + N'-' + RIGHT(a.X, 3)
             END AS Canon) b
WHERE e.Estado < 0
  AND (e.Pais IS NULL OR e.Pais IN (0, 34, 724, 351, 620))
  AND b.Canon IS NOT NULL
  AND e.CodPostal COLLATE Latin1_General_BIN <> b.Canon COLLATE Latin1_General_BIN;
DECLARE @EnviosCambiados int = @@ROWCOUNT;

-------------------------------------------------------------------------------------------------
-- 8. Resultado y después
-------------------------------------------------------------------------------------------------
SELECT (SELECT COUNT(*) FROM #MapaCP) AS CodigosPostalesQueCambian,
       @CanonicasCreadas AS CanonicasCreadas,
       @ViejosBorrados AS ViejosBorrados,
       (SELECT COUNT(*) FROM #MapaCP m WHERE EXISTS (SELECT 1 FROM dbo.CódigosPostales c WHERE c.Empresa = m.Empresa AND c.Número COLLATE Latin1_General_BIN = m.Viejo COLLATE Latin1_General_BIN)) AS ViejosQueSiguen,
       @ClientesCambiados AS Clientes,
       @VendedoresGrupoRepuestos AS VendedoresGrupoRepuestos,
       @ProveedoresCambiados AS Proveedores,
       @ExtractoRutaCambiados AS ExtractoRuta,
       @FamiliasVendedorCambiados AS FamiliasVendedor,
       @VendedoresCPCambiados AS VendedoresCodigoPostal,
       @EmpresasCambiadas AS Empresas,
       @EnviosCambiados AS EnviosPendientes;

-- Lo que siga sin borrar es porque algo lo referencia todavía: mirarlo antes del COMMIT.
SELECT 'Viejos que no se han podido borrar' AS Informe, RTRIM(m.Empresa) AS Empresa, '[' + RTRIM(m.Viejo) + ']' AS Viejo, m.Canon
FROM #MapaCP m
WHERE EXISTS (SELECT 1 FROM dbo.CódigosPostales c WHERE c.Empresa = m.Empresa AND c.Número COLLATE Latin1_General_BIN = m.Viejo COLLATE Latin1_General_BIN);

EXEC sp_executesql @SqlConteo, N'@Momento varchar(10)', @Momento = 'Despues';
EXEC sp_executesql @SqlVerConteo, N'@Momento varchar(10)', @Momento = 'Despues';

DECLARE @CPsQueCambian int = (SELECT COUNT(*) FROM #MapaCP);

IF @SoloSimular = 0
BEGIN
    COMMIT TRANSACTION;
    PRINT CONCAT('NestoAPI#596: CPs portugueses normalizados (COMMIT). Códigos postales: ', @CPsQueCambian,
                 '; clientes: ', @ClientesCambiados, '; envíos pendientes: ', @EnviosCambiados, '.');
END
ELSE
BEGIN
    ROLLBACK TRANSACTION;
    PRINT CONCAT('NestoAPI#596: SIMULACIÓN. Cambiarían ', @CPsQueCambian, ' códigos postales, ', @ClientesCambiados,
                 ' clientes y ', @EnviosCambiados, ' envíos pendientes. Deshecho (ROLLBACK): pon @SoloSimular = 0 para aplicarlo.');
END
GO
