/*
    Novedades de la versión 1.10.25.8 (09/09/2026, tarde).

    ⚠️ TODOS los cambios de esta tanda son de NestoAPI: Nesto NO lleva código nuevo.
    Aun así la versión es 1.10.25.8 porque las novedades solo llegan al popup si su Version es
    <= a la ClickOnce publicada, y los usuarios ya vieron la 1.10.25.7 esta mañana.

    POR TANTO:
      - Si se publica la ClickOnce (sale 1.10.25.8, la Revision ya está preparada): ejecutar
        este script DESPUÉS de publicar la API y la ClickOnce.
      - Si NO se publica la ClickOnce: NO ejecutar el script todavía. Las entradas quedarían por
        encima de la versión publicada y reaparecerían en cada actualización hasta alcanzarla.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - El log de denegación de "servir junto" ahora guarda el número de pedido (diagnóstico
        interno, NestoAPI#468): sin él no se podía reconstruir después qué se estaba guardando.

    Ejecutar en SSMS contra NV.
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.25.8'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-09';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
(@version, @fecha, 'Corregido', 'Ampliar un pedido ya no dice que se ha ampliado cuando no se ha guardado',
 'Al ampliar un pedido desde la plantilla, si el servidor rechazaba el guardado, salía igualmente "ampliado correctamente" y en realidad no se guardaba nada: el vendedor cerraba la ventana convencido de que la ampliación estaba hecha. Ahora, si el guardado se rechaza, se ve el motivo y el pedido queda como estaba. Lo mismo al unir dos pedidos desde la lista.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Un pedido con muestras ya no se bloquea por las unidades que él mismo tiene reservadas',
 'Al guardar un pedido con una muestra dentro y "Servir junto" desmarcado, el aviso de que la muestra se quedaría pendiente saltaba aunque hubiera stock de sobra, porque las unidades reservadas por el propio pedido se contaban como si fueran de otro. En pantalla decía que sí se podía y al guardar decía que no. Ahora las dos comprobaciones cuentan igual.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Mejorado', 'Un pedido que ya tenía "Servir junto" desmarcado no vuelve a preguntarlo en cada cambio',
 'Un pedido que llevaba días con la casilla desmarcada y una muestra dentro quedaba bloqueado para cualquier modificación, aunque el cambio no tuviera nada que ver con la muestra. Ahora solo se comprueba lo que empeora la situación: productos nuevos o cantidades que suben. Al desmarcar la casilla se sigue revisando el pedido entero.', 'NestoAPI', 1, 'sa'),

(@version, @fecha, 'Corregido', 'Al tocar un pedido ya facturado, el aviso dice que está facturado',
 'Si se intentaba modificar un pedido ya facturado que llevaba muestras, el mensaje que salía era el de las muestras, que no explicaba nada de lo que pasaba de verdad. Ahora se avisa primero de que el pedido está facturado.', 'NestoAPI', 1, 'sa');

SELECT Version, Categoria, Titulo FROM Novedades WHERE Version = @version ORDER BY Id;
