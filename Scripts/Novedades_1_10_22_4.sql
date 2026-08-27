SET NOCOUNT ON;
USE NV;
INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.22.4', '2026-08-26', 'Nuevo', 'Confirmar envio funciona tambien con los pedidos de la tienda online',
 'En Canales Externos, el boton "Confirmar envio" solo funcionaba con Amazon. Ahora con los pedidos de la tienda online tambien: escribe la agencia y el numero de seguimiento en el pedido de la web y avisa al cliente por correo, igual que se hacia con Amazon.', 'Nesto', 1, 'sa'),
('1.10.22.4', '2026-08-26', 'Mejorado', 'Los precios de la tienda online se calculan y publican desde Nesto',
 'Hasta ahora el precio publico de la web lo calculaba la propia tienda y Nesto se lo preguntaba. Desde hoy Nesto es quien manda: calcula el precio publico de cada producto (fijo, con el 30% de margen, o igualado al profesional) y lo publica a la web. Los precios del mostrador y de la web salen del mismo sitio y ya no pueden divergir.', 'NestoAPI', 1, 'sa'),
('1.10.22.4', '2026-08-26', 'Mejorado', 'Los stocks de la tienda online y de Odoo se refrescan cada noche',
 'Cada noche se republican automaticamente los productos que han tenido movimientos de almacen, para que el stock que ve la web y Odoo no se desvie del real aunque algun aviso puntual se pierda.', 'NestoAPI', 1, 'sa'),
('1.10.22.4', '2026-08-26', 'Corregido', 'Llegaban correos duplicados de Odoo al asignar un cliente a un vendedor',
 'Al cambiar el vendedor de un cliente, Odoo mandaba dos veces el correo de "Ha sido asignado". Era porque el mismo cambio se publicaba dos veces a la sincronizacion; ahora se agrupa y viaja una sola vez.', 'NestoAPI', 1, 'sa'),
('1.10.22.4', '2026-08-26', 'Corregido', 'Los codigos postales extranjeros ya no se crean con datos basura',
 'Al dar de alta un cliente extranjero sin poblacion, el codigo postal se creaba con el codigo del pais como poblacion y provincia ("IT"/"IT") y asi salia en el informe de codigos postales nuevos. Ahora se crean con los datos reales si los hay, o en blanco para completarlos, y se completan solos en cuanto un alta los traiga.', 'NestoAPI', 1, 'sa');
SELECT @@ROWCOUNT AS Insertadas;
SELECT Id, Version, Categoria, Titulo FROM Novedades WHERE Version = '1.10.22.4';
