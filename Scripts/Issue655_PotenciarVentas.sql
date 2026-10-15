/*
    NestoAPI#655 (stock obsoleto, épica #628): decisión «Potenciar ventas» sobre una propuesta de stock, con objetivo de venta
    (unidades al mes) y fecha de revisión (por defecto, 6 meses). Pensada para los propios (familias PRP, fórmula P), que nunca
    se liquidan.

    Ejecutar en SSMS contra NV como sa (ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    Requiere Scripts/Issue631_PropuestasStockDecisiones.sql. Sin este script la API funciona como hasta ahora: la decisión
    «Potenciar» responde 404 diciendo que falta y el resto (listas, resumen, aplicar, rechazar, posponer, silenciar) va igual.
    Se puede lanzar antes o después del deploy.

    1) Columnas nuevas en PropuestasStock y en el histórico PropuestasStockDecisiones: ObjetivoVentaMensual y PotenciarHasta.
    2) Las CHECK de la decisión (CK_PropuestasStock_Decision y CK_PropuestasStockDecisiones_Decision) admiten 'Potenciar':
       se borran y se vuelven a crear.
    3) Una potenciación lleva siempre objetivo y fecha; las demás decisiones, ninguno de los dos.
    4) El índice IX_PropuestasStock_Producto_Almacen_Mes no se toca: la búsqueda de la última potenciación del producto va por
       (Producto, Almacen, Mes) igual que la del silencio.

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PropuestasStockDecisiones') IS NULL OR COL_LENGTH('dbo.PropuestasStock', 'SilenciadaHasta') IS NULL
BEGIN
    RAISERROR('Falta el corte 3: lance antes Scripts/Issue631_PropuestasStockDecisiones.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

IF COL_LENGTH('dbo.PropuestasStock', 'ObjetivoVentaMensual') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD ObjetivoVentaMensual int NULL;         -- uds/mes que se quieren vender
IF COL_LENGTH('dbo.PropuestasStock', 'PotenciarHasta') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD PotenciarHasta date NULL;              -- fecha de revisión (incluida)
IF COL_LENGTH('dbo.PropuestasStockDecisiones', 'ObjetivoVentaMensual') IS NULL
    ALTER TABLE dbo.PropuestasStockDecisiones ADD ObjetivoVentaMensual int NULL;
IF COL_LENGTH('dbo.PropuestasStockDecisiones', 'PotenciarHasta') IS NULL
    ALTER TABLE dbo.PropuestasStockDecisiones ADD PotenciarHasta date NULL;
GO

-- La CHECK de la decisión, con 'Potenciar' (si ya lo admite, no se toca).
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStock_Decision' AND parent_object_id = OBJECT_ID('dbo.PropuestasStock')
           AND definition NOT LIKE '%Potenciar%')
    ALTER TABLE dbo.PropuestasStock DROP CONSTRAINT CK_PropuestasStock_Decision;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStock_Decision' AND parent_object_id = OBJECT_ID('dbo.PropuestasStock'))
    ALTER TABLE dbo.PropuestasStock WITH CHECK ADD CONSTRAINT CK_PropuestasStock_Decision
        CHECK (Decision IS NULL OR Decision IN ('Aplicar', 'Rechazar', 'Posponer', 'Silenciar', 'Potenciar'));
GO

IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStockDecisiones_Decision' AND parent_object_id = OBJECT_ID('dbo.PropuestasStockDecisiones')
           AND definition NOT LIKE '%Potenciar%')
    ALTER TABLE dbo.PropuestasStockDecisiones DROP CONSTRAINT CK_PropuestasStockDecisiones_Decision;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStockDecisiones_Decision' AND parent_object_id = OBJECT_ID('dbo.PropuestasStockDecisiones'))
    ALTER TABLE dbo.PropuestasStockDecisiones WITH CHECK ADD CONSTRAINT CK_PropuestasStockDecisiones_Decision
        CHECK (Decision IN ('Aplicar', 'Rechazar', 'Posponer', 'Silenciar', 'Potenciar'));
GO

-- Una potenciación lleva siempre objetivo (más de 0) y fecha de revisión; las demás decisiones, ninguno de los dos.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_PropuestasStock_Potenciacion' AND parent_object_id = OBJECT_ID('dbo.PropuestasStock'))
    ALTER TABLE dbo.PropuestasStock WITH CHECK ADD CONSTRAINT CK_PropuestasStock_Potenciacion
        CHECK ((Decision = 'Potenciar' AND ObjetivoVentaMensual > 0 AND PotenciarHasta IS NOT NULL)
            OR (ISNULL(Decision, '') <> 'Potenciar' AND ObjetivoVentaMensual IS NULL AND PotenciarHasta IS NULL));
GO

-- Los permisos no cambian (columnas nuevas de tablas que ya tienen GRANT); se repiten por si acaso.
GRANT SELECT, INSERT, UPDATE ON dbo.PropuestasStock TO [NUEVAVISION\RDS2016$];
GRANT SELECT, INSERT ON dbo.PropuestasStockDecisiones TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO
