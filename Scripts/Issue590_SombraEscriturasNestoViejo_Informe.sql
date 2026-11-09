/*
    NestoAPI#590: informe de la sombra de escrituras de Nesto viejo (sesión XE de Issue590_SombraEscriturasNestoViejo_XE.sql).
    Como sa. Solo lee los ficheros .xel; no cambia nada. Se puede lanzar con la sesión en marcha o ya parada.

    OJO, carga: leer los .xel (hasta 1 GB) y abrir el XML cuesta CPU en el servidor. Lanzarlo fuera de horario (o con @Desde
    reciente). Se lee UNA vez a #Eventos y todos los resúmenes salen de ahí.

    Qué sale:
      1) Por objeto (tabla escrita o procedimiento llamado) y tipo: veces, usuarios distintos, filas, primera y última vez, un ejemplo.
         Es la lista de lo que queda por «capar» (o de lo que ya tiene camino en Nesto y se puede bloquear).
      2) Por objeto y usuario: quién hace cada cosa (a quién hay que avisar o enseñar antes de bloquearlo).
      3) Por programa: Nesto (Nesto viejo) frente a DataProj (¿Ariadna vieja?).
      4) Sentencias de cursor de servidor (sp_cursor) agrupadas por su texto (la tabla va dentro).
    «Tabla» se saca del texto de la sentencia (lo que va detrás de INSERT INTO / UPDATE / DELETE FROM / MERGE INTO): es aproximado
    (en «UPDATE c SET … FROM Clientes c» sale el alias); el ejemplo de la última columna ayuda a reconocerlo.
*/
SET NOCOUNT ON;
DECLARE @Desde datetime2(0) = '20261109';    -- <-- desde cuándo (hora UTC de los eventos)

IF OBJECT_ID('tempdb..#Eventos') IS NOT NULL DROP TABLE #Eventos;

;WITH xe AS (
    SELECT CAST(f.event_data AS xml) AS x
    FROM sys.fn_xe_file_target_read_file(N'Issue590_SombraEscriturasNestoViejo*.xel', NULL, NULL, NULL) f
    WHERE f.timestamp_utc >= @Desde       -- columna disponible desde SQL Server 2017
)
SELECT IDENTITY(int, 1, 1)                                                                AS Id,
       x.value('(event/@timestamp)[1]', 'datetime2(0)')                                   AS MomentoUtc,
       x.value('(event/@name)[1]', 'varchar(40)')                                         AS Evento,
       x.value('(event/action[@name="client_app_name"]/value)[1]', 'nvarchar(128)')       AS Programa,
       x.value('(event/action[@name="server_principal_name"]/value)[1]', 'nvarchar(128)') AS Usuario,
       x.value('(event/action[@name="client_hostname"]/value)[1]', 'nvarchar(128)')       AS Equipo,
       x.value('(event/data[@name="object_name"]/value)[1]', 'nvarchar(256)')             AS Objeto,
       x.value('(event/data[@name="row_count"]/value)[1]', 'bigint')                      AS Filas,
       x.value('(event/data[@name="statement"]/value)[1]', 'nvarchar(max)')               AS Texto
INTO #Eventos
FROM xe;

-- Tipo de sentencia y tabla escrita, sacados del texto
ALTER TABLE #Eventos ADD Tipo varchar(10) NULL, Tabla nvarchar(256) NULL;
GO
;WITH t AS (
    SELECT e.Id, e.Texto,
           -- texto en minúsculas, sin saltos de línea y con un espacio delante (para que la palabra clave pueda ir al principio)
           N' ' + LOWER(REPLACE(REPLACE(REPLACE(LEFT(ISNULL(e.Texto, N''), 4000), NCHAR(13), N' '), NCHAR(10), N' '), NCHAR(9), N' ')) COLLATE Latin1_General_BIN AS s
    FROM #Eventos e
), p AS (
    SELECT t.*,
           NULLIF(PATINDEX(N'%[^a-z0-9_]insert %', s), 0) AS pI,
           NULLIF(PATINDEX(N'%[^a-z0-9_]update %', s), 0) AS pU,
           NULLIF(PATINDEX(N'%[^a-z0-9_]delete %', s), 0) AS pD,
           NULLIF(PATINDEX(N'%[^a-z0-9_]merge %',  s), 0) AS pM
    FROM t
), k AS (   -- la primera palabra clave que aparece
    SELECT p.*, v.Clase, v.Pos, v.Largo
    FROM p
    OUTER APPLY (SELECT TOP (1) Clase, Pos, Largo
                 FROM (VALUES ('INSERT', pI, 8), ('UPDATE', pU, 8), ('DELETE', pD, 8), ('MERGE', pM, 7)) a(Clase, Pos, Largo)
                 WHERE Pos IS NOT NULL ORDER BY Pos) v
), r AS (   -- lo que va detrás, sin «into» / «from» / «top (n)»
    SELECT k.*, LTRIM(SUBSTRING(s, Pos + Largo, 400)) AS resto FROM k WHERE Pos IS NOT NULL
), r2 AS (
    SELECT r.*,
           LTRIM(CASE WHEN resto LIKE N'into %' THEN SUBSTRING(resto, 6, 400)
                      WHEN resto LIKE N'from %' THEN SUBSTRING(resto, 6, 400)
                      WHEN resto LIKE N'top%'   THEN SUBSTRING(resto, CHARINDEX(N')', resto) + 1, 400)
                      ELSE resto END) AS resto2
    FROM r
), r3 AS (
    SELECT r2.*,
           LTRIM(CASE WHEN resto2 LIKE N'into %' THEN SUBSTRING(resto2, 6, 400)
                      WHEN resto2 LIKE N'from %' THEN SUBSTRING(resto2, 6, 400) ELSE resto2 END) AS resto3
    FROM r2
)
UPDATE e SET
    Tipo  = r3.Clase,
    -- primer «token» hasta espacio o paréntesis; sin comillas ni corchetes; solo el último trozo de NV..tabla / dbo.tabla
    Tabla = REVERSE(LEFT(REVERSE(x.tok), CHARINDEX(N'.', REVERSE(x.tok) + N'.') - 1))
FROM #Eventos e
JOIN r3 ON r3.Id = e.Id
CROSS APPLY (SELECT REPLACE(REPLACE(REPLACE(LEFT(r3.resto3, PATINDEX(N'%[ (]%', r3.resto3 + N' ') - 1), N'"', N''), N'[', N''), N']', N'') AS tok) x;

UPDATE #Eventos SET Tipo = 'EXEC', Tabla = Objeto
WHERE Evento = 'rpc_completed' AND Objeto LIKE N'prd%';
UPDATE #Eventos SET Tipo = 'CURSOR', Tabla = N'(sp_cursor)'
WHERE Evento = 'rpc_completed' AND Objeto = N'sp_cursor';

SELECT COUNT(*) Eventos, MIN(MomentoUtc) Desde, MAX(MomentoUtc) Hasta, COUNT(DISTINCT Usuario) Usuarios FROM #Eventos;

-- 1) Por objeto y tipo
SELECT Tipo, Tabla, Evento, COUNT(*) Veces, COUNT(DISTINCT Usuario) Usuarios, SUM(Filas) Filas,
       MIN(MomentoUtc) Primera, MAX(MomentoUtc) Ultima,
       MAX(LEFT(Texto, 300)) Ejemplo
FROM #Eventos
GROUP BY Tipo, Tabla, Evento
ORDER BY Veces DESC;

-- 2) Por objeto y usuario
SELECT Tipo, Tabla, Usuario, COUNT(*) Veces, SUM(Filas) Filas, MAX(MomentoUtc) Ultima
FROM #Eventos
GROUP BY Tipo, Tabla, Usuario
ORDER BY Tabla, Veces DESC;

-- 3) Por programa
SELECT Programa, Usuario, Equipo, COUNT(*) Veces, COUNT(DISTINCT Tabla) Objetos, MIN(MomentoUtc) Primera, MAX(MomentoUtc) Ultima
FROM #Eventos
GROUP BY Programa, Usuario, Equipo
ORDER BY Programa, Veces DESC;

-- 4) Cursores de servidor (la tabla va dentro del texto)
SELECT LEFT(Texto, 200) Sentencia, Programa, COUNT(*) Veces, COUNT(DISTINCT Usuario) Usuarios
FROM #Eventos WHERE Tipo = 'CURSOR'
GROUP BY LEFT(Texto, 200), Programa
ORDER BY Veces DESC;

-- Lo que no se ha sabido clasificar (si sale mucho, mirarlo a mano)
SELECT TOP (50) Evento, Objeto, Usuario, LEFT(Texto, 300) Texto FROM #Eventos WHERE Tipo IS NULL ORDER BY MomentoUtc DESC;
