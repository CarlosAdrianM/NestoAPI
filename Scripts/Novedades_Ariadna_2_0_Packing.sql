/*
    Novedades de Ariadna 2.0 (05/10/2026): el packing pasa a estar dentro de Salidas y se prepara pedido a pedido
    leyendo lo que se mete en cada caja (NestoAPI#556, API cd2e1293 + Ariadna 7edb272).

    La novedad 473 («Packing con la foto de cada bulto») contaba el packing de antes (botón «Empaquetar» del inicio,
    solo fotos): los mozos empiezan hoy, así que se corrige su texto en vez de añadir otra que la contradiga.
    Se añade además una novedad para «juntos o separados» y «No aparece».

    Ejecutar en SSMS contra NV como sa. Idempotente. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

UPDATE Novedades
SET Titulo = N'Packing: cada pedido, leyendo lo que metes en la caja',
    Descripcion = N'El packing está dentro de Salidas: cuando un picking está recogido, sale como «Packing» con el mismo número (si lo recogiste en papel, pulsa «Packing (F2)»). Eliges el pedido, ves sus productos y su comentario de picking y lees cada unidad que metes en la caja; si lees algo de otro pedido, Ariadna te dice de quién es. Con todo dentro, Intro hace la foto, cierra la caja y pasas al siguiente pedido. Un packing a medias se sigue cuando quieras, desde cualquier PDA.'
WHERE Id = 473 AND Ambito = 'Ariadna' AND Titulo = N'Packing con la foto de cada bulto';

SELECT @@ROWCOUNT AS Novedad473Corregida;

DECLARE @titulo nvarchar(400) = N'Packing: pedidos juntos o separados, y lo que no aparece';

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
SELECT '2.0', '2026-10-05', 'Nuevo', @titulo,
       N'Si hay dos pedidos del mismo cliente y dirección, Ariadna pregunta si van juntos en las mismas cajas o en bultos separados (mira el comentario de picking). Si en la mesa falta una unidad que se recogió, búscala primero en las cajas de los otros clientes; si no aparece, «No aparece (F3)» lo apunta y avisa a Compras. Cerrar una caja con algo sin meter pide confirmación, por si es que no cabe.',
       'Ariadna', 1, 'sa'
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = @titulo);

SELECT @@ROWCOUNT AS NovedadesInsertadasAhora;

-- Comprobación: la 473 con el título nuevo y la nueva al final.
SELECT Id, Version, Categoria, Titulo, Ambito, Publicada
FROM Novedades WHERE Ambito = 'Ariadna' ORDER BY Id;
