/*
    NestoAPI#590 (Carlos 09/11/26): las altas de clientes (y de contactos / direcciones de entrega) y los cambios de dirección se
    hacen en Nesto, que pasa por la API y valida la dirección con Google Maps. Nesto viejo escribe en Clientes con el login de
    cada usuario y sin validar nada.
    Requiere antes Issue590_fnEsNestoViejo.sql. Como sa en NV. Se puede lanzar entero y repetir (idempotente).

    EMPIEZA EN MODO SOMBRA: no bloquea nada, solo apunta en dbo.NestoViejoSombra (Operacion 'Direcciones') cada alta y cada
    cambio de dirección hecho desde Nesto viejo. Cuando la sombra diga que ya nadie lo necesita (o se haya avisado a quien sale):
        PASAR A BLOQUEO:  UPDATE ParametrosUsuario SET Valor = 'Bloqueo', [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_Direcciones_Modo';
    INTERRUPTOR (vuelta a sombra, una línea):
        UPDATE ParametrosUsuario SET Valor = 'Sombra',  [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_Direcciones_Modo';
    Apagado total: DISABLE TRIGGER dbo.trgClientesNestoViejoDirecciones ON dbo.Clientes;
    Excepción para un usuario (sigue pudiendo en modo Bloqueo, y queda apuntado como 'Permitido'):
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'NestoViejo_Direcciones', 'Alfredo', '1', 'NestoAPI#590', GETDATE());

    Columnas de dirección de dbo.Clientes (INFORMATION_SCHEMA.COLUMNS, 09/11/26; no hay latitud/longitud ni otras):
        Dirección char(50), CodPostal char(15), Población char(30), Provincia char(30), Pais varchar(2).
    Se consideran «alta» todas las filas nuevas (INSERT), sea cliente principal o contacto (PK Empresa, [Nº Cliente], Contacto):
    un contacto nuevo es una dirección de entrega nueva.

    RENDIMIENTO (Clientes tiene un trgClientesUpd pesado y hay UPDATE masivos, ver memoria «UPDATE masivos por lotes»):
      - La PRIMERA línea es IF dbo.fnEsNestoViejo() = 0 RETURN: para la API, SSMS, sa y los jobs (todo lo masivo) el coste es
        llamar a una función que solo mira APP_NAME(). No lee ninguna tabla.
      - Siendo Nesto viejo, si la sentencia es un UPDATE que no lleva ninguna columna de dirección en el SET (UPDATE()), sale sin mirar
        filas. Solo cuando la lleva compara inserted con deleted (por la PK, sin cursores) para quedarse con los cambios DE VERDAD
        (Nesto viejo puede mandar la columna con el mismo valor). La comparación sigue la intercalación de la columna
        (Modern_Spanish_CI_AI): un cambio solo de mayúsculas o de tildes no cuenta como cambio.
      - Se pone como PRIMER desencadenador de INSERT y UPDATE (sp_settriggerorder): en modo Bloqueo deshace antes de que corra
        trgClientesUpd (correos de cambio de dirección, Nesto_sync, LogModificaciones…). OJO: un ALTER de ESTE desencadenador le
        quita el «First»: volver a lanzar este script entero (lo vuelve a poner).
    Lo que NO cambia: trgClientesIns ya impedía desde 2019 crear clientes principales (y contactos con NIF) desde Nesto viejo salvo
    a Alfredo, Enrique, Carlos, Laura y sa; sigue igual. En sombra, lo que ese desencadenador deshace no llega a quedar apuntado.

    PRUEBA (tras lanzarlo): conexión de SSMS con «Opciones > Parámetros de conexión adicionales»: Application Name=Nesto
        BEGIN TRAN;
        UPDATE TOP (1) dbo.Clientes SET Dirección = RTRIM(Dirección) + ' X' WHERE Empresa = '1' AND Estado >= 0 AND LEN(Dirección) < 45;
        SELECT TOP 5 * FROM dbo.NestoViejoSombra WHERE Operacion = 'Direcciones' ORDER BY Id DESC;   -- tiene que salir 'Sombra'
        ROLLBACK;
    (En sombra la fila de NestoViejoSombra se deshace con el ROLLBACK de la prueba; es normal.)
    Ojo: esa prueba dispara también trgClientesUpd (correo «Cambio de Dirección» si no se deshace a tiempo: el ROLLBACK lo anula).
*/
USE NV;
GO
SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID(N'dbo.fnEsNestoViejo') IS NULL OR OBJECT_ID(N'dbo.fnNestoViejoModo') IS NULL
   OR OBJECT_ID(N'dbo.fnNestoViejoPermitido') IS NULL OR OBJECT_ID(N'dbo.NestoViejoSombra') IS NULL
BEGIN
    RAISERROR(N'Falta Issue590_fnEsNestoViejo.sql: lánzalo antes y vuelve a lanzar este. No se ha tocado nada.', 16, 1);
    SET NOEXEC ON;   -- el resto del script ya no se ejecuta
END
GO

-- ANTES: desencadenadores de Clientes y su orden
SELECT 'ANTES' Momento, t.name, t.is_disabled,
       OBJECTPROPERTY(t.object_id, 'ExecIsFirstInsertTrigger') PrimeroInsert, OBJECTPROPERTY(t.object_id, 'ExecIsFirstUpdateTrigger') PrimeroUpdate,
       (SELECT STRING_AGG(e.type_desc, ',') FROM sys.trigger_events e WHERE e.object_id = t.object_id) Eventos
FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.Clientes');
SELECT 'ANTES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]Direcciones%';
GO

-- 1) Filas «(defecto)»: modo Sombra y sin excepciones (si ya existen, NO se tocan: no se pisa un 'Bloqueo' ya puesto)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_Direcciones_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_Direcciones_Modo', '(defecto)', 'Sombra', 'NestoAPI#590', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_Direcciones' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_Direcciones', '(defecto)', '0', 'NestoAPI#590', GETDATE());
GO

-- 2) El desencadenador
CREATE OR ALTER TRIGGER dbo.trgClientesNestoViejoDirecciones ON dbo.Clientes
AFTER INSERT, UPDATE
AS
BEGIN
    -- NestoAPI#590 09/11/26: altas y direcciones de clientes solo desde Nesto (API + Google Maps).
    IF dbo.fnEsNestoViejo() = 0 RETURN;     -- API, SSMS, sa, jobs: nada más (primera línea a propósito)
    SET NOCOUNT ON;

    DECLARE @hayDeleted bit = CASE WHEN EXISTS (SELECT 1 FROM deleted) THEN 1 ELSE 0 END;
    IF @hayDeleted = 1
       AND NOT (UPDATE(Dirección) OR UPDATE(CodPostal) OR UPDATE(Población) OR UPDATE(Provincia) OR UPDATE(Pais))
        RETURN;                             -- UPDATE sin columnas de dirección en el SET (Vendedor, Ruta, Estado...)

    DECLARE @cambios TABLE (Clave nvarchar(100), Detalle nvarchar(2000));
    INSERT @cambios (Clave, Detalle)
    SELECT TOP (200)
           LEFT(RTRIM(i.Empresa) + N'/' + RTRIM(i.[Nº Cliente]) + N'/' + RTRIM(i.Contacto), 100),
           LEFT(CASE WHEN d.Empresa IS NULL
                     THEN N'ALTA: ' + ISNULL(RTRIM(i.Nombre), N'') + N' | ' + ISNULL(RTRIM(i.Dirección), N'') + N' | ' + ISNULL(RTRIM(i.CodPostal), N'')
                          + N' ' + ISNULL(RTRIM(i.Población), N'') + N' (' + ISNULL(RTRIM(i.Provincia), N'') + N') ' + ISNULL(i.Pais, N'')
                     ELSE N'CAMBIO: ' + ISNULL(RTRIM(d.Dirección), N'') + N' | ' + ISNULL(RTRIM(d.CodPostal), N'') + N' ' + ISNULL(RTRIM(d.Población), N'')
                          + N' (' + ISNULL(RTRIM(d.Provincia), N'') + N') ' + ISNULL(d.Pais, N'')
                          + N'  ->  ' + ISNULL(RTRIM(i.Dirección), N'') + N' | ' + ISNULL(RTRIM(i.CodPostal), N'') + N' ' + ISNULL(RTRIM(i.Población), N'')
                          + N' (' + ISNULL(RTRIM(i.Provincia), N'') + N') ' + ISNULL(i.Pais, N'')
                END, 2000)
    FROM inserted i
    LEFT JOIN deleted d ON d.Empresa = i.Empresa AND d.[Nº Cliente] = i.[Nº Cliente] AND d.Contacto = i.Contacto
    WHERE d.Empresa IS NULL                                   -- alta (o cambio de la clave)
       OR ISNULL(i.Dirección, '') <> ISNULL(d.Dirección, '')
       OR ISNULL(i.CodPostal, '') <> ISNULL(d.CodPostal, '')
       OR ISNULL(i.Población, '') <> ISNULL(d.Población, '')
       OR ISNULL(i.Provincia, '') <> ISNULL(d.Provincia, '')
       OR ISNULL(i.Pais, '')      <> ISNULL(d.Pais, '');

    IF NOT EXISTS (SELECT 1 FROM @cambios) RETURN;            -- llevaba la columna, pero con el mismo valor

    DECLARE @modo varchar(10) = dbo.fnNestoViejoModo('Direcciones');
    DECLARE @permitido bit = dbo.fnNestoViejoPermitido('Direcciones');

    IF @modo = 'Bloqueo' AND @permitido = 0
    BEGIN
        RAISERROR (N'Esto ya solo se puede hacer desde Nesto: las altas de clientes y de direcciones de entrega, y los cambios de dirección (dirección, código postal, población, provincia y país), se hacen en Nesto, que comprueba la dirección. No se ha guardado nada.', 16, 1);
        ROLLBACK TRANSACTION;
        -- Tras el ROLLBACK del desencadenador, esto ya no se deshace: queda el rastro del intento.
        INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
        SELECT 'Direcciones', 'Bloqueado', 'Clientes', Clave, Detalle FROM @cambios;
        RETURN;
    END

    INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
    SELECT 'Direcciones', CASE WHEN @modo = 'Bloqueo' THEN 'Permitido' ELSE 'Sombra' END, 'Clientes', Clave, Detalle FROM @cambios;
END
GO

-- 3) Primero de la fila (el ALTER de arriba le quita el «First»; aquí se le vuelve a poner). Solo si ningún otro lo es.
IF NOT EXISTS (SELECT 1 FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.Clientes') AND t.name <> N'trgClientesNestoViejoDirecciones'
               AND OBJECTPROPERTY(t.object_id, 'ExecIsFirstInsertTrigger') = 1)
    EXEC sp_settriggerorder @triggername = N'dbo.trgClientesNestoViejoDirecciones', @order = N'First', @stmttype = N'INSERT';
ELSE PRINT 'Otro desencadenador de Clientes ya es el primero de INSERT: no se cambia el orden.';
IF NOT EXISTS (SELECT 1 FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.Clientes') AND t.name <> N'trgClientesNestoViejoDirecciones'
               AND OBJECTPROPERTY(t.object_id, 'ExecIsFirstUpdateTrigger') = 1)
    EXEC sp_settriggerorder @triggername = N'dbo.trgClientesNestoViejoDirecciones', @order = N'First', @stmttype = N'UPDATE';
ELSE PRINT 'Otro desencadenador de Clientes ya es el primero de UPDATE: no se cambia el orden.';
GO

-- DESPUÉS: trgClientesNestoViejoDirecciones con PrimeroInsert = 1 y PrimeroUpdate = 1; trgClientesIns y trgClientesUpd sin cambios
SELECT 'DESPUES' Momento, t.name, t.is_disabled, t.modify_date,
       OBJECTPROPERTY(t.object_id, 'ExecIsFirstInsertTrigger') PrimeroInsert, OBJECTPROPERTY(t.object_id, 'ExecIsFirstUpdateTrigger') PrimeroUpdate,
       (SELECT STRING_AGG(e.type_desc, ',') FROM sys.trigger_events e WHERE e.object_id = t.object_id) Eventos
FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.Clientes');
SELECT 'DESPUES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]Direcciones%';
GO
SET NOEXEC OFF;
GO

-- SEGUIMIENTO de la sombra (quién, cuántas, qué):
-- SELECT Usuario, Resultado, COUNT(*) Veces, MIN(Fecha) Primera, MAX(Fecha) Ultima
-- FROM dbo.NestoViejoSombra WHERE Operacion = 'Direcciones' GROUP BY Usuario, Resultado ORDER BY Veces DESC;
-- SELECT TOP 100 * FROM dbo.NestoViejoSombra WHERE Operacion = 'Direcciones' ORDER BY Id DESC;

/* VUELTA ATRÁS completa (el interruptor de arriba suele bastar):
DROP TRIGGER IF EXISTS dbo.trgClientesNestoViejoDirecciones;
*/
