**Para:** Xabier Vicuña (Verifacti)
**Asunto:** Reenvío de facturas tras una caída de más de un día: ¿`modify` o `create`?

Hola, Xabier:

Gracias por la respuesta del día 23 sobre las caídas. Con lo que nos contaste hemos repasado la documentación de `verifactu/create` y nos surge una duda antes de programar el reenvío.

**Nuestra situación:** en nuestro sistema de gestión la fecha y el número de la factura se asignan al facturar, y no podemos cambiarlos después: la factura se contabiliza con esa fecha y la serie tiene que seguir en orden. Si la API de Verifacti no está disponible, o se cae nuestra conexión, la factura queda creada con su fecha y su número, pero pendiente de enviar.

**El problema:** la documentación dice que en `create` la `fecha_expedicion` tiene que ser la del día. Si la caída dura más de un día, no podríamos mandar la factura con su fecha real.

Nuestra idea es mandar esas facturas con **`PUT modify`**, igual que hacemos ya con las rechazadas (`rechazo_previo = "X"`), manteniendo la fecha y el número originales. Nos gustaría que nos confirmaras:

1. ¿Sirve `modify` para una factura que **nunca llegó** a Verifacti por la caída, o solo para las que se enviaron y fueron rechazadas? Si no sirve, ¿qué alternativa hay que respete la fecha original?
2. En ese reenvío, ¿hay que mandar **`incidencia = "S"`**? ¿Lo admite `modify`?
3. ¿Hace falta mandar también **`fecha_operacion`** si coincide con la fecha de expedición original? ¿En qué formato (`DD-MM-YYYY`, como `fecha_expedicion`)?
4. Si la caída se resuelve el mismo día, ¿basta con el `create` normal añadiendo `incidencia = "S"`?

Un saludo, y gracias de nuevo,

Carlos Adrián Martínez
Nueva Visión
