-- NestoAPI#787 (épica #784, corte 0): borrado de las filas de Inventarios pendientes (Estado 1, NºTraspaso NULL) que no
-- son de ningún inventario. Se lanza tras la respuesta de Andre (sugerencia 964).
--
-- Comprobado en producción el 03/11/26: hay 46 filas en Estado 1 en toda la tabla, pero solo 2 están realmente sueltas
-- (Estado 1 y NºTraspaso NULL), y son las que se borran:
--   - 942471: NUEVAVISION\Javier, 20260930, ALC, Aplicacion 'NestoAPI' (no 'Nesto'), producto 45487, 0 -> 1.
--   - 940191: NUEVAVISION\Patricia, fecha 20360508, REI, Aplicacion 'Nesto', producto 38669, 4 -> 4.
--
-- Las otras 44 (NUEVAVISION\Andre, Aplicacion 'Nesto', del 20260126 al 20260910: 15 ALC, 14 ALG, 15 REI) NO se borran:
-- llevan el NºTraspaso de una entrega de factura (diario _EntregFac: 78060, 78184, 78848, 78944, 80020, 80159, 80639) y
-- tienen gemelas en AMZ con esos mismos traspasos en Estado 3. prdContabilizarInventario solo toca NºTraspaso NULL, así
-- que nunca las contabiliza (ojo: las de ALC tienen StockReal 0 con StockCalculado > 0). Hay que averiguar qué proceso
-- las escribe; mientras tanto se enseñan en el primer SELECT, solo como información.
--
-- El DELETE va por NºOrden explícito (y Estado 1, NºTraspaso NULL y su usuario) para no borrar nada más; si el recuento
-- no es 2, no borra nada.
--
-- Uso: lanzar tal cual (@Aplicar = 0) para ver las filas; con @Aplicar = 1 las borra dentro de una transacción.
USE NV
GO
SET NOCOUNT ON

DECLARE @Aplicar bit = 0

-- Informativo: las de Andre con traspaso de entrega de factura (NO se borran)
SELECT 'Informativo: NO se borran' AS Nota, i.NºOrden, i.Usuario, i.Almacén, CONVERT(char(8), i.Fecha, 112) AS Fecha,
       RTRIM(i.Número) AS Producto, i.StockCalculado, i.StockReal, i.Estado, i.NºTraspaso,
       (SELECT COUNT(*) FROM Inventarios a WHERE a.Empresa = i.Empresa AND a.NºTraspaso = i.NºTraspaso AND a.Número = i.Número
            AND a.Almacén = 'AMZ' AND a.Estado = 3) AS GemelasAMZEstado3
	FROM Inventarios i
	WHERE i.Estado = 1 AND i.NºTraspaso IS NOT NULL AND i.Usuario = 'NUEVAVISION\Andre'
	ORDER BY i.NºTraspaso, i.Número, i.Almacén

DECLARE @Filas TABLE (NºOrden int PRIMARY KEY)
INSERT INTO @Filas (NºOrden) VALUES
	(942471),                                                               -- NUEVAVISION\Javier, 20260930, ALC
	(940191)                                                                -- NUEVAVISION\Patricia, 20360508, REI

DECLARE @Esperadas int = 2
DECLARE @Encontradas int

SELECT @Encontradas = COUNT(*)
	FROM Inventarios i INNER JOIN @Filas f ON f.NºOrden = i.NºOrden
	WHERE i.Estado = 1 AND i.NºTraspaso IS NULL
	  AND (   (i.Usuario = 'NUEVAVISION\Javier' AND i.Almacén = 'ALC' AND i.Fecha >= '20260930' AND i.Fecha < '20261001')
	       OR (i.Usuario = 'NUEVAVISION\Patricia' AND i.Almacén = 'REI' AND i.Fecha >= '20360508' AND i.Fecha < '20360509'))

-- Lo que se va a borrar
SELECT i.NºOrden, i.Usuario, i.Almacén, CONVERT(char(8), i.Fecha, 112) AS Fecha, RTRIM(i.Número) AS Producto, RTRIM(i.Descripción) AS Descripción,
       i.StockCalculado, i.StockReal, i.Estado, i.NºTraspaso, RTRIM(i.Aplicacion) AS Aplicacion
	FROM Inventarios i INNER JOIN @Filas f ON f.NºOrden = i.NºOrden
	ORDER BY i.Usuario, i.NºOrden

-- Todas las pendientes de la tabla (tras el borrado deberían quedar solo las 44 de Andre con traspaso)
SELECT Usuario, Almacén, COUNT(*) AS Filas, SUM(CASE WHEN NºTraspaso IS NULL THEN 1 ELSE 0 END) AS SinTraspaso,
       CONVERT(char(8), MIN(Fecha), 112) AS Desde, CONVERT(char(8), MAX(Fecha), 112) AS Hasta
	FROM Inventarios WHERE Estado = 1 GROUP BY Usuario, Almacén ORDER BY Usuario, Almacén

SELECT @Esperadas AS Esperadas, @Encontradas AS Encontradas

IF @Aplicar = 1 BEGIN
	IF @Encontradas <> @Esperadas BEGIN
		RAISERROR('Se esperaban %d filas y hay %d: no se borra nada. Revisar antes de seguir.', 16, 1, @Esperadas, @Encontradas)
	END ELSE BEGIN
		BEGIN TRAN
		DELETE i
			FROM Inventarios i INNER JOIN @Filas f ON f.NºOrden = i.NºOrden
			WHERE i.Estado = 1 AND i.NºTraspaso IS NULL
			  AND i.Usuario IN ('NUEVAVISION\Javier', 'NUEVAVISION\Patricia')
		IF @@ROWCOUNT <> @Esperadas BEGIN
			ROLLBACK
			RAISERROR('El DELETE no ha borrado %d filas: deshecho.', 16, 1, @Esperadas)
		END ELSE BEGIN
			COMMIT
			SELECT 'Borradas' AS Resultado, @Esperadas AS Filas
		END
	END
END
GO
