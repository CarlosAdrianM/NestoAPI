-- =============================================================================
-- Novedades de la version 1.10.22.3 (25/08/2026)
--
-- Solo lo que el usuario NOTA. Se dejan fuera a proposito, por no cambiar nada
-- de lo que ve o hace la gente:
--   - Agencias: cerrar el envio y contabilizar su reembolso pasan al servidor
--     (slice A4.1 de la modernizacion). Va con un parametro por usuario que
--     esta APAGADO: hasta activarlo, todo funciona exactamente igual que hoy.
--   - Soporte del nuevo campo de precio publico que envia la tienda online
--     (sentinel -1). Hoy no hay ningun producto marcado asi, y no se activara
--     hasta que la tienda de produccion tenga el modulo nuevo.
--   - Una referencia duplicada en la tienda ya no devuelve el precio de otro
--     producto: afecta a 484 productos y se registra para poder limpiarlos.
--   - El picking pasa a ejecutarse en exclusiva (ver mas abajo el efecto que
--     SI se nota).
-- =============================================================================
USE NV
GO

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Fecha_Modificación)
VALUES

-- ---------- NestoAPI ----------
('1.10.22.3', '2026-08-25', 'Corregido',
 'El packing podia salir con el DOBLE de unidades de las pedidas',
 'Si se sacaba picking dos veces seguidas sin dar tiempo a que terminara el primero, las dos ejecuciones cogian los mismos pedidos y cada una reservaba su hueco en la estanteria. La hoja de packing salia con el doble de cantidad y se habria servido de mas cobrando lo pedido. Paso el 25/08 con el picking 99327 y se detecto al revisar una hoja impresa. Ahora solo se puede sacar un picking a la vez, y si aun asi algo dejara una linea descuadrada, el packing NO se imprime y avisa de que pedido y que producto revisar.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.3', '2026-08-25', 'Corregido',
 'Los productos que no estan en la tienda online salian con precio publico 0 EUR',
 'La ficha mostraba "Precio publico final: 0,00 EUR" en los productos que no estan publicados en la web, y al venderlos en la tienda el precio salia a cero. Ahora se calcula a partir del precio profesional, igual que lo hace la web. Afecta a la ficha de producto en Nesto, en la app de vendedores y en la de clientes.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.3', '2026-08-25', 'Mejorado',
 'Los envios que esperan a que el cliente los recoja salen en Incidentados',
 'Cuando la agencia deja un paquete en un punto de recogida esperando al cliente, el envio se quedaba como tramitado y nadie se enteraba; si el cliente no iba a por el, acababa volviendo. Ahora sale en la pestana de Incidentados de Agencias, con una columna nueva que dice el motivo tal y como lo da la agencia ("DISPONIBLE PARA RECOGER"), para distinguirlo de una incidencia normal.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.3', '2026-08-25', 'Corregido',
 'Meter una linea con cantidad 0 parecia un fallo del programa',
 'Al intentar crear una linea de pedido con cantidad 0 (o con un producto inexistente o de baja), el aviso parecia un error del sistema. Ahora se ve claro que es un dato a corregir.',
 'NestoAPI', 1, 'sa', GETDATE()),

-- ---------- Nesto ----------
('1.10.22.3', '2026-08-25', 'Corregido',
 'El boton de sacar picking se podia pulsar dos veces',
 'Mientras se sacaba el picking la ventana se quedaba con el mensaje "Sacando Picking...", pero el boton seguia activo. Si tardaba mas de la cuenta era facil volver a pulsarlo pensando que no habia pasado nada, y eso duplicaba las cantidades del packing. Ahora el boton se desactiva hasta que termina.',
 'Nesto', 1, 'sa', GETDATE()),

('1.10.22.3', '2026-08-25', 'Mejorado',
 'Agencias: nueva columna con el motivo de la incidencia',
 'La pestana de Incidentados muestra una columna "Incidencia" con el motivo que da la agencia, y ese motivo se incluye tambien al copiar el envio completo, que es justo el dato que hace falta al reclamar.',
 'Nesto', 1, 'sa', GETDATE())
GO

-- VERIFICACION (debe devolver 6 filas):
SELECT Version, Categoria, Titulo, Ambito FROM Novedades WHERE Version = '1.10.22.3' ORDER BY Ambito, Categoria;
GO
