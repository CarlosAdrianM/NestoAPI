/*
    30/10/26 (Carlos): ruta de reparto de los códigos postales de Madrid Y de sus clientes, según el Excel
    «rutas por dias.xlsx» (hoja «Hoja1 (2)», la corregida): 01 lunes (N-I), 02 martes (N-II), 03 miércoles (N-III),
    04 jueves (N-VI), 05 viernes (N-IV, N-V y carretera de Toledo). Los de Madrid que no están en el Excel pasan a 00
    (no hay ninguna ruta «Sin ruta»).
    - Solo empresa 1 (las rutas que valen son las de la 1). Solo códigos de 5 cifras 28xxx, España y provincia Madrid
      (se quedan fuera 28204 LA LAGUNA/Tenerife y 28207 BREMEN).
    - Los códigos con ruta 16 (Ruta diaria, nuestra ruta de la zona de Algete) NO se tocan, ni ellos ni sus clientes.
    - Pueblos traducidos a códigos por la descripción: Alcalá = Alcalá de Henares (sin 28812 Valverde de Alcalá);
      Torrejón = Torrejón de Ardoz; Las Rozas sin Rozas de Puerto Real; Guadarrama sin 28608 «Apeadero»;
      El Escorial incluye 28200 San Lorenzo de El Escorial; Aravaca va por el 28023.
    - 28053 estaba en martes Y miércoles en el Excel: se deja en MARTES (como en la hoja 1). Cambiar abajo si no.
    - CLIENTES: de los códigos postales que cambian, solo los clientes cuya ruta es la que tenía su código postal
      (los heredados: FW, 00). Los que tienen otra ruta (de vendedor, AT, REI, 16…) no se tocan. Por LOTES de 2.000
      (el trigger trgClientesUpd es cuadrático con muchas filas).
    - Activa las rutas 01-05 de la empresa 1 (estaban Activa = 0).
    - 2.ª parte: pone el país a 23 códigos postales extranjeros que estaban como España o sin país.

    Lanzar como sa en DC2016. Por defecto @Aplicar = 0: solo enseña lo que cambiaría.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @Aplicar bit = 0;

DECLARE @Rutas TABLE (CP varchar(5) PRIMARY KEY, Ruta char(3) NOT NULL);
INSERT INTO @Rutas (CP, Ruta) VALUES
    ('28001','03'),
    ('28002','03'),
    ('28003','01'),
    ('28004','01'),
    ('28005','05'),
    ('28006','03'),
    ('28007','02'),
    ('28008','01'),
    ('28009','02'),
    ('28010','01'),
    ('28011','05'),
    ('28012','05'),
    ('28013','01'),
    ('28014','03'),
    ('28015','01'),
    ('28016','03'),
    ('28017','03'),
    ('28018','02'),
    ('28019','05'),
    ('28020','01'),
    ('28021','05'),
    ('28022','02'),
    ('28023','04'),
    ('28024','05'),
    ('28025','05'),
    ('28026','05'),
    ('28027','03'),
    ('28028','03'),
    ('28029','01'),
    ('28030','02'),
    ('28031','03'),
    ('28032','03'),
    ('28033','02'),
    ('28034','01'),
    ('28035','01'),
    ('28036','03'),
    ('28037','03'),
    ('28038','02'),
    ('28039','01'),
    ('28040','01'),
    ('28041','05'),
    ('28042','02'),
    ('28043','02'),
    ('28044','05'),
    ('28045','05'),
    ('28046','03'),
    ('28047','05'),
    ('28048','04'),
    ('28049','01'),
    ('28050','01'),
    ('28051','03'),
    ('28052','03'),
    ('28053','02'),
    ('28054','03'),
    ('28055','01'),
    ('28100','01'),
    ('28108','01'),
    ('28109','01'),
    ('28200','04'),
    ('28210','04'),
    ('28211','04'),
    ('28219','04'),
    ('28220','04'),
    ('28221','04'),
    ('28222','04'),
    ('28223','04'),
    ('28224','04'),
    ('28229','04'),
    ('28230','04'),
    ('28231','04'),
    ('28232','04'),
    ('28240','04'),
    ('28248','04'),
    ('28250','04'),
    ('28260','04'),
    ('28280','04'),
    ('28292','04'),
    ('28300','05'),
    ('28312','05'),
    ('28320','05'),
    ('28330','05'),
    ('28339','05'),
    ('28340','05'),
    ('28341','05'),
    ('28342','05'),
    ('28343','05'),
    ('28350','05'),
    ('28370','05'),
    ('28380','05'),
    ('28400','04'),
    ('28411','04'),
    ('28430','04'),
    ('28439','04'),
    ('28440','04'),
    ('28450','04'),
    ('28459','04'),
    ('28480','04'),
    ('28490','04'),
    ('28500','03'),
    ('28510','03'),
    ('28520','03'),
    ('28521','03'),
    ('28522','03'),
    ('28523','03'),
    ('28524','03'),
    ('28529','03'),
    ('28530','03'),
    ('28540','03'),
    ('28550','03'),
    ('28560','03'),
    ('28600','05'),
    ('28670','05'),
    ('28679','05'),
    ('28690','04'),
    ('28691','04'),
    ('28700','01'),
    ('28701','01'),
    ('28702','01'),
    ('28703','01'),
    ('28706','01'),
    ('28707','01'),
    ('28708','01'),
    ('28709','01'),
    ('28710','01'),
    ('28750','01'),
    ('28760','01'),
    ('28770','01'),
    ('28780','01'),
    ('28790','01'),
    ('28800','02'),
    ('28801','02'),
    ('28802','02'),
    ('28803','02'),
    ('28804','02'),
    ('28805','02'),
    ('28806','02'),
    ('28807','02'),
    ('28808','02'),
    ('28809','02'),
    ('28810','02'),
    ('28813','02'),
    ('28819','02'),
    ('28820','02'),
    ('28821','02'),
    ('28822','02'),
    ('28823','02'),
    ('28830','02'),
    ('28831','02'),
    ('28840','02'),
    ('28850','02'),
    ('28870','02'),
    ('28880','02'),
    ('28890','02'),
    ('28900','05'),
    ('28901','05'),
    ('28902','05'),
    ('28903','05'),
    ('28904','05'),
    ('28905','05'),
    ('28906','05'),
    ('28907','05'),
    ('28908','05'),
    ('28909','05'),
    ('28910','05'),
    ('28911','05'),
    ('28912','05'),
    ('28913','05'),
    ('28914','05'),
    ('28915','05'),
    ('28916','05'),
    ('28917','05'),
    ('28918','05'),
    ('28919','05'),
    ('28920','05'),
    ('28921','05'),
    ('28922','05'),
    ('28923','05'),
    ('28924','05'),
    ('28925','05'),
    ('28930','05'),
    ('28931','05'),
    ('28932','05'),
    ('28933','05'),
    ('28934','05'),
    ('28935','05'),
    ('28936','05'),
    ('28937','05'),
    ('28938','05'),
    ('28939','05'),
    ('28940','05'),
    ('28941','05'),
    ('28942','05'),
    ('28943','05'),
    ('28944','05'),
    ('28945','05'),
    ('28946','05'),
    ('28947','05'),
    ('28971','05'),
    ('28980','05'),
    ('28981','05'),
    ('28982','05'),
    ('28983','05'),
    ('28984','05');

DECLARE @Paises TABLE (CP varchar(15) PRIMARY KEY, Pais varchar(2) NOT NULL);
INSERT INTO @Paises (CP, Pais) VALUES
    ('04460','MX'), ('1030','BE'), ('1050','BE'), ('1069','NL'), ('1204','CH'), ('16014','IT'), ('1936','CH'),
    ('2000','BE'), ('2046','CH'), ('2555','CH'), ('2580','PT'), ('2665','PT'), ('2819','NL'), ('2912','NL'),
    ('4532','CY'), ('4537','BE'), ('6240','BE'), ('8042','AT'), ('8680','AT'), ('9210','FR'),
    ('00153','IT'), ('85680','FR'), ('85740','FR');

-- Los códigos postales que cambian, con su ruta de antes y la nueva (se calcula ANTES de tocar nada)
DECLARE @Cambios TABLE (CP varchar(5) PRIMARY KEY, Vieja varchar(3) NOT NULL, Nueva varchar(3) NOT NULL);
INSERT INTO @Cambios (CP, Vieja, Nueva)
SELECT RTRIM(cp.Número), RTRIM(cp.Ruta), RTRIM(ISNULL(r.Ruta, '00'))
FROM dbo.[CódigosPostales] cp
LEFT JOIN @Rutas r ON r.CP = RTRIM(cp.Número)
WHERE cp.Empresa = '1' AND RTRIM(cp.Número) LIKE '28[0-9][0-9][0-9]' AND LEN(RTRIM(cp.Número)) = 5
  AND cp.Pais = 'ES' AND cp.Provincia LIKE 'MADRID%'
  AND RTRIM(cp.Ruta) <> '16'
  AND RTRIM(cp.Ruta) <> RTRIM(ISNULL(r.Ruta, '00'));

SELECT Vieja AS RutaActual, Nueva AS RutaNueva, COUNT(*) AS CodigosPostales FROM @Cambios GROUP BY Vieja, Nueva ORDER BY 1, 2;

SELECT x.Vieja AS RutaActual, x.Nueva AS RutaNueva, COUNT(*) AS Clientes, SUM(CASE WHEN c.Estado >= 0 THEN 1 ELSE 0 END) AS Activos
FROM dbo.Clientes c JOIN @Cambios x ON x.CP = RTRIM(c.CodPostal)
WHERE c.Empresa = '1' AND RTRIM(c.Ruta) = x.Vieja
GROUP BY x.Vieja, x.Nueva ORDER BY 1, 2;

SELECT r.CP AS EnElExcelPeroNoEnLaTabla, r.Ruta FROM @Rutas r
WHERE NOT EXISTS (SELECT 1 FROM dbo.[CódigosPostales] cp WHERE cp.Empresa = '1' AND RTRIM(cp.Número) = r.CP);

SELECT RTRIM(cp.Número) AS CP, RTRIM(cp.Descripción) AS Descripcion, cp.Pais AS PaisActual, p.Pais AS PaisNuevo
FROM dbo.[CódigosPostales] cp JOIN @Paises p ON p.CP = RTRIM(cp.Número)
WHERE cp.Empresa = '1' AND ISNULL(cp.Pais, '') <> p.Pais;

IF @Aplicar = 0
BEGIN
    PRINT 'Solo se ha mirado. Para aplicar, poner @Aplicar = 1.';
    RETURN;
END

-- 1) Rutas 01-05 activas
UPDATE dbo.Rutas SET Activa = 1 WHERE Empresa = '1' AND RTRIM(Número) IN ('01','02','03','04','05') AND Activa = 0;
PRINT CONCAT('Rutas activadas: ', @@ROWCOUNT);

-- 2) Códigos postales
UPDATE cp SET Ruta = x.Nueva
FROM dbo.[CódigosPostales] cp JOIN @Cambios x ON x.CP = RTRIM(cp.Número)
WHERE cp.Empresa = '1' AND RTRIM(cp.Ruta) = x.Vieja;
PRINT CONCAT('Códigos postales cambiados: ', @@ROWCOUNT);

-- 3) Clientes, por lotes (idempotente: solo los que siguen con la ruta vieja)
DECLARE @Lote int = 1, @Total int = 0;
WHILE @Lote > 0
BEGIN
    ;WITH Pendientes AS (
        SELECT TOP (2000) c.Ruta, x.Nueva
        FROM dbo.Clientes c JOIN @Cambios x ON x.CP = RTRIM(c.CodPostal)
        WHERE c.Empresa = '1' AND RTRIM(c.Ruta) = x.Vieja
    )
    UPDATE Pendientes SET Ruta = Nueva;
    SET @Lote = @@ROWCOUNT;
    SET @Total += @Lote;
    PRINT CONCAT('  lote de clientes: ', @Lote, ' (total ', @Total, ')');
END
PRINT CONCAT('Clientes cambiados: ', @Total);

-- 4) Países de los extranjeros
UPDATE cp SET Pais = p.Pais
FROM dbo.[CódigosPostales] cp JOIN @Paises p ON p.CP = RTRIM(cp.Número)
WHERE cp.Empresa = '1' AND ISNULL(cp.Pais, '') <> p.Pais;
PRINT CONCAT('Países puestos: ', @@ROWCOUNT);
