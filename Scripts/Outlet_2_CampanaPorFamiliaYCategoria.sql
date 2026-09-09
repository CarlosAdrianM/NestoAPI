-- =====================================================================================
-- Outlet por familia + categoría (paso 2 de 3): la campaña "Outlet" en DescuentosProducto
-- 09/09/2026 — NestoAPI#467
--
-- PRERREQUISITOS (el script se niega a escribir si no se cumplen)
--   1) Scripts/Outlet_1_DDL_SubGrupoProducto.sql ejecutado (columna SubGrupoProducto).
--   2) NestoAPI desplegada con el nivel familia+grupo+subgrupo del motor (#467). Si se
--      insertan estas filas con la API vieja, el nivel familia+grupo las leería como
--      "Maystar en COS" y descontaría el 15 % a TODA la cosmética de Maystar.
--
-- QUÉ HACE
--   Crea una fila de tarifa por marca × categoría Outlet (COS/OUT Outlet Estética, PEL/OUT
--   Outlet Peluquería, COS/OUM Outlet Maquillaje y Uñas) con el % que hoy aplica la regla
--   de catálogo de PrestaShop de esa marca, AudienciaOferta 2 "Profesionales y público" (o
--   1 "Solo profesionales" si la regla de PS era solo para ese grupo) y Campana = 'Outlet'.
--   Solo crea la fila en las categorías donde la marca tiene hoy productos publicables; si
--   mañana Maystar entra también en PEL/OUT, se añade la fila desde la pantalla de Campañas.
--   Después encola en Nesto_sync todos los productos alcanzados para que la pasada de
--   5 minutos los republique con el descuento.
--
-- CÓMO EJECUTARLO
--   Tal cual (@DryRun = 1) solo informa. Con @DryRun = 0, en SSMS (sa) contra NV.
--   Idempotente: no duplica filas ni encolados.
--
-- OJO (negocio): una fila de tarifa la cobra GestorPrecios en TODOS los pedidos (Nesto,
--   NestoApp, tienda). Hoy el profesional tiene el Outlet en la web pero no en mostrador ni
--   en la app; con esto se unifica (objetivo de #423).
-- =====================================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @DryRun  bit           = 1;
DECLARE @Campana nvarchar(100) = N'Outlet';
DECLARE @Usuario varchar(30)   = 'Outlet desde PrestaShop';
DECLARE @Hoy     date          = CAST(GETDATE() AS date);

IF COL_LENGTH('dbo.DescuentosProducto', 'SubGrupoProducto') IS NULL
BEGIN
    RAISERROR ('Falta la columna DescuentosProducto.SubGrupoProducto: ejecutar antes Outlet_1_DDL_SubGrupoProducto.sql', 16, 1);
    RETURN;
END

-- ---------------------------------------------------------------------------------
-- 1) Reglas de PrestaShop, marca a marca (informe "Reglas de precio del Outlet", 09/09/2026)
--    Familia = código de la familia en Nesto. SoloCategoria: NULL = cualquier categoría Outlet.
-- ---------------------------------------------------------------------------------
DECLARE @reglas TABLE (
    Familia       char(10)     NOT NULL,
    SoloCategoria varchar(7)   NULL,
    DescuentoPro  decimal(5,4) NOT NULL,
    DescuentoPub  decimal(5,4) NULL,      -- NULL = el público hereda el del profesional
    Audiencia     tinyint      NOT NULL,  -- 2 = profesionales y público, 1 = solo profesionales
    ReglaPS       varchar(40)  NOT NULL,
    Marca         varchar(50)  NOT NULL);

INSERT INTO @reglas (Familia, SoloCategoria, DescuentoPro, DescuentoPub, Audiencia, ReglaPS, Marca) VALUES
('agv',        NULL,      0.3500, NULL,   2, '#239',      'agv'),
('Ainhoa',     NULL,      0.2000, NULL,   2, '#249',      'Ainhoa'),
('Anubis',     NULL,      0.3089, 0.2500, 2, '#177-#180', 'Anubis Cosmetics'),        -- 30,89 % pro / 25 % público
('Ardell',     NULL,      0.3000, NULL,   2, '#174',      'Ardell'),
('Belclinic',  NULL,      0.2000, NULL,   2, '#588',      'Belclinic'),
('BEOX',       NULL,      0.2000, NULL,   2, '#590',      'BEOX'),
('Blancabarb', NULL,      0.2500, NULL,   2, '#240',      'Blanca de Barberá'),
('Cazcarra',   NULL,      0.2000, NULL,   2, '#242',      'Cazcarra - Ten Image Professional'),
('C.Foraneos', NULL,      0.3000, NULL,   2, '#189',      'Cosméticos Foráneos'),
('CV',         NULL,      0.2500, NULL,   2, '#182',      'CV Primary Essence'),
('Diapason',   NULL,      0.2000, NULL,   2, '#589',      'Diapason Cosmetics'),
('Doman',      NULL,      0.3000, NULL,   2, '#592',      'Doman'),
('Du',         NULL,      0.2000, NULL,   2, '#186',      'Du Cosmetics'),
('EvaProf',    NULL,      0.1500, NULL,   2, '#585',      'Eva Professional'),
('Faby',       NULL,      0.2500, NULL,   2, '#243',      'Faby'),
('Fama',       NULL,      0.2500, NULL,   2, '#236',      'Fama Fabre'),
('Giubra',     NULL,      0.2500, NULL,   2, '#237',      'Giubra'),
('Greenik',    NULL,      0.2000, NULL,   2, '#244',      'Greenik'),
('Irene Ríos', NULL,      0.2500, NULL,   2, '#187',      'Irene Ríos'),
('Kach',       NULL,      0.3500, NULL,   2, '#591',      'Kach'),
('Lisap',      NULL,      0.3500, NULL,   2, '#188',      'Lisap'),
('Masglo',     NULL,      0.3000, NULL,   2, '#169',      'Masglo'),
('Maystar',    NULL,      0.1500, NULL,   2, '#597',      'Maystar'),
('Mina',       NULL,      0.2000, NULL,   2, '#593',      'Mina'),
('ODA',        NULL,      0.1000, NULL,   2, '#586',      'ODA - Optimum Derma Acidate'),
('Paraiso',    NULL,      0.1000, NULL,   2, '#245',      'Paraíso Cosmetics'),
('Genéricos',  'PEL/OUT', 0.2500, NULL,   2, '#251',      'Productos Genéricos'),      -- la regla era solo Outlet Peluquería
('Schwarzkop', NULL,      0.5000, NULL,   2, '#587',      'Schwarzkopf'),
('Staleks',    NULL,      0.1500, NULL,   1, '#247',      'Staleks'),                  -- solo profesionales
('Starpil',    NULL,      0.1500, NULL,   1, '#594',      'Starpil'),                  -- solo profesionales
('Thuya',      NULL,      0.2500, NULL,   2, '#246',      'Thuya'),
('UniónLáser', NULL,      0.1500, NULL,   1, '#595',      'Unión Láser'),              -- solo profesionales
('Silverfox',  NULL,      0.1500, NULL,   1, '#596',      'Weelko'),                   -- solo profesionales (familia Silverfox)
-- Reglas que en PrestaShop se llaman "Outlet <marca>" pero descuentan la marca ENTERA.
-- Aquí solo alcanzan a la marca en Outlet; el resto de la marca pierde el descuento cuando
-- PS apague la regla (si se quiere conservar: campaña de FAMILIA aparte, sin subgrupo).
('Moyra',      NULL,      0.2500, NULL,   2, '#176 (marca entera en PS)', 'Moyra'),
('JorgeGarza', NULL,      0.4000, NULL,   2, '#235 (marca entera en PS)', 'Jorge de la Garza'),
('Valmy',      NULL,      0.2500, NULL,   2, '#248 (marca entera en PS)', 'Valmy');

SELECT 'FAMILIA INEXISTENTE' AS aviso, r.Familia, r.Marca
FROM @reglas r
WHERE NOT EXISTS (SELECT 1 FROM dbo.Familias f WHERE f.Empresa = '1' AND f.Número = r.Familia);

-- ---------------------------------------------------------------------------------
-- 2) Filas a crear: marca × categoría Outlet con productos publicables hoy
-- ---------------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#filas')     IS NOT NULL DROP TABLE #filas;
IF OBJECT_ID('tempdb..#alcance')   IS NOT NULL DROP TABLE #alcance;

SELECT r.Familia, s.Grupo, s.SubGrupo, r.DescuentoPro, r.DescuentoPub, r.Audiencia, r.ReglaPS, r.Marca,
       COUNT(DISTINCT p.Número) AS Productos,
       YaExiste = CASE WHEN EXISTS (
            SELECT 1 FROM dbo.DescuentosProducto d
            WHERE d.Empresa = '1' AND d.Familia = r.Familia AND d.GrupoProducto = s.Grupo
              AND d.SubGrupoProducto = s.SubGrupo AND d.[Nº Producto] IS NULL
              AND (d.[Nº Cliente] IS NULL OR RTRIM(d.[Nº Cliente]) = '')
              AND (d.NºProveedor  IS NULL OR RTRIM(d.NºProveedor)  = '')
              AND d.FiltroProducto IS NULL AND d.CantidadMínima < 2
              AND (d.FechaHasta IS NULL OR d.FechaHasta >= @Hoy)) THEN 1 ELSE 0 END
INTO #filas
FROM dbo.ProductosCategoriasSecundarias s
JOIN dbo.Productos p ON p.Empresa = s.Empresa AND p.Número = s.Número
JOIN @reglas r ON r.Familia = p.Familia
   AND (r.SoloCategoria IS NULL OR r.SoloCategoria = RTRIM(s.Grupo) + '/' + RTRIM(s.SubGrupo))
WHERE s.Empresa = '1' AND s.SubGrupo IN ('OUT', 'OUM') AND p.Estado >= 0
GROUP BY r.Familia, s.Grupo, s.SubGrupo, r.DescuentoPro, r.DescuentoPub, r.Audiencia, r.ReglaPS, r.Marca;

-- Productos alcanzados (los que hay que republicar)
SELECT DISTINCT RTRIM(p.Número) AS Producto, f.Marca, f.DescuentoPro
INTO #alcance
FROM #filas f
JOIN dbo.ProductosCategoriasSecundarias s ON s.Empresa = '1' AND s.Grupo = f.Grupo AND s.SubGrupo = f.SubGrupo
JOIN dbo.Productos p ON p.Empresa = s.Empresa AND p.Número = s.Número AND p.Familia = f.Familia AND p.Estado >= 0;

-- ---------------------------------------------------------------------------------
-- 3) Informe
-- ---------------------------------------------------------------------------------
-- 3a) Las filas, una por marca × categoría
SELECT f.Marca, RTRIM(f.Familia) AS Familia, RTRIM(f.Grupo) + '/' + RTRIM(f.SubGrupo) AS Categoria, f.ReglaPS,
       CAST(f.DescuentoPro * 100 AS decimal(5,2)) AS PctPro,
       CAST(ISNULL(f.DescuentoPub, f.DescuentoPro) * 100 AS decimal(5,2)) AS PctPublico,
       CASE f.Audiencia WHEN 2 THEN 'Profesionales y público' ELSE 'Solo profesionales' END AS SePublicaA,
       f.Productos, CASE f.YaExiste WHEN 1 THEN 'ya existe' ELSE 'nueva' END AS Estado
FROM #filas f ORDER BY f.Marca, Categoria;

-- 3b) Filas de PRODUCTO de tarifa vigentes sobre productos alcanzados. En el motor el nivel
--     de producto gana si es MAYOR: si su % es mayor que el de la categoría, Nesto cobra ese
--     y la tienda anuncia el de la categoría (solo si la fila de producto no viaja, aud 0).
--     Decidir: borrarlas / cerrarlas (FechaHasta) / ponerles audiencia.
SELECT 'FILA DE PRODUCTO SOBRE UN OUTLET' AS aviso, a.Marca, a.Producto, d.[Nº Orden] AS Id,
       CAST(CASE WHEN d.Descuento > 0 THEN d.Descuento
                 WHEN d.Precio > 0 AND p.PVP > 0 THEN 1 - d.Precio / p.PVP ELSE 0 END * 100 AS decimal(5,2)) AS PctProducto,
       CAST(a.DescuentoPro * 100 AS decimal(5,2)) AS PctOutlet,
       d.Precio, d.AudienciaOferta, RTRIM(d.Campana) AS Campana, d.FechaHasta,
       CASE WHEN (CASE WHEN d.Descuento > 0 THEN d.Descuento WHEN d.Precio > 0 AND p.PVP > 0 THEN 1 - d.Precio / p.PVP ELSE 0 END) > a.DescuentoPro
            THEN 'gana el producto: Nesto cobra más descuento del que anuncia la tienda' ELSE 'gana el Outlet' END AS Efecto
FROM #alcance a
JOIN dbo.Productos p ON p.Empresa = '1' AND p.Número = a.Producto
JOIN dbo.DescuentosProducto d ON d.Empresa = '1' AND RTRIM(d.[Nº Producto]) = a.Producto
WHERE (d.[Nº Cliente] IS NULL OR RTRIM(d.[Nº Cliente]) = '')
  AND (d.NºProveedor  IS NULL OR RTRIM(d.NºProveedor)  = '')
  AND d.FiltroProducto IS NULL AND d.CantidadMínima < 2
  AND (d.FechaHasta IS NULL OR d.FechaHasta >= @Hoy)
ORDER BY a.Marca, a.Producto;

-- 3c) Filas de FAMILIA (sin subgrupo) vigentes de esas marcas: en el motor la categoría las
--     sobrescribe, así que solo son informativas (p. ej. Jorge de la Garza 30 % aud 0)
SELECT 'FILA DE FAMILIA EXISTENTE' AS aviso, r.Marca, d.[Nº Orden] AS Id, RTRIM(d.GrupoProducto) AS Grupo,
       CAST(d.Descuento * 100 AS decimal(5,2)) AS Pct, d.AudienciaOferta, RTRIM(d.Campana) AS Campana, d.FechaHasta
FROM dbo.DescuentosProducto d JOIN @reglas r ON r.Familia = d.Familia
WHERE d.Empresa = '1' AND d.SubGrupoProducto IS NULL AND d.[Nº Producto] IS NULL
  AND (d.[Nº Cliente] IS NULL OR RTRIM(d.[Nº Cliente]) = '')
  AND (d.NºProveedor  IS NULL OR RTRIM(d.NºProveedor)  = '')
  AND (d.FechaHasta IS NULL OR d.FechaHasta >= @Hoy)
ORDER BY r.Marca;

-- 3d) Marcas con productos publicables en Outlet SIN regla en PrestaShop (siguen sin descuento, como hoy)
SELECT 'SIN REGLA EN PS' AS aviso, RTRIM(fa.Descripción) AS Marca, RTRIM(p.Familia) AS Familia,
       RTRIM(s.Grupo) + '/' + RTRIM(s.SubGrupo) AS Categoria, COUNT(DISTINCT p.Número) AS Productos
FROM dbo.ProductosCategoriasSecundarias s
JOIN dbo.Productos p ON p.Empresa = s.Empresa AND p.Número = s.Número
JOIN dbo.Familias fa ON fa.Empresa = '1' AND fa.Número = p.Familia
WHERE s.Empresa = '1' AND s.SubGrupo IN ('OUT', 'OUM') AND p.Estado >= 0
  AND NOT EXISTS (SELECT 1 FROM @reglas r WHERE r.Familia = p.Familia)
GROUP BY fa.Descripción, p.Familia, s.Grupo, s.SubGrupo
ORDER BY Productos DESC;

DECLARE @nNuevas int = (SELECT COUNT(*) FROM #filas WHERE YaExiste = 0);
DECLARE @nExisten int = (SELECT COUNT(*) FROM #filas WHERE YaExiste = 1);
DECLARE @nProductos int = (SELECT COUNT(*) FROM #alcance);
PRINT 'Filas nuevas: ' + CAST(@nNuevas AS varchar) + ' | ya existentes: ' + CAST(@nExisten AS varchar)
    + ' | productos alcanzados (a republicar): ' + CAST(@nProductos AS varchar);

IF @DryRun = 1
BEGIN
    PRINT 'DRY RUN: no se ha escrito nada. Poner @DryRun = 0 para ejecutar.';
    RETURN;
END

-- ---------------------------------------------------------------------------------
-- 4) Escritura
-- ---------------------------------------------------------------------------------
BEGIN TRAN;

INSERT INTO dbo.DescuentosProducto
    (Empresa, Familia, GrupoProducto, SubGrupoProducto, CantidadMínima, Descuento, Precio,
     DescuentoPublico, AudienciaOferta, FechaDesde, FechaHasta, Campana, Usuario, [Fecha Modificación])
SELECT '1', f.Familia, f.Grupo, f.SubGrupo, 1, f.DescuentoPro, NULL,
       CASE WHEN f.Audiencia = 2 THEN f.DescuentoPub END,
       f.Audiencia, NULL, NULL, @Campana, @Usuario, GETDATE()
FROM #filas f
WHERE f.YaExiste = 0;
PRINT 'Insertadas: ' + CAST(@@ROWCOUNT AS varchar);

-- Republicación (misma guarda que NVEntities.EncolarProductosSync)
INSERT INTO dbo.Nesto_sync (Tabla, ModificadoId, Usuario, FechaModificacion)
SELECT 'Productos', a.Producto, @Usuario, GETDATE()
FROM #alcance a
WHERE NOT EXISTS (SELECT 1 FROM dbo.Nesto_sync ns
                  WHERE ns.Tabla = 'Productos' AND RTRIM(ns.ModificadoId) = a.Producto AND ns.Sincronizado IS NULL);
PRINT 'Encolados en Nesto_sync: ' + CAST(@@ROWCOUNT AS varchar);

COMMIT;

-- ---------------------------------------------------------------------------------
-- 5) Comprobación
-- ---------------------------------------------------------------------------------
SELECT RTRIM(Campana) AS Campana, AudienciaOferta, COUNT(*) AS Filas
FROM dbo.DescuentosProducto WHERE Empresa = '1' AND Campana = @Campana
GROUP BY Campana, AudienciaOferta;

SELECT COUNT(*) AS PendientesNestoSync FROM dbo.Nesto_sync WHERE Sincronizado IS NULL;

-- ---------------------------------------------------------------------------------
-- MARCHA ATRÁS: desde la pantalla de Campañas ("Outlet" → borrar campaña) o por la API,
--   DELETE /api/Campanas/PorNombre/Outlet, que borra las filas Y republica lo alcanzado.
--   Por SQL: DELETE dbo.DescuentosProducto WHERE Empresa='1' AND Campana=N'Outlet' AND
--   SubGrupoProducto IS NOT NULL; y volver a encolar los productos de #alcance.
-- ---------------------------------------------------------------------------------
