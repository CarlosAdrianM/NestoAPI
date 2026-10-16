/*
    NestoAPI#656 (stock obsoleto, épica #628), cortes 1 y 2:
    1) «Pendiente de servir» (DemandaNoServida) pasa a ser solo el pendiente vencido: líneas −1 con fecha de entrega antes del
       fin del mes de referencia (antes, −1 y 1 sin mirar la fecha). No cambia la columna, solo lo que se guarda en ella.
    2) Columna nueva ServidoConRetraso: unidades albaranadas en los 12 meses que acaban en el mes de referencia que salieron
       más de PropuestasStock:DiasRetraso días (2 por defecto) después de la fecha de entrega.

    Ejecutar en SSMS contra NV como sa (ALTER y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos veces.
    LANZAR ANTES DE PUBLICAR: la API nueva inserta y lee la columna; sin ella, el job no guarda las propuestas (el correo lo
    dice) y la pantalla de propuestas de Nesto falla al leer.
    Las filas ya guardadas quedan con 0 (DEFAULT). Para rellenar septiembre, regenerarlo (POST
    api/PropuestasStock/Generar?mes=2026-09&regenerar=true, requiere Scripts/Issue630_RegenerarPropuestasStock.sql).

    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
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

IF COL_LENGTH('dbo.PropuestasStock', 'ServidoConRetraso') IS NULL
    ALTER TABLE dbo.PropuestasStock ADD ServidoConRetraso int NOT NULL
        CONSTRAINT DF_PropuestasStock_ServidoConRetraso DEFAULT (0) WITH VALUES;   -- uds servidas con retraso en 12 meses
GO

-- Los permisos no cambian (columna nueva de una tabla que ya tiene GRANT); se repiten por si acaso.
GRANT SELECT, INSERT, UPDATE ON dbo.PropuestasStock TO [NUEVAVISION\RDS2016$];
GO

SET NOEXEC OFF;
GO
