# Fotos y activacion de 25 fichas de Mirplay (campana SEO de fichas incompletas)

09/09/2026 - Para el equipo del servidor de PrestaShop (produccion).

## El problema que estamos atacando

La familia **Mirplay mobiliario** (fabricante 381) tiene **217 referencias vivas** en
Nesto. Las 217 existen ya como producto en PrestaShop, pero **203 estan `active = 0`,
sin ninguna imagen y con la descripcion vacia**: lo unico que hay en
`description` es el nombre en mayusculas mas la coletilla de sobre-pedido que
inyecta el modulo. Son fichas que nacieron del pipeline y que nadie ha tenido
tiempo de rellenar.

Esta es la **primera tanda de 25**. Si sale bien, repetiremos el mismo circuito con
el resto de Mirplay y con otras familias, asi que nos interesa tanto el resultado
como dejar el procedimiento afinado.

**Las 25 son 25 modelos distintos.** Hemos dejado fuera a proposito toda referencia
que en Nesto tenga hermanas de color (Chloe tiene 32, Check 4, Ralph 2...). Esas
familias las vamos a tratar como producto padre y variantes, y hasta que eso este
decidido no queremos crear en la tienda veinte fichas casi identicas compitiendo
entre si. Ninguna de las 25 de esta lista tiene ese problema.

## Reparto de tareas

| Tarea | Quien | Estado |
|---|---|---|
| Elegir las 25 referencias | Nesto (hecho con datos de ambos lados) | Hecho |
| Escribir nombre, descripcion corta y larga | Nesto | Hecho, se publica por el bus |
| **Subir las fotos a cada producto** | **Vosotros** | **Lo que os pedimos** |
| **Activar los productos** | **Vosotros** | **Lo que os pedimos** |
| Revision de calidad final | Companeros humanos de Nueva Vision | Despues |

Las fotos no pueden ir por el canal NestoSync: el mensaje `Productos` lleva un campo
`UrlFoto`, pero es de **lectura** (la URL sale de vuestra propia tienda y solo viaja
hacia Odoo), y el webservice de PrestaShop lo tenemos en **solo lectura**
(`post="false"` en `/api`). Por eso os lo pedimos a vosotros.

## Lo que llega por vuestro lado sin que hagais nada

Ejecutamos en Nesto un script que rellena `Nombre`, `DescripcionBreve` y
`Descripcion` de las 25 y las encola. En un plazo de minutos el modulo NestoSync
recibira el mensaje `Productos` de cada una con los tres textos informados y los
escribira en `name`, `description_short` y `description`.

Tres avisos sobre esos textos:

1. **El nombre cambia.** Pasan de `LAVACABEZAS OLIVER` a
   `Lavacabezas Oliver de polipropileno - Mirplay`. Es deliberado: el nombre viejo es
   el de la ficha interna de Nesto y no sirve para posicionar.
   Al cambiar el nombre cambia el `link_rewrite`, pero **como los productos estan
   inactivos y nunca han estado publicados, no hay URL antigua que redirigir**.
2. **No anadimos la coletilla de sobre-pedido**: la sigue poniendo el modulo
   (`NESTOSYNC_LABELS_ENABLED`). Si la veis duplicada, avisadnos.
3. **El marcado esta limitado a proposito** a `<p> <h2> <h3> <ul> <li> <strong> <em>`,
   sin atributos, sin `<h1>`, sin `<style>` y sin `<script>`, para que no toque el
   CSS ni las cabeceras de vuestra plantilla.

**Los precios no se tocan.** Las 25 quedan en modo derivado (el 30 % de siempre),
que es exactamente lo que ya calculan hoy.

## Lo que os pedimos, en orden

### 1. Subir las fotos

Al final de este documento teneis, por cada referencia, el `id_product` de vuestra
base y las URLs de las fotos **en la web del fabricante**, en el orden en que las
queremos. Para cada producto:

- Descargad las imagenes de esas URLs (son JPG a tamano completo, publicas).
- Subidlas al producto **en ese orden**. La **primera es la portada** (`cover`).
- Poned `legend` (el alt) con el texto que os indicamos en cada linea. Es el alt que
  Google lee, asi que preferimos ese y no el nombre de fichero.
- Si alguna URL da 404 o la foto es claramente de otro modelo, **saltadla y decidnos
  cual**: las hemos extraido de la galeria de la ficha del fabricante de forma
  automatica y puede haberse colado alguna.

Son **79 imagenes** repartidas en 25 productos.

### 2. Comprobar que la descripcion ha llegado

Antes de activar, mirad una ficha cualquiera de la lista y confirmad que
`description_short` y `description` tienen ya el texto nuevo. Si siguen con el
nombre en mayusculas, la cola de Nesto no ha terminado: esperad y avisadnos.

### 3. Activar los 25 productos

`active = 1`. Comprobad de paso que:

- Aparecen en la marca **Mirplay mobiliario** (`id_manufacturer` 381).
- Tienen categoria asignada y se ven en el arbol.
- El precio que se muestra es coherente con el resto de la familia.

### 4. Devolvernos un informe

Una linea por referencia con: `reference`, `id_product`, numero de imagenes subidas,
`id_default_image`, `active` y la **URL publica final de la ficha**. Esa lista es la
que van a revisar nuestros companeros, y las URLs nos ahorran buscarlas una a una.

## Preguntas que nos ayudan a sistematizar esto

Vamos a repetir el circuito muchas veces, asi que nos interesan estas tres
respuestas mas que la tanda en si:

1. **Subida de imagenes: que es lo mas comodo para vosotros?** Un CSV con
   `reference;orden;url;alt` como el que va aqui, un ZIP con las fotos ya nombradas,
   o que os pasemos las URLs y useis vuestro propio script? Nos adaptamos al formato
   que menos trabajo manual os genere.
2. **Podriais automatizarlo?** Si un dia el modulo NestoSync recibiera una lista de
   URLs de imagen dentro del mensaje `Productos` y las descargara solo, esto dejaria
   de necesitar intervencion. Antes de proponerlo como desarrollo queremos saber si
   lo veis viable y que problemas le veis (duplicados al republicar, peso, tiempos).
3. **Cuantas fichas mas hay en este estado?** Nosotros contamos 203 solo en Mirplay.
   Si podeis sacar el recuento de **productos con `active = 0` y sin imagen** por
   marca, nos sirve para priorizar por donde seguimos.

---

## Las 25 referencias

### 45735 - Lavacabezas Oliver de polipropileno - Mirplay

- `id_product` **66635** - nombre actual en la tienda: `LAVACABEZAS OLIVER`
- Ficha del fabricante: https://mirplaysalon.com/producto/lavacabezas-oliver
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver-2.jpg
   alt: `Lavacabezas Oliver de polipropileno`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver-6.jpg
   alt: `Lavacabezas Oliver de polipropileno (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver.jpg
   alt: `Lavacabezas Oliver de polipropileno (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver-3.jpg
   alt: `Lavacabezas Oliver de polipropileno (vista 4 de 4)`

### 45732 - Lavacabezas portátil Lee con depósito de 10 litros - Mirplay

- `id_product` **66638** - nombre actual en la tienda: `LAVACABEZAS LEE`
- Ficha del fabricante: https://mirplaysalon.com/producto/lavacabezas-portatil-lee
- 3 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/01/Lee_Fondo-Lee.jpg
   alt: `Lavacabezas portátil Lee con depósito de 10 litros`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/01/Lee_Fondo-Lee-2.jpg
   alt: `Lavacabezas portátil Lee con depósito de 10 litros (vista 2 de 3)`
3. https://mirplaysalon.com/wp-content/uploads/2022/01/Lee_Fondo-Lee-3.jpg
   alt: `Lavacabezas portátil Lee con depósito de 10 litros (vista 3 de 3)`

### 45729 - Reposapiés Joker para lavacabezas - Mirplay

- `id_product` **66641** - nombre actual en la tienda: `LAVACABEZAS JOKER`
- Ficha del fabricante: https://mirplaysalon.com/producto/reposapies-joker
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/02/0031_Joker_Fondo-Joker.jpg
   alt: `Reposapiés Joker para lavacabezas`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/02/0029_Joker_Fondo-Joker.jpg
   alt: `Reposapiés Joker para lavacabezas (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2022/02/0030_Joker_Fondo-Joker.jpg
   alt: `Reposapiés Joker para lavacabezas (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2022/02/0028_Joker_Fondo-Joker.jpg
   alt: `Reposapiés Joker para lavacabezas (vista 4 de 4)`

### 45723 - Cojín alzador Bonnie para lavacabezas - Mirplay

- `id_product` **66647** - nombre actual en la tienda: `LAVACABEZAS BONNIE`
- Ficha del fabricante: https://mirplaysalon.com/producto/cojin-alzador-bonnie
- 2 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/02/0034_Bonnie_Fondo-Bonnie.jpg
   alt: `Cojín alzador Bonnie para lavacabezas`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/02/0033_Bonnie_Fondo-Bonnie.jpg
   alt: `Cojín alzador Bonnie para lavacabezas (vista 2 de 2)`

### 45821 - Sillón de barbero Prince de aluminio inyectado - Mirplay

- `id_product` **66549** - nombre actual en la tienda: `SILLON DE BARBERO PRINCE`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-prince
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Prince_Fondo-Prince.jpg
   alt: `Sillón de barbero Prince de aluminio inyectado`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/PRINCE_1_Fondo-PRINCE_1.jpg
   alt: `Sillón de barbero Prince de aluminio inyectado (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/PRINCE_6_Fondo-PRINCE_6.jpg
   alt: `Sillón de barbero Prince de aluminio inyectado (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/PRINCE_4_Fondo-PRINCE_4.jpg
   alt: `Sillón de barbero Prince de aluminio inyectado (vista 4 de 4)`

### 45812 - Sillón de barbero Bart vintage con soporte de toalla - Mirplay

- `id_product` **66558** - nombre actual en la tienda: `SILLON DE BARBERO BART`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-bart
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Bart_Fondo-Bart.jpg
   alt: `Sillón de barbero Bart vintage con soporte de toalla`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/Bart-4_Fondo-Bart-4.jpg
   alt: `Sillón de barbero Bart vintage con soporte de toalla (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/Bart-2_Fondo-Bart-2.jpg
   alt: `Sillón de barbero Bart vintage con soporte de toalla (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/Bart-3_Fondo-Bart-3.jpg
   alt: `Sillón de barbero Bart vintage con soporte de toalla (vista 4 de 4)`

### 45820 - Sillón de barbero Karl clásico y económico - Mirplay

- `id_product` **66550** - nombre actual en la tienda: `SILLON DE BARBERO KARL`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-karl
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Karl_Fondo-Karl.jpg
   alt: `Sillón de barbero Karl clásico y económico`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/Karl-4_Fondo-Karl-4.jpg
   alt: `Sillón de barbero Karl clásico y económico (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/Karl-2_Fondo-Karl-2.jpg
   alt: `Sillón de barbero Karl clásico y económico (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/Karl-5_Fondo-Karl-5.jpg
   alt: `Sillón de barbero Karl clásico y económico (vista 4 de 4)`

### 45811 - Sillón de barbero Asher hidráulico - Mirplay

- `id_product` **66559** - nombre actual en la tienda: `SILLON DE BARBERO ASHER`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-asher
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher-2.jpg
   alt: `Sillón de barbero Asher hidráulico`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher.jpg
   alt: `Sillón de barbero Asher hidráulico (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher-3.jpg
   alt: `Sillón de barbero Asher hidráulico (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher-4.jpg
   alt: `Sillón de barbero Asher hidráulico (vista 4 de 4)`

### 45817 - Sillón de barbero Dave retro con acolchado en rombos - Mirplay

- `id_product` **66553** - nombre actual en la tienda: `SILLON DE BARBERO DAVE`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-dave
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Dave-2_Fondo-Dave-2.jpg
   alt: `Sillón de barbero Dave retro con acolchado en rombos`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/Dave_Fondo-Dave.jpg
   alt: `Sillón de barbero Dave retro con acolchado en rombos (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/Dave-4_Fondo-Dave-4.jpg
   alt: `Sillón de barbero Dave retro con acolchado en rombos (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/DAVE_3_WEB_Fondo-DAVE_3_WEB.jpg
   alt: `Sillón de barbero Dave retro con acolchado en rombos (vista 4 de 4)`

### 45822 - Sillón de barbero Silvia B con reposapiés antivuelco - Mirplay

- `id_product` **66548** - nombre actual en la tienda: `SILLON DE BARBERO SILVIA B`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-silvia-b
- 2 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Silvia-B_Fondo-Silvia-B-2.jpg
   alt: `Sillón de barbero Silvia B con reposapiés antivuelco`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/Silvia-B_Fondo-Silvia-B.jpg
   alt: `Sillón de barbero Silvia B con reposapiés antivuelco (vista 2 de 2)`

### 45818 - Sillón de barbero Joey 1 hidráulico acolchado - Mirplay

- `id_product` **66552** - nombre actual en la tienda: `SILLON DE BARBERO JOEY 1`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-barbero-joey-1
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey.jpg
   alt: `Sillón de barbero Joey 1 hidráulico acolchado`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey-4.jpg
   alt: `Sillón de barbero Joey 1 hidráulico acolchado (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey-2.jpg
   alt: `Sillón de barbero Joey 1 hidráulico acolchado (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey-3.jpg
   alt: `Sillón de barbero Joey 1 hidráulico acolchado (vista 4 de 4)`

### 44497 - Sillón de corte Alya B con respaldo de madera natural - Mirplay

- `id_product` **66453** - nombre actual en la tienda: `SILLON ALYA B`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-corte-alya-b
- 1 imagen, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2023/06/ALYA-B-BR-2_Fondo-ALYA-B-BR-2.jpg
   alt: `Sillón de corte Alya B con respaldo de madera natural`   **(portada)**

### 45800 - Sillón de corte Horace con capitoné y base cromada - Mirplay

- `id_product` **66570** - nombre actual en la tienda: `SILLON HORACE`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-corte-horace
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/HORACE3salon.17_Fondo-HORACE3salon.17.jpg
   alt: `Sillón de corte Horace con capitoné y base cromada`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/HORACE4salon.17_Fondo-HORACE4salon.17.jpg
   alt: `Sillón de corte Horace con capitoné y base cromada (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/HORACE2salon.17_Fondo-HORACE2salon.17.jpg
   alt: `Sillón de corte Horace con capitoné y base cromada (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/HORACEsalon.17_Fondo-HORACEsalon.17.jpg
   alt: `Sillón de corte Horace con capitoné y base cromada (vista 4 de 4)`

### 45750 - Sillón de corte Bertie de líneas redondeadas - Mirplay

- `id_product` **66620** - nombre actual en la tienda: `SILLON BERTIE`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-corte-bertie
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large1_Fondo-Bertie-Large1.jpg
   alt: `Sillón de corte Bertie de líneas redondeadas`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large3_Fondo-Bertie-Large3.jpg
   alt: `Sillón de corte Bertie de líneas redondeadas (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large2_Fondo-Bertie-Large2.jpg
   alt: `Sillón de corte Bertie de líneas redondeadas (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large_Fondo-Bertie-Large.jpg
   alt: `Sillón de corte Bertie de líneas redondeadas (vista 4 de 4)`

### 45788 - Sillón de corte Cindy con bomba hidráulica y freno - Mirplay

- `id_product` **66582** - nombre actual en la tienda: `SILLON CINDY`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-corte-cindy
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small3_Fondo-Cindy-Small3.jpg
   alt: `Sillón de corte Cindy con bomba hidráulica y freno`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small_Fondo-Cindy-Small.jpg
   alt: `Sillón de corte Cindy con bomba hidráulica y freno (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small2_Fondo-Cindy-Small2.jpg
   alt: `Sillón de corte Cindy con bomba hidráulica y freno (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small1_Fondo-Cindy-Small1.jpg
   alt: `Sillón de corte Cindy con bomba hidráulica y freno (vista 4 de 4)`

### 45799 - Sillón de corte infantil Gat con cinturón de seguridad - Mirplay

- `id_product` **66571** - nombre actual en la tienda: `SILLON GAT`
- Ficha del fabricante: https://mirplaysalon.com/producto/sillon-de-corte-infantil-gat
- 2 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/GAT-2_Fondo-GAT-2.jpg
   alt: `Sillón de corte infantil Gat con cinturón de seguridad`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2021/12/GAT-1_Fondo-GAT-1.jpg
   alt: `Sillón de corte infantil Gat con cinturón de seguridad (vista 2 de 2)`

### 45854 - Tocador Bobbie B de melamina a doble cara - Mirplay

- `id_product` **66516** - nombre actual en la tienda: `TOCADOR BOBBIE B`
- Ficha del fabricante: https://mirplaysalon.com/producto/tocador-bobbie-b
- 1 imagen, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/01/0047_Capa-1_Fondo-Capa-1.jpg
   alt: `Tocador Bobbie B de melamina a doble cara`   **(portada)**

### 45849 - Taburete de sillín Ian con ruedas antipelo - Mirplay

- `id_product` **66521** - nombre actual en la tienda: `TABURETE IAN`
- Ficha del fabricante: https://mirplaysalon.com/producto/taburete-ian
- 4 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/01/0010_IAN5.salon_.17_Fondo-IAN5.salon_.17.jpg
   alt: `Taburete de sillín Ian con ruedas antipelo`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/01/0011_IAN3.salon_.17_Fondo-IAN3.salon_.17.jpg
   alt: `Taburete de sillín Ian con ruedas antipelo (vista 2 de 4)`
3. https://mirplaysalon.com/wp-content/uploads/2022/01/0012_IAN2.salon_.17_Fondo-IAN2.salon_.17.jpg
   alt: `Taburete de sillín Ian con ruedas antipelo (vista 3 de 4)`
4. https://mirplaysalon.com/wp-content/uploads/2022/01/0013_IAN4.salon_.17_Fondo-IAN4.salon_.17.jpg
   alt: `Taburete de sillín Ian con ruedas antipelo (vista 4 de 4)`

### 45851 - Taburete Nil con ruedas de silicona - Mirplay

- `id_product` **66519** - nombre actual en la tienda: `TABURETE NIL`
- Ficha del fabricante: https://mirplaysalon.com/producto/taburete-nil
- 1 imagen, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2021/12/0006_Nil_Fondo-Nil.jpg
   alt: `Taburete Nil con ruedas de silicona`   **(portada)**

### 45749 - Bandeja auxiliar ajustable Ari para sillón - Mirplay

- `id_product` **66621** - nombre actual en la tienda: `SILLON ARI`
- Ficha del fabricante: https://mirplaysalon.com/producto/bandeja-ajustable-ari
- 3 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2025/01/Ari.jpg
   alt: `Bandeja auxiliar ajustable Ari para sillón`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2025/01/ARI-Adjustable-tray-Large.jpg
   alt: `Bandeja auxiliar ajustable Ari para sillón (vista 2 de 3)`
3. https://mirplaysalon.com/wp-content/uploads/2025/01/ARI-Adjustable-tray-Large-1.jpg
   alt: `Bandeja auxiliar ajustable Ari para sillón (vista 3 de 3)`

### 45711 - Calentador y esterilizador de toallas Ivar 18 L con luz UV - Mirplay

- `id_product` **66659** - nombre actual en la tienda: `COMPLEMENTO IVAR`
- Ficha del fabricante: https://mirplaysalon.com/producto/calentador-toallas-ivar
- 1 imagen, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/07/0005_IVAR_1_Fondo-IVAR_1.jpg
   alt: `Calentador y esterilizador de toallas Ivar 18 L con luz UV`   **(portada)**

### 45710 - Calentador y esterilizador de toallas Hank 10,6 L - Mirplay

- `id_product` **66660** - nombre actual en la tienda: `COMPLEMENTO HANK`
- Ficha del fabricante: https://mirplaysalon.com/producto/calentador-toallas-hank
- 1 imagen, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/07/0004_HANK_1_1x1_Fondo-HANK_1_1x1.jpg
   alt: `Calentador y esterilizador de toallas Hank 10,6 L`   **(portada)**

### 45717 - Poste de barbero Paul de acero inoxidable - Mirplay

- `id_product` **66653** - nombre actual en la tienda: `COMPLEMENTO PAUL`
- Ficha del fabricante: https://mirplaysalon.com/producto/poste-de-barbero-paul
- 1 imagen, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/05/0029_PAUL_Fondo-PAUL.jpg
   alt: `Poste de barbero Paul de acero inoxidable`   **(portada)**

### 45713 - Alfombra antifatiga Layla para puesto de trabajo - Mirplay

- `id_product` **66657** - nombre actual en la tienda: `COMPLEMENTO LAYLA`
- Ficha del fabricante: https://mirplaysalon.com/producto/alfombra-antifatiga-layla
- 2 imagenes, en este orden:

1. https://mirplaysalon.com/wp-content/uploads/2022/02/0014_LAYLA_1_Fondo-LAYLA_1.jpg
   alt: `Alfombra antifatiga Layla para puesto de trabajo`   **(portada)**
2. https://mirplaysalon.com/wp-content/uploads/2022/02/0001_Layla.jpg
   alt: `Alfombra antifatiga Layla para puesto de trabajo (vista 2 de 2)`

### 44367 - Sillón de pedicura Anita con bañera de pies - Mirplay Beauty

- `id_product` **66457** - nombre actual en la tienda: `SILLON DE PEDICURA CON BAÑERA DE PIES`
- Ficha del fabricante: https://www.mirplaybeauty.com/products/p/anita
- 11 imagenes, en este orden:

1. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/b7f68cd2-2ff4-4adc-bed4-bdb4f3026b74/Anita+-+2000+x+20001.jpg
   alt: `Sillón de pedicura Anita con bañera de pies`   **(portada)**
2. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/8c101b9d-8336-4bbd-be53-4bd9a68b732e/Anita+-+2000+x+200012.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 2 de 11)`
3. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/3f5dffb2-b01a-489f-bacc-541c4018a424/Anita+-+2000+x+200011.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 3 de 11)`
4. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/d0d50778-8075-437c-8051-a3c2ea2fc797/Anita+-+2000+x+20004.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 4 de 11)`
5. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/ad9ebcf7-c2db-4896-bd88-1738b54a1bbb/Anita+-+2000+x+20005.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 5 de 11)`
6. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/02deb3be-3ba0-4f8d-b42a-f2bf1f7b7a28/Anita+-+2000+x+2000.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 6 de 11)`
7. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/fe518b46-a428-4866-aa51-d00afad41537/Anita+-+2000+x+20007.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 7 de 11)`
8. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/251c6ac3-579c-49dd-aeb5-d37c892f86df/Anita+-+2000+x+20009.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 8 de 11)`
9. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/9091f85e-9e5d-468a-bfe5-f53a1463dfe7/Anita+-+2000+x+2006.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 9 de 11)`
10. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/d32c3c52-02cf-4ef0-8d33-53ca680b6f60/Anita+-+2000+x+20003.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 10 de 11)`
11. https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/cb85414d-84bd-4011-85c9-3c992a424539/Anita+-+2000+x+20008.jpg
   alt: `Sillón de pedicura Anita con bañera de pies (vista 11 de 11)`

---

## Lo mismo en CSV, por si os viene mejor

```
reference;id_product;orden;url;alt
45735;66635;1;https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver-2.jpg;Lavacabezas Oliver de polipropileno
45735;66635;2;https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver-6.jpg;Lavacabezas Oliver de polipropileno (vista 2 de 4)
45735;66635;3;https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver.jpg;Lavacabezas Oliver de polipropileno (vista 3 de 4)
45735;66635;4;https://mirplaysalon.com/wp-content/uploads/2022/01/Oliver_Fondo-Oliver-3.jpg;Lavacabezas Oliver de polipropileno (vista 4 de 4)
45732;66638;1;https://mirplaysalon.com/wp-content/uploads/2022/01/Lee_Fondo-Lee.jpg;Lavacabezas portátil Lee con depósito de 10 litros
45732;66638;2;https://mirplaysalon.com/wp-content/uploads/2022/01/Lee_Fondo-Lee-2.jpg;Lavacabezas portátil Lee con depósito de 10 litros (vista 2 de 3)
45732;66638;3;https://mirplaysalon.com/wp-content/uploads/2022/01/Lee_Fondo-Lee-3.jpg;Lavacabezas portátil Lee con depósito de 10 litros (vista 3 de 3)
45729;66641;1;https://mirplaysalon.com/wp-content/uploads/2022/02/0031_Joker_Fondo-Joker.jpg;Reposapiés Joker para lavacabezas
45729;66641;2;https://mirplaysalon.com/wp-content/uploads/2022/02/0029_Joker_Fondo-Joker.jpg;Reposapiés Joker para lavacabezas (vista 2 de 4)
45729;66641;3;https://mirplaysalon.com/wp-content/uploads/2022/02/0030_Joker_Fondo-Joker.jpg;Reposapiés Joker para lavacabezas (vista 3 de 4)
45729;66641;4;https://mirplaysalon.com/wp-content/uploads/2022/02/0028_Joker_Fondo-Joker.jpg;Reposapiés Joker para lavacabezas (vista 4 de 4)
45723;66647;1;https://mirplaysalon.com/wp-content/uploads/2022/02/0034_Bonnie_Fondo-Bonnie.jpg;Cojín alzador Bonnie para lavacabezas
45723;66647;2;https://mirplaysalon.com/wp-content/uploads/2022/02/0033_Bonnie_Fondo-Bonnie.jpg;Cojín alzador Bonnie para lavacabezas (vista 2 de 2)
45821;66549;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Prince_Fondo-Prince.jpg;Sillón de barbero Prince de aluminio inyectado
45821;66549;2;https://mirplaysalon.com/wp-content/uploads/2021/12/PRINCE_1_Fondo-PRINCE_1.jpg;Sillón de barbero Prince de aluminio inyectado (vista 2 de 4)
45821;66549;3;https://mirplaysalon.com/wp-content/uploads/2021/12/PRINCE_6_Fondo-PRINCE_6.jpg;Sillón de barbero Prince de aluminio inyectado (vista 3 de 4)
45821;66549;4;https://mirplaysalon.com/wp-content/uploads/2021/12/PRINCE_4_Fondo-PRINCE_4.jpg;Sillón de barbero Prince de aluminio inyectado (vista 4 de 4)
45812;66558;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Bart_Fondo-Bart.jpg;Sillón de barbero Bart vintage con soporte de toalla
45812;66558;2;https://mirplaysalon.com/wp-content/uploads/2021/12/Bart-4_Fondo-Bart-4.jpg;Sillón de barbero Bart vintage con soporte de toalla (vista 2 de 4)
45812;66558;3;https://mirplaysalon.com/wp-content/uploads/2021/12/Bart-2_Fondo-Bart-2.jpg;Sillón de barbero Bart vintage con soporte de toalla (vista 3 de 4)
45812;66558;4;https://mirplaysalon.com/wp-content/uploads/2021/12/Bart-3_Fondo-Bart-3.jpg;Sillón de barbero Bart vintage con soporte de toalla (vista 4 de 4)
45820;66550;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Karl_Fondo-Karl.jpg;Sillón de barbero Karl clásico y económico
45820;66550;2;https://mirplaysalon.com/wp-content/uploads/2021/12/Karl-4_Fondo-Karl-4.jpg;Sillón de barbero Karl clásico y económico (vista 2 de 4)
45820;66550;3;https://mirplaysalon.com/wp-content/uploads/2021/12/Karl-2_Fondo-Karl-2.jpg;Sillón de barbero Karl clásico y económico (vista 3 de 4)
45820;66550;4;https://mirplaysalon.com/wp-content/uploads/2021/12/Karl-5_Fondo-Karl-5.jpg;Sillón de barbero Karl clásico y económico (vista 4 de 4)
45811;66559;1;https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher-2.jpg;Sillón de barbero Asher hidráulico
45811;66559;2;https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher.jpg;Sillón de barbero Asher hidráulico (vista 2 de 4)
45811;66559;3;https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher-3.jpg;Sillón de barbero Asher hidráulico (vista 3 de 4)
45811;66559;4;https://mirplaysalon.com/wp-content/uploads/2025/01/Asher_Fondo-Asher-4.jpg;Sillón de barbero Asher hidráulico (vista 4 de 4)
45817;66553;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Dave-2_Fondo-Dave-2.jpg;Sillón de barbero Dave retro con acolchado en rombos
45817;66553;2;https://mirplaysalon.com/wp-content/uploads/2021/12/Dave_Fondo-Dave.jpg;Sillón de barbero Dave retro con acolchado en rombos (vista 2 de 4)
45817;66553;3;https://mirplaysalon.com/wp-content/uploads/2021/12/Dave-4_Fondo-Dave-4.jpg;Sillón de barbero Dave retro con acolchado en rombos (vista 3 de 4)
45817;66553;4;https://mirplaysalon.com/wp-content/uploads/2021/12/DAVE_3_WEB_Fondo-DAVE_3_WEB.jpg;Sillón de barbero Dave retro con acolchado en rombos (vista 4 de 4)
45822;66548;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Silvia-B_Fondo-Silvia-B-2.jpg;Sillón de barbero Silvia B con reposapiés antivuelco
45822;66548;2;https://mirplaysalon.com/wp-content/uploads/2021/12/Silvia-B_Fondo-Silvia-B.jpg;Sillón de barbero Silvia B con reposapiés antivuelco (vista 2 de 2)
45818;66552;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey.jpg;Sillón de barbero Joey 1 hidráulico acolchado
45818;66552;2;https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey-4.jpg;Sillón de barbero Joey 1 hidráulico acolchado (vista 2 de 4)
45818;66552;3;https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey-2.jpg;Sillón de barbero Joey 1 hidráulico acolchado (vista 3 de 4)
45818;66552;4;https://mirplaysalon.com/wp-content/uploads/2021/12/Joey_Fondo-Joey-3.jpg;Sillón de barbero Joey 1 hidráulico acolchado (vista 4 de 4)
44497;66453;1;https://mirplaysalon.com/wp-content/uploads/2023/06/ALYA-B-BR-2_Fondo-ALYA-B-BR-2.jpg;Sillón de corte Alya B con respaldo de madera natural
45800;66570;1;https://mirplaysalon.com/wp-content/uploads/2021/12/HORACE3salon.17_Fondo-HORACE3salon.17.jpg;Sillón de corte Horace con capitoné y base cromada
45800;66570;2;https://mirplaysalon.com/wp-content/uploads/2021/12/HORACE4salon.17_Fondo-HORACE4salon.17.jpg;Sillón de corte Horace con capitoné y base cromada (vista 2 de 4)
45800;66570;3;https://mirplaysalon.com/wp-content/uploads/2021/12/HORACE2salon.17_Fondo-HORACE2salon.17.jpg;Sillón de corte Horace con capitoné y base cromada (vista 3 de 4)
45800;66570;4;https://mirplaysalon.com/wp-content/uploads/2021/12/HORACEsalon.17_Fondo-HORACEsalon.17.jpg;Sillón de corte Horace con capitoné y base cromada (vista 4 de 4)
45750;66620;1;https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large1_Fondo-Bertie-Large1.jpg;Sillón de corte Bertie de líneas redondeadas
45750;66620;2;https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large3_Fondo-Bertie-Large3.jpg;Sillón de corte Bertie de líneas redondeadas (vista 2 de 4)
45750;66620;3;https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large2_Fondo-Bertie-Large2.jpg;Sillón de corte Bertie de líneas redondeadas (vista 3 de 4)
45750;66620;4;https://mirplaysalon.com/wp-content/uploads/2021/12/Bertie-Large_Fondo-Bertie-Large.jpg;Sillón de corte Bertie de líneas redondeadas (vista 4 de 4)
45788;66582;1;https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small3_Fondo-Cindy-Small3.jpg;Sillón de corte Cindy con bomba hidráulica y freno
45788;66582;2;https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small_Fondo-Cindy-Small.jpg;Sillón de corte Cindy con bomba hidráulica y freno (vista 2 de 4)
45788;66582;3;https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small2_Fondo-Cindy-Small2.jpg;Sillón de corte Cindy con bomba hidráulica y freno (vista 3 de 4)
45788;66582;4;https://mirplaysalon.com/wp-content/uploads/2022/03/Cindy-Small1_Fondo-Cindy-Small1.jpg;Sillón de corte Cindy con bomba hidráulica y freno (vista 4 de 4)
45799;66571;1;https://mirplaysalon.com/wp-content/uploads/2021/12/GAT-2_Fondo-GAT-2.jpg;Sillón de corte infantil Gat con cinturón de seguridad
45799;66571;2;https://mirplaysalon.com/wp-content/uploads/2021/12/GAT-1_Fondo-GAT-1.jpg;Sillón de corte infantil Gat con cinturón de seguridad (vista 2 de 2)
45854;66516;1;https://mirplaysalon.com/wp-content/uploads/2022/01/0047_Capa-1_Fondo-Capa-1.jpg;Tocador Bobbie B de melamina a doble cara
45849;66521;1;https://mirplaysalon.com/wp-content/uploads/2022/01/0010_IAN5.salon_.17_Fondo-IAN5.salon_.17.jpg;Taburete de sillín Ian con ruedas antipelo
45849;66521;2;https://mirplaysalon.com/wp-content/uploads/2022/01/0011_IAN3.salon_.17_Fondo-IAN3.salon_.17.jpg;Taburete de sillín Ian con ruedas antipelo (vista 2 de 4)
45849;66521;3;https://mirplaysalon.com/wp-content/uploads/2022/01/0012_IAN2.salon_.17_Fondo-IAN2.salon_.17.jpg;Taburete de sillín Ian con ruedas antipelo (vista 3 de 4)
45849;66521;4;https://mirplaysalon.com/wp-content/uploads/2022/01/0013_IAN4.salon_.17_Fondo-IAN4.salon_.17.jpg;Taburete de sillín Ian con ruedas antipelo (vista 4 de 4)
45851;66519;1;https://mirplaysalon.com/wp-content/uploads/2021/12/0006_Nil_Fondo-Nil.jpg;Taburete Nil con ruedas de silicona
45749;66621;1;https://mirplaysalon.com/wp-content/uploads/2025/01/Ari.jpg;Bandeja auxiliar ajustable Ari para sillón
45749;66621;2;https://mirplaysalon.com/wp-content/uploads/2025/01/ARI-Adjustable-tray-Large.jpg;Bandeja auxiliar ajustable Ari para sillón (vista 2 de 3)
45749;66621;3;https://mirplaysalon.com/wp-content/uploads/2025/01/ARI-Adjustable-tray-Large-1.jpg;Bandeja auxiliar ajustable Ari para sillón (vista 3 de 3)
45711;66659;1;https://mirplaysalon.com/wp-content/uploads/2022/07/0005_IVAR_1_Fondo-IVAR_1.jpg;Calentador y esterilizador de toallas Ivar 18 L con luz UV
45710;66660;1;https://mirplaysalon.com/wp-content/uploads/2022/07/0004_HANK_1_1x1_Fondo-HANK_1_1x1.jpg;Calentador y esterilizador de toallas Hank 10,6 L
45717;66653;1;https://mirplaysalon.com/wp-content/uploads/2022/05/0029_PAUL_Fondo-PAUL.jpg;Poste de barbero Paul de acero inoxidable
45713;66657;1;https://mirplaysalon.com/wp-content/uploads/2022/02/0014_LAYLA_1_Fondo-LAYLA_1.jpg;Alfombra antifatiga Layla para puesto de trabajo
45713;66657;2;https://mirplaysalon.com/wp-content/uploads/2022/02/0001_Layla.jpg;Alfombra antifatiga Layla para puesto de trabajo (vista 2 de 2)
44367;66457;1;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/b7f68cd2-2ff4-4adc-bed4-bdb4f3026b74/Anita+-+2000+x+20001.jpg;Sillón de pedicura Anita con bañera de pies
44367;66457;2;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/8c101b9d-8336-4bbd-be53-4bd9a68b732e/Anita+-+2000+x+200012.jpg;Sillón de pedicura Anita con bañera de pies (vista 2 de 11)
44367;66457;3;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/3f5dffb2-b01a-489f-bacc-541c4018a424/Anita+-+2000+x+200011.jpg;Sillón de pedicura Anita con bañera de pies (vista 3 de 11)
44367;66457;4;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/d0d50778-8075-437c-8051-a3c2ea2fc797/Anita+-+2000+x+20004.jpg;Sillón de pedicura Anita con bañera de pies (vista 4 de 11)
44367;66457;5;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/ad9ebcf7-c2db-4896-bd88-1738b54a1bbb/Anita+-+2000+x+20005.jpg;Sillón de pedicura Anita con bañera de pies (vista 5 de 11)
44367;66457;6;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/02deb3be-3ba0-4f8d-b42a-f2bf1f7b7a28/Anita+-+2000+x+2000.jpg;Sillón de pedicura Anita con bañera de pies (vista 6 de 11)
44367;66457;7;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/fe518b46-a428-4866-aa51-d00afad41537/Anita+-+2000+x+20007.jpg;Sillón de pedicura Anita con bañera de pies (vista 7 de 11)
44367;66457;8;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/251c6ac3-579c-49dd-aeb5-d37c892f86df/Anita+-+2000+x+20009.jpg;Sillón de pedicura Anita con bañera de pies (vista 8 de 11)
44367;66457;9;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/9091f85e-9e5d-468a-bfe5-f53a1463dfe7/Anita+-+2000+x+2006.jpg;Sillón de pedicura Anita con bañera de pies (vista 9 de 11)
44367;66457;10;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/d32c3c52-02cf-4ef0-8d33-53ca680b6f60/Anita+-+2000+x+20003.jpg;Sillón de pedicura Anita con bañera de pies (vista 10 de 11)
44367;66457;11;https://images.squarespace-cdn.com/content/v1/67c9b79e9b734d29516135d0/cb85414d-84bd-4011-85c9-3c992a424539/Anita+-+2000+x+20008.jpg;Sillón de pedicura Anita con bañera de pies (vista 11 de 11)
```
