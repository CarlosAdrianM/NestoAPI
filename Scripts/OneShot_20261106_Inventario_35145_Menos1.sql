/*
    06/11/26 - Corrección del recuento de Alfredo en Algete (Novedades 1112, comentario 761): el 35145 (LK OPC 66/00) son 60 en el hueco
    004/003/008, no 61 (una lectura repetida). La contabilización 81327 (11:05) dejó stock 61 y Ubicaciones 004/003/008 = 61.

    PROPUESTA (pendiente del OK de Carlos, sa en NV): en vez de tocar ExtractoProducto y Ubicaciones a mano, se crea una fila de inventario
    de hoy para ese hueco con StockCalculado 61 y StockReal 60 y se contabiliza por el camino normal (POST api/Inventarios/Contabilizar
    ALG de hoy, o F6 en Ariadna), que regulariza ExtractoProducto (-1, diario _RegInvent) y deja Ubicaciones en 60 en la misma transacción.
    Hasta que se contabilice, esa fila pendiente no bloquea nada (los desencadenadores dejan pasar la contabilización desde hoy).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM Inventarios WHERE Empresa = '1' AND Almacén = 'ALG' AND Número = '35145' AND Estado = 1 AND NºTraspaso IS NULL)
BEGIN
    PRINT 'Ya hay una fila pendiente del 35145 en ALG: no se crea otra.';
END
ELSE
BEGIN
    INSERT INTO Inventarios (Empresa, Almacén, Fecha, Número, Descripción, Grupo, Subgrupo, Familia, StockCalculado, StockReal, Estado, Valor,
                             NºTraspaso, Aplicacion, Pasillo, Fila, Columna, Usuario, PorCodigoBarras)
    SELECT Empresa, Almacén, CAST(GETDATE() AS date), Número, Descripción, Grupo, Subgrupo, Familia, 61, 60, 1, Valor,
           NULL, 'NestoAPI', Pasillo, Fila, Columna, 'NUEVAVISION\Carlos', 0
    FROM Inventarios WHERE NºOrden = 948771;   -- la fila contabilizada de Alfredo (004/003/008, 54 -> 61)
    PRINT 'Fila creada: 35145 en 004/003/008, 61 -> 60. Ahora contabilizar ALG de hoy (POST api/Inventarios/Contabilizar {Empresa:"1", Almacen:"ALG", Fecha:hoy}).';
END

SELECT NºOrden, Número, Pasillo + '/' + Fila + '/' + Columna Ubi, StockCalculado, StockReal, Estado, Usuario
FROM Inventarios WHERE Empresa = '1' AND Almacén = 'ALG' AND Número = '35145' AND Fecha >= CAST(GETDATE() AS date);
