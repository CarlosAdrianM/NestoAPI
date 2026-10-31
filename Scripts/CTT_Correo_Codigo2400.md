**Asunto:** Integración API – significado del código de estado 2400 y cupo de consultas

Hola:

Somos Nueva Visión (cliente con integración por API desde el 21/09). Consultamos el seguimiento de nuestros envíos con `trf/web-tracking/v1.0/shippings` (por lotes, cada 2 horas) y os escribimos por dos dudas:

**1. Código de estado 2400**

Desde ayer, 23/09, a las 22:00, el envío **0082800082809772536836** nos devuelve como último evento de tipo STATUS el código **2400**, con la descripción **vacía**. El evento anterior era «EN REPARTO».

- ¿Qué significa el 2400? ¿Es un estado final (entregado, devuelto…) o intermedio (ausente, nuevo intento, en punto de recogida…)?
- ¿Nos podéis pasar la lista completa de códigos de estado con su descripción? Ahora mismo tenemos contemplados 0000, 0500, 0900, 1200, 1500, 1600, 2100 y 3000.
- ¿Es normal que un evento llegue sin descripción?

**2. Cupo de consultas**

¿Cuál es el límite de peticiones a la API de seguimiento (por minuto, hora o día)? Queremos ajustar la frecuencia sin llegar a recibir un 429.

Muchas gracias.

Un saludo,
Carlos Adrián Martínez
Nueva Visión
