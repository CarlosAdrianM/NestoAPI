/*
    Novedades de Ariadna 2.27 (31/10/26): Ariadna#71 (firma con lo que se firma), #72 (furgoneta bulto a bulto), #73 (cobro con
    tarjeta y autorización), #74 (fotos del packing), #76 (autorización: ¿hay alguien de Administración?) y los globos.
    Necesita la API del 31/10 y los scripts Issue770, Issue771, Issue772 e Issue773 (ya lanzados).
    Ejecutar en SSMS contra NV como sa cuando la 2.27 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Reparto: «En la furgoneta», bulto a bulto',
 N'Ariadna sabe en todo momento qué bultos lleva en la furgoneta: los que carga por la mañana, los que recoge en las tiendas o en los clientes y los que entrega. El contador «En la furgoneta» se ve siempre; tóquelo (o «→» con el cuadro vacío) para ver la lista. Lea cada bulto al cargarlo, al entregarlo, al recogerlo y, por la tarde, en «Descargar furgoneta» al dejarlo en Algete. De momento leer al entregar es voluntario: si no lee ninguno, se dan por entregados todos los de la parada; si lee solo algunos, los demás siguen en la furgoneta.',
 N'Almacén'),
(2, N'Nuevo', N'Reparto: terminar la jornada',
 N'En «Descargar furgoneta», «Terminar jornada» (F4) cierra el día. Si queda algún bulto en la furgoneta, se lo enseña y, si confirma, se avisa a Almacén y a Dirección. La furgoneta tiene que acabar el día vacía.',
 N'Almacén'),
(3, N'Nuevo', N'Reposiciones: recogida al leer el último bulto',
 N'Al recoger una reposición en la tienda, lea todos sus bultos (R…-1, R…-2…): cuando lee el último, la reposición queda recogida y la tienda lo ve en Nesto al momento. Ya no hace falta que la tienda pulse «El repartidor ya se la ha llevado».',
 N'Almacén,Tiendas'),
(4, N'Nuevo', N'Reparto: el cliente ve lo que firma',
 N'Encima del recuadro de la firma sale lo que confirma el cliente: los bultos que recibe y, si paga, el importe y si es en efectivo o con tarjeta. La firma se guarda con ese texto, la fecha, la hora, el lugar, el nombre y el DNI.',
 N'Almacén'),
(5, N'Nuevo', N'Reparto: cobrar con tarjeta (F6)',
 N'Si el cliente quiere pagar con tarjeta, pulse F6: Ariadna crea el enlace de pago y abre WhatsApp con el móvil del cliente y el mensaje ya escrito; solo tiene que enviarlo (también se puede mandar por correo). Cuando el cliente paga, Ariadna se lo dice con «PAGADO» y puede seguir con la firma. También vale en «Cobrar una deuda» (F7 en la ficha). Solo para Nueva Visión: en la empresa 3 se cobra siempre en efectivo.',
 N'Almacén'),
(6, N'Nuevo', N'Reparto: reembolso sin cobrar, con autorización (F7)',
 N'Para dejar un envío con reembolso sin cobrarlo hace falta la autorización de Administración: pulse «Pedir autorización» (F7) y escriba el motivo. Ariadna le dice cuántas personas de Administración lo están viendo y le avisa en cuanto contesten. Si no hay nadie conectado o no contestan, llame por teléfono y escriba quién se lo ha autorizado («Autoriza … por teléfono»).',
 N'Almacén'),
(7, N'Mejorado', N'Packing: las fotos ya no van por bulto',
 N'En el packing basta con una foto por pedido, y puede hacer las que quiera con «Añadir foto» (o la tecla de la cámara), aunque luego reparta los productos en varios bultos. Cerrar el bulto sigue siendo con Intro o F4. Las fotos las verá también el cliente en el enlace de seguimiento.',
 N'Almacén'),
(8, N'Nuevo', N'Globos con las novedades',
 N'Cuando Ariadna estrena algo, la primera vez que entre en esa pantalla le sale un globo señalando lo nuevo. Ciérrelo con cualquier tecla o tocándolo: ya no le volverá a salir.',
 NULL);
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.27', '2026-10-31', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
