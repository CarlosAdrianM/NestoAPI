-- ===========================================================================
-- Script: Issue148_Paises_semilla_completa.sql
-- Fecha:  23/07/2026
-- Issue:  NestoAPI#148 / Nesto#428 (completar la tabla Paises)
--
-- Anade a dbo.Paises los paises que falten (Europa completa + America + principales de
-- Asia/Africa/Oceania), para que el SelectorPais (alta de cliente, NIF incorrectos...) los
-- ofrezca todos y no solo los ~35 iniciales (p.ej. Ucrania no estaba).
--
-- Id = codigo numerico ISO 3166-1 (como los ya existentes). Codigo = alfa-2. UnionEuropea = bit.
-- IDEMPOTENTE: solo inserta los que NO existen ya (por Codigo). Re-ejecutable sin problema.
-- La app usa Codigo, no Id, asi que es solo datos de referencia (riesgo nulo).
-- ===========================================================================

SET NOCOUNT ON;

;WITH nuevos (Id, Codigo, Nombre, UE) AS (
    SELECT * FROM (VALUES
    -- Europa (UE ya sembrada; se listan igualmente, el NOT EXISTS evita duplicar)
     (8,'AL','Albania',0),(20,'AD','Andorra',0),(40,'AT','Austria',1),(112,'BY','Bielorrusia',0),
     (56,'BE','Bélgica',1),(70,'BA','Bosnia y Herzegovina',0),(100,'BG','Bulgaria',1),(191,'HR','Croacia',1),
     (196,'CY','Chipre',1),(203,'CZ','Chequia',1),(208,'DK','Dinamarca',1),(233,'EE','Estonia',1),
     (246,'FI','Finlandia',1),(250,'FR','Francia',1),(268,'GE','Georgia',0),(276,'DE','Alemania',1),
     (292,'GI','Gibraltar',0),(300,'GR','Grecia',1),(348,'HU','Hungría',1),(352,'IS','Islandia',0),
     (372,'IE','Irlanda',1),(380,'IT','Italia',1),(428,'LV','Letonia',1),(438,'LI','Liechtenstein',0),
     (440,'LT','Lituania',1),(442,'LU','Luxemburgo',1),(470,'MT','Malta',1),(498,'MD','Moldavia',0),
     (492,'MC','Mónaco',0),(499,'ME','Montenegro',0),(528,'NL','Países Bajos',1),(807,'MK','Macedonia del Norte',0),
     (578,'NO','Noruega',0),(616,'PL','Polonia',1),(620,'PT','Portugal',1),(642,'RO','Rumanía',1),
     (643,'RU','Rusia',0),(674,'SM','San Marino',0),(688,'RS','Serbia',0),(703,'SK','Eslovaquia',1),
     (705,'SI','Eslovenia',1),(724,'ES','España',1),(752,'SE','Suecia',1),(756,'CH','Suiza',0),
     (792,'TR','Turquía',0),(804,'UA','Ucrania',0),(826,'GB','Reino Unido',0),(336,'VA','Ciudad del Vaticano',0),
    -- America
     (840,'US','Estados Unidos',0),(124,'CA','Canadá',0),(484,'MX','México',0),(320,'GT','Guatemala',0),
     (84,'BZ','Belice',0),(222,'SV','El Salvador',0),(340,'HN','Honduras',0),(558,'NI','Nicaragua',0),
     (188,'CR','Costa Rica',0),(591,'PA','Panamá',0),(170,'CO','Colombia',0),(862,'VE','Venezuela',0),
     (218,'EC','Ecuador',0),(604,'PE','Perú',0),(68,'BO','Bolivia',0),(600,'PY','Paraguay',0),
     (152,'CL','Chile',0),(32,'AR','Argentina',0),(858,'UY','Uruguay',0),(76,'BR','Brasil',0),
     (192,'CU','Cuba',0),(214,'DO','República Dominicana',0),(630,'PR','Puerto Rico',0),(388,'JM','Jamaica',0),
    -- Asia / Oriente Medio
     (156,'CN','China',0),(392,'JP','Japón',0),(410,'KR','Corea del Sur',0),(356,'IN','India',0),
     (360,'ID','Indonesia',0),(764,'TH','Tailandia',0),(704,'VN','Vietnam',0),(608,'PH','Filipinas',0),
     (458,'MY','Malasia',0),(702,'SG','Singapur',0),(344,'HK','Hong Kong',0),(158,'TW','Taiwán',0),
     (586,'PK','Pakistán',0),(50,'BD','Bangladés',0),(376,'IL','Israel',0),(784,'AE','Emiratos Árabes Unidos',0),
     (682,'SA','Arabia Saudí',0),(634,'QA','Catar',0),(414,'KW','Kuwait',0),(422,'LB','Líbano',0),
     (400,'JO','Jordania',0),(364,'IR','Irán',0),(368,'IQ','Irak',0),
    -- Africa
     (504,'MA','Marruecos',0),(12,'DZ','Argelia',0),(788,'TN','Túnez',0),(818,'EG','Egipto',0),
     (434,'LY','Libia',0),(710,'ZA','Sudáfrica',0),(566,'NG','Nigeria',0),(404,'KE','Kenia',0),
     (288,'GH','Ghana',0),(686,'SN','Senegal',0),(384,'CI','Costa de Marfil',0),(24,'AO','Angola',0),
     (508,'MZ','Mozambique',0),(231,'ET','Etiopía',0),(834,'TZ','Tanzania',0),(800,'UG','Uganda',0),
    -- Oceania
     (36,'AU','Australia',0),(554,'NZ','Nueva Zelanda',0)
    ) v(Id, Codigo, Nombre, UE)
)
INSERT INTO dbo.Paises (Id, Codigo, Nombre, UnionEuropea)
SELECT n.Id, n.Codigo, n.Nombre, CAST(n.UE AS bit)
FROM nuevos n
WHERE NOT EXISTS (SELECT 1 FROM dbo.Paises p WHERE p.Codigo = n.Codigo)
  AND NOT EXISTS (SELECT 1 FROM dbo.Paises p WHERE p.Id = n.Id);   -- por si algun Id ya existiera

-- @@ROWCOUNT se captura JUSTO despues del INSERT (cualquier otra sentencia lo resetea);
-- el COUNT total va aparte porque una subconsulta dentro de CONCAT/PRINT no esta permitida.
DECLARE @insertados int = @@ROWCOUNT;
DECLARE @total int = (SELECT COUNT(*) FROM dbo.Paises);
PRINT CONCAT('Paises insertados: ', @insertados, '. Total en la tabla: ', @total, '.');
GO
