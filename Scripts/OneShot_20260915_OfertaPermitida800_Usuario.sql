-- NestoAPI#481: la oferta autorizada 6+1 de Staleks (NºOrden 800, creada por Manuel el 10/09/2026
-- 11:29) quedó grabada con el usuario del pool de IIS y la familia en minúsculas porque
-- OfertasPermitidas.Usuario era Computed en el EDMX (arreglado en 68e62172). Corrige la fila.
-- Ejecutar UNA vez en NV (SSMS). Idempotente: solo toca la fila si sigue con el usuario del pool.
UPDATE OfertasPermitidas
SET Usuario = 'NUEVAVISION\Manuel', Familia = 'Staleks'
WHERE [NºOrden] = 800 AND Usuario = 'NUEVAVISION\RDS2016$';

SELECT [NºOrden], Familia, Usuario, [FechaModificación] FROM OfertasPermitidas WHERE [NºOrden] = 800;
