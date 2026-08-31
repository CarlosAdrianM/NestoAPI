Ya está el cobro completo: el pedido, la pasarela y el apunte del cobro contra el pedido. Con esto el circuito se cierra solo, sin que nadie toque nada a mano.

## Un solo paso: crear el pedido devuelve la pasarela

**No llaméis a `api/Pagos`.** Con `pagarConTarjeta: true`, la respuesta de `POST api/Pedidos/Cliente` ya trae los parámetros de Redsys firmados, por el importe del pedido:

```json
{
  "numero": 920123,
  "total": 112.83,
  "requierePago": true,
  "pago": {
    "idPago": 4521,
    "urlRedsys": "https://sis.redsys.es/sis/realizarPago",
    "ds_SignatureVersion": "HMAC_SHA256_V1",
    "ds_MerchantParameters": "eyJEU19NRVJDSEFOVF9BTU9VTlQiOi...",
    "ds_Signature": "y8Xy...",
    "tokenAcceso": "6f0b...",
    "urlPaginaPago": "https://api.nuevavision.es/pago/6f0b..."
  },
  "lineas": [ ... ],
  "avisos": ["El pedido no se prepara hasta que se recibe el pago."]
}
```

Los tres campos `urlRedsys`, `ds_MerchantParameters` y `ds_Signature` (más `ds_SignatureVersion`) son exactamente los que ya usáis para los efectos de cartera: **vuestro WebView con auto-POST y los deep links `nestotiendas://pago/ok|ko` funcionan tal cual**, sin tocar nada.

**Por qué va aquí y no en una llamada vuestra a `api/Pagos`**: el importe lo tiene que decir el servidor. Si lo mandarais vosotros, una petición manipulada pagaría 1 € por un pedido de 100. Por lo mismo, `api/Pagos` ignora ahora el número de pedido si viene de fuera.

## Qué pasa después, sin que hagáis nada

1. El cliente paga en la pasarela.
2. Redsys nos avisa a nosotros (server to server, no depende de que la app siga abierta ni de que el móvil tenga cobertura).
3. **El cobro entra como prepago del pedido**, y el pedido deja de estar retenido y sale a preparar.

Si el pago sale **KO**, el pedido se queda creado pero **retenido**: no se prepara ni se envía. No hace falta que lo canceléis. Es a propósito: un pedido sin cobrar se recupera —el cliente reintenta, o lo llamamos— y un cobro sin pedido es un problema contable. Al revés no tiene arreglo.

Y si la notificación de Redsys llega repetida (pasa), el cobro **no se duplica**.

## Lo que sí necesitamos de vosotros

- **Tratad el `ok` del deep link como «pago enviado», no como «pedido confirmado y pagado»**. Quien confirma el cobro es la notificación de Redsys, no el retorno del navegador. En la práctica llegan casi a la vez, pero si pintáis la pantalla de «¡gracias!» con el `ok` y consultáis el estado después, mejor.
- **Consultar el estado**: `GET api/Pagos/{idPago}` con el `idPago` de la respuesta. Estados: `Pendiente`, `Autorizado`, `Denegado`.
- Si `requierePago` viene `true` pero `pago` viene `null`, es que el pedido se creó y la pasarela no se pudo abrir. El motivo está en `avisos`; ahí lo suyo es enseñar el pedido como pendiente de pago, no reintentar la creación (crearíais un pedido duplicado).

## Dos cosas que no van a pasar, para que no las programéis

- **No mandamos ningún correo con enlace de pago al cliente.** Esto no es un enlace de pago: es un cobro online, el cliente está delante. Su correo solo viaja a Redsys, para la autenticación y el justificante del banco.
- **No hay que enviar el importe, ni el cliente, ni la cuenta contable, ni nada del cobro.** Todo sale del pedido que el servidor acaba de crear.

## Pago con tarjeta guardada: todavía no

Sabemos que lo cómodo sería que un cliente que ya ha comprado no tuviera que teclear la tarjeta otra vez, y es a donde queremos ir. Hoy **no** está: la API no pide ni guarda el token de tarjeta de Redsys, así que de momento cada pedido pasa por la pasarela con los datos de la tarjeta.

Va en su propia issue (NestoAPI#178) porque tiene su miga: hay que pedir el token en el primer cobro, guardarlo asociado al cliente y usarlo luego por referencia, con lo que eso implica de autenticación reforzada. Cuando esté, para vosotros no cambia el contrato: seguiréis llamando al mismo endpoint, y la respuesta os dirá si hay que abrir la pasarela o si el cobro ya se ha hecho con la tarjeta guardada.

## Podéis probar en cuanto os avisemos

Está en master y va en el despliegue de hoy. Os avisamos al publicar y, si os parece, hacemos juntos un pedido pequeño de verdad para ver el circuito entero: pedido creado → pasarela → cobrado → prepago → pedido a preparar.
