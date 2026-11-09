/*
    NestoAPI#841 (Carlos 09/11/26): nuevo tipo de festivo «RepartoPropio» = día sin reparto propio (no sale la furgoneta: el
    repartidor libra). Lo dan de alta con antelación Dirección o Almacén (POST api/Festivos con Tipo = "RepartoPropio").
    Efectos (todo en la API, sin más cambios de esquema):
      - Nuestras rutas (AT/16, agencia 14) no reparten ese día: fecha de entrega, picking de rutas internas (el de las 9:00 no
        se lanza), etiqueta RP, paradas de notas de entrega... pasan al siguiente día de reparto.
      - Las reposiciones de tiendas (las lleva la furgoneta) no llegan ese día: ni ALG→tienda ni tienda→ALG; la siguiente del calendario.
      - El aviso de las 22:00 de furgoneta sin descargar no salta.
      - NO es festivo de ningún almacén ni tienda: Algete, Reina y Alcobendas trabajan (GestorFestivos.EsFestivo no lo cuenta).

    La tabla dbo.Festivos (Scripts/Issue779_Festivos.sql) tiene CK_Festivos_Tipo con la lista cerrada de tipos: sin este script,
    POST api/Festivos con RepartoPropio falla en el INSERT (la API ya acepta el tipo). Tipo es nvarchar(20): «RepartoPropio» cabe.

    Lanzar como sa en NV ANTES de dar de alta el primer día sin reparto propio (da igual antes o después de publicar la API).
    Idempotente: si la restricción ya admite RepartoPropio (o no hay tabla), no hace nada.
*/
SET NOCOUNT ON;
USE NV;
GO
IF OBJECT_ID(N'dbo.Festivos', N'U') IS NULL
BEGIN
    PRINT N'No existe dbo.Festivos: lance antes Scripts/Issue779_Festivos.sql (este script no hace nada).';
END
ELSE IF EXISTS (SELECT 1 FROM sys.check_constraints
                WHERE parent_object_id = OBJECT_ID(N'dbo.Festivos') AND name = N'CK_Festivos_Tipo'
                  AND definition LIKE N'%''RepartoPropio''%')
BEGIN
    PRINT N'CK_Festivos_Tipo ya admite RepartoPropio: nada que hacer.';
END
ELSE
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Festivos') AND name = N'CK_Festivos_Tipo')
        ALTER TABLE dbo.Festivos DROP CONSTRAINT CK_Festivos_Tipo;
    ALTER TABLE dbo.Festivos WITH CHECK ADD CONSTRAINT CK_Festivos_Tipo
        CHECK (Tipo IN (N'Nacional', N'Autonómica', N'Empresa', N'Algete', N'Alcobendas', N'Madrid', N'RepartoPropio'));
    PRINT N'CK_Festivos_Tipo ampliada con RepartoPropio.';
END
GO

-- Comprobación: la definición tiene que acabar en ... OR [Tipo]=N'RepartoPropio'
SELECT name, definition FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'dbo.Festivos');
