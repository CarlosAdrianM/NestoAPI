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
    ('Corregido', N'NIF incorrectos: los botones solo aparecen con un cliente seleccionado',
     N'En la ventana de clientes con NIF incorrecto parecía que los botones no funcionaban cuando no había ningún cliente seleccionado en la lista. Ahora, sin selección, en su lugar se lee «Selecciona un cliente de la lista para corregir su NIF o marcarlo como extranjero», y los botones salen en cuanto se elige uno.');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa'
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: deben salir 6 filas, sin repetidos.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Version = @version ORDER BY Id;
