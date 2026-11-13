/*
    13/11/26: globo de Nesto para la casilla nueva de la Plantilla de venta (Novedades 1261): «Dejar una plantilla nueva
    abierta al grabar el pedido», en el paso «Finalizar». La casilla es discreta a propósito, así que el globo la señala.
    Pantalla «PlantillaVenta» (PlantillaVentaView), ancla PlantillaVenta.MantenerAbierta (puesta en la casilla).
    La casilla está en el último paso del asistente: desde esta misma versión de Nesto, un globo cuyo control está en la
    pantalla pero todavía no se ve ESPERA y sale la primera vez que se ve, pegado a él (aquí, al llegar a «Finalizar»).
    Con un Nesto anterior saldría centrado al abrir la plantilla: por eso VersionDesde tiene que ser la versión que lleva
    ese cambio. VersionDesde 1.10.61.2; si la versión que se publica es otra, cámbiela aquí ANTES de lanzarlo.
    Lanzar DESPUÉS de publicar Nesto.
    Orden: el siguiente al mayor que haya para Nesto. Dura 45 días. Para todos (Grupos NULL).
    Ejecutar en SSMS contra NV como sa (o nuevavision: solo INSERT). Idempotente: no repite un globo con la misma Aplicación + Ancla.
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @fin date = DATEADD(day, 45, @hoy);
DECLARE @orden int = ISNULL((SELECT MAX(Orden) FROM dbo.Globos WHERE Aplicacion = 'Nesto'), 0);

DECLARE @g TABLE (Aplicacion varchar(20), VersionDesde varchar(23), Pantalla varchar(100), Ancla varchar(200),
                  Titulo nvarchar(200), Texto nvarchar(1000), Grupos nvarchar(500), Orden int);
INSERT INTO @g VALUES
('Nesto', '1.10.61.2', 'PlantillaVenta', 'PlantillaVenta.MantenerAbierta', N'¿Pasa varios pedidos seguidos?',
 N'Marque esta casilla y, cada vez que grabe un pedido, se abrirá además una plantilla vacía para el siguiente cliente. Es una preferencia suya: solo cambia en su usuario.',
 NULL, @orden + 10);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
SELECT g.Aplicacion, g.VersionDesde, g.Pantalla, g.Ancla, g.Titulo, g.Texto, g.Grupos, @hoy, @fin, g.Orden
FROM @g g
WHERE NOT EXISTS (SELECT 1 FROM dbo.Globos x WHERE x.Aplicacion = g.Aplicacion AND x.Ancla = g.Ancla);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;

SELECT Id, Aplicacion, VersionDesde, Pantalla, Ancla, Orden, FechaDesde, FechaHasta, Activo FROM dbo.Globos
WHERE Aplicacion = 'Nesto' ORDER BY Pantalla, Orden;
