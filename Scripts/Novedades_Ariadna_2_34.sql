/*
    Novedades de Ariadna 2.34 (11/11/26): Ariadna#100 (Android cierra Ariadna al hacer la foto: rastro y vuelta), borrar una fila
    del recuento desde la PDA (incidencia 1186), Ariadna#99 («Ir» con la app de navegación que se prefiera), el aviso de las
    reservas del hueco mientras se cuenta (épica NestoAPI#784, el 37215), la furgoneta (incidencias 1189 y 1190: descargar con el
    papel de la nota, «quitar sin leer» y el aviso que tapaba la cámara) y las etiquetas cruzadas del packing (incidencia 1169).
    Necesita la API del 11/11 por la tarde (master tras 93531c9c). Ejecutar en SSMS contra NV como sa cuando la 2.34 llegue a la
    prueba cerrada. Idempotente (compara por Ámbito + Título).
    NO duplicar: las incidencias 1168 y 1019 (Santiago y Pedro, Ariadna se cierra al hacer la foto), 1169 (Santiago, etiquetas
    cruzadas), 1189 y 1190 (Miguel, la furgoneta) se contestan en su hilo con la versión; la 1186 (Alfredo) se marca Implementada
    con la 1.10.60.0 de Nesto por PUT. Aquí sí lleva fila lo que cambia en la PDA.
    Se omite a propósito: el mensaje de error con el flujo, el pedido y la hora (solo se ve en ELMAH).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Santiago, Pedro, Alfredo y Miguel',
 N'Que Ariadna no pierda el trabajo cuando se cierra al hacer la foto y que no se crucen las etiquetas entre dos PDA (Santiago y Pedro), poder borrar una fila del recuento que sobra (Alfredo), y poder vaciar siempre la furgoneta y elegir la aplicación de mapas (Miguel) salen de lo que nos han contado por «Avisar». Muchas gracias: sigan contándonos.',
 NULL),
(2, N'Corregido', N'Si Ariadna se cierra al hacer una foto, vuelve a donde estaba',
 N'En algunas PDA, Android cierra Ariadna al abrir la cámara. Ahora la foto gasta menos memoria y, si aun así se cierra, al volver a abrir Ariadna le lleva al mismo sitio en el que estaba para repetir la foto, sin empezar de nuevo. Si le sigue pasando, avísenos por «Avisar»: ahora queda apuntado qué estaba haciendo y podremos verlo.',
 N'Almacén'),
(3, N'Corregido', N'Packing: con dos PDA en el mismo picking, F8 saca su etiqueta y no la del compañero',
 N'Si dos personas empaquetaban el mismo picking, al terminar una entrega Ariadna resaltaba la siguiente; si esa la acababa de terminar el compañero, F8 volvía a sacar la etiqueta del pedido del compañero en vez de la suya. Ahora F8 saca la de la entrega que usted acaba de terminar. Para reimprimir la de otra entrega, entre en ella o elíjala con las flechas.',
 N'Almacén'),
(4, N'Nuevo', N'Reparto: la furgoneta se puede vaciar siempre',
 N'Tres cambios al descargar la furgoneta. Uno: vale leer el papel de la nota de entrega (NE…), igual que al cargar; si esa nota no figura en la furgoneta, Ariadna le dice que no hay que descargarla. Dos: si le queda un bulto que no puede leer (no tiene etiqueta o ya no está), tóquelo en la lista de «En la furgoneta» (en la PDA, elíjalo con las flechas y pulse F8) y confirme con «Sí, quitarlo sin leer» (F8 otra vez; Intro no lo quita). Queda apuntado y se avisa a Almacén. Tres: con la cámara abierta ya se ven los avisos, como «ya estaba descargado», que antes solo pitaban.',
 N'Almacén'),
(5, N'Mejorado', N'Reparto: lo que sale sin cargar aparece en su furgoneta al poner la incidencia',
 N'Si se lleva un paquete que no leyó al cargar y no puede entregarlo, al registrar la incidencia Ariadna lo apunta en su furgoneta. Así, a la vuelta, lo descarga como cualquier otro, sin tener que cargarlo antes. Y la parada de una recogida o de una nota de cobro ya no deja ningún bulto apuntado al cargar: lo recogido lleva su propia etiqueta NR.',
 N'Almacén'),
(6, N'Nuevo', N'Inventario: borrar una fila del recuento que sobra',
 N'En las comprobaciones del inventario, las filas que la API permite borrar (un producto sin ubicación, una fila duplicada o una rellenada con unidades reservadas) se pueden quitar desde la PDA: F8 y, para confirmar, F8 otra vez (Intro no borra). Ojo: borrar una fila NO es contar 0; el producto se queda como estaba, sin regularizar. Solo se pueden borrar filas de hoy y solo quien tiene el permiso.',
 N'Almacén'),
(7, N'Mejorado', N'Inventario: aviso si el hueco tiene unidades reservadas',
 N'Al rellenar una ubicación para contarla, si hay unidades de ese hueco ya recogidas para un pedido, Ariadna lo avisa y deja el aviso a la vista mientras se cuenta: esas unidades no están en el hueco y no hay que contarlas ni buscarlas.',
 N'Almacén'),
(8, N'Mejorado', N'Reparto: «Ir» abre la aplicación de mapas que usted prefiera',
 N'En el menú «⋯» de la lista del reparto, «Navegar con» deja elegir entre la aplicación de mapas del móvil, Google Maps o Waze. Ariadna lo recuerda en ese aparato. «Ruta completa» sigue abriéndose en Google Maps, porque las otras no admiten paradas intermedias.',
 N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.34', '20261111', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
