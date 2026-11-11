/*
    Feria online 12-16/11/2026 (Carlos 11/11/26). Dos tablas fuera del EDMX que lee la API por SQL; lanzar como sa en NV ANTES
    de publicar la API. Idempotente: se puede relanzar entero (no duplica filas ni pisa las ya activadas/restauradas).

    1. dbo.CampanasCondicionContado: los productos de una campaña cuyas ofertas exigen pago al contado. Al CREAR un pedido (o
       presupuesto) con alguno de ellos y sin ir ya al contado (plazos CONTADO, CR o PRE, forma TAR o con prepago), Nesto pregunta
       «¿Cambiar a contado?» (POST api/PedidosVenta/AvisoCondicionesCampana). No bloquea nada. Caché de 10 min en la API.
       Sin tabla o sin filas vigentes: nunca avisa.

    2. dbo.CampanasAplicarDto: las fichas con «Aplicar Dto» = 0 que llevan oferta en la campaña. El job de Hangfire
       «campanas-aplicar-dto» (cada hora en punto y al arrancar la API) les pone «Aplicar Dto» = 1 desde las 00:00 del primer día
       (guarda el valor anterior en AplicarDtoAnterior/FechaActivado) y se lo devuelve desde las 00:00 del día siguiente al
       último (FechaRestaurado), solo si sigue a 1 (si alguien la cambió, no la pisa y lo apunta en ELMAH). Cada cambio deja su
       fila en Modificaciones. Sustituye a los OneShot_20261111_Feria_AplicarDto_Activar / OneShot_20261117_..._Restaurar.
       Mientras esté a 1, a esas fichas también les aplican los descuentos generales de cada cliente (aceptado, Carlos 10/11/26).
       Cambiar «Aplicar Dto» no encola nada en Nesto_sync (trgProductosUpd no lo mira), pero sí pone Revisado = 0 en la ficha.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.CampanasCondicionContado', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampanasCondicionContado (
        Campana varchar(50) NOT NULL,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        FechaDesde date NOT NULL,
        FechaHasta date NOT NULL,
        TextoAviso nvarchar(500) NULL,          -- NULL: el texto por defecto de la API
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CampanasCondicionContado_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CampanasCondicionContado PRIMARY KEY CLUSTERED (Campana, Empresa, Producto),
        CONSTRAINT CK_CampanasCondicionContado_Fechas CHECK (FechaDesde <= FechaHasta)
    );
END
GO
GRANT SELECT ON dbo.CampanasCondicionContado TO [NUEVAVISION\RDS2016$];
GO
IF OBJECT_ID(N'dbo.CampanasAplicarDto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CampanasAplicarDto (
        Campana varchar(50) NOT NULL,
        Empresa char(3) NOT NULL,
        Producto char(15) NOT NULL,
        FechaDesde date NOT NULL,
        FechaHasta date NOT NULL,
        AplicarDtoAnterior bit NULL,            -- lo que tenía la ficha al activarla (lo pone el job)
        FechaActivado datetime NULL,            -- lo pone el job
        FechaRestaurado datetime NULL,          -- lo pone el job
        Usuario varchar(50) NOT NULL,
        FechaModificacion datetime NOT NULL CONSTRAINT DF_CampanasAplicarDto_FechaModificacion DEFAULT (GETDATE()),
        CONSTRAINT PK_CampanasAplicarDto PRIMARY KEY CLUSTERED (Campana, Empresa, Producto),
        CONSTRAINT CK_CampanasAplicarDto_Fechas CHECK (FechaDesde <= FechaHasta)
    );
END
GO
GRANT SELECT, UPDATE ON dbo.CampanasAplicarDto TO [NUEVAVISION\RDS2016$];
GO

-- 1. Feria online: las 126 referencias con condición de contado (empresa 1, del 12 al 16/11/2026).
DECLARE @campana varchar(50) = 'FERIA-ONLINE-1126';
DECLARE @refs TABLE (Producto char(15) NOT NULL PRIMARY KEY);
INSERT INTO @refs (Producto) VALUES
        ('45939'),
        ('45940'),
        ('45938'),
        ('45941'),
        ('45942'),
        ('45655'),
        ('45382'),
        ('45492'),
        ('45184'),
        ('45493'),
        ('45183'),
        ('45958'),
        ('45664'),
        ('45665'),
        ('45304'),
        ('45305'),
        ('45253'),
        ('45438'),
        ('45441'),
        ('45437'),
        ('45434'),
        ('45087'),
        ('45086'),
        ('45462'),
        ('45687'),
        ('45686'),
        ('45370'),
        ('45369'),
        ('45383'),
        ('45915'),
        ('45313'),
        ('45927'),
        ('45926'),
        ('45307'),
        ('45961'),
        ('45208'),
        ('45145'),
        ('45929'),
        ('45930'),
        ('45931'),
        ('45932'),
        ('45466'),
        ('45465'),
        ('45464'),
        ('45945'),
        ('45946'),
        ('45947'),
        ('45948'),
        ('45949'),
        ('45950'),
        ('45951'),
        ('45952'),
        ('45953'),
        ('45954'),
        ('45452'),
        ('45472'),
        ('45474'),
        ('45475'),
        ('45427'),
        ('45429'),
        ('45428'),
        ('44999'),
        ('45256'),
        ('45272'),
        ('45935'),
        ('45934'),
        ('45374'),
        ('45274'),
        ('41159'),
        ('43119'),
        ('43376'),
        ('43377'),
        ('43948'),
        ('44245'),
        ('44470'),
        ('45095'),
        ('45136'),
        ('45185'),
        ('43953'),
        ('43954'),
        ('43955'),
        ('43956'),
        ('43957'),
        ('43958'),
        ('45962'),
        ('45960'),
        ('45959'),
        ('45299'),
        ('45300'),
        ('45301'),
        ('45302'),
        ('45670'),
        ('45239'),
        ('45240'),
        ('45241'),
        ('45242'),
        ('45243'),
        ('45244'),
        ('45342'),
        ('45343'),
        ('45344'),
        ('45345'),
        ('45346'),
        ('45347'),
        ('45348'),
        ('23532'),
        ('23595'),
        ('23527'),
        ('36384'),
        ('23531'),
        ('43428'),
        ('43579'),
        ('23474'),
        ('23600'),
        ('23599'),
        ('36383'),
        ('24114'),
        ('23604'),
        ('43451'),
        ('43580'),
        ('36398'),
        ('44276'),
        ('43593'),
        ('45146'),
        ('45147'),
        ('45148');

INSERT INTO dbo.CampanasCondicionContado (Campana, Empresa, Producto, FechaDesde, FechaHasta, TextoAviso, Usuario)
SELECT @campana, '1', r.Producto, '20261112', '20261116', NULL, 'NUEVAVISION\Carlos'
FROM @refs r
WHERE NOT EXISTS (SELECT 1 FROM dbo.CampanasCondicionContado c
                  WHERE c.Campana = @campana AND c.Empresa = '1' AND c.Producto = r.Producto);
PRINT N'Contado: filas nuevas ' + CAST(@@ROWCOUNT AS nvarchar(10));

-- Referencias de la lista que no tienen ficha en la empresa 1 (no deberían salir; si salen, revíselas).
SELECT r.Producto AS SinFichaEnEmpresa1
FROM @refs r
WHERE NOT EXISTS (SELECT 1 FROM dbo.Productos p WHERE p.Empresa = '1' AND p.Número = r.Producto);
GO

-- 2. Feria online: las 13 fichas a desbloquear (12 de la empresa 1 + la 45492 de la empresa 3), del 12 al 16/11/2026.
DECLARE @campana varchar(50) = 'FERIA-ONLINE-1126';
DECLARE @fichas TABLE (Empresa char(3) NOT NULL, Producto char(15) NOT NULL, PRIMARY KEY (Empresa, Producto));
INSERT INTO @fichas (Empresa, Producto) VALUES
        ('1', '45342'), ('1', '45343'), ('1', '45344'), ('1', '45345'), ('1', '45346'), ('1', '45347'), ('1', '45348'),
        ('1', '45492'), ('3', '45492'), ('1', '45493'), ('1', '45941'), ('1', '45383'), ('1', '45670');

INSERT INTO dbo.CampanasAplicarDto (Campana, Empresa, Producto, FechaDesde, FechaHasta, Usuario)
SELECT @campana, f.Empresa, f.Producto, '20261112', '20261116', 'NUEVAVISION\Carlos'
FROM @fichas f
WHERE EXISTS (SELECT 1 FROM dbo.Productos p WHERE p.Empresa = f.Empresa AND p.Número = f.Producto)
  AND NOT EXISTS (SELECT 1 FROM dbo.CampanasAplicarDto c
                  WHERE c.Campana = @campana AND c.Empresa = f.Empresa AND c.Producto = f.Producto);
PRINT N'Aplicar Dto: filas nuevas ' + CAST(@@ROWCOUNT AS nvarchar(10));

SELECT f.Empresa, f.Producto AS SinFicha
FROM @fichas f
WHERE NOT EXISTS (SELECT 1 FROM dbo.Productos p WHERE p.Empresa = f.Empresa AND p.Número = f.Producto);
GO

-- Comprobación
SELECT Campana, COUNT(*) AS Productos, MIN(FechaDesde) AS Desde, MAX(FechaHasta) AS Hasta
FROM dbo.CampanasCondicionContado GROUP BY Campana;

SELECT RTRIM(c.Empresa) AS Empresa, RTRIM(c.Producto) AS Producto, RTRIM(p.Nombre) AS Nombre, p.[Aplicar Dto] AS AplicarDtoAhora,
       c.FechaDesde, c.FechaHasta, c.AplicarDtoAnterior, c.FechaActivado, c.FechaRestaurado
FROM dbo.CampanasAplicarDto c
LEFT JOIN dbo.Productos p ON p.Empresa = c.Empresa AND p.Número = c.Producto
ORDER BY c.Campana, c.Producto, c.Empresa;
