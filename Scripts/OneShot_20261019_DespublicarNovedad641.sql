-- 19/10/26 (lanzar como sa): la 641 está duplicada con la sugerencia 637 de Andre, ya Implementada en Ariadna 2.11.
UPDATE dbo.Novedades SET Publicada = 0 WHERE Id = 641 AND Version = '2.11';
SELECT Id, Publicada, Titulo FROM dbo.Novedades WHERE Id IN (637, 641);
