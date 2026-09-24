/*
    Novedades de la versión 1.10.31.0 (24/09/2026).

    Tanda: NestoAPI + Nesto (ClickOnce 1.10.31.0). SÍ se sube la tercera cifra: se estrenan cosas que
    el usuario ve (sugerir características y buscador en Novedades; la cuenta del recibo bancario).

    ORDEN:
      1) Scripts/Issue526_SugerenciasNovedades.sql (como sa), ANTES de publicar la API.
      2) Publicar NestoAPI (antes que TNV: la app nueva manda regalos que solo entiende la API nueva).
      3) Publicar la ClickOnce 1.10.31.0 (Clean + Rebuild).
      4) ESTE script. Idempotente: compara por Versión + Título.

    SE OMITE A PROPÓSITO (el usuario de Nesto no lo nota):
      - #524/#525/#530: precios, Ganavisiones y regalos en los pedidos de la app de clientes (TNV).
      - #531: el endpoint con el que contesta el asistente (se nota en los comentarios, no hace falta contarlo).
      - Nesto#488 (resto): comandos fuera del hilo de la interfaz (sin síntoma visible hoy).
      - Tests, issues, refactorizaciones.

    Ejecutar en SSMS contra NV. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.31.0'; -- <-- AJUSTAR si se publica otra versión
DECLARE @fecha DATE = '2026-09-24';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000));

INSERT INTO @novedades (Categoria, Titulo, Descripcion) VALUES
    ('Nuevo', N'Podéis sugerir características nuevas desde Novedades',
     N'¿Se os ocurre que en alguna pantalla iría bien un botón, un dato o un atajo? Abrid Novedades y pulsad «Sugerir nueva característica»: escribid la idea como os salga y, si queréis, pegad una captura de la pantalla (Win + Mayús + S y luego Ctrl+V). Las sugerencias aparecen en una página nueva, «Sugerencias pendientes», a la que se llega con la flecha de la derecha desde la última versión. Ahí las veis todas, podéis votarlas y comentarlas, igual que las novedades. Las revisamos a diario: las que se hagan pasarán a su versión como novedad, conservando los votos y los comentarios. Si algo no se entiende, os preguntará por los comentarios el asistente de desarrollo (sale como «Claude (asistente IA)»).'),
    ('Nuevo', N'Buscador en Novedades',
     N'En la ventana de Novedades hay un buscador. Escribid una o varias palabras (da igual con o sin tildes) y os salen las novedades y sugerencias que las contienen, con su versión. Al pulsar una, la ventana salta a esa versión y la deja marcada, para que podáis comentarla sin tener que buscarla a mano.'),
    ('Nuevo', N'Con recibo bancario se ve qué cuenta se va a cargar',
     N'En la plantilla de ventas (al finalizar) y en el detalle del pedido, cuando la forma de pago es recibo bancario aparece la cuenta a la que se va a girar (por ejemplo, «ES91 …… 4321 — CaixaBank») y, si el cliente tiene varias, podéis elegir. Si el cliente no tiene ninguna cuenta válida (no tiene, está de baja o el IBAN está mal), sale un aviso en rojo: el recibo no se podría mandar al banco, así que pedid la cuenta o elegid otra forma de pago. Avisa, pero no impide guardar.'),
    ('Mejorado', N'Los regalos solo se pueden meter si hay stock, y un pedido no sale a medias si solo quedaría el regalo',
     N'Un regalo (Ganavisión, regalo por importe, material promocional) ya no se puede añadir si no hay stock para darlo: al guardar, Nesto os dice qué regalo no tiene stock para que elijáis otro. Lo mismo al aceptar un presupuesto o al subir la cantidad de un regalo que ya estaba en el pedido. Además, si un pedido se sirve por partes y lo único que se quedaría pendiente es un regalo, ya no sale a medias: espera a poder salir entero. Así no hay que hacer un envío solo para el regalo, de 0 €.'),
    ('Mejorado', N'El seguimiento de CTT entiende todos sus estados',
     N'En Agencias, los envíos de CTT reconocen ya todos los estados de la agencia: el «nuevo reparto» (cuando el cliente estaba ausente y vuelve a salir) sigue como tramitado, las devoluciones pasan a devueltos y los envíos estacionados, con reparto fallido o entregados en parte pasan a incidentados para que se revisen.'),
    ('Nuevo', N'Campana de avisos: os enteráis cuando os contestan en Novedades',
     N'Arriba a la derecha de la cinta hay una campana con el número de avisos sin leer. Os llega un aviso cuando el asistente de desarrollo contesta a un comentario vuestro en Novedades o cuando un compañero os menciona con @. El aviso llega al momento, sin tener que cerrar ni refrescar nada; al pulsarlo se abre Novedades justo en ese comentario. Desde la campana podéis marcar los avisos como leídos o borrarlos.'),
    ('Nuevo', N'Menciones con @ en Novedades',
     N'Al escribir un comentario o una sugerencia en Novedades podéis poner @ seguido del nombre de un compañero (por ejemplo, @Carlos). Al teclear la @ sale una lista con los nombres que se va filtrando según escribís: elegid con las flechas y Enter o con el ratón. La persona mencionada recibe un aviso en su campana y, al pulsarlo, va directa a ese comentario (o a la sugerencia). Se puede mencionar a cualquier compañero que use Nesto; da igual escribir el nombre con o sin tildes o mayúsculas. No os llega aviso de vuestras propias menciones, y si alguien ya recibe aviso porque le están contestando, no le llega dos veces. Un truco: si el asistente no entiende lo que pedís, mencionad a @Carlos para que lo mire.'),
    ('Mejorado', N'Con picking, el modo de entrega se pide a almacén',
     N'Si un pedido ya está en preparación (tiene picking) o ha salido parte hoy, ya no se puede cambiar el modo de entrega desde el pedido o la plantilla: Nesto explica por qué y os ofrece pedírselo a almacén con un correo. Lo intentarán, pero puede que ya no llegue a tiempo. Si una parte del pedido salió otro día y ahora no tiene picking, sí se puede cambiar para que el resto salga junto.'),
    ('Corregido', N'Los envíos de CTT se confirman en Amazon',
     N'Al confirmar en Amazon un pedido enviado por CTT, ahora se manda el transportista y el servicio «CTT 48h» como Amazon los pide, así que el pedido se marca como enviado. Además, Nesto espera la respuesta de Amazon: si algún pedido no se puede marcar, os dice el motivo en lugar de dar la confirmación por buena.'),
    ('Corregido', N'NIF incorrectos: los botones solo aparecen con un cliente seleccionado',
     N'En la ventana de clientes con NIF incorrecto parecía que los botones no funcionaban cuando no había ningún cliente seleccionado en la lista. Ahora, sin selección, en su lugar se lee «Selecciona un cliente de la lista para corregir su NIF o marcarlo como extranjero», y los botones salen en cuanto se elige uno.'),
    ('Mejorado', N'Crear albarán y factura desde cualquier pestaña del pedido',
     N'En el detalle del pedido, los botones Crear Albarán, Crear Factura, Crear Albarán y Factura e Imprimir están ahora en una barra debajo de las pestañas, así que se pueden usar desde Líneas sin volver a Cabecera. Si estáis escribiendo en una línea y pulsáis uno de ellos (o Guardar), la línea se da por terminada antes, para que no se quede nada sin guardar.'),
    ('Mejorado', N'La raya de colores junto a vuestro nombre dice si estáis conectados con el servidor',
     N'La rayita que hay arriba entre vuestro nombre y el del equipo ahora indica la conexión real con el servidor: verde, todo bien; ámbar, se ha perdido la conexión y Nesto está reintentando solo; roja, la sesión ha caducado. Si pasáis el ratón por encima, lo explica.'),
    ('Nuevo', N'En Agencias se ve qué envíos están ya en reparto',
     N'En la pestaña Tramitados hay una columna que marca los envíos que la agencia ya ha sacado a reparto (en principio, se entregan hoy), otra con el último evento que da la agencia, y en el título de la pestaña cuántos hay en reparto. El envío sigue en Tramitados: solo es un dato más.'),
    ('Corregido', N'Teclear la fecha de entrega en la plantilla ya no cierra Nesto',
     N'Si en la plantilla de ventas se tecleaba a mano una fecha de entrega anterior a la primera posible, Nesto daba un error. Ahora se ajusta sola a la primera fecha posible.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 14 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
