/*
    31/10/26: globo de Ariadna para que el repartidor lea TODOS los bultos de la parada al entregar (NestoAPI#772/#774).
    La lectura que abre la ficha ya cuenta como bulto entregado: si lee solo esa en un envío de 3 bultos, los otros 2 siguen
    apuntados en su furgoneta. Apunta al contador «En la furgoneta», justo después del globo de la furgoneta (Orden 15).
    Ejecutar en SSMS contra NV (sa o nuevavision). Idempotente.
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);

IF NOT EXISTS (SELECT 1 FROM dbo.Globos WHERE Aplicacion = 'Ariadna' AND Titulo = N'Lea todos los bultos al entregar')
    INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
    VALUES ('Ariadna', '2.27', 'Reparto', 'Reparto.EnLaFurgoneta', N'Lea todos los bultos al entregar',
            N'Al entregar, lea todos los bultos de la parada, también el que usó para abrir la ficha. Si lee solo algunos, los demás siguen apuntados en su furgoneta; si no lee ninguno, se entregan todos.',
            NULL, @hoy, DATEADD(day, 45, @hoy), 15);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;
