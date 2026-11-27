/*
    NestoAPI#913 corte 2 (27/11/26): el producto y el número de serie del aparato como DATO del inmovilizado, no solo escritos en la
    descripción («… ref.44276 n/s SHE-RFS009-2511111», que además es char(50) y a veces se corta).

    OPCIONAL. La API funciona igual sin él: GET api/Aparatos/Inmovilizados/Propuestas, POST api/Aparatos/Inmovilizados/Enlazar y los
    descuadres leen la serie del texto. Con él:
      - la lectura usa las columnas cuando están rellenas (mandan sobre el texto);
      - al enlazar un aparato con su inmovilizado, la API rellena también Producto y NumeroSerie de la ficha (solo si están vacías).
    No se rellena nada aquí: las columnas nacen vacías y se van llenando con cada enlace por la API (con su rastro en Modificaciones).

    Qué hace (se puede lanzar más de una vez):
      1. Añade dbo.Inmovilizados.Producto char(15) NULL y dbo.Inmovilizados.NumeroSerie varchar(30) NULL (los mismos tipos que
         CabAlquileres.Producto y CabAlquileres.NumeroSerie), cada una solo si no existe.
      2. GRANT UPDATE en dbo.Inmovilizados a la cuenta de la API (hasta hoy solo la LEÍA: verbo nuevo).
      3. GRANT UPDATE en dbo.CabAlquileres (ya lo tiene por la ventana de Alquileres; se repite porque GRANT es idempotente).

    ANTES de lanzarlo, comprobar que nada inserta en Inmovilizados SIN lista de columnas (INSERT INTO Inmovilizados VALUES (…) o
    INSERT … SELECT *), en Nesto viejo, en algún prd* o en el alta de inmovilizados de la contabilidad: dos columnas más lo romperían.
    Consulta para mirarlo (solo lectura):
        SELECT OBJECT_NAME(object_id) AS Objeto FROM sys.sql_modules WHERE definition LIKE '%INSERT%Inmovilizados%';

    Lanzar como sa en la BD NV.
*/
USE NV;
GO
IF COL_LENGTH('dbo.Inmovilizados', 'Producto') IS NULL
    EXEC (N'ALTER TABLE dbo.Inmovilizados ADD Producto char(15) NULL;');
GO
IF COL_LENGTH('dbo.Inmovilizados', 'NumeroSerie') IS NULL
    EXEC (N'ALTER TABLE dbo.Inmovilizados ADD NumeroSerie varchar(30) NULL;');
GO
GRANT UPDATE ON dbo.Inmovilizados TO [NUEVAVISION\RDS2016$];
GO
GRANT UPDATE ON dbo.CabAlquileres TO [NUEVAVISION\RDS2016$];
GO
-- Comprobación: las dos columnas y los permisos de la API
SELECT c.name AS Columna, t.name AS Tipo, c.max_length AS Longitud, c.is_nullable AS AdmiteNull
FROM sys.columns c
JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.Inmovilizados') AND c.name IN ('Producto', 'NumeroSerie');

SELECT OBJECT_NAME(pe.major_id) AS Tabla, pr.name AS Principal, pe.permission_name AS Permiso
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.major_id IN (OBJECT_ID('dbo.Inmovilizados'), OBJECT_ID('dbo.CabAlquileres')) AND pr.name = 'NUEVAVISION\RDS2016$';
GO
