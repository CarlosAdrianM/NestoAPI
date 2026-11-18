/*
    18/11/26: dos globos de Nesto para la versión 1.10.64.0.
      1) Plantilla de ventas, desplegable «Servir» (ancla PlantillaVenta.ModoServicio): el modo nuevo «Junto con lo pendiente del
         cliente» (sugerencia 1340). El desplegable está en el último paso: el globo espera y sale al llegar a él.
      2) Ficha del evento, botón «Subir dosier…» (ancla MantenimientoEventos.Dosier): el dosier que se manda solo (sugerencia 1332).
    Las anclas van en Nesto 1.10.64.0: con un Nesto anterior no hay ancla. Lanzar DESPUÉS de publicar Nesto 1.10.64.0; si la versión
    que se publica es otra, cámbiela aquí ANTES de lanzarlo.
    Orden: los siguientes al mayor que haya para Nesto. Duran 45 días. Para todos (Grupos NULL): el del dosier solo lo ve quien
    entra en la ficha de un evento.
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
('Nesto', '1.10.64.0', 'PlantillaVenta', 'PlantillaVenta.ModoServicio', N'Nuevo: «Junto con lo pendiente del cliente»',
 N'Si el cliente tiene algo pendiente para esta dirección (por ejemplo, una nota de entrega que espera un producto), aquí aparece un modo nuevo para que este pedido espere y salga en el mismo envío. Solo se ofrece cuando hay algo con lo que juntarlo, y debajo verá con qué saldrá.',
 NULL, @orden + 10),
('Nesto', '1.10.64.0', 'MantenimientoEventos', 'MantenimientoEventos.Dosier', N'Nuevo: el dosier se envía solo',
 N'Suba aquí el dosier del evento en PDF. Nesto lo manda por correo a las inscritas con la plaza pagada unas 2 horas antes de empezar, y al momento a quien se inscriba después. No hace falta enviarlo a mano.',
 NULL, @orden + 20);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
SELECT g.Aplicacion, g.VersionDesde, g.Pantalla, g.Ancla, g.Titulo, g.Texto, g.Grupos, @hoy, @fin, g.Orden
FROM @g g
WHERE NOT EXISTS (SELECT 1 FROM dbo.Globos x WHERE x.Aplicacion = g.Aplicacion AND x.Ancla = g.Ancla);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;

SELECT Id, Aplicacion, VersionDesde, Pantalla, Ancla, Orden, FechaDesde, FechaHasta, Activo FROM dbo.Globos
WHERE Aplicacion = 'Nesto' ORDER BY Pantalla, Orden;
