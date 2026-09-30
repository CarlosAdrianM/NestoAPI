-- NestoAPI#574: en Ariadna «recoger» es un solo proceso, venga el trabajo de un picking de pedidos
-- o de una reposición a tienda. Para que los escaneos sirvan a los dos, PreparacionEscaneos deja
-- de hablar solo de pickings:
--
--   Picking (int)  →  NumeroOrigen (int)     el número del picking o el del traspaso
--   (nueva)           TipoOrigen (char(4))   PICK = picking de pedidos · REPO = reposición a tienda
--
-- La tabla se creó el 30/09/26 (Issue556_Ariadna_EscaneosYBultos.sql) y todavía no la escribe nadie:
-- el script comprueba que sigue vacía y, si no lo está, no toca nada.
--
-- EnviosAgenciaBultos NO cambia: un bulto es siempre de un pedido y de un picking.
--
-- ORDEN: lanzar ANTES de publicar el API que trae el cambio (el API nuevo ya lee NumeroOrigen).
-- Se puede lanzar dos veces sin problema.
--
-- PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
--
-- Es DDL: desde SSMS con `sa` (el login `nuevavision` no tiene ALTER).

USE NV;
GO

IF COL_LENGTH('dbo.PreparacionEscaneos', 'Picking') IS NOT NULL
   AND EXISTS (SELECT 1 FROM dbo.PreparacionEscaneos)
BEGIN
    RAISERROR('PreparacionEscaneos ya tiene escaneos: este script es para la tabla vacía. No se ha cambiado nada.', 16, 1);
    SET NOEXEC ON;
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND name = 'IX_PreparacionEscaneos_Picking')
    DROP INDEX IX_PreparacionEscaneos_Picking ON dbo.PreparacionEscaneos;
GO

IF COL_LENGTH('dbo.PreparacionEscaneos', 'Picking') IS NOT NULL
   AND COL_LENGTH('dbo.PreparacionEscaneos', 'NumeroOrigen') IS NULL
    EXEC sp_rename 'dbo.PreparacionEscaneos.Picking', 'NumeroOrigen', 'COLUMN';
GO

IF COL_LENGTH('dbo.PreparacionEscaneos', 'TipoOrigen') IS NULL
    ALTER TABLE dbo.PreparacionEscaneos
        ADD TipoOrigen char(4) NOT NULL
            CONSTRAINT DF_PreparacionEscaneos_TipoOrigen DEFAULT ('PICK')
            CONSTRAINT CK_PreparacionEscaneos_TipoOrigen CHECK (TipoOrigen IN ('PICK', 'REPO'));
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND name = 'IX_PreparacionEscaneos_Origen')
    CREATE INDEX IX_PreparacionEscaneos_Origen ON dbo.PreparacionEscaneos (Empresa, TipoOrigen, NumeroOrigen, Fase);
GO

SET NOEXEC OFF;
GO

-- Comprobación: tienen que salir NumeroOrigen y TipoOrigen, y no Picking
SELECT c.name AS Columna, t.name AS Tipo, c.max_length AS Longitud, c.is_nullable AS AdmiteNulos
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.PreparacionEscaneos') AND c.name IN ('Picking', 'NumeroOrigen', 'TipoOrigen');
GO
