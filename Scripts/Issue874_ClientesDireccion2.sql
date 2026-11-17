/*
    NestoAPI#874 (Carlos 17/11/26): «Dirección 2» del cliente (piso, local + rótulo, casa, nave u otro) y su tipo.

    Qué hace (como sa en NV; se puede lanzar entero y repetir):
      0) Informa (no cambia nada) de los disparadores de Clientes y de los módulos que hacen SELECT * / INSERT ... SELECT *
         sobre Clientes: una columna nueva puede romper un «INSERT INTO otra SELECT * FROM inserted».
      1) Clientes.Direccion2 nvarchar(100) NULL   (si no existe; lote propio)
      2) Clientes.TipoLugar  varchar(10)  NULL   (si no existe; lote propio)
      3) CK_Clientes_TipoLugar: NULL o Piso/Local/Casa/Nave/Otro (si no existe; mismos valores que TiposLugar.TODOS)
      4) Interruptor ParametrosUsuario «(defecto)» ExigirDireccion2Alta = '0' (solo si no existe: no pisa un '1' ya puesto)
      5) Comprobación

    Columnas fuera del EDMX: la API las lee y escribe con SQL plano (Direccion2ClienteSql). Sin GRANT: la API ya hace
    UPDATE en Clientes y un ALTER TABLE ADD no lo necesita. Las columnas son NULL sin DEFAULT: el ALTER es solo de metadatos.

    Disparadores (copia del repo: Issue716_trgClientesUpd_BloqueSyncEficiente.sql + OneShot_20261112): trgClientesUpd trabaja
    columna a columna con UPDATE(col) y nombra siempre las columnas; no copia la fila entera a ninguna tabla. El UPDATE de la API
    (solo Direccion2 y TipoLugar, tras el alta) no entra en ningún bloque ni encola en Nesto_sync. El paso 0 lo comprueba en
    la BD de verdad: si lista algo con «*», mírelo antes de seguir.

    El interruptor nace APAGADO ('0'): NestoApp no manda TipoLugar hasta su gemela (NestoApp#286) y con '1' no podría dar altas.
    Nesto (Nesto#643) ya lo exige en su pantalla, así que sus altas llevan la Dirección 2 aunque esté a '0'. Encender con
        UPDATE dbo.ParámetrosUsuario SET Valor = '1' WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ExigirDireccion2Alta';
    cuando NestoApp#286 esté publicada y no queden versiones viejas.
*/
USE NV;
GO
SET NOCOUNT ON;
GO

-- 0) Solo informa
SELECT t.name AS Disparador, t.is_disabled AS Desactivado
FROM sys.triggers t WHERE t.parent_id = OBJECT_ID('dbo.Clientes');

SELECT OBJECT_SCHEMA_NAME(m.object_id) + '.' + OBJECT_NAME(m.object_id) AS Modulo, o.type_desc AS Tipo
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
WHERE m.definition LIKE '%Clientes%'
  AND (m.definition LIKE '%inserted.*%' OR m.definition LIKE '%deleted.*%'
       OR m.definition LIKE '%select *%from%clientes%' OR m.definition LIKE '%select * from inserted%'
       OR m.definition LIKE '%select * from deleted%')
ORDER BY 1;
GO

-- 1) Direccion2
IF COL_LENGTH('dbo.Clientes', 'Direccion2') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.Clientes ADD Direccion2 nvarchar(100) NULL;');
    PRINT N'Añadida Clientes.Direccion2.';
END
ELSE
    PRINT N'Clientes.Direccion2 ya existía.';
GO

-- 2) TipoLugar
IF COL_LENGTH('dbo.Clientes', 'TipoLugar') IS NULL
BEGIN
    EXEC(N'ALTER TABLE dbo.Clientes ADD TipoLugar varchar(10) NULL;');
    PRINT N'Añadida Clientes.TipoLugar.';
END
ELSE
    PRINT N'Clientes.TipoLugar ya existía.';
GO

-- 3) Los valores de TipoLugar (si se añade uno en TiposLugar.TODOS, hay que cambiar esta restricción)
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Clientes_TipoLugar' AND parent_object_id = OBJECT_ID('dbo.Clientes'))
BEGIN
    EXEC(N'ALTER TABLE dbo.Clientes WITH CHECK ADD CONSTRAINT CK_Clientes_TipoLugar
           CHECK (TipoLugar IS NULL OR TipoLugar IN (''Piso'', ''Local'', ''Casa'', ''Nave'', ''Otro''));');
    PRINT N'Añadida CK_Clientes_TipoLugar.';
END
ELSE
    PRINT N'CK_Clientes_TipoLugar ya existía.';
GO

-- 4) Interruptor (apagado; no se pisa si ya existe)
IF NOT EXISTS (SELECT 1 FROM dbo.ParámetrosUsuario WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'ExigirDireccion2Alta')
    INSERT INTO dbo.ParámetrosUsuario (Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', '(defecto)', 'ExigirDireccion2Alta', '0', 'NestoAPI#874', GETDATE());
GO

-- 5) Comprobación
SELECT c.name AS Columna, TYPE_NAME(c.user_type_id) AS Tipo, c.max_length AS Bytes, c.is_nullable AS Nulos
FROM sys.columns c WHERE c.object_id = OBJECT_ID('dbo.Clientes') AND c.name IN ('Direccion2', 'TipoLugar');

SELECT name, definition FROM sys.check_constraints WHERE name = 'CK_Clientes_TipoLugar';

SELECT Empresa, Usuario, Clave, Valor, Usuario2, [Fecha Modificación]
FROM dbo.ParámetrosUsuario WHERE Clave = 'ExigirDireccion2Alta';
GO
