/*
    NestoAPI#590 (Carlos 20/11/26): la forma de pago ([Forma Pago]) y el CCC de un pedido se cambian en Nesto (el pedido pasa por la
    API y sus reglas). Mismo patrón que Issue590_PlazosPagoPedido_BloqueoNestoViejo.sql (ya con la corrección del 20/11/26 de las altas).
    Requiere antes Issue590_fnEsNestoViejo.sql. Como sa en NV. Se puede lanzar entero y repetir (idempotente).

    EMPIEZA EN MODO SOMBRA: Nesto viejo sigue pudiendo cambiarlos y cada cambio queda apuntado en dbo.NestoViejoSombra
    (Operacion 'FormaPagoPedido'). Se pasará a Bloqueo cuando la sombra enseñe que no para nada legítimo.
    PASAR A BLOQUEO (una línea):
        UPDATE ParametrosUsuario SET Valor = 'Bloqueo', [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_FormaPagoPedido_Modo';
    VOLVER A SOLO APUNTAR:
        UPDATE ParametrosUsuario SET Valor = 'Sombra',  [Fecha Modificación] = GETDATE() WHERE Empresa = '1' AND Usuario = '(defecto)' AND Clave = 'NestoViejo_FormaPagoPedido_Modo';
    Apagado total: DISABLE TRIGGER dbo.trgCabPedidoVtaNestoViejoFormaPago ON dbo.CabPedidoVta;
    Excepción para un usuario (sigue pudiendo en modo Bloqueo, y queda apuntado como 'Permitido'):
        INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
        VALUES ('1', 'NestoViejo_FormaPagoPedido', 'Aida', '1', 'NestoAPI#590', GETDATE());

    QUÉ APUNTA (y en Bloqueo, bloquea): un UPDATE de dbo.CabPedidoVta hecho desde Nesto viejo (APP_NAME() = 'Nesto') que cambie DE
    VERDAD [Forma Pago] de un pedido QUE YA TENÍA forma de pago, o el CCC de un pedido QUE YA TENÍA CCC. Cada columna se mira por
    separado. LAS ALTAS NO: Nesto viejo crea un pedido insertando la cabecera vacía y rellenándola después con un UPDATE (de
    vacío/NULL al valor del cliente); eso no es un cambio y no se bloquea ni se apunta (la lección del 20/11/26 con los plazos: 37
    pedidos bloqueados). Por la misma razón, poner un CCC a un pedido que no lo tenía tampoco cuenta; quitarlo (de valor a vacío) sí.
    QUÉ NO MIRA: la API, SSMS, sa y los jobs (primera línea: fnEsNestoViejo() = 0 -> RETURN). Tampoco lo que cambie otro
    desencadenador o un procedimiento en cadena desde la misma conexión (TRIGGER_NESTLEVEL() > 1).
    No toca PlazosPago (lo lleva Issue590_PlazosPagoPedido_BloqueoNestoViejo.sql, desencadenador aparte).

    RENDIMIENTO: para todo lo que no es Nesto viejo, el coste es una función que solo mira APP_NAME(). Siendo Nesto viejo, si el
    UPDATE no lleva [Forma Pago] ni CCC en el SET, sale sin mirar filas.
    ORDEN: en Bloqueo hace ROLLBACK de toda la sentencia (también lo que hubieran hecho los demás desencadenadores de CabPedidoVta).

    PRUEBA (tras lanzarlo y pasar a Bloqueo): conexión de SSMS con «Opciones > Parámetros de conexión adicionales»: Application Name=Nesto
        BEGIN TRAN;
        UPDATE TOP (1) dbo.CabPedidoVta SET [Forma Pago] = 'EFC' WHERE Empresa = '1' AND Número = (SELECT MAX(Número) FROM dbo.CabPedidoVta
            WHERE Empresa = '1' AND ISNULL(RTRIM([Forma Pago]), '') NOT IN ('', 'EFC'));
        -- en Bloqueo tiene que dar el error «Esto ya solo se puede hacer desde Nesto…» y no cambiar nada; en Sombra, cambia y lo apunta
        IF @@TRANCOUNT > 0 ROLLBACK;
        SELECT TOP 5 * FROM dbo.NestoViejoSombra WHERE Operacion = 'FormaPagoPedido' ORDER BY Id DESC;
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
SELECT 'ANTES' Momento, t.name, t.is_disabled FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.CabPedidoVta');
SELECT 'ANTES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]FormaPagoPedido%';
GO

-- 1) Filas «(defecto)»: modo SOMBRA y sin excepciones (si ya existen, NO se tocan)
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_FormaPagoPedido_Modo' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_FormaPagoPedido_Modo', '(defecto)', 'Sombra', 'NestoAPI#590', GETDATE());
IF NOT EXISTS (SELECT 1 FROM ParametrosUsuario WHERE Empresa = '1' AND Clave = 'NestoViejo_FormaPagoPedido' AND Usuario = '(defecto)')
    INSERT INTO ParametrosUsuario (Empresa, Clave, Usuario, Valor, Usuario2, [Fecha Modificación])
    VALUES ('1', 'NestoViejo_FormaPagoPedido', '(defecto)', '0', 'NestoAPI#590', GETDATE());
GO

-- 2) El desencadenador
CREATE OR ALTER TRIGGER dbo.trgCabPedidoVtaNestoViejoFormaPago ON dbo.CabPedidoVta
AFTER UPDATE
AS
BEGIN
    -- NestoAPI#590 20/11/26: la forma de pago y el CCC de un pedido solo se cambian desde Nesto (API).
    IF dbo.fnEsNestoViejo() = 0 RETURN;                 -- API, SSMS, sa, jobs: nada más (primera línea a propósito)
    IF NOT UPDATE([Forma Pago]) AND NOT UPDATE(CCC) RETURN;   -- el UPDATE no lleva ninguna de las dos en el SET
    IF TRIGGER_NESTLEVEL() > 1 RETURN;                  -- lo cambia otro desencadenador, no el usuario
    SET NOCOUNT ON;

    DECLARE @cambios TABLE (Clave nvarchar(100), Detalle nvarchar(2000));
    INSERT @cambios (Clave, Detalle)
    SELECT TOP (200)
           LEFT(RTRIM(i.Empresa) + N'/' + CONVERT(nvarchar(12), i.Número), 100),
           LTRIM(
               CASE WHEN ISNULL(RTRIM(i.[Forma Pago]), '') <> ISNULL(RTRIM(d.[Forma Pago]), '') AND ISNULL(RTRIM(d.[Forma Pago]), '') <> ''
                    THEN N'FORMA PAGO: ' + ISNULL(RTRIM(d.[Forma Pago]), N'') + N' -> ' + ISNULL(RTRIM(i.[Forma Pago]), N'') + N' ' ELSE N'' END
             + CASE WHEN ISNULL(RTRIM(i.CCC), '') <> ISNULL(RTRIM(d.CCC), '') AND ISNULL(RTRIM(d.CCC), '') <> ''
                    THEN N'CCC: ' + ISNULL(RTRIM(d.CCC), N'') + N' -> ' + ISNULL(RTRIM(i.CCC), N'') + N' ' ELSE N'' END)
             + N'(cliente ' + ISNULL(RTRIM(i.[Nº Cliente]), N'') + N'/' + ISNULL(RTRIM(i.Contacto), N'') + N')'
    FROM inserted i
    INNER JOIN deleted d ON d.Empresa = i.Empresa AND d.Número = i.Número
    WHERE
      -- 20/11/26: al CREAR un pedido, Nesto viejo inserta la cabecera vacía y la rellena con un UPDATE (de vacío/NULL al valor del
      -- cliente). Eso no es un cambio: solo cuenta cuando el pedido YA tenía forma de pago (o ya tenía CCC). Cada columna por separado.
         (ISNULL(RTRIM(i.[Forma Pago]), '') <> ISNULL(RTRIM(d.[Forma Pago]), '') AND ISNULL(RTRIM(d.[Forma Pago]), '') <> '')
      OR (ISNULL(RTRIM(i.CCC), '') <> ISNULL(RTRIM(d.CCC), '') AND ISNULL(RTRIM(d.CCC), '') <> '');

    IF NOT EXISTS (SELECT 1 FROM @cambios) RETURN;            -- llevaba las columnas, pero sin cambio de verdad (o era el alta)

    DECLARE @modo varchar(10) = dbo.fnNestoViejoModo('FormaPagoPedido');
    DECLARE @permitido bit = dbo.fnNestoViejoPermitido('FormaPagoPedido');

    IF @modo = 'Bloqueo' AND @permitido = 0
    BEGIN
        RAISERROR (N'Esto ya solo se puede hacer desde Nesto: la forma de pago y el CCC de un pedido se cambian en el pedido en Nesto. No se ha guardado nada.', 16, 1);
        ROLLBACK TRANSACTION;
        -- Tras el ROLLBACK del desencadenador, esto ya no se deshace: queda el rastro del intento.
        INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
        SELECT 'FormaPagoPedido', 'Bloqueado', 'CabPedidoVta', Clave, Detalle FROM @cambios;
        RETURN;
    END

    INSERT INTO dbo.NestoViejoSombra (Operacion, Resultado, Tabla, Clave, Detalle)
    SELECT 'FormaPagoPedido', CASE WHEN @modo = 'Bloqueo' THEN 'Permitido' ELSE 'Sombra' END, 'CabPedidoVta', Clave, Detalle FROM @cambios;
END
GO

-- DESPUÉS: trgCabPedidoVtaNestoViejoFormaPago creado y activo; los demás, sin cambios
SELECT 'DESPUES' Momento, t.name, t.is_disabled, t.modify_date FROM sys.triggers t WHERE t.parent_id = OBJECT_ID(N'dbo.CabPedidoVta');
SELECT 'DESPUES' Momento, Empresa, Clave, Usuario, Valor FROM ParametrosUsuario WHERE Empresa = '1' AND Clave LIKE 'NestoViejo[_]FormaPagoPedido%';
GO
SET NOEXEC OFF;
GO

-- SEGUIMIENTO:
-- SELECT TOP 100 * FROM dbo.NestoViejoSombra WHERE Operacion = 'FormaPagoPedido' ORDER BY Id DESC;

/* VUELTA ATRÁS completa (el interruptor de arriba suele bastar):
DROP TRIGGER IF EXISTS dbo.trgCabPedidoVtaNestoViejoFormaPago;
*/
