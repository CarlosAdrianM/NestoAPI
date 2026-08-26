# nestosync: cambios para el cutover de precios → v1.5.0 (26/08/2026)

Contexto: en Nesto hemos simplificado el contrato de sincronización de precios ANTES de arrancar
el cutover. Afecta directamente a lo que implementa la rama `feat/precio-publico-derivado-1.4.0`
(commit `aa6c4f7`, instalada en producción): **la mayor parte de esa maquinaria ya no hace falta
y hay que retirarla antes del re-sync**. El cambio va a favor del módulo: desaparecen los modos y
los sentinels; solo viajan precios absolutos.

## Qué cambia en el contrato

### ANTES (lo que implementa la 1.4.0 hoy)

- Mensaje `Tabla = "PrestashopProductos"` con `PVP_IVA_Incluido` en tres modos
  (positivo/`null`/`-1`), interpretados por `PublicPriceDeriver.resolveMode()`.
- El modo se persiste en `nestosync_public_price` para recalcular `product.price` cuando llega
  un cambio de PVP (`deriveForProduct`).
- En `getProductConfig()`, `PrecioPublicoFinal` está deliberadamente SIN mapear en
  `field_mapping` ("ya NO escribe product.price"); solo se usa en `creation_fields`.

### AHORA

**Solo el mensaje `Tabla = "Productos"`**, y el público llega ya resuelto por Nesto:

```json
{
  "Tabla": "Productos",
  "Source": "Nesto",
  "Producto": "17404",
  "PrecioProfesional": 24.60,     // PVP base imponible (igual que hasta ahora)
  "PrecioPublicoFinal": 42.52,    // público CON IVA, ya calculado por Nesto — SIEMPRE > 0
  ...
}
```

- `PrecioPublicoFinal` es **siempre un precio absoluto con IVA, siempre > 0**. Nunca llega `-1`,
  `0` ni `null` a interpretar (si un producto no tiene precio calculable, no publicamos mensaje).
- Nesto es el único dueño del cálculo: el 30 %, el "mismo precio que profesional" y los fijados
  a mano se resuelven en nuestro lado antes de publicar. Al cambiar un PVP en Nesto, publicamos
  un mensaje nuevo con AMBOS precios ya recalculados: el módulo no tiene que derivar nada nunca.
- Redondeo half-up a 2 decimales (vuestro `PS_PRICE_ROUND_MODE = HALF_UP`), IVA real por
  producto (21/10/4/exento).
- El mensaje `Tabla = "PrestashopProductos"` **deja de publicarse**.

## Cambios concretos en el módulo (v1.5.0)

1. **`config/entity_configs.php` → `getProductConfig().field_mapping`**: volver a mapear
   `PrecioPublicoFinal` → `price` (como en la 1.3.0: `type: decimal`, `transform: tax_exclude`,
   `compare: round`). Esto es lo único imprescindible: la 1.4.0 IGNORA ese campo en updates, así
   que con el contrato nuevo los precios no se actualizarían nunca.
2. **Retirar la derivación**: quitar la entidad `PrestashopProductos` de `getEntityConfigs()`,
   `PublicPriceHandler`, `PublicPriceDeriver`, la tabla `nestosync_public_price` (upgrade que la
   borre) y la config `NESTOSYNC_PRO_DISCOUNT_PCT`. Si preferís hacerlo en dos pasos, basta con
   quitar la entidad del registro: sin mensajes que la alimenten, el resto queda inerte.
3. **Conservar**: `SpecificPriceHandler` (`PrecioProfesional` → specific_price base imponible,
   `reduction = 0`) y el fix de IVA del profesional que entró en la 1.4.0.
4. **`creation_fields`** ya usa `PrecioPublicoFinal` para el precio inicial: sigue valiendo tal
   cual.

## Pregunta que necesitamos respondida ANTES del GO

Vuestro diseño pone el profesional como specific_price fijo (`reduction = 0`) y a la vez existe
`nv_group.reduction = 30 %` en el grupo Profesionales (id 2). Con el contrato nuevo el público es
`product.price` y el profesional el specific_price — los dos absolutos:

- ¿El −30 % del grupo se aplica ENCIMA de alguno de los dos en algún escenario (ficha, carrito,
  API)? Si se aplica encima del specific_price, un profesional compraría un 30 % POR DEBAJO de
  tarifa; en los productos de precio igualado (Weelko/UnionLaser/Staleks/DDUUEETT/Fama, ~981
  refs) pasaría lo mismo con cualquier visitante identificado del grupo.
- Nuestra propuesta: con los dos precios llegando absolutos, la reducción de grupo sobra —
  confirmad que quitarla no rompe la visualización de precio dual que añadisteis en `cdc6055`.

## Coordinación del cutover (orden propuesto)

1. Vosotros publicáis la v1.5.0 con los cambios de arriba (sin ella, el re-sync no escribiría
   ningún precio).
2. Nosotros desplegamos NestoAPI y repuntamos la suscripción push al webhook de producción.
3. Lanzamos el re-sync completo (~7.000 referencias vivas, mensaje `Productos` estándar). Dura
   varias horas; os avisamos al arrancar. Contra esto verificáis vuestro dry-run (los 866
   cambios previstos).
4. Con el dry-run verificado y la pregunta del grupo resuelta: borráis las reglas igualadoras de
   catálogo e inmediatamente después nosotros re-publicamos las ~981 referencias afectadas con el
   público ya igualado. Ventana corta y acordada: durante ella esos productos se ven caros en la
   web (nunca baratos).

## Notas

- Con ~7.000 mensajes, Pub/Sub puede entregar algún duplicado si el ack tarda más que el
  deadline; entendemos que el `compare` por campo del `GenericProcessor` lo hace idempotente —
  confirmadlo.
- Referencias duplicadas en PrestaShop (239 refs / 484 productos): el precio va dirigido a UNA
  `reference`; mientras haya dos productos con la misma, el módulo escribirá en el que encuentre.
  Sigue pendiente deduplicar por vuestro lado.
- El backup que tomasteis (`nv_price_backup_20260826` y `nv_specific_price_backup_20260826`)
  sigue siendo la red de seguridad del cutover: no lo borréis hasta validar en producción.

---

# ADENDA (26/08/2026, tras vuestros dos avisos)

## 1. Nombres y descripciones: viajan ahora DENTRO del mensaje `Productos`

Tenéis razón en que retirar `PrestashopProductos` cortaba el sync de textos. Resuelto en NestoAPI:
el mensaje `Tabla = "Productos"` incorpora tres campos nuevos, con los mismos nombres que ya
conocéis del mensaje viejo:

```json
{
  "Tabla": "Productos",
  "Producto": "17404",
  "Nombre": "NOMBRE FICHA",                          // el de siempre (ficha de Nesto)
  "NombrePersonalizado": "Nombre bonito para la web", // o null
  "Descripcion": "…",                                 // o null
  "DescripcionBreve": "…",                            // o null
  "PrecioProfesional": 24.60,
  "PrecioPublicoFinal": 42.52,
  ...
}
```

- **Semántica de `null`** (las claves siempre viajan presentes, serializamos sin omitir nulls):
  `null` = sin personalización en Nesto → **NO toquéis el texto que tenga la tienda**. Solo se
  escribe cuando llega valor. Es la misma semántica que teníais con `array_key_exists`.
- Para el módulo: reintroducir `MultilangFieldHandler` (bien conservado) mapeando estos tres
  campos, pero **bajo la entidad `Productos`**, no como entidad aparte.
- `Nombre` (ficha) y `NombrePersonalizado` siguen siendo cosas distintas: para el nombre de la
  web manda `NombrePersonalizado` cuando no es null.

## 2. Referencias duplicadas: anotado

Confirmado que `getRow` escribe en el primero que encuentre. Lo dejamos como riesgo conocido y la
deduplicación sigue pendiente de vuestro lado; mientras tanto, si detectáis que un precio "cae en
el otro", la referencia concreta nos vale para priorizarla.

## 3. Corrección al alcance del re-sync (punto 3 de la coordinación)

Donde decía ~7.000 referencias: el re-sync inicial cubrirá **solo los precios fijados a mano y
los igualados con el sentinel (~1.100 referencias, menos de una hora)**. Los de modo derivado
(30 %) NO se republican: su precio en la tienda ya es el correcto y se irán refrescando según se
toquen en Nesto. Acotad el cruce con vuestro dry-run a fijos + igualados.
