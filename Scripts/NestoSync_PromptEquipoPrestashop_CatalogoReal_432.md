# Petición al equipo del módulo de PrestaShop — productos sin stock (NestoAPI#432)

**Contexto**: al retirarse la consulta legacy (finales de agosto de 2026), el pipeline
NestoSync se quedó sin puerta de publicación. La puerta ya está implementada en NestoAPI
(`PuertaPublicacionTienda`, NestoAPI#432): decide qué productos viajan a la tienda con las
reglas de negocio que aplicaba el legacy. Los productos ya publicados **se quedan como
están** (decisión de Carlos, 01/09/26): no hay retirada masiva, entre otras cosas porque
los productos entran inactivos y los activáis a mano, así que ya hay una puerta humana del
lado de la tienda.

Queda UNA sola cosa, y es una pregunta:

## Los productos sin stock

La exclusión que más productos movía en el legacy era **"sin stock fuera de la web"**
(salvo las categorías Ofertas y Nueva Colección Essie). Medido el 01/09/26: 1.688 productos
vivos con stock < 1, de los que 1.674 ya están publicados.

En el pipeline nuevo **no** vamos a replicar eso dejando de publicar: si a un producto sin
stock dejan de llegarle mensajes, se le congela el precio en la tienda (y desde el cutover
de precios eso es un problema de verdad, no cosmético).

La pregunta: **¿podéis ocultar del catálogo los productos sin stock desde el propio
PrestaShop** (opción nativa de disponibilidad/visibilidad, o en el módulo al recibir el
stock a 0), manteniendo el producto y sus datos actualizados? La tienda ya recibe el stock
por el pipeline, así que tiene el dato. Si es viable, la regla queda donde mejor funciona:
en el consumidor, con excepciones por categoría (Ofertas / Nueva Colección Essie) si siguen
haciendo falta.
