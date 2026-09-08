# Respuesta a los tres correos del equipo de SEO (08/09/26)

Los tres están mirados contra la base de datos y contra el código, no de memoria. Dos de ellos
tenían un diagnóstico incompleto y el tercero parte de una premisa que no se sostiene, así que van
por orden de lo que os cambia.

---

## 1. Los 34 vídeos sin descripción

**Lo que teníais bien**: son exactamente 34 sobre 778, y vuestras dos listas (23 + 11) cuadran una a
una con ellos. Buen recuento.

**Lo que no**: decís que «los 34 tienen además cero productos asociados y cero protocolo». No es así.
**14 de los 34 tienen protocolo redactado** —hasta 4.243 caracteres— y **6 tienen productos
asociados**. En total, 16 de los 34 tienen material en Nesto que la ficha no estaba enseñando.

No es culpa vuestra que no lo vierais, y el motivo es nuestro:

- el listado `GET /api/Videos` **no expone `Protocolo` ni los productos**, solo la `Descripcion`;
- y `GET /api/videos/{id}` devuelve **403** para los vídeos de menos de tres años si no vais
  autenticados. Los capítulos de *Estética desde 0*, que son la mitad de vuestra Lista A, son todos
  recientes: los pudisteis pedir, pero os los denegó. Los dos que sí comprobasteis (1504 y 1641) son
  viejos y esos sí están vacíos de verdad. De ahí la generalización.

### Lo que hemos hecho

Los vídeos del listado traen ahora un campo nuevo:

```
DescripcionParaFicha
```

Se rellena así, en el servidor:

1. si el vídeo tiene `Descripcion` en Nesto → **es la `Descripcion`, tal cual, entera**;
2. si no la tiene pero sí tiene protocolo → **el arranque del protocolo**, en texto plano, ~400
   caracteres cortados por palabra entera;
3. si no hay ninguna de las dos → **`null`**, y os quedáis con vuestro texto de reserva.

Del protocolo sale **solo el arranque, nunca el protocolo entero**: es el contenido por el que paga
un cliente y no va a una página pública. Lo que sale está limpiado a conciencia, porque los
protocolos venían con basura real: la cabecera «Protocolo Profesional de Tratamiento Estético /
Introducción» con la que arrancan **los 778** (si se colara, tendríais 778 fichas con la misma
primera frase, que para Google es peor que no tener ninguna), los enlaces «Ver paso en video», los
corchetes que quedaban vacíos al quitarlos, los trozos que empiezan a media frase en minúscula y el
literal `Error en la creación del protocolo`, que es lo que graba nuestro generador cuando falla y
que es, tal cual, todo el «protocolo» del vídeo 1830.

**Lo que tenéis que cambiar vosotros**: una línea. Leed `DescripcionParaFicha` y, si viene `null`,
seguid con vuestro texto de reserva actual. `Descripcion` se queda como está y sigue significando lo
mismo que hasta ahora —lo que hay escrito en Nesto—, para no romperle el campo a nadie.

### Cómo os queda la Lista A

De los 23, **12 pasan a tener descripción automática** el día que publiquemos, sin que nadie escriba
nada:

```
1962, 1960, 1937, 1894, 1849, 1847, 1822, 1821, 1667, 1660, 1468, 1330
```

Y **quedan 11 que sí hay que escribir a mano**, porque en Nesto no hay nada aprovechable:

```
1830  [Est. desde 0] Cap. 8: Foliculitis        (su protocolo es el mensaje de error, no sirve)
1819  ¿Se debe hacer deporte después de un tratamiento estético?
1662  Yolanda, ¿qué es la sobreexfoliación?
1661  ¿Vitamina C con dermapen?                 (tiene 3 productos: vuestro reserva sí tiene material)
1545  Pro Touch Concealer de Ten Image
1475  Kobido: masaje reafirmante japonés
1434  Manos deslumbrantes en las fiestas
1407  R+Structurant de UFAES                    (tiene 3 productos)
1405  Curso de Holistica en estética
1184  #soapnails con Microfiber de Faby
1639  Anubismed TRAINING FACIAL
```

Once son bastantes menos que veintitrés, y son justo los que de verdad necesitan que alguien vea el
vídeo. Vuestro criterio de redacción nos parece el correcto y lo pasamos tal cual a quien las
escriba.

Un aviso honesto sobre las 12 automáticas: son un **suelo, no un techo**. Varias empiezan por «En
este protocolo, abordaremos…», que es exactamente la fórmula que decís que no queréis. Resuelven el
hueco y le dan a Google algo específico que indexar, pero si más adelante alguien escribe la buena,
la `Descripcion` manda siempre sobre el extracto.

### Sobre la Lista B

Decisión pendiente de negocio, no la tomamos nosotros. Dos apuntes para cuando se tome:

- **1427** (*¡Directo en Facebook!*) tiene protocolo, así que a partir de la publicación tendrá
  descripción automática aunque nadie la escriba. Si se retira, da igual.
- Retirar hoy un vídeo del listado **no es reversible por nuestro lado**: hoy retirar significa
  borrar la fila, y se lleva por delante transcripción, protocolo y productos. Va en el punto 2.

---

## 2. El listado no marca las bajas

### Pregunta 1: ¿fueron deliberadas las 77?

**Casi con seguridad no salieron del sistema.** Hay un borrado deliberado, pero no puede ser este.

Poner un vídeo en **privado** en YouTube lo borra de nuestra base de datos en la siguiente pasada:
es una decisión tomada en abril y está en el código. Pero ese camino **solo mira los vídeos
publicados después del más reciente que ya tenemos**, así que solo puede actuar sobre novedades: el
catálogo antiguo no lo vuelve a mirar nunca. Vuestros 77 son antiguos. Y no hay ningún otro borrado
de vídeos, ni en la API ni en el proceso que los da de alta.

Así que, o se borraron a mano en la base de datos en algún momento, o son fichas que la tienda creó
a partir de respuestas antiguas. **Pasadnos la lista de los 77 ids** y lo cerramos: si aparecen en
nuestro histórico, sabremos cuándo se fueron; si no, la conclusión es que la tienda tenía fichas que
Nesto no llegó a tener nunca.

Mientras tanto: **no los repongáis todavía**. Si su vídeo de YouTube está retirado o en privado, la
baja es correcta aunque el camino fuera feo.

### Pregunta 2: ¿puede el listado marcar la baja?

**Sí, y vamos a hacerlo.** De vuestras tres propuestas cogemos la segunda, `FechaBaja` (nula
mientras el vídeo esté vivo), más el `?incluirBajas=true` en el listado. La razón de preferir la
fecha al booleano es que cuesta lo mismo y contesta además a *«¿cuándo se retiró?»*, que es
justamente lo que hoy no podemos deciros de los 77.

Y hay un motivo nuestro, más gordo que el vuestro, para hacerlo: **hoy retirar un vídeo es un
DELETE en cascada**. Se lleva la transcripción, el protocolo y los productos asociados, y eso no se
recupera; la única copia que existe de una ficha retirada es la vuestra. Con `FechaBaja` la retirada
deja de destruir nada.

No lo tenéis todavía: lleva un cambio de esquema en la base de datos, regenerar el modelo y tocar el
proceso de alta. Va en el siguiente lote, no en este. Cuando esté os avisamos con el contrato exacto
del campo, y entonces podéis quitar las guardas.

### Dos cosas que no habíais visto, en esa misma zona

**a) Teníais 7 vídeos duplicados.** Siete pares de fichas distintas apuntando al **mismo vídeo de
YouTube**: mismo id de YouTube, misma fecha, mismo título. Son contenido duplicado en vuestro
catálogo y no aparecían en vuestro informe.

La causa es nuestra: el proceso que da de alta los vídeos insertaba **una fila por pista de
subtítulos**. Un vídeo con dos pistas —la manual y la automática, o dos idiomas— entraba dos veces.
Por eso en casi todos los pares uno tiene protocolo y el otro está vacío: la segunda pista bajaba en
blanco. De paso, duplicaba también el aviso de «nuevo protocolo». Ya está arreglado, y las 7 filas
sobrantes se van a limpiar: **esas 7 URLs desaparecerán del listado y las podéis dar de baja** por
vuestro mecanismo de siempre. Es el resultado que queréis.

**b) La paginación se podía saltar vídeos.** El listado se ordena por fecha de publicación y hay
vídeos que comparten fecha exacta. Al paginarlo con `skip`/`take` en 16 peticiones independientes,
un empate a caballo de una frontera de página puede hacer que una fila salga dos veces en una página
y desaparezca de la siguiente. Es decir: **el barrido completo podía dejar fuera un vídeo vivo, y
vuestra reconciliación lo habría despublicado.**

No os ha pasado. Lo hemos comprobado: tres barridos completos devuelven 778 de 778 sin repetidos, y
hoy ningún empate cae en frontera de página. Pero eso es suerte posicional y cambia cada vez que se
sube un vídeo. Ya está cerrado añadiendo un desempate estable al orden.

O sea que vuestras guardas eran razonables y estabais protegiendo un riesgo **real**. Con esto y con
`FechaBaja` dejan de hacer falta; hasta entonces, mantenedlas.

---

## 3. Las tres referencias de cursos

Aquí hay que pararse, porque la petición es «corregidlo en Nesto, no en PrestaShop» y **corregirlo en
Nesto no arreglaría nada**.

**Los cursos no se sincronizan desde Nesto. Nunca lo han hecho.**

- La referencia que viaja a la tienda es el número de producto de Nesto.
- **Todos** los cursos están marcados en Nesto como producto *ficticio*, y lo primero que mira la
  puerta de publicación es justamente eso: un ficticio no sale hacia la tienda. La consulta que
  durante años decidió el catálogo, antes de la puerta actual, también los excluía por lo mismo.
- En Nesto existen **un solo `MF0581`** («DEPILACIÓN DEL VELLO TEMPORAL O DEFINITIVO») y **un solo
  `MF0797`** («ELECTROESTETICA»). No existe **ningún** producto con guion bajo en la referencia:
  `MF0581_2`, `MF0581_3`, `MF0796_3` y `MF0797_3` **no existen en Nesto**. `MF0796`, tampoco.
- Y la clave de producto es (empresa, número): **Nesto no puede emitir dos fichas con la referencia
  `MF0581`** aunque quisiera.

De ahí se sigue todo lo demás:

- La **referencia duplicada** se creó en PrestaShop. Nesto no la ha mandado nunca y no puede.
- La ficha **66355** con referencia `MF0581_3` y título `MF0797_3`: esa referencia no existe en
  ninguna parte más que en vuestra tienda. Alguien la escribió ahí, y con un error de tecleo.
- La ficha **67031** con `MF0796`: igual, no existe en Nesto.
- La ficha **67344** `MF0797` «ELECTROESTETICA», inactiva, es la única que **sí** se parece a un dato
  de Nesto: el nombre es literalmente el que tiene en el ERP.

**Conclusión: esto se arregla en PrestaShop, y el sync no os lo va a sobrescribir**, porque nunca os
manda un curso. Podéis tocarlo con tranquilidad, que es justo lo contrario de lo que temíais.

Lo único cierto del lado de Nesto es que a nuestros códigos les falta el nivel del certificado (el
`_2` / `_3`) y que `MF0796` no está dado de alta. Pero eso no os afecta mientras los cursos no
viajen.

Si lo que se quiere de verdad es que el ERP pase a llevar el catálogo de cursos —dar de alta los
códigos oficiales completos y quitarles el ficticio— es una conversación distinta y con
consecuencias en cómo se facturan. Decirlo y se estudia; no es una decisión que se tome desde SEO.

Y estamos de acuerdo con vuestro argumento de fondo: los códigos de los certificados de
profesionalidad son búsquedas casi sin competencia y merece la pena que cuadren. Simplemente el
sitio donde arreglarlo es el otro.

---

## Resumen de qué os llega y cuándo

| | Cuándo | Qué tenéis que hacer |
|---|---|---|
| `DescripcionParaFicha` en el listado | próxima publicación de la API | leer el campo; si es `null`, vuestro texto de reserva |
| 12 de la Lista A con descripción automática | con lo anterior | nada |
| 11 de la Lista A a escribir a mano | pendiente de negocio | nada; os avisamos |
| 7 fichas duplicadas fuera del listado | próxima publicación | darlas de baja por vuestro mecanismo |
| Paginación con desempate estable | próxima publicación | nada |
| `FechaBaja` + `?incluirBajas=true` | siguiente lote | quitar las guardas cuando os avisemos |
| Los 77 ids | os los pedimos | pasarnos la lista |
| Referencias de cursos | ya | corregirlas en PrestaShop, no las sobrescribe nadie |

Gracias por los tres correos: el de los duplicados y el de la paginación no habrían salido sin el
recuento de fichas que hicisteis vosotros.

---

# Apéndice (mismo día, después de enviar lo de arriba)

**`FechaBaja` ya no va «en el siguiente lote»: está hecha.** La columna está creada en la base de
datos y el código ya la usa, así que entra en la misma publicación que todo lo demás. Corregimos por
tanto lo que os dijimos arriba.

El contrato es este:

```
GET /api/Videos?skip=0&take=50                      -> solo los vivos (comportamiento de siempre)
GET /api/Videos?skip=0&take=50&incluirBajas=true    -> el catálogo COMPLETO, cada uno con su FechaBaja
```

Y cada elemento del listado trae un campo más:

```
FechaBaja    null mientras el vídeo está vivo; la fecha en que se retiró si está de baja
```

Con eso **podéis dejar de deducir la baja por ausencia**: pedid el listado con `incluirBajas=true` y
dad de baja exactamente lo que venga con `FechaBaja` distinta de null. Las dos guardas —el recorrido
inequívoco hasta el final y el tope del 10 %— dejan de tener sentido, porque ya no estáis
interpretando un silencio.

Un vídeo de baja se comporta como si no existiera en todo lo demás: no sale del listado normal, no
sale del buscador, no sale en los correos post-compra y su ficha `GET /api/videos/{id}` sigue
devolviendo 404, igual que cuando la fila se borraba.

**Lo importante para vosotros**: retirar un vídeo **ya no destruye nada**. Antes era un borrado en
cascada que se llevaba la transcripción, el protocolo y los productos asociados, y la única copia
que quedaba en el mundo era la vuestra. Ahora es reversible por los dos lados: si un vídeo vuelve a
ser público en YouTube, se reactiva solo en la siguiente pasada.

Eso hace, de paso, que la pregunta de los **77 ids** deje de ser urgente para el futuro —esto no
volverá a pasar— pero sigue interesándonos la lista para entender qué ocurrió. Cuando podáis.

---

# Apéndice 2 — Los 77 ya están explicados, y os dijimos algo que no era

Con vuestra lista hemos encontrado la causa, y **contradice lo que os contestamos**. Os dijimos que
el borrado de «privado en YouTube» solo miraba los vídeos más nuevos que el último de la base de
datos, y que por tanto no podía haber tocado el catálogo antiguo. Era falso.

El proceso pregunta a YouTube por los vídeos publicados **después de una fecha**, y esa fecha la
calculaba un método que tenía, en su primera línea, un `return DateTime.MinValue` de pruebas con el
cuerpo de verdad detrás, sin alcanzar nunca. O sea que la fecha era siempre «el principio de los
tiempos» y **cada pasada recorría el canal entero**, no las novedades. Nosotros leímos el filtro y
no comprobamos qué valor le llegaba.

Así que lo que pasó fue esto: el 24/04/26 se decidió que poner un vídeo en privado en YouTube
equivalía a retirarlo, pensando que solo afectaría a lo nuevo. En la primera pasada revisó los 855 y
borró de golpe **todos los que alguna vez se habían puesto en privado**. Vuestros 77.

Fueron deliberadas en el criterio (esos vídeos estaban en privado o retirados) y accidentales en el
alcance (se pretendía aplicar solo a novedades). **Hicisteis bien en darlos de baja y en no
reponerlos.**

**Sobre vuestra pista de los cuatro ids bajos**: la hemos comprobado y la hipótesis no se sostiene,
aunque el olfato era bueno. Los ids 2, 7, 15 y 40 fueron filas reales de Nesto: sus vecinos (3, 9,
10, 13, 14, 17, 18, 19) siguen vivos, así que estaban intercalados con vídeos que existen. Y hemos
buscado sus cuatro vídeos por id de YouTube y por título: no están en la tabla bajo ningún otro id.
Se borraron, como los otros 73; simplemente son de los más antiguos del canal.

**Esto no puede repetirse.** Retirar un vídeo ya no borra la fila, solo le pone `FechaBaja`. Y el
recorrido del canal entero se queda como está —ahora a propósito y documentado—, porque es
justamente lo que mantiene al día el estado de privacidad en los dos sentidos: si un vídeo antiguo
se pone en privado se da de baja, y si vuelve a ser público se reactiva solo.

Si en algún momento queréis recuperar alguno de los 77, decídnoslo: habría que volver a ponerlo
público en YouTube y el proceso lo daría de alta otra vez, aunque sin el protocolo ni los productos
que tenía, que sí se perdieron.

---

# Apéndice 3 — Respuesta a los cuatro puntos del 08/09/26 (tarde)

## 1. Los 77: cerrados

De acuerdo, y gracias por el cruce con Search Console. Cero impresiones en tres meses con un corte
en 12 es un dato mucho más concluyente que cualquier cosa que pudiéramos mirar nosotros. Cerrados.

## 2. El listado por defecto: (a), confirmado. Podéis desplegar con calma

**`GET /api/Videos` sin `incluirBajas` seguirá devolviendo solo los vivos.** Es el comportamiento por
defecto y no va a cambiar: el parámetro nace en `false` y el filtro se aplica siempre que no se pida
lo contrario.

Pero como pedís no enteraros después, hemos hecho algo mejor que confirmarlo por escrito. El filtro
estaba metido en medio de un método; lo hemos sacado a una función con nombre propio,
`QuitarLasBajas`, con vuestro caso escrito encima en el comentario —las 84 fichas que el módulo
1.6.6 reactivaría— y **cuatro tests que lo fijan**:

- el listado por defecto no devuelve las bajas;
- con `incluirBajas` sí las devuelve, y marcadas;
- el parámetro por defecto es `false` en el servicio;
- y también en el controlador, o sea en la superficie HTTP.

Si alguien lo cambia sin querer, la suite se pone roja antes de que salga de aquí. **Desplegad
cuando os venga bien**, no dependéis de nuestro calendario.

## 3. Los extractos: aquí van los 12, y vuestra condición NO se cumple entera

Pedíais 3 o 4; van **los 12**, porque el reparto importa y con cuatro elegidos por nosotros no lo
veríais. Y por delante, lo que no cuadra:

**Vuestra condición era «siempre de la introducción y nunca del procedimiento». Hoy solo 4 de los 12
salen de una introducción de verdad.** Los otros 8 salen del primer bloque de prosa del protocolo,
que a veces cuelga de un encabezado de paso.

Dos matices a favor y uno en contra:

- El extracto es **siempre un solo párrafo**: nunca encadena dos bloques. Así que estructuralmente
  no puede empezar en la introducción y acabar metido en los pasos, que era vuestro miedo.
- Ninguno de los 12 contiene **ni una dosis, ni un tiempo, ni un modo de empleo**.
- Pero algunos sí son, literalmente, la primera frase del primer paso.

### Los 4 que salen de una introducción

```
1962  En este protocolo, abordaremos las metodologías más efectivas para el tratamiento estético
      facial utilizando una comprensión detallada de los vectores faciales. Cada paso del proceso
      está diseñado para maximizar los beneficios de las técnicas estéticas y asegurar resultados
      óptimos.

1960  En este protocolo, exploraremos una serie de técnicas avanzadas en el campo de la estética
      profesional. Cada paso está diseñado para maximizar los beneficios del tratamiento,
      adaptándose a las necesidades específicas del cliente.

1937  El verano presenta desafíos únicos para los tratamientos estéticos debido a la mayor
      temperatura y exposición solar. Este protocolo está diseñado para guiar a los profesionales en
      la aplicación de tratamientos seguros y efectivos durante esta temporada.

1894  Bienvenidos a este protocolo profesional de tratamiento estético donde exploraremos el uso de
      diferentes tecnologías y metodologías para el cuidado de la piel.
```

### Los 8 que salen del primer bloque, con el encabezado del que cuelgan

```
1822  (bajo «Definición y Comprensión»)
      El manto hidrolipídico es una emulsión epicutánea que actúa como la primera barrera protectora
      de la piel, compuesta por una mezcla de lípidos y agua que se secreta principalmente a través
      de las glándulas sebáceas y sudoríparas.

1660  (bajo «La Mesoterapia Virtual y El Ultrasonido»)
      Este es un término que se refiere al lanzamiento de sustancias al mesodermo. El objetivo de
      este procedimiento es mejorar la apariencia de la piel y reducir los signos de envejecimiento.

1849  (bajo «Descripción Inicial»)
      Comenzamos el tratamiento con una introducción a las corrientes continuas. Se destaca su
      importancia en la estética debido a la dirección constante de los electrones.

1821  (bajo «Diagnóstico Inicial»)
      Comprender la composición de la microbiota cutánea, que incluye bacterias como el
      estafilococo, hongos y ácaros. Esta composición es esencial para mantener la salud de la piel.

1847  (bajo «Paso 1: Introducción y Preparación»)
      Antes de iniciar cualquier tratamiento estético, es crucial comprender el estado actual de la
      piel. Se debe realizar un diagnóstico visual y táctil para determinar las necesidades
      específicas de cada cliente. A continuación, se recomienda preparar el área de trabajo,
      asegurándose de que todos los materiales estén limpios y desinfectados.

1667  (bajo «1. Diagnóstico Inicial»)
      Comenzamos el tratamiento con una evaluación exhaustiva de la piel, concentrando nuestros
      esfuerzos en identificar el fototipo del cliente. Este paso es crucial para entender la
      capacidad genética de la piel para producir melanina, lo que nos permitirá adaptar el
      tratamiento estético de manera precisa.

1468  (bajo «Paso 1: Presentación del producto»)
      En este tratamiento, se va a utilizar un producto de la marca Eva bisn para tratar las zonas
      del contorno de ojos. Este producto servirá para eliminar las ojeras y las bolsas de los ojos.

1330  (bajo «Paso 2: Comenzamos con la base»)
      A los comenzamos a aplicar la base. Se puede optar por una brocha más difuminada o que se note
      más. En este caso, se opta por una aplicación más compacta.
```

**El 1330 no lo publiquéis en ningún caso**: la frase está rota de origen, porque la transcripción
del vídeo venía así. Ese pasa a la lista de los que hay que escribir a mano, con lo que serían 12.

### Elegid vosotros, que la decisión es de Google

Hay tres formas de dejarlo y nos vale cualquiera; la palanca está de nuestro lado y se cambia en un
rato:

- **A. Como está**: 12 extractos, 11 si quitamos el 1330. Cuatro de introducción, el resto del
  primer bloque de prosa, ninguno con dosis ni tiempos.
- **B. Vuestra regla al pie de la letra**: solo lo que cuelgue de un encabezado de introducción.
  Quedan **4**. Se pierden ocho descripciones a cambio de una garantía formal.
- **C. La intermedia, que es la que recomendaríamos**: descartar solo lo que cuelgue de un
  encabezado que sea claramente un paso numerado («Paso N», «1.»). Caen 1330, 1468, 1847 y 1667;
  quedan **8**, todos conceptuales o de introducción. Se pierde el 1847, que es una pena porque se
  lee como una introducción, pero la regla es objetiva y no depende de que alguien juzgue cada texto.

Decidnos A, B o C y lo dejamos así antes de publicar.

## 4. La baja por privacidad: se puede esperar, y no es lío

Buena observación, y el efecto que describís es real: hoy el interruptor de privacidad de YouTube
publica y despublica páginas de la tienda en la pasada de esa noche.

**Se puede meter un periodo de gracia** y no es caro: hace falta guardar desde cuándo se ve el vídeo
en privado —una columna más— y no dar la baja hasta que pasen los días que digáis. Va en el
siguiente lote, no en esta publicación, porque toca otra vez la base de datos.

Antes de fijar el número, sabed qué se cambia por qué, porque no sale gratis en las dos direcciones:

- **Sin gracia** (hoy): un vídeo privado dos días hace que su URL vaya a 410 y vuelva a 200.
- **Con gracia de N días**: eso desaparece, pero un vídeo que se retira **de verdad** deja la página
  publicada respondiendo 200 durante N días **con el vídeo sin poder reproducirse**, porque en
  YouTube ya es privado.

O sea que elegís entre un 410 que va y viene y una página con el reproductor roto unos días.
Decidnos el número de días —3 nos parece razonable— o decidnos que preferís avisar a quien gestiona
el canal y lo dejamos como está. Las dos nos parecen defendibles.
