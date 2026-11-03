-- NestoAPI#787 (épica #784, corte 0): borrado de las filas de Inventarios pendientes (Estado 1) que no son de ningún
-- inventario. Se lanza tras la respuesta de Andre (sugerencia 964).
--
-- Comprobado en producción el 03/11/26: hay 46 filas en Estado 1 en toda la tabla y son exactamente estas:
--   - 44 de NUEVAVISION\Andre, Aplicacion 'Nesto', del 20260126 al 20260910 (15 ALC, 14 ALG, 15 REI). OJO: NO tienen el
--     NºTraspaso a NULL: llevan el traspaso de una entrega de factura (_EntregFac: 78060, 78184, 78848, 78944, 80020,
--     80159, 80639), igual que sus gemelas de AMZ, que están en Estado 3. Como el SP solo contabiliza NºTraspaso NULL,
--     nunca se iban a contabilizar, pero las de ALC tienen StockReal 0 con StockCalculado > 0: si alguien les quitase el
--     traspaso, dejarían ese stock a cero. Ningún movimiento de ExtractoProducto de esos traspasos es de ALC ni de REI.
--   - 1 de NUEVAVISION\Javier del 20260930 en ALC, Aplicacion 'NestoAPI' (no 'Nesto'), NºTraspaso NULL (45487, 0 -> 1).
--   - 1 de NUEVAVISION\Patricia con fecha 20360508 en REI, Aplicacion 'Nesto', NºTraspaso NULL (38669, 4 -> 4).
-- El DELETE va por NºOrden explícito (y Estado 1 y esos usuarios) para no borrar nada más; si el recuento no es 46, no
-- borra nada.
--
-- Uso: lanzar tal cual (@Aplicar = 0) para ver las filas; con @Aplicar = 1 las borra dentro de una transacción.
USE NV
GO
SET NOCOUNT ON

DECLARE @Aplicar bit = 0

DECLARE @Filas TABLE (NºOrden int PRIMARY KEY)
INSERT INTO @Filas (NºOrden) VALUES
	-- NUEVAVISION\Andre (44)
	(936081), (936082), (936084),                                           -- 20260126, traspaso 78060
	(936527), (936529),                                                     -- 20260205, traspaso 78184
	(938474), (938475), (938476), (938477), (938478),                       -- 20260325, traspaso 78848 (ALC)
	(938479), (938480), (938481), (938482), (938483),                       -- 20260325, traspaso 78848 (ALG)
	(938489), (938490), (938491), (938492), (938493),                       -- 20260325, traspaso 78848 (REI)
	(938528), (938529), (938530), (938531), (938534), (938535),             -- 20260407, traspaso 78944
	(941337), (941338), (941340),                                           -- 20260702, traspaso 80020
	(941739), (941740), (941742),                                           -- 20260713, traspaso 80159
	(941965), (941966), (941967), (941968),                                 -- 20260910, traspaso 80639 (ALC)
	(941969), (941970), (941971), (941972),                                 -- 20260910, traspaso 80639 (ALG)
	(941977), (941978), (941979), (941980),                                 -- 20260910, traspaso 80639 (REI)
	-- NUEVAVISION\Javier (1)
	(942471),                                                               -- 20260930, ALC
	-- NUEVAVISION\Patricia (1)
	(940191)                                                                -- 20360508, REI

DECLARE @Esperadas int = 46
DECLARE @Encontradas int

SELECT @Encontradas = COUNT(*)
	FROM Inventarios i INNER JOIN @Filas f ON f.NºOrden = i.NºOrden
	WHERE i.Estado = 1
	  AND (   (i.Usuario = 'NUEVAVISION\Andre' AND i.Aplicacion = 'Nesto' AND i.Fecha >= '20260101' AND i.Fecha < '20261001' AND i.Almacén IN ('ALG', 'REI', 'ALC'))
	       OR (i.Usuario = 'NUEVAVISION\Javier' AND i.Almacén = 'ALC' AND i.Fecha >= '20260930' AND i.Fecha < '20261001' AND i.NºTraspaso IS NULL)
	       OR (i.Usuario = 'NUEVAVISION\Patricia' AND i.Almacén = 'REI' AND i.Fecha >= '20360508' AND i.Fecha < '20360509' AND i.NºTraspaso IS NULL))

-- Lo que se va a borrar
SELECT i.NºOrden, i.Usuario, i.Almacén, CONVERT(char(8), i.Fecha, 112) AS Fecha, RTRIM(i.Número) AS Producto, RTRIM(i.Descripción) AS Descripción,
       i.StockCalculado, i.StockReal, i.Estado, i.NºTraspaso, RTRIM(i.Aplicacion) AS Aplicacion
	FROM Inventarios i INNER JOIN @Filas f ON f.NºOrden = i.NºOrden
	ORDER BY i.Usuario, i.NºOrden

-- Todas las pendientes de la tabla (tras el borrado no debería quedar ninguna de antes de hoy)
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
			WHERE i.Estado = 1
			  AND i.Usuario IN ('NUEVAVISION\Andre', 'NUEVAVISION\Javier', 'NUEVAVISION\Patricia')
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
