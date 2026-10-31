# Variantes de producto (combinaciones) en el mensaje `Productos` de NestoSync

10/09/2026 - Para el equipo del modulo prestashop-nestosync. NestoAPI#477.

## Calendario

- **Desarrollar y probar ahora**, en vuestro entorno, con los mensajes de ejemplo de abajo.
- **NO subir el ZIP a produccion hasta el martes 15/09/2026.** Ese dia publicamos la version de
  NestoAPI que empieza a mandar el campo `Variante`; hasta entonces ningun mensaje lo llevara, y
  como el contrato es "sin `Variante`, todo como hoy", el orden entre vuestro ZIP y nuestra API no
  importa, pero no queremos dos cambios en produccion en dias en los que no estamos.

## Lo que queremos conseguir

Hoy cada referencia de Nesto es un producto plano en PrestaShop. Para las familias de color
(Mirplay tiene 27 familias con 148 referencias: Check, Check BR, Check G, Check GY son el mismo
sillon en cuatro colores) eso nos deja fichas casi identicas compitiendo entre si en Google y una
experiencia pobre para el cliente. Queremos que cada familia sea **UNA ficha con combinaciones**:
la ficha de la referencia "principal", y una combinacion por referencia de la familia (la principal
incluida).

El dato de que referencia es variante de cual **vive en Nesto** y os llega por el bus. Vosotros no
teneis que decidir nada: solo aplicar lo que dice el mensaje.

## Lo nuevo en el mensaje `Productos`

Un campo opcional `Variante`:

```json
{
  "Tabla": "Productos",
  "Producto": "45814",
  "Nombre": "SILLON DE BARBERO CHECK BR",
  "PrecioPublicoFinal": 741.00,
  "CodigoBarras": "8400000458142",
  "Stocks": [ { "Almacen": "ALG", "Cantidad": 2 } ],
  "Variante": { "Principal": "45813", "Atributo": "Color", "Valor": "Marron", "Orden": 2 }
}
```

Semantica:

| `Variante` | Que significa | Que haceis |
|---|---|---|
| ausente o `null` | producto plano, lo de siempre | lo de siempre |
| con valor | esta referencia es una **combinacion** del producto cuya `reference` es `Principal` | lo de abajo |

La principal de la familia **tambien viaja con `Variante`**, con `Principal` igual a si misma
(`"Producto": "45813", "Variante": { "Principal": "45813", ... "Valor": "Negro", "Orden": 1 }`).
Es la senal de que esa ficha lleva combinaciones y de cual es su propia combinacion.

## Que hacer con un mensaje con `Variante`

1. Localizar el producto de PrestaShop con `reference = Principal`. Si no existe todavia, crearlo
   con los datos de este mismo mensaje como producto (nombre, textos, precio...) y seguir; el
   mensaje de la principal llegara tambien y lo completara.
2. Asegurar en ese producto el atributo `Atributo` (por ejemplo "Color") y el valor `Valor`,
   **creandolos por nombre si no existen**. Nesto no conoce los ids de PrestaShop y no los va a
   conocer.
3. Crear o actualizar la combinacion de ese producto para ese valor, con:
   - `reference` = `Producto` del mensaje (**esto es lo que no puede fallar**: es lo que luego
     leemos en `order_detail.product_reference` al importar el pedido en Nesto).
   - `ean13` = `CodigoBarras`.
   - Impacto de precio = `PrecioPublicoFinal` del mensaje menos el precio del producto principal.
   - Stock de la combinacion = el de `Stocks` del mensaje, con la misma regla que ya usais para el
     stock del producto.
   - Posicion = `Orden`.
4. **No tocar los textos** (`NombrePersonalizado`, `Descripcion`, `DescripcionBreve`) cuando el
   mensaje lleva `Variante` y `Producto` != `Principal`: la ficha es la de la principal.
5. Si existe un **producto plano** con `reference = Producto` (hoy las 148 referencias de Mirplay
   existen asi, `active = 0`), al crear la combinacion hay que **desactivarlo o borrarlo**. Que no
   queden los dos. Decidid vosotros cual de las dos cosas y decidnoslo.
6. Fotos: siguen siendo vuestras, ahora por combinacion (`image` asociada a la combinacion).

## Que hacer con un mensaje sin `Variante` de una referencia que HOY es combinacion

Ocurre cuando en Nesto se saca una referencia de su familia: le encolamos un mensaje sin
`Variante`. Lo correcto es **quitar la combinacion** de la principal y tratar la referencia como
producto plano a partir de ahi (crear el producto plano si no existe). Es un caso raro; si
preferis hacerlo a mano al principio, decidnoslo y lo dejamos documentado.

## Lo que NO cambia

- Los pedidos: `order_detail.product_reference` de una combinacion con `reference` propia lleva
  esa `reference` (PrestaShop 8.2.x, `Cart.php:718` y `OrderDetail.php:769`). Nuestro importador
  lee exactamente ese campo, asi que si la combinacion lleva la referencia de Nesto, el pedido
  entra solo.
- Odoo: ignora `Variante` (mapea por lista de campos) y sigue viendo productos planos.
- Todo lo demas del mensaje.

## Piloto

**Check** (4 referencias): principal `45813`; combinaciones `45813`, `45814`, `45815`, `45816`.
Os avisaremos cuando encolemos la familia. Comprobaciones que haremos nosotros desde el webservice:
un solo producto activo con `reference = 45813`, cuatro combinaciones con sus `reference`, y un
pedido de prueba de la combinacion `45814` para verificar el importador.

## Preguntas para vosotros

1. Migracion del punto 5: desactivar o borrar el producto plano.
2. El caso raro de "sale de la familia": automatico o a mano.
3. Al cambiar el nombre de un producto, se recalcula el `link_rewrite`? (Nos importa para
   NestoAPI#479, los nombres en minusculas; si se recalcula, necesitamos redirecciones 301.)
