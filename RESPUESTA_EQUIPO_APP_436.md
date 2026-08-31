Gracias por el detalle del informe, y sobre todo por los controles: nos habéis ahorrado el diagnóstico.

## 1. El 404: teníais razón, no estaba desplegado

El código salió a master minutos después de vuestra prueba (commits `5102490`, `8741b2a` y `86541d6`). Falta **publicar la API en producción**; os avisamos en cuanto esté y lo volvéis a probar.

La ruta es la definitiva: **`POST api/Pedidos/Cliente`**. Y hay un segundo endpoint que os interesa (punto 2).

Las issues #434 y #435 ya están cerradas; **#436 sigue abierta solo por la pieza del cobro** que os explico en el punto 4.2.

## 2. Los portes: tenéis endpoint, y es mejor que abrir `CalcularPortes`

**`POST api/Pedidos/Cliente/Portes`**

Mismo cuerpo que el pedido (las líneas del carrito). Devuelve:

```json
{
  "baseImponibleProductos": 87.60,
  "portes": 5.50,
  "portesGratis": false,
  "importeMinimoSinPortes": 100.00,
  "faltaParaPortesGratis": 12.40
}
```

Ahí tenéis vuestro «te faltan 12,40 € para el envío gratis».

Preferimos esto a abriros `CalcularPortes` por una razón concreta: **aquel recibe la base imponible y el código postal en la petición**, y ninguno de los dos los puede decir el cliente (se los pondría a su gusto y se regalaría el envío). El nuevo calcula el envío del **mismo** pedido que se crearía —mismos precios, misma ficha, mismas condiciones de pago—, así que la cifra del carrito es exactamente la que se va a pagar. Para que no se separen nunca, el POST del pedido, el PUT y el carrito montan el cálculo con el mismo código.

Un matiz sobre el diagnóstico del 401, porque nos importa que quede claro: **`[Authorize]` NO exige identidad de empleado**, se conforma con cualquier autenticado, y un JWT de cliente vale (es el que usa hoy `api/EnviosAgencias/UltimoEnvioCliente`). Así que ese 401 no sale de una regla de empleado: o la petición no llevaba el `Bearer` del cliente, o se lo está comiendo el preflight CORS del navegador.

**Esto os afecta**: el endpoint nuevo lleva el mismo `[Authorize]`. Si al probarlo también da 401, el problema es el token o el CORS, no el endpoint. En ese caso mandadnos la petición tal cual (cabeceras, y los *claims* del token sin el token entero) y lo miramos.

## 3. Los validadores: preferimos dejarlos, y os explico por qué

El análisis de los Ganavisiones es fino y tenéis razón en cómo funciona la cadena. Pero hay un dato que cambia el cuadro: **hoy no podéis mandar líneas de regalo**. El DTO solo lleva producto y cantidad, y el precio y el descuento los pone el servidor línea a línea. No hay forma de decir «esta va a cero». Así que el escenario de pedir 5 regalos teniendo 2 no existe todavía, y por eso mismo Ganavisiones aún no está soportado en este endpoint (vuestro punto 4.3).

Sobre omitir la validación en general: los validadores no solo vigilan lo que toca el usuario. **También cazan un error nuestro de configuración** —un descuento o una oferta mal dados de alta que el motor de precios aplica de buena fe y la tabla de autorizados no contempla—. En Nesto eso lo resuelve un vendedor delante de la pantalla; en la app no hay nadie, y ahí preferimos un pedido no creado, con su motivo, a una venta a precio equivocado que llega hasta la factura.

El coste no nos parece razón: `EsPedidoValido` es el mismo que pagan Nesto y NestoApp en cada pedido que meten.

Lo que sí hacemos es comprometernos a mirarlo con datos: cada fallo de validación queda registrado con el pedido serializado, así que en cuanto publiquéis vemos los primeros días. Si aparecen falsos positivos, los arreglamos en el origen (o afinamos el validador concreto) en vez de apagar la red entera. Si con datos delante sigue molestando, lo revisamos.

Y cuando metamos Ganavisiones será por **vuestra segunda opción**: el servidor recalcula qué líneas son regalo y cuántos puntos dan las demás, ignorando lo que venga del cliente. Igual que con los precios.

## 4. Vuestros cinco detalles

### 4.1. Qué devuelve el 2xx

```json
{
  "empresa": "1",
  "numero": 920123,
  "cliente": "15191",
  "contacto": "0",
  "formaPago": "TAR",
  "plazosPago": "PRE",
  "baseImponible": 87.60,
  "total": 112.83,
  "portes": 5.50,
  "requierePago": true,
  "lineas": [
    {
      "producto": "12345",
      "texto": "CHAMPU DE PRUEBA",
      "cantidad": 2,
      "precioUnitario": 10.00,
      "descuento": 0.10,
      "baseImponible": 18.00,
      "total": 21.78
    }
  ],
  "avisos": ["El pedido no se prepara hasta que se recibe el pago."]
}
```

Tenéis el número para la pantalla de confirmación, y las líneas con el precio que ha calculado el servidor (útil si queréis contrastar con lo que enseñabais en el carrito).

### 4.2. Cómo se enlaza el pago

Hoy el endpoint **no** devuelve los parámetros de Redsys. El flujo es:

1. `POST api/Pedidos/Cliente` → os da el número de pedido.
2. `POST api/Pagos` → os da `UrlRedsys`, `Ds_MerchantParameters`, `Ds_Signature`, `Ds_SignatureVersion`, `IdPago`, `TokenAcceso` y `UrlPaginaPago`.
3. Vuestro WebView con auto-POST y los deep links, **tal cual los tenéis**.

Es decir: reutilizáis lo de los efectos de cartera sin tocar nada.

Nos queda una pieza a nosotros: que la notificación de Redsys añada el prepago al pedido creado. Podéis integrar ya el flujo; mientras no esté, el cobro lo cuadramos a mano.

Y una tranquilidad sobre el orden (creamos el pedido antes de cobrar): el pedido se crea con plazos `PRE`, y **el picking retiene los pedidos `PRE` hasta que los prepagos cubren el total**. Un pedido de la app sin pagar no se sirve. Un pedido sin cobrar se persigue o se cancela; un cobro sin pedido es un problema contable.

### 4.3. Líneas de regalo de Ganavisiones

No soportadas todavía, y a propósito (ver punto 3). Cuando se metan, **no habrá que marcarlas**: lo deducirá el servidor.

### 4.4. La forma de venta APP

Aplicada. Y la lista hardcodeada de `RellenadorPickingService` ya no existe: las formas de venta salen de `Constantes`, y **APP entra en «tienda online» para el picking**, así que se prepara y se envía igual que uno de la web.

Lo que **no** hemos metido, a propósito: el albarán a precio de público final. Esa regla es para clientes en estado 8 con vendedor NV, y los vuestros son mayoritariamente profesionales: su albarán tiene que salir a su precio. Si en algún caso concreto no os cuadra, decídnoslo.

### 4.5. Condiciones de pago

Lo aplicamos nosotros, como preferíais. Ya está hecho:

```
GET api/PlazosPago/CondicionesPago?empresa=1&cliente=15191&canal=APP
```

Con `canal=APP`: por defecto **tarjeta al contado** (`formaPagoRecomendada` = `TAR`, `plazoPagoRecomendado` = `PRE`); el crédito **solo si está en su ficha**, y nunca por defecto; y con impagados o deuda vencida, **solo `TAR`** —ni `EFC` ni `TRN`—, exactamente vuestra política.

Y no hace falta que la apliquéis vosotros al confirmar: si mandáis una forma de pago que la política no autoriza para ese cliente, el pedido se crea con la recomendada. La política de riesgo vive en un solo sitio, y es el mismo que responde a esa consulta.
