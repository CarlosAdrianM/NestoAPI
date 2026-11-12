/*
    NestoAPI#590 (Carlos 12/11/26: «mira quién puede seguir facturando en Nesto viejo; hay que ir cortando ya para todo el mundo»).

    POR QUÉ: desde Nesto viejo todavía se pueden crear algunas facturas, deshacerlas y modificar su cabecera (el detalle de qué
    y quién está en la memoria de la sesión del 12/11/26, no aquí). La columna Usuario de la factura no dice por dónde entró,
    así que hoy NO se sabe quién usa de verdad cada cosa: primero se mide.

    QUÉ HACE ESTE SCRIPT: un único desencadenador en dbo.CabFacturaVta que, SOLO cuando la conexión es Nesto viejo
    (APP_NAME() = 'Nesto'), apunta en dbo.NestoViejoSombra (Operacion 'Facturas') cada factura creada (ALTA), deshecha (BAJA) o
    modificada (CAMBIO), con la serie, el número, el cliente y si vino de un procedimiento o de una sentencia directa.
    No toca los procedimientos. Requiere antes Issue590_fnEsNestoViejo.sql. Como sa en NV. Se puede lanzar entero y repetir.

    EMPIEZA EN MODO SOMBRA (solo apunta, no impide nada): primero hay que ver quién lo usa y avisarle.
    PASAR A BLOQUEAR (una línea; desde ese momento Nesto viejo no puede crear, deshacer ni modificar facturas):
        UPDATE ParametrosUsuario SET Valor = 'Bloqueo', [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_Facturas_Modo';
    VOLVER A SOLO APUNTAR:
        UPDATE ParametrosUsuario SET Valor = 'Sombra',  [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_Facturas_Modo';
    Apagado total: DISABLE TRIGGER dbo.trgCabFacturaVtaNestoViejo ON dbo.CabFacturaVta;
    Excepción para un usuario (sigue pudiendo en modo Bloqueo, y queda apuntado como 'Permitido'):
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'NestoViejo_Facturas', 'Alfredo', '1', 'NestoAPI#590', GETDATE());

    QUÉ NO AFECTA: la API (NUEVAVISION\RDS2016$, EntityFramework / NestoAPI-Hangfire), SSMS, sa y los jobs: primera línea,
    fnEsNestoViejo() = 0 -> RETURN. El coste para ellos es una función que solo mira APP_NAME().
    EN BLOQUEO: ROLLBACK de toda la sentencia (y, si venía de prdCrearFacturaVta o prdDeshacerFacturaVta, de toda su transacción:
    no queda nada a medias) y el mensaje «Esto ya solo se puede hacer desde Nesto…».

    ANTES DE PASAR A BLOQUEO hay que comprobar que Nesto (nuevo) cubre lo que salga en la sombra: facturar la serie GB, deshacer
    facturas y lo que resulte ser cada CAMBIO directo (¿marcas de impresión?, ¿cobros?). Ver el informe de abajo.

    PRUEBA (tras lanzarlo): conexión de SSMS con «Opciones > Parámetros de conexión adicionales»: Application Name=Nesto
        BEGIN TRAN;
        UPDATE TOP (1) dbo.CabFacturaVta SET Fecha = Fecha WHERE Empresa = '1' AND Fecha >= DATEADD(day, -2, GETDATE());
        ROLLBACK;   -- en Sombra el apunte se deshace con el ROLLBACK de la prueba; para verlo, COMMIT (no cambia ningún dato: Fecha = Fecha)
        SELECT TOP 5 * FROM dbo.NestoViejoSombra WHERE Operacion = 'Facturas' ORDER BY Id DESC;
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

-- ANTES
SELECT 'ANTES' Momento, t.name, t.is_disabled FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.CabFacturaVta');
SELECT 'ANTES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]Facturas%';
GO

-- 1) Filas «(defecto)»: modo SOMBRA y sin excepciones (si ya existen, NO se tocan)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_Facturas_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_Facturas_Modo', '(defecto)', 'Sombra', 'NestoAPI#590', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_Facturas' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_Facturas', '(defecto)', '0', 'NestoAPI#590', GETDATE());
GO

-- 2) El desencadenador
CREATE OR ALTER TRIGGER dbo.trgCabFacturaVtaNestoViejo ON dbo.CabFacturaVta
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    -- NestoAPI#590 12/11/26: las facturas solo se crean, deshacen y modifican desde Nesto (API).
    IF dbo.fnEsNestoViejo() = 0 RETURN;     -- API, SSMS, sa, jobs: nada más (primera línea a propósito)
    SET NOCOUNT ON;

    DECLARE @origen nvarchar(40) = CASE WHEN @@NESTLEVEL > 1 THEN N'por procedimiento' ELSE N'sentencia directa' END;
    DECLARE @cambios TABLE (Clave nvarchar(100), Detalle nvarchar(2000));

    INSERT @cambios (Clave, Detalle)
    SELECT TOP (200)
           LEFT(RTRIM(COALESCE(i.Empresa, d.Empresa)) + N'/' + RTRIM(COALESCE(i.[Número], d.[Número])), 100),
           CASE WHEN d.[Número] IS NULL THEN N'ALTA' WHEN i.[Número] IS NULL THEN N'BAJA' ELSE N'CAMBIO' END
             + N' serie ' + ISNULL(RTRIM(COALESCE(i.Serie, d.Serie)), N'')
             + N', cliente ' + ISNULL(RTRIM(COALESCE(i.[Nº Cliente], d.[Nº Cliente])), N'')
             + N', fecha ' + ISNULL(CONVERT(nvarchar(10), COALESCE(i.Fecha, d.Fecha), 103), N'')
             + N' (' + @origen + N')'
    FROM inserted i
    FULL OUTER JOIN deleted d ON d.Empresa = i.Empresa AND d.[Número] = i.[Número];

    IF NOT EXISTS (SELECT 1 FROM @cambios) RETURN;            -- la sentencia no tocó ninguna fila

    DECLARE @modo varchar(10) = dbo.fnNestoViejoModo('Facturas');
    DECLARE @permitido bit = dbo.fnNestoViejoPermitido('Facturas');

    IF @modo = 'Bloqueo' AND @permitido = 0
    BEGIN
        RAISERROR (N'Esto ya solo se puede hacer desde Nesto: las facturas se crean, se deshacen y se modifican en Nesto. No se ha guardado nada.', 16, 1);
        ROLLBACK TRANSACTION;
        -- Tras el ROLLBACK del desencadenador, esto ya no se deshace: queda el rastro del intento.
        INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
        SELECT 'Facturas', 'Bloqueado', 'CabFacturaVta', Clave, Detalle FROM @cambios;
        RETURN;
    END

    INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
    SELECT 'Facturas', CASE WHEN @modo = 'Bloqueo' THEN 'Permitido' ELSE 'Sombra' END, 'CabFacturaVta', Clave, Detalle FROM @cambios;
END
GO

-- DESPUÉS
SELECT 'DESPUES' Momento, t.name, t.is_disabled, t.modify_date FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.CabFacturaVta');
SELECT 'DESPUES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]Facturas%';
GO
SET NOEXEC OFF;
GO

/* INFORME (a los 3-5 días): quién hace qué desde Nesto viejo con las facturas
SELECT Usuario,
       CASE WHEN Detalle LIKE 'ALTA%' THEN 'Crear' WHEN Detalle LIKE 'BAJA%' THEN 'Deshacer' ELSE 'Modificar' END Que,
       CASE WHEN Detalle LIKE '%(por procedimiento)' THEN 'procedimiento' ELSE 'directo' END Como,
       COUNT(*) Veces, MIN(Fecha) Primera, MAX(Fecha) Ultima
FROM dbo.NestoViejoSombra WHERE Operacion = 'Facturas'
GROUP BY Usuario, CASE WHEN Detalle LIKE 'ALTA%' THEN 'Crear' WHEN Detalle LIKE 'BAJA%' THEN 'Deshacer' ELSE 'Modificar' END,
         CASE WHEN Detalle LIKE '%(por procedimiento)' THEN 'procedimiento' ELSE 'directo' END
ORDER BY Usuario, Que;
*/

/* VUELTA ATRÁS completa:
DROP TRIGGER IF EXISTS dbo.trgCabFacturaVtaNestoViejo;
*/
