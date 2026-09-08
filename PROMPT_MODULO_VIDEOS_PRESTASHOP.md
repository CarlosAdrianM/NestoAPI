# Encargo para el módulo de vídeos de PrestaShop

**Versión**: esto entra en la **1.7.2** del módulo. (Una versión anterior de este documento
preguntaba por una "1.6.7" que no existe: producción está en la 1.6.6 y lo siguiente que se sube es
la 1.7.2, que salta de MINOR porque añade una columna, cambia el contrato del sync y elimina las dos
guardas.)

**Contexto**: los tres correos del equipo de SEO del 08/09/26. La parte de NestoAPI ya está hecha y
verificada contra producción; esto es lo que falta al otro lado. Todo lo de aquí abajo entra en
vigor **en la próxima publicación de NestoAPI**, así que el módulo se puede preparar antes: los dos
campos nuevos son aditivos y hasta entonces sencillamente no vendrán.

Son **dos cambios**, y los dos consisten en dejar de adivinar algo que ahora la API dice.

---

## Cambio 1 — La descripción de la ficha ya no la compone el módulo

### Qué pasaba

34 de los 778 vídeos no tenían `Descripcion` en Nesto, y la ficha se quedaba en la frase del título.
El módulo hace bien en componer un texto de reserva, pero **14 de esos 34 sí tenían un protocolo
redactado**: lo que fallaba es que el listado no lo exponía. Ya no hace falta que el módulo invente
nada en ese caso.

### El campo nuevo

Cada elemento de `GET /api/Videos` trae ahora:

```
DescripcionParaFicha
```

Lo compone el servidor así, por este orden:

1. si el vídeo tiene descripción en Nesto → **es esa descripción, entera y tal cual**;
2. si no la tiene pero sí tiene protocolo → **el arranque del protocolo**, ya en texto plano y
   recortado por palabra entera (~400 caracteres). Con una excepción decidida por el equipo de SEO
   el 08/09/26: si ese primer párrafo cuelga de un encabezado que es un paso numerado («Paso 1»,
   «1.»), **no se publica nada** y el campo viene `null`;
3. si no hay ninguna de las dos → **`null`**.

### Qué tiene que hacer el módulo

```
descripcion_de_la_ficha = DescripcionParaFicha ?? (texto de reserva actual del módulo)
```

Y ya está. Puntos importantes:

- **No lo tratéis como HTML.** Viene en texto plano, ya sin etiquetas, sin los enlaces «Ver paso en
  video» y con las entidades HTML descodificadas. Si lo pintáis sin escapar, un `&` de un nombre de
  producto os romperá el marcado.
- **`null` es un caso normal, no un error.** Va a pasar en unos cuantos vídeos y ahí vuestro texto de
  reserva sigue siendo lo correcto. No lo sustituyáis por cadena vacía.
- **`Descripcion` no cambia** y sigue significando lo mismo que siempre (lo que hay escrito en
  Nesto). No la quitéis: hay otros consumidores. Simplemente dejad de usarla para pintar la ficha.
- **No dupliquéis la lógica.** Si veis un caso en que el texto que llega no os gusta, decidlo y se
  arregla en el servidor. La razón de haberlo puesto ahí es que la misma decisión la necesitan la
  tienda, la app y el escritorio, y si cada uno se la escribe se separan con el tiempo.

### Lo que NO resuelve

Quedan 11 vídeos de la Lista A del correo de SEO que en Nesto no tienen nada aprovechable y siguen
necesitando que alguien vea el vídeo y escriba la descripción a mano. Eso no es trabajo del módulo.

---

## Cambio 2 — Las bajas dejan de deducirse por ausencia

### Qué pasaba

Como el listado no marcaba la baja, el módulo la deduce recorriendo el listado entero y dando de
baja lo que ya no aparece. Funciona, pero para que un fallo de la API no despublique media web hay
que rodearlo de guardas: solo reconciliar si el recorrido llegó al final de forma inequívoca, y
nunca si los ausentes pasan del 10 % del catálogo.

Esas guardas eran razonables y protegían un riesgo **real**: el listado se ordenaba por fecha de
publicación sin desempate estable, y con `skip`/`take` en peticiones independientes un vídeo con la
fecha repetida podía caerse de un barrido completo. Ya está arreglado en el servidor (orden total
por fecha e Id), pero además ahora la baja se dice explícitamente.

### El contrato nuevo

```
GET /api/Videos?skip=0&take=50                     -> solo los vivos (igual que hasta ahora)
GET /api/Videos?skip=0&take=50&incluirBajas=true   -> el catálogo COMPLETO, marcado
```

Y cada elemento trae:

```
FechaBaja      null mientras el vídeo está vivo; la fecha en que se retiró si está de baja
```

### Qué tiene que hacer el módulo

El barrido de sincronización pasa a pedir **`incluirBajas=true`** y a decidir por el campo, no por
la ausencia:

- `FechaBaja == null` → vídeo vivo: crear o actualizar la ficha.
- `FechaBaja != null` → vídeo retirado: dar de baja la ficha, exactamente igual que hoy.
- **Un vídeo que no venga en el listado ya no significa nada.** No lo deis de baja por eso.

Con esto **las dos guardas se pueden quitar**: ya no estáis interpretando un silencio, así que un
barrido incompleto deja fichas sin tocar en vez de despublicarlas.

### Detalles que conviene tener claros

- **La baja es reversible por los dos lados.** Si un vídeo vuelve a ser público en YouTube, el
  proceso que los da de alta le quita la `FechaBaja` en su siguiente pasada y volverá a apareceros
  como vivo. Vuestra reactivación tiene que funcionar, no solo la baja.
- **Un vídeo de baja se comporta como inexistente en todo lo demás**: no sale del listado normal, no
  sale del buscador, no sale en los correos post-compra, y `GET /api/videos/{id}` **sigue
  devolviendo 404**. O sea que no cambia nada de lo que ya hacíais con la ficha en sí.
- **Retirar un vídeo ya no destruye nada.** Antes era un borrado en cascada que se llevaba la
  transcripción, el protocolo y los productos asociados; la única copia que quedaba en el mundo era
  la vuestra. Eso se acabó, y por eso ya no hace falta tratar la baja con miedo.

---

## Aviso: 7 fichas van a desaparecer del listado

Ya está hecho en la base de datos (08/09/26). Había **7 pares de fichas apuntando al mismo vídeo de
YouTube** —mismo id de YouTube, misma fecha, mismo título—, o sea contenido duplicado en la tienda.
La causa era nuestra: el proceso de alta insertaba una fila por cada pista de subtítulos, y un vídeo
con dos pistas entraba dos veces. Ya está corregido y las 7 filas sobrantes se han borrado: el
catálogo pasa de **778 a 771**.

Los ids de Nesto que desaparecen son **los altos de cada par**, que son los que tienen la URL con el
número pegado detrás (`/video/tutoria-diciembre-2025-1838`):

```
1747, 1826, 1828, 1834, 1836, 1838, 1853
```

Y los que se quedan vivos son los bajos, los de la URL limpia y la que está indexada:

```
1746, 1825, 1827, 1833, 1835, 1837, 1852
```

**Estas 7 NO se dan de baja solas, y aquí nos equivocamos nosotros.** La primera versión de este
documento decía que con el mecanismo de siempre se resolvían solas. Es falso, y lo detectó el equipo
del módulo: es consecuencia del propio cambio 2. El mecanismo que las daría de baja es el de la
ausencia, que es justo el que os pedimos quitar; y como esas filas ya no existen en origen, nunca
van a llegar con `FechaBaja`. Sin hacer nada se quedarían publicadas para siempre.

Hay que darlas de baja **por id, una vez**, en el propio upgrade del módulo.

Ninguna de las 7 URLs limpias se toca.

---

## El detalle también trae `DescripcionParaFicha`

Preguntabais si `GET /api/videos/{id}` lo traería o solo el listado. **Lo trae también**, con
exactamente el mismo criterio. Vuestra solución de guardar el campo del listado en la ficha local
funciona igual, pero así no se os queda viejo entre dos sincronizaciones.

---

## Lo que este encargo NO incluye

El tercer correo de SEO, el de las referencias de cursos mal puestas (`MF0581` duplicada,
`MF0581_3` en la ficha de Electroestética, `MF0796` sin el `_3`). **No es de este módulo ni se
arregla en Nesto.**

Comprobado: los cursos **nunca** han viajado de Nesto a la tienda. Todos están marcados en Nesto como
producto ficticio y la puerta de publicación los descarta en su primera regla; la consulta que
durante años decidió el catálogo también los excluía por lo mismo. En Nesto existe un solo `MF0581`
y un solo `MF0797`, y ningún producto tiene guion bajo en la referencia.

O sea que esas fichas están mantenidas a mano en PrestaShop, y **el sync no las va a sobrescribir**.
Se corrigen allí con tranquilidad. Solo hacía falta saberlo antes de tocarlas.

---

## Resumen para estimar

| Cambio | Dónde | Tamaño |
|---|---|---|
| Leer `DescripcionParaFicha` con reserva a lo actual | pintado de la ficha | una línea |
| Pedir `incluirBajas=true` y decidir por `FechaBaja` | barrido de sincronización | pequeño |
| Quitar la guarda del "recorrido inequívoco" | barrido de sincronización | borrar código |
| Quitar la guarda del 10 % | barrido de sincronización | borrar código |
| Que la reactivación funcione, no solo la baja | barrido de sincronización | comprobar |

Nada de esto es urgente para que la tienda siga funcionando: si el módulo no se toca, todo sigue
como está hoy. Lo que se gana es dejar de adivinar dos cosas que ahora la API dice.

Cualquier duda del contrato, preguntad antes de interpretarlo.
