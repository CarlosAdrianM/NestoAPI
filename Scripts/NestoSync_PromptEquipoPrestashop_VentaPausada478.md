# Pausar y reanudar la venta de una casa entera: campo `VentaPausada` en el mensaje `Productos`

15/09/2026 - Para el equipo del modulo prestashop-nestosync. NestoAPI#478. Mismo hilo que
prestashop-nestosync#28 (variantes, NestoAPI#477).

## Lo que queremos conseguir

El 10/09 hubo que retirar de la tienda toda la casa Mirplay (tarifa del proveedor erronea). No
existia ningun interruptor: se hizo a mano en el back office, y la lista de "que estaba activo
antes" (39 referencias de 217) vive en un correo. Reactivar es otro trabajo manual y es el que se
olvida.

A partir de la version de NestoAPI que publicamos hoy, Nesto tiene una casilla por familia
(«Venta pausada en tienda») y cada producto de la familia os llega por el bus con el campo
`VentaPausada`. Vosotros no decidis nada: aplicais lo que dice el mensaje y recordais lo que
pausasteis para reactivar EXACTAMENTE eso y nada mas.

## Lo nuevo en el mensaje `Productos`

Un campo `VentaPausada` (booleano) que **siempre lleva valor** en los mensajes de esta version:

```json
{
  "Tabla": "Productos",
  "Producto": "45813",
  "Nombre": "SILLON DE BARBERO CHECK",
  "PrecioPublicoFinal": 741.00,
  "Stocks": [ { "Almacen": "ALG", "Cantidad": 2 } ],
  "VentaPausada": true
}
```

| `VentaPausada` | Que significa | Que haceis |
|---|---|---|
| ausente o `null` | mensaje de una version anterior de NestoAPI (o de otro emisor) | **nada** en cuanto a pausa: lo de siempre con el resto del mensaje |
| `true` | la casa esta pausada en Nesto | si el producto esta `active = 1`: ponerlo `active = 0` **y marcarlo como "pausado por Nesto"** (marca propia vuestra, la que querais). Si ya estaba `active = 0` por otro motivo: NO marcarlo |
| `false` | la casa NO esta pausada | si el producto lleva vuestra marca "pausado por Nesto": `active = 1` y quitar la marca. Si no lleva la marca: **no tocar `active`** |

Las dos reglas que no pueden fallar:

1. **Reactivar solo lo que se pauso por aqui.** Un producto que estaba `active = 0` antes de la
   pausa (descatalogado por vosotros, sin foto, lo que sea) tiene que seguir `active = 0` cuando
   la casa se reanude. La marca propia es lo que distingue los dos casos.
2. **`null` no despausa.** Un Nesto viejo o cualquier otro emisor que no conozca el campo no
   puede reactivar nada por accidente.

Con `VentaPausada = true` el resto del mensaje (precio, stock, textos, categorias) se aplica
igual que siempre: la ficha sigue actualizandose, solo que inactiva. Asi, cuando llegue el
`false`, la ficha ya esta al dia (es justo el caso de Mirplay: se corrige la tarifa en Nesto, se
republica la familia con el precio bueno y `VentaPausada = false`, y todo vuelve de una vez).

## Como os llegan los mensajes

- Al marcar o desmarcar la casilla en Nesto, NestoAPI encola TODOS los productos vivos de la
  familia (con el motivo «Familia pausada en tienda» / «Familia reanudada en tienda»). Para
  Mirplay son 217 mensajes seguidos.
- Cualquier republicacion posterior de un producto (cambio de precio, stock...) lleva el valor
  vigente de su familia. Es idempotente: recibir `true` sobre algo ya pausado por Nesto no hace
  nada; recibir `false` sobre algo sin la marca, tampoco.

## Lo que NO cambia

- La regla de que la reactivacion no es automatica sigue siendo vuestra (prestashop-nestosync#8):
  el `false` solo reactiva lo que lleva la marca "pausado por Nesto". Lo demas sigue siendo manual.
- `PuertaPublicacionTienda` sigue sin sacar nada de la tienda por si mismo (#432): lo ya publicado
  se queda. Este campo es el unico camino por el que Nesto pide desactivar.
- Odoo ignora el campo (mapea por lista de campos).

## Piloto

Mirplay, que hoy esta pausada a mano. Orden que proponemos:

1. Vosotros subis el ZIP con el campo.
2. Nosotros marcamos la casilla de Mirplay en Nesto: os llegan 217 `true`. Como los 217 ya estan
   `active = 0` (pausa manual), **ninguno debe llevar la marca** (regla: solo se marca lo que
   estaba activo). Comprobamos por webservice que nada cambio.
3. Cuando Mirplay conteste con la tarifa buena: corregimos precios, desmarcamos la casilla y os
   llegan 217 `false`. Como ninguno lleva la marca, **no se reactiva nada** — y reactivamos a mano
   las 39 de la lista del correo del 10/09, por ultima vez.
4. A partir de ahi el circuito ya es automatico para la siguiente casa.

Si preferis que en el paso 2 marqueis a mano las 39 como "pausado por Nesto" para que el paso 3
las reactive solo, decidnoslo: nos vale cualquiera de las dos.

## Preguntas para vosotros

1. Donde guardais la marca "pausado por Nesto" (campo propio, tabla auxiliar, feature...). Solo
   por saberlo para las comprobaciones por webservice.
2. Si un producto pausado recibe stock 0 o vuelve a tener stock: se aplica la regla de stock de
   siempre, pero sin tocar `active` mientras lleve la marca. Confirmadnos que es asi.
