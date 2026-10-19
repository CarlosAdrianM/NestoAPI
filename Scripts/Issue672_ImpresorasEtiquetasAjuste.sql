/*
    NestoAPI#672: tabla ImpresorasEtiquetasAjuste (desplazamiento X/Y por impresora de etiquetas).

    Ejecutar en SSMS contra NV como sa (CREATE y GRANT: el login nuevavision no puede). Idempotente: se puede lanzar dos
    veces. No está en el EDMX (la API la lee y escribe con SQL a mano), así que se puede lanzar antes o después del deploy:
    sin ella, ninguna impresora tiene ajuste y las etiquetas salen como hasta ahora; guardar un ajuste responde 503.

    Una fila por impresora (la ruta del parámetro ImpresoraCodBarras, sin espacios y en MAYÚSCULAS, que es como la guarda la
    API: \\RDS2016\ETIQUETASREINA). Desplazamientos en puntos de la impresora (203 ppp: 8 puntos = 1 mm): X positivo corre
    la etiqueta a la derecha, Y positivo hacia abajo. La API los suma a las coordenadas de las etiquetas de producto, de
    hueco y de bulto. Sin fila (o con 0 y 0), el EPL es el de siempre.

    Se mantiene desde la API (PUT api/Etiquetas/Ajuste, Dirección e Informática), después de probar con POST api/Etiquetas/Prueba.
    GRANT: SELECT, INSERT y UPDATE (no se borra: para quitar el ajuste se pone a 0 y 0).
*/

SET NOCOUNT ON;
USE NV;
GO

IF OBJECT_ID('dbo.ImpresorasEtiquetasAjuste') IS NULL
BEGIN
    CREATE TABLE dbo.ImpresorasEtiquetasAjuste (
        Impresora varchar(100) NOT NULL,                -- \\RDS2016\ETIQUETASREINA (normalizada por la API)
        DesplazamientoX int NOT NULL CONSTRAINT DF_ImpresorasEtiquetasAjuste_X DEFAULT (0),
        DesplazamientoY int NOT NULL CONSTRAINT DF_ImpresorasEtiquetasAjuste_Y DEFAULT (0),
        Usuario varchar(50) NULL,                       -- quién lo cambió por última vez
        FechaModificacion datetime NOT NULL CONSTRAINT DF_ImpresorasEtiquetasAjuste_Fecha DEFAULT (GETDATE()),

        CONSTRAINT PK_ImpresorasEtiquetasAjuste PRIMARY KEY CLUSTERED (Impresora),
        -- Como mucho el tamaño de la etiqueta (30 × 20 mm = 240 × 160 puntos)
        CONSTRAINT CK_ImpresorasEtiquetasAjuste_Rango CHECK (DesplazamientoX BETWEEN -240 AND 240 AND DesplazamientoY BETWEEN -160 AND 160)
    );
END
GO

GRANT SELECT, INSERT, UPDATE ON dbo.ImpresorasEtiquetasAjuste TO [NUEVAVISION\RDS2016$];
GO
