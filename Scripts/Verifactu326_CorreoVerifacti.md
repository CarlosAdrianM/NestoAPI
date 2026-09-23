# Correo a Verifacti — datos de producción para el QR (NestoAPI#326)

Borrador para que Carlos lo envíe al soporte de Verifacti. Redactado el 23/09/2026.

---

**Asunto:** Formato del QR en producción: prefijo del numserie y NIF del emisor

Hola,

Somos Nueva Visión (NIF A78368255). Estamos integrados con vuestra API en modo sandbox desde julio y todas nuestras facturas se registran correctamente. Antes de pasar a producción el 1 de diciembre queremos poder generar el QR tributario en nuestro sistema como respaldo, por si alguna vez falla el envío y la factura tiene que imprimirse antes de tener vuestra respuesta.

Mirando las URL de QR que nos devolvéis en el sandbox, por ejemplo:

`https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B75777847&numserie=8f44_NV2615639&fecha=23-09-2026&importe=339.47`

vemos dos cosas que no podemos deducir por nuestra cuenta:

1. **El prefijo del `numserie`** (`8f44_` en el ejemplo). ¿Qué es exactamente? ¿Es fijo por emisor? ¿Cambia al pasar a producción? ¿Hay algún endpoint de la API donde consultarlo (datos del emisor o de las series)?
2. **El `nif`** (`B75777847` en el ejemplo), que no es el nuestro. Entendemos que es el NIF de pruebas del sandbox. ¿En producción será nuestro NIF (A78368255) tal cual?

Y dos preguntas más, ya que estamos:

3. ¿El formato del resto de la URL (host `www2.agenciatributaria.gob.es` en producción, `fecha` como dd-MM-yyyy e `importe` con punto decimal) es el mismo en producción?
4. Si generamos nosotros el QR con esos datos, ¿hay algo más que debamos tener en cuenta para que sea idéntico al que devolvéis vosotros?

Muchas gracias,

Carlos Adrián Martínez
Nueva Visión

---

**Cuando respondan:** rellenar en el `Web.config` las claves `Verifacti:PrefijoNumSerie` y `Verifacti:NifEmisor` (hoy vacías), y cablear el QR local como respaldo en `GeneradorPdfFacturasQuestPdf` cuando falte `VerifactuQR` (paso 3 de #326). El generador (`GeneradorQrVerifactu`) ya está hecho y probado contra la URL real.
