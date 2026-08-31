# Los productos de grupos que el módulo no reconoce acaban en "Accesorios", en silencio

31/08/2026

## El síntoma

Al sincronizar los cursos, han aparecido en la tienda bajo la categoría **Accesorios**. Deberían
estar bajo *Formación Estética*, que es donde los tiene el negocio.

## Lo que Nesto manda

Esto es la respuesta literal de `GET /api/Productos/Publicar/90005` (*CURSO DE DRENAJE
LINFÁTICO*), que es exactamente el contenido del mensaje que os llega por el bus:

```json
{
  "Producto": "90005",
  "Nombre": "CURSO DE DRENAJE LINFATICO",
  "Grupo": "CUR",
  "Subgrupo": "Cursos de Perfeccionamiento",
  "Familia": "Cursos",
  "CategoriasSecundarias": []
}
```

**En ningún campo aparece "Accesorios".** Nesto está mandando `CUR` / *Cursos de
Perfeccionamiento*. Lo podéis reproducir vosotros mismos con esa URL, es un GET anónimo.

## La pista

"Accesorios" es, literalmente, la descripción del grupo **`ACC`** de Nesto. Que un producto del
grupo `CUR` acabe ahí apunta a un **destino por defecto fijo** cuando el mapeo no encuentra
correspondencia, más que a un error de datos.

## Un detalle nuestro que probablemente esté en el fondo del asunto

En el mensaje hay una **asimetría incómoda, y es culpa nuestra**:

| Campo | Qué lleva |
|---|---|
| `Grupo` | el **CÓDIGO** (`"CUR"`, `"COS"`, `"APA"`…) |
| `Subgrupo` | la **DESCRIPCIÓN** (`"Cursos de Perfeccionamiento"`) |
| `Familia` | la **DESCRIPCIÓN** (`"Cursos"`) |

No se puede arreglar renombrando los campos: `Subgrupo` viaja como descripción desde el primer
día y cambiarlo os rompería a vosotros y a Odoo. Lo decimos porque **si el mapeo de la categoría
principal se hace por `Grupo`, lo que recibe es un código de tres letras que no es el nombre de
ninguna categoría** — y ahí es fácil que caiga en el valor por defecto.

Si os sirve, podemos añadir al mensaje el código del subgrupo (`SubgrupoCodigo`) y el de la
familia (`FamiliaCodigo`); ya existen en nuestro DTO y hoy **no** los mandamos precisamente para
no cambiaros el contrato sin acordarlo. Decidnos si los queréis.

## Lo que pedimos

**1. Que no haya destino por defecto silencioso.** Si el módulo no sabe a qué categoría va un
producto, lo que menos daño hace es **no tocarle la categoría** y dejar constancia (log, contador,
lo que uséis), no meterlo en una categoría cualquiera. Un producto en la categoría equivocada es
peor que un producto sin categorizar: nadie lo detecta, y el que lo detecta no sabe por qué.

**2. Que el mapeo cubra todos los grupos que mandamos.** Son solo siete, y estos son todos, con
lo que llevamos publicado de cada uno:

| Grupo | Descripción | Productos publicados |
|---|---|---|
| `COS` | Cosméticos | 3.636 |
| `PEL` | Peluquería | 1.839 |
| `APA` | Aparatos | 1.075 |
| `ACC` | Accesorios | 1.068 |
| `MTP` | Materias primas | 191 |
| `CUR` | **Cursos** | 82 |
| `ACP` | Otros Aparatos | 29 |

Sospechamos que los que fallan son los tres de abajo. En pares Grupo/Subgrupo son **71 en total**;
os pasamos la lista completa si os viene bien.

Los subgrupos de los tres grupos pequeños, por si ayuda a confirmarlo:

| Par | Descripción del subgrupo | Productos |
|---|---|---|
| `CUR/PER` | Cursos de Perfeccionamiento | 47 |
| `CUR/INI` | Cursos de Iniciación | 27 |
| `CUR/TRA` | Prácticas de Trabajos de Cursos | 5 |
| `CUR/CUR` | Cursos | 3 |
| `MTP/MTP` | Materias primas | 191 |
| `ACP/ACP` | Otros aparatos | 29 |

## Lo que necesitamos saber

1. **¿Cómo se decide hoy la categoría principal?** ¿Por `Grupo`, por `Subgrupo`, por los dos? ¿Con
   una tabla de mapeo en el módulo, o por nombre contra `category_lang`?
2. **¿Existe un valor por defecto** cuando no encuentra correspondencia? ¿Cuál es y dónde está?
3. **¿Cuántos productos hay hoy en la tienda por ese camino?** No solo los 82 cursos: cualquier
   producto de `MTP` o `ACP` podría estar igual. Es el número que nos falta para saber el tamaño
   real del desvío.

## Lo que NO os estamos pidiendo todavía

El negocio quiere que los cursos queden bajo *Formación Estética*, con cuatro subcategorías
(Cursos de iniciación, Formación para esteticistas, Webinars y Cursos Certificados). **Dos de
ellas no existen todavía en Nesto**, así que primero las creamos nosotros y os las mandamos ya
clasificadas. Eso va aparte y llegará después.

Lo de esta petición es solo el comportamiento por defecto: **mientras siga habiendo una categoría
de descarte silenciosa, da igual cómo clasifiquemos nosotros — acabará en el mismo sitio.**
