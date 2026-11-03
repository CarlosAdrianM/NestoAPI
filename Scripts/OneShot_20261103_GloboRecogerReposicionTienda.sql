/*
    03/11/26: globo de Ariadna para el repartidor: las reposiciones que se recogen en una tienda (bultos R{traspaso}-N) se leen
    desde la pestaña «En la furgoneta» en modo Recoger, no desde «Entregar» de la parada (Miguel, novedad 971: cuadro rojo
    «no es de esta parada» con la 81261 de Reina). Mismo ancla y orden siguiente al globo del sábado (Orden 15).
    Ejecutar en SSMS contra NV (sa o nuevavision). Idempotente.
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);

IF NOT EXISTS (SELECT 1 FROM dbo.Globos WHERE Aplicacion = 'Ariadna' AND Titulo = N'Las reposiciones de la tienda se recogen aquí')
    INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
    VALUES ('Ariadna', '2.27', 'Reparto', 'Reparto.EnLaFurgoneta', N'Las reposiciones de la tienda se recogen aquí',
            N'La reposición que le entrega una tienda para Algete (bultos R…) se lee en esta pestaña, en el modo «Recoger». En «Entregar» de la parada solo valen los bultos que usted lleva para esa tienda. Al leer el último bulto, la tienda ve en Nesto que ya se la ha llevado.',
            NULL, @hoy, DATEADD(day, 45, @hoy), 16);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;
