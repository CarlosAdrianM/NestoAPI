/*
    Fechas de vigencia en OfertasPermitidas (las ofertas tipo "6+2").  31/08/2026.

    POR QUE
    -------
    Las ofertas de producto se meten hoy desde Nesto viejo y NO SE PUEDEN FECHAR: la tabla no
    tiene columnas de fecha. Ni siquiera con un UPDATE por fuera, porque no hay donde escribir.
    La unica forma de apagar una oferta es borrar la fila y acordarse de hacerlo.

    Las ofertas COMBINADAS (otra tabla, OfertasCombinadas) si tienen FechaDesde y FechaHasta desde
    siempre. Esto iguala las dos.

    Caso que lo motiva: peticion por correo del 31/08/2026 de poner el 6+2 del producto 44724.
    A las 10:49 de ese dia ELMAH registro "No se encuentra autorizacion para la oferta del
    producto 44724" — un pedido rebotando porque la oferta no existia todavia. Se creo a mano a
    las 11:42, sin fecha de fin, como todas.

    SEMANTICA: la misma que las campanas de #423
    --------------------------------------------
        (FechaDesde IS NULL OR FechaDesde <= hoy) AND (FechaHasta IS NULL OR FechaHasta >= hoy)

    NULL = sin limite por ese lado, asi que NULL/NULL = SIEMPRE VIGENTE: las 118 filas que ya
    existen no cambian de comportamiento. Las dos fechas son INCLUSIVAS y de tipo `date`, sin
    hora: una oferta que acaba el 30/09 vale todo el dia 30.

    QUIEN LA RESPETA
    ----------------
    `ServicioPrecios.BuscarOfertasPermitidas`, que es el UNICO punto de lectura de esta tabla.
    Filtrando ahi lo respetan de una vez los dos validadores que la consultan
    (ValidadorOfertasPermitidas y ValidadorDescuentosPermitidos): una oferta caducada deja de
    autorizar el pedido, que es justo el sentido de ponerle fecha de fin.

    OJO: a diferencia de los descuentos de #423, aqui NO hace falta ningun job que republique
    nada. OfertasPermitidas no viaja a la tienda: es una tabla de AUTORIZACION del pedido, no de
    precios publicados. Que caduque no cambia ningun mensaje del bus.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
USE NV;
GO

-- =============================================================================
-- 1) ANTES
-- =============================================================================
PRINT '--- A) Reparto actual (todas deben quedar NULL/NULL) ---';
SELECT Ambito = CASE WHEN Cliente IS NOT NULL THEN 'de un cliente concreto'
                     WHEN Número IS NOT NULL THEN 'producto'
                     WHEN Familia IS NOT NULL THEN 'familia'
                     ELSE 'otro' END,
       Filas = COUNT(*),
       ConDenegar = SUM(CASE WHEN Denegar = 1 THEN 1 ELSE 0 END)
FROM OfertasPermitidas
WHERE Empresa = '1'
GROUP BY CASE WHEN Cliente IS NOT NULL THEN 'de un cliente concreto'
              WHEN Número IS NOT NULL THEN 'producto'
              WHEN Familia IS NOT NULL THEN 'familia'
              ELSE 'otro' END
ORDER BY Filas DESC;

-- =============================================================================
-- 2) LAS COLUMNAS
-- =============================================================================
-- Nullable y sin DEFAULT: metadata-only, instantaneo, y no reescribe ninguna fila.
IF COL_LENGTH('dbo.OfertasPermitidas', 'FechaDesde') IS NULL
BEGIN
    ALTER TABLE dbo.OfertasPermitidas ADD FechaDesde date NULL;
    PRINT 'Columna FechaDesde creada.';
END
ELSE
    PRINT 'Columna FechaDesde ya existia: no se toca.';
GO

IF COL_LENGTH('dbo.OfertasPermitidas', 'FechaHasta') IS NULL
BEGIN
    ALTER TABLE dbo.OfertasPermitidas ADD FechaHasta date NULL;
    PRINT 'Columna FechaHasta creada.';
END
ELSE
    PRINT 'Columna FechaHasta ya existia: no se toca.';
GO

-- Un rango al reves no caduca: no vale NUNCA, y eso no lo quiere nadie.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_OfertasPermitidas_Vigencia')
BEGIN
    ALTER TABLE dbo.OfertasPermitidas WITH CHECK
        ADD CONSTRAINT CK_OfertasPermitidas_Vigencia
        CHECK (FechaDesde IS NULL OR FechaHasta IS NULL OR FechaDesde <= FechaHasta);
    PRINT 'Restriccion CK_OfertasPermitidas_Vigencia creada.';
END
ELSE
    PRINT 'Restriccion CK_OfertasPermitidas_Vigencia ya existia: no se toca.';
GO

-- =============================================================================
-- 3) DESPUES
-- =============================================================================
PRINT '--- B) Las dos columnas existen, son date y admiten nulos ---';
SELECT c.name, t.name AS Tipo, c.is_nullable
FROM sys.columns c JOIN sys.types t ON t.user_type_id = c.user_type_id
WHERE c.object_id = OBJECT_ID('dbo.OfertasPermitidas') AND c.name IN ('FechaDesde', 'FechaHasta');

PRINT '--- C) La restriccion, activa y de fiar (is_disabled y is_not_trusted a 0) ---';
SELECT name, is_disabled, is_not_trusted, definition
FROM sys.check_constraints WHERE name = 'CK_OfertasPermitidas_Vigencia';

PRINT '--- D) NINGUNA fila debe tener fechas todavia (0 y 0) ---';
SELECT ConFechaDesde = SUM(CASE WHEN FechaDesde IS NOT NULL THEN 1 ELSE 0 END),
       ConFechaHasta = SUM(CASE WHEN FechaHasta IS NOT NULL THEN 1 ELSE 0 END)
FROM OfertasPermitidas;

/*
    MARCHA ATRAS:

        ALTER TABLE dbo.OfertasPermitidas DROP CONSTRAINT CK_OfertasPermitidas_Vigencia;
        ALTER TABLE dbo.OfertasPermitidas DROP COLUMN FechaDesde;
        ALTER TABLE dbo.OfertasPermitidas DROP COLUMN FechaHasta;

    Segura mientras no haya ofertas con fechas puestas: con NULL/NULL en todo, el codigo nuevo y
    el viejo se comportan igual.
*/
