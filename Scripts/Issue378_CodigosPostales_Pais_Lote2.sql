-- Issue #378 — LOTE 2: clasificación manual de los 115 CPs que quedaron con Pais NULL
-- tras el primer backfill. Clasificados uno a uno a partir del listado del 03/08 (número +
-- población + provincia). Guard Pais IS NULL en todos los UPDATE: si ya se corrigió alguno
-- desde la ventana de mantenimiento, no se pisa.
--
-- Quedan deliberadamente en NULL: '' / '0' (basura) y '99999 PRUEBA' (test).
-- OJO: '00000 LAYOS (TOLEDO)', '0611 CACERES' y '80500 MOLINA DE SEGURA (MURCIA)' son
-- clientes ESPAÑOLES con el CP mal grabado: se les pone ES aquí y el número se corrige
-- cuando toque desde la ventana (cambiar el número es tocar la PK y la FK de Clientes).
--
-- EJECUTAR EN: NV (DC2016\SQL2017).

-- Revisión previa: qué se va a clasificar y cómo
SELECT RTRIM(Empresa) AS Empresa, RTRIM(Número) AS Numero, RTRIM(Descripción) AS Poblacion, RTRIM(Provincia) AS Provincia
FROM dbo.CódigosPostales
WHERE Pais IS NULL
ORDER BY Número, Empresa;

-- Italia (provincias italianas: ROMA, NAPOLI, CASERTA, SALERNO, PALERMO...)
UPDATE dbo.CódigosPostales SET Pais = 'IT'
WHERE Pais IS NULL AND RTRIM(Número) IN (
    '00012','00049','00142','00175','00192','00195','58100','64032','72017','73017',
    '80026','80063','81020','81055','83020','84014','84084','90018','90041','91019','98039');

-- Francia (Provincia FRANCE/SAVOIE + 97232 Martinica = FR ultramar)
UPDATE dbo.CódigosPostales SET Pais = 'FR'
WHERE Pais IS NULL AND RTRIM(Número) IN (
    '55600','62114','63730','69003','73700','78120','79100','83700','93290','93330','97232');

-- Alemania
UPDATE dbo.CódigosPostales SET Pais = 'DE'
WHERE Pais IS NULL AND RTRIM(Número) IN ('60439','60488','79798');

-- Reino Unido (postcodes; BT = Irlanda del Norte → GB)
UPDATE dbo.CódigosPostales SET Pais = 'GB'
WHERE Pais IS NULL AND RTRIM(Número) IN (
    'BR3 4RL','BT132QU','BT36 4PE','BXN17 7HD','CM4 0QW','EC1A 9HP','EC2A 2FA','EC4R 3TT',
    'HU12 8EE','IV1 3XW','N16 6RJ','NN4 8BY','NW6 1HG','SE11 5NH','SW1E 5BH','W3 8ED');

-- Irlanda (Eircodes + Tubbercurry CO. SLIGO)
UPDATE dbo.CódigosPostales SET Pais = 'IE'
WHERE Pais IS NULL AND RTRIM(Número) IN ('A63 W897','D04 X2K5','K67NY94','EIRA');

-- Países Bajos
UPDATE dbo.CódigosPostales SET Pais = 'NL'
WHERE Pais IS NULL AND RTRIM(Número) IN ('1051 HA','1107DL','1343 AR','2912R','3068 JN');

-- México (MEJICO/MÉXICO D.F./SONORA/QUINTANA ROO)
UPDATE dbo.CódigosPostales SET Pais = 'MX'
WHERE Pais IS NULL AND RTRIM(Número) IN ('0670O','76137','77539','83200');

-- Estados Unidos (TX, ARIZONA, UNITED STATES)
UPDATE dbo.CódigosPostales SET Pais = 'US'
WHERE Pais IS NULL AND RTRIM(Número) IN ('77459','85138','MN55447');

-- Portugal (rezagados: Provincia LISBOA / MADEIRA)
UPDATE dbo.CódigosPostales SET Pais = 'PT'
WHERE Pais IS NULL AND RTRIM(Número) IN ('1250149','9000042');

-- Resto de sueltos
UPDATE dbo.CódigosPostales SET Pais = 'CZ' WHERE Pais IS NULL AND RTRIM(Número) = '150 00';   -- PRAHA
UPDATE dbo.CódigosPostales SET Pais = 'RO' WHERE Pais IS NULL AND RTRIM(Número) = '615200';   -- TARGU NEAMT
UPDATE dbo.CódigosPostales SET Pais = 'CN' WHERE Pais IS NULL AND RTRIM(Número) = '261061';   -- WEIFANG
UPDATE dbo.CódigosPostales SET Pais = 'CL' WHERE Pais IS NULL AND RTRIM(Número) = '207';      -- CALAMA
UPDATE dbo.CódigosPostales SET Pais = 'BR' WHERE Pais IS NULL AND RTRIM(Número) = '70000';    -- BRASILIA
UPDATE dbo.CódigosPostales SET Pais = 'SV' WHERE Pais IS NULL AND RTRIM(Número) = '951';      -- EL SALVADOR
UPDATE dbo.CódigosPostales SET Pais = 'CH' WHERE Pais IS NULL AND RTRIM(Número) = '407';      -- VERBIER (VALAIS)
UPDATE dbo.CódigosPostales SET Pais = 'LU' WHERE Pais IS NULL AND RTRIM(Número) = 'L1855';    -- LUXEMBURGO
UPDATE dbo.CódigosPostales SET Pais = 'LV' WHERE Pais IS NULL AND RTRIM(Número) = 'LV-1026';  -- RIGA

-- Españoles con CP mal grabado (número se corregirá aparte)
UPDATE dbo.CódigosPostales SET Pais = 'ES'
WHERE Pais IS NULL AND RTRIM(Número) IN ('00000','0611','80500');

-- Verificación: deberían quedar solo la basura ('', '0') y '99999' de prueba
SELECT RTRIM(Empresa) AS Empresa, RTRIM(Número) AS Numero, RTRIM(Descripción) AS Poblacion, RTRIM(Provincia) AS Provincia
FROM dbo.CódigosPostales
WHERE Pais IS NULL
ORDER BY Número, Empresa;

SELECT ISNULL(Pais,'(null)') AS Pais, COUNT(*) AS N
FROM dbo.CódigosPostales
GROUP BY ISNULL(Pais,'(null)')
ORDER BY N DESC;
