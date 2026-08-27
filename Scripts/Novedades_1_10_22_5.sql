/*
    Novedades de la versión 1.10.22.5 (27/08/2026).

    SE OMITEN A PROPÓSITO (no son perceptibles todavía para el usuario):
      - #407 Buscador: ya indexa los productos anulados y api/buscador acepta incluirAnulados.
        La capacidad está, pero hasta que la app de la tienda la use nadie ve nada distinto.
      - #387 Buzón de notificaciones: tabla y endpoints listos en el servidor; la pantalla del
        buzón es de la app (TiendasNuevaVision#36), así que se contará cuando ella salga.
      - Nesto#340 (Agencias, slice A1): las agencias de transporte se leen ya de la API en vez de
        ir contra la base de datos. Refactorización interna, sin cambio visible.
      - #418/#419 son issues creadas hoy, sin código en esta versión.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.22.5', '2026-08-27', 'Nuevo', 'Nueva pantalla para marcar las marcas que se venden al mismo precio al publico',
 'En Herramientas hay una pantalla nueva, "Mant. familias", donde se marca que familias se venden al publico al MISMO precio que al profesional (Weelko, Staleks, Union Laser, Fama Fabre, Dduueett...). Hasta ahora esa lista no estaba en ninguna pantalla y solo se podia cambiar por dentro. Al marcar o desmarcar una familia, los productos de esa familia se actualizan solos en la tienda online en unos minutos.', 'Nesto', 1, 'sa'),

('1.10.22.5', '2026-08-27', 'Corregido', 'Un producto nuevo de esas marcas ya no sale mas caro de la cuenta en la web',
 'Al dar de alta un producto de una marca que se vende al mismo precio al publico y al profesional, salia a la venta en la tienda un 42,86% mas caro, porque la web le aplicaba el descuento por defecto. No daba ningun error: simplemente el precio estaba mal hasta que alguien lo corregia a mano. Ahora el precio sale bien desde la primera publicacion, y ademas cada noche se revisa que no quede ninguno sin marcar.', 'NestoAPI', 1, 'sa'),

('1.10.22.5', '2026-08-27', 'Mejorado', 'El cliente de la tienda online recibe un enlace de seguimiento que funciona',
 'Al confirmar el envio de un pedido de la tienda online, a la web viajaba solo el numero de seguimiento pelado, y con la agencia generica el cliente se encontraba un seguimiento que no llevaba a ninguna parte. Ahora viaja el enlace completo del transportista, que es el que el cliente puede pulsar para ver donde esta su pedido.', 'Nesto', 1, 'sa'),

('1.10.22.5', '2026-08-27', 'Corregido', 'Crear el fichero de una remesa de pagos que no existe ahora avisa en vez de dar un error tecnico',
 'Si en la pantalla de cartera de pagos se tecleaba en la casilla de la remesa un numero que no era de una remesa (por ejemplo, el numero de orden de un movimiento de proveedor), salia un error tecnico incomprensible. Ahora dice claramente que esa remesa no existe y recuerda que, para pagar un movimiento suelto, hay que indicar el numero de orden y el banco.', 'NestoAPI', 1, 'sa');

SELECT @@ROWCOUNT AS Insertadas;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.22.5' ORDER BY Id;
