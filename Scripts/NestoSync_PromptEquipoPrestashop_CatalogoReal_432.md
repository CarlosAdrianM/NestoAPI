# Petición al equipo del módulo de PrestaShop — catálogo real (NestoAPI#432)

**Contexto**: al retirarse la consulta legacy (finales de agosto de 2026), el pipeline
NestoSync se quedó sin puerta de publicación: publica todo lo que se encola. Vamos a poner
la puerta en NestoAPI, pero antes necesitamos medir el desvío REAL, y eso solo se ve desde
el catálogo de PrestaShop (lo que Nesto_sync registra como "mandado" no equivale a "hoy
está en la tienda": la consulta legacy corrigió detrás mientras siguió ejecutándose).

## 1. Lo que necesitamos: un export del catálogo

Un CSV (o consulta) con **todos los productos** de la tienda, con estas columnas:

- `id_product`
- `reference` (es el número de producto de Nesto)
- `active` (0/1)
- `date_add`

Con eso cruzamos contra Nesto y sacamos: (a) cuántos productos hay en la tienda que la
puerta nueva excluiría, y (b) si conviene retirarlos, desactivarlos o dejarlos. La retirada,
si la hay, se decidirá con esos números y os la pediríamos como operación aparte (sabemos
que `Estado < 0` ya desactiva vía prestashop-nestosync#8).

## 2. Una pregunta que puede ahorrar mucho trabajo: los sin stock

La exclusión que más productos movía en el legacy era **"sin stock fuera de la web"**
(salvo categorías Ofertas y Nueva Colección Essie). Medido hoy: 1.688 productos vivos con
stock < 1, de los que 1.674 ya están publicados.

En el pipeline nuevo **no queremos** replicar eso saltándonos la publicación: si dejamos de
mandar actualizaciones de un producto sin stock, se le congela el precio en la tienda (y
desde el cutover de precios eso es un problema de verdad).

La pregunta: **¿podéis ocultar del catálogo los productos sin stock desde el propio
PrestaShop** (opción nativa de disponibilidad/visibilidad, o en el módulo al recibir el
stock a 0), manteniendo el producto y sus datos actualizados? La tienda ya recibe el stock
por el pipeline, así que tiene el dato. Si eso es viable, la regla de stock desaparece de
la puerta y queda donde mejor funciona: en el consumidor, con excepciones por categoría
(Ofertas / Nueva Colección Essie) si siguen haciendo falta.

## 3. Qué NO os pedimos todavía

- Ninguna retirada masiva: primero los números del punto 1.
- Ningún cambio en la puerta de publicación: esa va en NestoAPI.
