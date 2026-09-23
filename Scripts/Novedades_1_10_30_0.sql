/*
    Novedades de la versión 1.10.30.0 (23/09/2026, segunda publicación del día).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.30.0). SÍ se sube la tercera cifra: se estrenan dos cosas
    que el usuario ve (votos y comentarios en Novedades; el combo «Servir» que solo deja elegir lo que
    tiene sentido).

    ORDEN:
      1) Scripts/Issue520_FeedbackNovedades.sql (como sa): crea las tablas de votos y comentarios con sus
         GRANT. Se puede lanzar antes o después de publicar la API: sin tablas, Novedades funciona como
         siempre (sin votos ni comentarios).
      2) Publicar NestoAPI.
      3) Publicar la ClickOnce 1.10.30.0 (Clean + Rebuild).
      4) ESTE script. Idempotente: compara por Versión + Título.

    RECORDATORIO (no es de esta versión): a las 18:00 arranca el job del Agente de SQL de la carga de
    #498 si se creó con Scripts/Issue498_CargaFechasCompras_JobAgente.sql.

    SE OMITE A PROPÓSITO (el usuario no lo nota):
      - Nesto#340 fase 4A (Agencias a CommunityToolkit) y #326 (proveedor de Verifactu en un único punto).
      - El código compartido entre la plantilla y el detalle de pedido.
      - Que en tienda el servidor corrija el modo en vez de rechazarlo (los clientes ya solo ofrecen ese).
      - El endpoint de revisión del feedback (lo usa Informática).
      - Tests, issues.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.30.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-23';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Ahora podéis opinar sobre cada novedad: me gusta, no me gusta y comentarios',
     N'Debajo de cada novedad hay un 👍 y un 👎 para decir qué os parece (un voto por persona; pulsando otra vez el mismo se quita) y un botón «Comentarios» para contar lo que queráis: si algo no funciona como esperabais, si se os ocurre una mejora o si os ha venido bien. Se puede adjuntar una captura sin guardar ningún archivo: haced el recorte (Win + Mayús + S), abrid los comentarios y Nesto os pregunta si queréis adjuntar la imagen copiada (también vale Ctrl+V). Los comentarios los veis todos, con el nombre de quien los escribe, y cada uno puede borrar los suyos. Los leemos a diario para mejorar el programa y arreglar lo que nos contéis.'),
    ('Mejorado', N'El desplegable «Servir» solo deja elegir lo que tiene sentido para el pedido',
     N'En la plantilla y en el detalle del pedido, los modos de entrega que no tienen sentido con el stock del pedido aparecen en gris y, al pasar el ratón, explican por qué. Si todo el pedido tiene stock en Algete, solo se puede «Todo junto»; si no hay nada que traer de las tiendas, no se puede «Tras reponer de tiendas»; y en los pedidos de tienda (Alcobendas y Reina) el cliente se lleva lo que hay, así que solo «Según vaya entrando». Si al cambiar las líneas o el almacén el modo que habíais elegido deja de valer, Nesto lo cambia al que corresponde y os avisa. Y si el stock cambia justo mientras se monta el pedido, al guardar os dice qué modo elegir.'),
    ('Corregido', N'Cambiar el reembolso de un envío tramitado ya no da error de liquidación',
     N'En Agencias, al cambiar el reembolso de un envío ya tramitado (por ejemplo, un reembolso devuelto que se pone a cero) a veces salía el error «No se puede liquidar el movimiento… pendiente del movimiento 0,00 €» y había que intentarlo otra vez. Ya no pasa: sale a la primera.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 3 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
