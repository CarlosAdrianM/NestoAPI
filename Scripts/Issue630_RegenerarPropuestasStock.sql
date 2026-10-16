/*
    NestoAPI#630 (stock obsoleto, épica #628): regenerar un mes de propuestas ya calculado
    (POST api/PropuestasStock/Generar?mes=AAAA-MM&regenerar=true).

    El job borra las filas de PropuestasStock de ese mes y las vuelve a insertar, en una transacción, solo si ninguna tiene
    decisión (ni Decision en la fila ni filas en PropuestasStockDecisiones de ese mes). Hasta ahora la cuenta del API solo tenía
    SELECT, INSERT y UPDATE: hace falta DELETE en PropuestasStock. PropuestasStockDecisiones sigue sin DELETE (el histórico no se
    borra nunca; además su clave ajena impide borrar una propuesta con historia).

    Ejecutar en SSMS contra NV como sa (GRANT: el login nuevavision no puede). Idempotente. Lanzar ANTES de usar regenerar=true:
    sin él, el job falla al borrar («permiso DELETE denegado»), no se borra nada (transacción) y el correo lo dice.
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.PropuestasStock', 'U') IS NULL
BEGIN
    RAISERROR('Falta la tabla: lance antes Scripts/Issue630_PropuestasStock.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PropuestasStock TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO
