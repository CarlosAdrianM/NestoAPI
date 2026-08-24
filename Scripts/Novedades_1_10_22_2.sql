-- =============================================================================
-- Novedades de la version 1.10.22.2 (24/08/2026)
--
-- Solo lo que el usuario NOTA. Se dejan fuera a proposito, por no cambiar nada
-- de lo que ve o hace la gente:
--   - Agencias: el pedido pasa a leerse por la API en vez de por Entity
--     Framework (slice A3 de la modernizacion).
--   - ViewModelBase deja Prism y pasa a CommunityToolkit.Mvvm.
--   - El filtro de ruido de bots de ELMAH pasa de lista negra a lista blanca.
--   - Las denegaciones de negocio dejan de registrarse como errores.
--   - El picking de las 11h pasa del Task Scheduler a Hangfire (el aviso por
--     correo si SE cuenta, mas abajo).
--   - Limpieza de la configuracion de TLS y renombrado de ElmahHelper.Notificar.
--   - Nucleo del contraasiento del cuadre de banco: todavia no hay boton.
--   - Consulta del cash flow, que es una herramienta de administracion.
-- =============================================================================
USE NV
GO

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Fecha_Modificación)
VALUES

-- ---------- Nesto ----------
('1.10.22.2', '2026-08-24', 'Corregido',
 'La aplicacion no arrancaba si el servidor de dominio no respondia',
 'Cuando el servidor que guarda los permisos de usuario dejaba de responder unos minutos, Nesto no arrancaba, la ventana de Detalle de Pedido no abria y sacar picking daba errores en cadena. Ahora, si eso vuelve a pasar, la aplicacion funciona igual: como mucho veras menos opciones de las habituales hasta que el servidor se recupere.',
 'Nesto', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Mejorado',
 'Nesto responde mas agil al pulsar botones',
 'Se consultaban tus permisos al servidor de dominio cada vez que pulsabas una tecla o hacias clic, cientos de veces por sesion. Ahora se consultan una sola vez. Si te cambian los permisos mientras tienes Nesto abierto, tendras que cerrarlo y volver a entrar para que los coja.',
 'Nesto', 1, 'sa', GETDATE()),

-- ---------- NestoAPI ----------
('1.10.22.2', '2026-08-24', 'Corregido',
 'No se podia crear un cliente extranjero si su codigo postal ya existia en Espana',
 'Al dar de alta un cliente de fuera cuyo codigo postal coincide con uno espanol (por ejemplo el 13210, que en Francia es Saint-Remy-de-Provence y en Espana Villarta de San Juan), el alta fallaba. Ya se puede crear. La ficha del cliente lleva su poblacion, su provincia y su pais correctos; la tabla de codigos postales sigue mostrando solo uno de los dos paises, que es otro tema pendiente.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Corregido',
 'Subir facturas a Amazon fallaba justo despues de reiniciar el servidor',
 'Durante los primeros minutos tras un reinicio, las llamadas a Amazon podian fallar con un error de conexion segura, tanto al pulsar "Subir factura" como en el proceso automatico. Y el proceso automatico no lo cantaba: se quedaba en verde aunque no hubiera podido hablar con Amazon. Ya no ocurre.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Mejorado',
 'El picking automatico de las 11h avisa al almacen por correo',
 'Hasta ahora, si a las 11h no salia picking, en el almacen no habia forma de saber si es que no habia nada que sacar, si habia fallado algo o si la tarea ni siquiera se habia ejecutado, y habia que preguntar a Informatica. Ahora llega un correo a almacen@nuevavision.es en los dos primeros casos, con un mensaje distinto para cada uno. Si no sale picking Y no llega correo, es que la tarea no se ejecuto.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Corregido',
 'El picking de las 11h podia adelantar un dia las entregas',
 'El picking de cierre dependia del segundo exacto en que arrancara: si se retrasaba y cruzaba las 11:00, sacaba tambien las entregas del dia siguiente, sin avisar. Se evitaba lanzandolo a las 10:59:40, lo que dejaba fuera los pedidos metidos en esos ultimos veinte segundos. Ya no depende de la hora de arranque, asi que esos pedidos entran.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Corregido',
 'Sacar picking de un pedido que no existe daba un error de sistema',
 'Si se pedia el picking de un numero de pedido que no existe (o de otra empresa), salia un error tecnico que no decia ni que pedido se habia pedido. Ahora avisa con el numero: "No existe el pedido 924645 en la empresa 1".',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Corregido',
 'El mensaje al no poder desmarcar "Servir junto" no se entendia',
 'Cuando habia varias muestras que se quedarian pendientes, el mensaje repetia la lista de productos dos veces dentro de la misma frase y parecia un unico producto con un nombre larguisimo. Ahora la lista sale una sola vez, al final, con el codigo delante del nombre para poder localizarla en el pedido.',
 'NestoAPI', 1, 'sa', GETDATE()),

('1.10.22.2', '2026-08-24', 'Corregido',
 'Copiar factura: faltaba un dato y parecia un fallo del programa',
 'Al hacer un abono con cargo sin indicar el cliente o el contacto de destino, el aviso parecia un error del sistema, asi que se reintentaba en vez de corregir el dato. Ahora se ve claro que es un dato que falta.',
 'NestoAPI', 1, 'sa', GETDATE())
GO

-- VERIFICACION (debe devolver 9 filas):
SELECT Version, Categoria, Titulo, Ambito FROM Novedades WHERE Version = '1.10.22.2' ORDER BY Ambito, Categoria;
GO
