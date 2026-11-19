/*
    Ariadna 2.40 (19/11/26): hora prevista de fin de ruta (NestoAPI#881), servicio técnico en la parada, y cobrar de más del
    reembolso con una confirmación. NECESITA la API del 19/11/26 por la noche publicada ANTES (POST api/RepartoPropio/Estimacion).
    Lleva las Novedades y el GLOBO de la banda «Fin previsto» (ancla Reparto.FinPrevisto, pantalla Reparto; el ancla existe desde
    la 2.40: con una Ariadna anterior no sale).
    Ejecutar en SSMS contra NV como sa cuando la 2.40 llegue a las PDA. Idempotente.
*/
SET NOCOUNT ON;
USE NV;

-- 1) Novedades
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Reparto: hora prevista de fin de la ruta',
 N'Encima de la lista de paradas verá «Fin previsto 16:20»: la hora a la que termina la ruta con lo que lleva, contando los desplazamientos y el tiempo de cada parada. Se actualiza sola cuando carga, entrega, cambia el orden o le entra una parada nueva, y también con el paso del tiempo si todavía no ha salido. Si pasa de la hora de cierre de Algete se pone en rojo y le dice cuánto se pasa: avise a Almacén para quitar algo de la ruta.',
 NULL),
(2, N'Nuevo', N'Reparto: paradas con servicio técnico',
 N'Si una parada lleva servicio técnico, lo verá en la lista y al abrirla, con lo que hay que hacer y el tiempo previsto: «Servicio técnico: Cambio de tubos · 90 min». Ese tiempo ya cuenta en la hora prevista de fin.',
 NULL),
(3, N'Mejorado', N'Reparto: se puede cobrar más que el reembolso',
 N'Si el cliente paga algo más junto con el reembolso, teclee el importe que le da. Ariadna le pide confirmarlo una vez («El reembolso es de 45,50 € y va a cobrar 50,00 €») y lo de más queda a cuenta del cliente. Para cobrar menos sigue haciendo falta autorización.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.40', '20261119', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;

-- 2) Globo de la banda (45 días). Solo sale cuando la banda se ve: en la lista de paradas y con alguna parada pendiente.
DECLARE @hoy date = CAST(GETDATE() AS date);
DECLARE @orden int = ISNULL((SELECT MAX(Orden) FROM dbo.Globos WHERE Aplicacion = 'Ariadna'), 0);

INSERT INTO dbo.Globos (Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, Texto, Grupos, FechaDesde, FechaHasta, Orden)
SELECT 'Ariadna', '2.40', 'Reparto', 'Reparto.FinPrevisto', N'Nuevo: hora prevista de fin de ruta',
       N'Aquí verá a qué hora termina la ruta con lo que lleva cargado. No tiene que pulsar nada: se actualiza sola. Si se pone en rojo es que pasa de la hora de cierre: avise a Almacén antes de salir para quitar algo de la ruta.',
       NULL, @hoy, DATEADD(day, 45, @hoy), @orden + 10
WHERE NOT EXISTS (SELECT 1 FROM dbo.Globos x WHERE x.Aplicacion = 'Ariadna' AND x.Ancla = 'Reparto.FinPrevisto');
SELECT @@ROWCOUNT AS GlobosInsertadosAhora;

SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Ambito = 'Ariadna' AND Version = '2.40' ORDER BY Id;
SELECT Aplicacion, VersionDesde, Pantalla, Ancla, Titulo, FechaDesde, FechaHasta, Orden FROM dbo.Globos WHERE Aplicacion = 'Ariadna' AND Ancla = 'Reparto.FinPrevisto';
