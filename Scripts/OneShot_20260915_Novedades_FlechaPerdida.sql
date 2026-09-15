-- Novedades 1.10.27.0: la flecha "→" de "Herramientas → Mantenimiento de familias" se grabó como "?"
-- porque el literal del INSERT iba sin prefijo N (el script ya está corregido). El login nuevavision
-- no tiene UPDATE sobre Novedades: ejecutar en SSMS.
UPDATE dbo.Novedades
SET Descripcion = REPLACE(Descripcion, N'Herramientas ? Mantenimiento', N'Herramientas > Mantenimiento')
WHERE Version = '1.10.27.0' AND Descripcion LIKE 'En Herramientas ? Mantenimiento%';

SELECT LEFT(Descripcion, 60) FROM dbo.Novedades WHERE Version = '1.10.27.0' AND Descripcion LIKE 'En Herramientas%';
