/*
    NestoAPI#732 (decisión de Carlos 29/10/26): «Si un evento pide varios productos existe la posibilidad de que se requieran
    todos o que se requieran algunos. Debería definirse en el evento cuál de las dos aplica.»

    Eventos.ProductosRequeridosTodos:
      0 = «Alguno» (por defecto, lo de siempre): basta con haber comprado o pedido UNO de los productos requeridos.
      1 = «Todos»: hay que haber comprado o pedido CADA uno (mismas líneas válidas y misma ventana de 60 días).
    La API lo expone como EventoDTO.ProductosRequeridosModo ("Alguno" / "Todos").

    Fuera del EDMX, como el resto de #732: la API lo lee y lo escribe por SQL y comprueba antes que existe (COL_LENGTH). Sin
    el script, todos los eventos son «Alguno» y el PUT con "Todos" contesta que falta este script.

    Requiere antes Scripts/Issue732_EventosProductosRequeridos.sql (no es imprescindible, pero sin él no hay productos que exigir).
    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

IF COL_LENGTH('dbo.Eventos', 'ProductosRequeridosTodos') IS NULL
    ALTER TABLE dbo.Eventos ADD ProductosRequeridosTodos bit NOT NULL
        CONSTRAINT DF_Eventos_ProductosRequeridosTodos DEFAULT (0);
GO

-- La API lee y actualiza la columna por SQL (SELECT/UPDATE sobre dbo.Eventos); Eventos ya tenía permisos (#611), se repite por si acaso
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Eventos TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT COL_LENGTH('dbo.Eventos', 'ProductosRequeridosTodos') AS ColumnaProductosRequeridosTodos,
       SUM(CASE WHEN ProductosRequeridosTodos = 1 THEN 1 ELSE 0 END) AS EventosQueExigenTodos,
       COUNT(*) AS Eventos
FROM dbo.Eventos;
GO
