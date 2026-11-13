/*
    13/11/26: globo de Nesto para el botón nuevo «Pago a plazos…» del detalle del pedido (NestoAPI#864, Nesto#640).
    Pantalla «DetallePedido» (DetallePedidoView), ancla DetallePedido.PagoAPlazos (puesta en el botón).
    El botón solo se ve si la API ofrece el pago a plazos, y está en la pestaña Cabecera: el globo ESPERA a que el botón
    se vea (globos que esperan a su control, desde la 1.10.61.2) y sale pegado a él.
    El ancla va en Nesto 1.10.61.3: con un Nesto anterior no hay ancla. VersionDesde 1.10.61.3; si la versión que se
    publica es otra, cámbiela aquí ANTES de lanzarlo. Lanzar DESPUÉS de publicar Nesto 1.10.61.3.
    Orden: el siguiente al mayor que haya para Nesto. Dura 45 días. Para todos (Grupos NULL).
    Ejecutar en SSMS contra NV como sa. Idempotente: no repite un globo con la misma Aplicación + Ancla.
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @fin date = DATEADD(day, 45, @hoy);
DECLARE @orden int = ISNULL((SELECT MAX(Orden) FROM dbo.Globos WHERE Aplicacion = 'Nesto'), 0);

DECLARE @g TABLE (Aplicacion varchar(20), VersionDesde varchar(23), Pantalla varchar(100), Ancla varchar(200),
                  Titulo nvarchar(200), Texto nvarchar(1000), Grupos nvarchar(500), Orden int);
INSERT INTO @g VALUES
('Nesto', '1.10.61.3', 'DetallePedido', 'DetallePedido.PagoAPlazos', N'Nuevo: pago a plazos',
 N'El cliente puede financiar este pedido. Pulse aquí y elija cómo se lo manda: a su app, donde termina el pago desde el móvil, o con un enlace. Debajo verá cómo va y le avisaremos por la campana cuando se apruebe.',
 NULL, @orden + 10);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
SELECT g.Aplicacion, g.VersionDesde, g.Pantalla, g.Ancla, g.Titulo, g.Texto, g.Grupos, @hoy, @fin, g.Orden
FROM @g g
WHERE NOT EXISTS (SELECT 1 FROM dbo.Globos x WHERE x.Aplicacion = g.Aplicacion AND x.Ancla = g.Ancla);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;

SELECT Id, Aplicacion, VersionDesde, Pantalla, Ancla, Orden, FechaDesde, FechaHasta, Activo FROM dbo.Globos
WHERE Aplicacion = 'Nesto' ORDER BY Pantalla, Orden;
