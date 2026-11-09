/*
    NestoAPI#843 (Carlos, 09/11/26): importe mínimo de compra de los productos requeridos de un evento «gratuito por compra».
    Caso: evento 6 «Rentabiliza cada ML de tus viales»: hay que haber gastado al menos 400 € (base imponible) en cualquiera de
    los 14 viales Eva Visnú requeridos; con el modo «Alguno» bastaba con un vial.

    Eventos.ImporteMinimoProductosRequeridos decimal(10, 2) NULL:
      NULL o 0 = sin mínimo (lo de siempre: basta con haber comprado o pedido el producto).
      > 0      = la suma de la base imponible de las líneas que cuentan como compra (las mismas que el justificante de #732:
                 cantidad positiva, estado -1/1/2/4, pedidos del cliente desde 60 días antes del requisito) tiene que llegar a
                 este importe por cada asistente de la inscripción.
    La API lo expone como EventoDTO.ImporteMinimoProductosRequeridos (GET/PUT/POST api/Eventos).

    Fuera del EDMX, como el resto de #732: la API lo lee y lo escribe por SQL y comprueba antes que existe (COL_LENGTH). Sin
    el script, ningún evento tiene mínimo y el PUT con un importe mayor que 0 contesta que falta este script.

    Ejecutar en SSMS contra NV como sa (ALTER). Idempotente: se puede lanzar dos veces.
    PRECAUCIÓN (#542, #294): nada de índices filtrados ni de columnas calculadas indexadas.
*/

SET NOCOUNT ON;
USE NV;
GO

-- 1) La columna, en su propio lote (si ya existe, no se toca)
IF COL_LENGTH('dbo.Eventos', 'ImporteMinimoProductosRequeridos') IS NULL
    ALTER TABLE dbo.Eventos ADD ImporteMinimoProductosRequeridos decimal(10, 2) NULL;
GO

-- 2) La restricción (no negativo), en otro lote: el lote anterior ya ha creado la columna. Solo si no existe.
IF COL_LENGTH('dbo.Eventos', 'ImporteMinimoProductosRequeridos') IS NOT NULL
   AND OBJECT_ID('dbo.CK_Eventos_ImporteMinimoProductosRequeridos', 'C') IS NULL
    EXEC (N'ALTER TABLE dbo.Eventos ADD CONSTRAINT CK_Eventos_ImporteMinimoProductosRequeridos
              CHECK (ImporteMinimoProductosRequeridos IS NULL OR ImporteMinimoProductosRequeridos >= 0);');
GO

-- La API lee y actualiza la columna por SQL (SELECT/UPDATE sobre dbo.Eventos); Eventos ya tenía permisos (#611), se repite por si acaso
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Eventos TO [NUEVAVISION\RDS2016$];
GO

------------------------------------------------------------------------------------------------
-- VERIFICACIÓN
------------------------------------------------------------------------------------------------
SELECT COL_LENGTH('dbo.Eventos', 'ImporteMinimoProductosRequeridos') AS ColumnaImporteMinimo,
       CASE WHEN OBJECT_ID('dbo.CK_Eventos_ImporteMinimoProductosRequeridos', 'C') IS NULL THEN 'NO' ELSE 'SÍ' END AS Restriccion;
GO

-- (Desde Nesto, Clientes › Eventos, se pone en el evento 6: «Importe mínimo de compra (€)» = 400. No hace falta tocarlo aquí.)
