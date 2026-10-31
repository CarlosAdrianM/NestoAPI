/*
    31/10/26: los globos de Ariadna 2.27 y Nesto 1.10.53.1 (NestoAPI#774). Textos revisados.
    Ejecutar en SSMS contra NV como sa (o nuevavision: solo INSERT). Idempotente: no repite un globo con la misma Aplicación + Ancla.
    Duran 45 días. Retirar uno: UPDATE dbo.Globos SET Activo = 0 WHERE Id = …
*/
SET NOCOUNT ON;
USE NV;

DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @fin date = DATEADD(day, 45, @hoy);

DECLARE @g TABLE (Aplicacion varchar(20), VersionDesde varchar(23), Pantalla varchar(100), Ancla varchar(200),
                  Titulo nvarchar(200), Texto nvarchar(1000), Grupos nvarchar(500), Orden int);
INSERT INTO @g VALUES
('Ariadna', '2.27', 'Reparto', 'Reparto.EnLaFurgoneta', N'Lo que lleva en la furgoneta',
 N'Escanee todos los bultos: al cargar la furgoneta, al entregarlos, al recoger los de la reposición en la tienda y al descargarlos en Algete. Aquí ve en todo momento lo que lleva encima.',
 NULL, 10),
('Ariadna', '2.27', 'Reparto', 'Reparto.CobrarConTarjeta', N'Cobrar con tarjeta (F6)',
 N'Si el cliente quiere pagar con tarjeta, pulse F6: se abre WhatsApp con el enlace de pago listo para enviar y Ariadna le avisa en cuanto pague.',
 NULL, 20),
('Ariadna', '2.27', 'Reparto', 'Reparto.PedirAutorizacion', N'Reembolso sin cobrar (F7)',
 N'Si no puede cobrar un reembolso, pulse F7 y escriba el motivo: Administración le contesta aquí. Si no hay nadie, llame por teléfono.',
 NULL, 30),
('Ariadna', '2.27', 'Packing', 'Packing.AnadirFoto', N'Fotos del packing',
 N'Basta con una foto por pedido, aunque luego lo reparta en varios bultos. Puede añadir las que quiera.',
 NULL, 10),
('Nesto', '1.10.53.1', 'EnviarReposicion', 'EnviarReposicion.EstadoRecogida', N'Recogida de la reposición',
 N'Aquí verá cuándo se lleva el repartidor la reposición: al leer el último bulto queda recogida sola. Ya no hay que pulsar ningún botón.',
 N'Tiendas, Almacén', 10),
('Nesto', '1.10.53.1', 'Principal', 'Principal.CampanaAutorizaciones', N'Autorizaciones de reembolso',
 N'Cuando un repartidor pida permiso para no cobrar un reembolso, le saldrá una ventana roja con pitido. Conteste en el momento: el cliente está esperando.',
 N'Administración, Dirección', 10);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
SELECT g.Aplicacion, g.VersionDesde, g.Pantalla, g.Ancla, g.Titulo, g.Texto, g.Grupos, @hoy, @fin, g.Orden
FROM @g g
WHERE NOT EXISTS (SELECT 1 FROM dbo.Globos x WHERE x.Aplicacion = g.Aplicacion AND x.Ancla = g.Ancla);
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;

SELECT Id, Aplicacion, VersionDesde, Ancla, Grupos, FechaDesde, FechaHasta, Activo FROM dbo.Globos ORDER BY Aplicacion, Pantalla, Orden;
