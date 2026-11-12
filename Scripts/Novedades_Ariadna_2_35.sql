/*
    Novedades de Ariadna 2.35 (12/11/26): Ariadna#101 («No está» deja el hueco a cero, NestoAPI#858), incidencia 1232 (recibir
    una reposición leyendo una unidad y tecleando cuántas), Ariadna#102 («Me dan algo que no esperaba», NestoAPI#859) y el pedido
    «todo junto» retenido en el packing (NestoAPI#858 punto 4).
    Necesita la API del 12/11 por la tarde. Ejecutar en SSMS contra NV como sa cuando la 2.35 llegue a las PDA.
    Idempotente (compara por Ámbito + Título).
    NO duplicar: la 1232 (Andre) se contesta en su hilo con la versión. Aquí sí lleva fila lo que cambia en la PDA.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'«No está»: el hueco se queda a cero y Ariadna le manda al siguiente',
 N'Al marcar «No está» en una recogida (picking o reposición), Ariadna deja ese hueco a cero en el momento, para que no vuelva a salir en otro picking, y le manda al siguiente hueco donde debería haber de ese producto. Si no queda en ningún hueco, lo da como falta. Para evitar pulsaciones sin querer hay que pulsar F3 dos veces. Marque «No está» solo cuando haya mirado bien el hueco: lo que marque queda como inventario.',
 N'Almacén'),
(2, N'Nuevo', N'Pedidos que esperan al vendedor: no se empaquetan',
 N'Si a un pedido que debe salir completo le falta algo, en el packing aparece en naranja «Esperando al vendedor: no empaquetar» con la hora límite. Déjelo aparte: Ariadna no deja meterlo en caja ni sacar su etiqueta. Si el vendedor decide servir lo que hay, vuelve a salir normal; si decide esperar, desaparece de la lista y lo ya cogido queda pendiente de ubicar.',
 N'Almacén'),
(3, N'Mejorado', N'Recibir una reposición: lea una unidad y teclee cuántas son',
 N'Al recibir una reposición ya no hace falta leer las unidades una a una: lea una y teclee cuántas hay iguales. Gracias, Andre, por contárnoslo.',
 N'Almacén'),
(4, N'Nuevo', N'Reparto: «Me dan algo que no esperaba»',
 N'Si un cliente le entrega mercancía que no estaba prevista, desde la parada (o desde la lista, eligiendo el cliente) pulse «Me dan algo que no esperaba», indique los bultos y qué es, y si puede haga una foto. Ariadna crea la recogida, la apunta en su furgoneta y avisa al almacén. Si ese cliente tiene una recogida de agencia en camino, se lo dice antes para que decida. Al llegar a Algete se descarga leyendo su código, como cualquier otra recogida.',
 NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.35', '20261112', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
