/*
    Novedades de la versión 1.10.25.0 (01/09/2026).

    Sube la tercera cifra porque la ventana de Ofertas cambia de nombre y de alcance
    (Ofertas y descuentos, con precio fijo) y el menú estrena los 16 iconos propios.

    SE OMITE TODO LO DEMÁS A PROPÓSITO (nada de esto lo nota el usuario de Nesto):
      - El arreglo del pedido desde la app de clientes (RequestContext): duró un día y desde
        fuera se ve como que "vuelve a funcionar"; se cuenta al equipo de la app directamente.
      - La puerta de publicación de productos hacia la tienda (#432): maquinaria interna del
        pipeline; lo notará quien mire la tienda, no quien use Nesto.
      - El apunte del reembolso por la puerta canónica (#431), el encolado por lotes (#433 —
        el usuario solo nota que marcar una familia grande ya no se queda pensando, y eso va
        en la entrada de abajo), la constante del estado 8 (#424), el prestashop-login (#426),
        el Trim del email del login de la tienda (#425) y el recorte del DTO (#428/#429):
        internos o para clientes de la tienda, no para usuarios de Nesto.
      - Las slices A3 de agencias (#340): refactor sin cambio visible.
      - El filtro corto del buscador de clientes (#440): mismo mensaje para el usuario, solo
        cambia el ruido de ELMAH.

    Ejecutar en SSMS contra NV DESPUÉS de publicar.
*/

SET NOCOUNT ON;
USE NV;

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario)
VALUES
('1.10.25.0', '2026-09-01', 'Nuevo', 'La ventana de Ofertas pasa a llamarse "Ofertas y descuentos" y estrena el precio fijo',
 'La ventana de Ofertas Combinadas ya tenia de todo (combinadas, por familia, escalonadas, campanas y ofertas de producto), asi que pasa a llamarse "Ofertas y descuentos" en el menu. Y la pestana de Campanas estrena la columna "Precio fijo": ademas de un % de descuento, ahora se puede decir "este producto a 10 euros", con sus fechas desde y hasta, y el precio deja de aplicarse solo el dia que toca. El precio fijo va en euros (no en %), solo puede ponerse a productos concretos (no a familias enteras) y se aplica unicamente si es menor que el precio que saldria sin el.', 'Nesto', 1, 'sa'),

('1.10.25.0', '2026-09-01', 'Nuevo', 'Al crear un contacto, Nesto ofrece copiar los datos del principal',
 'Al crear un contacto nuevo de un cliente que ya existe, Nesto pregunta si se quieren copiar las personas de contacto (el correo de las facturas, por ejemplo) y las cuentas bancarias del contacto principal. Hasta ahora habia que pedirselo a administracion por correo y copiarlo a mano. Se puede aceptar sin miedo: si algo ya estaba copiado, no se duplica.', 'Nesto', 1, 'sa'),

('1.10.25.0', '2026-09-01', 'Nuevo', 'Los jefes de ventas ya pueden cambiar estado y vendedor de los clientes de su equipo',
 'Desde Vendedores - Ficha, un jefe de ventas puede cambiar el estado y el vendedor de los clientes de su equipo sin pedirselo a informatica. El desplegable de vendedores le ensena solo su equipo (mas el generico NV), y si el cliente lo lleva alguien de fuera de su equipo, los campos salen deshabilitados con el motivo. Administracion sigue viendo y pudiendo todo, como siempre.', 'Nesto', 1, 'sa'),

('1.10.25.0', '2026-09-01', 'Mejorado', 'Cada boton del menu tiene ya su propio icono',
 'Habia 21 botones del menu compartiendo icono con otro (ocho opciones distintas con el muneco azul del cliente, por ejemplo), con lo que el icono no servia para encontrar nada. Ahora cada boton tiene el suyo: los alquileres llevan su aparato con el reloj, las remesas sus recibos, las agencias su paquete, los bancos su edificio... Encontrar una opcion de un vistazo vuelve a ser posible.', 'Nesto', 1, 'sa'),

('1.10.25.0', '2026-09-01', 'Nuevo', 'El picking respeta los dias que el cliente cierra',
 'Si un cliente tiene marcado en su ficha que cierra un dia de la semana (por ejemplo los lunes), sus pedidos ya no cogen picking cuando la entrega caeria en ese dia: se quedan pendientes y salen solos en la primera pasada cuya entrega caiga en dia abierto. Cuando un pedido se retiene por esto, el usuario del pedido recibe un correo con el motivo (con copia a administracion). Para forzar la salida de un pedido concreto basta con ponerle una fecha de entrega. La marca de dias viene del Nesto antiguo: si algun cliente tiene un dato desfasado, avisad para corregirlo.', 'Nesto', 1, 'sa'),

('1.10.25.0', '2026-09-01', 'Mejorado', 'Marcar una familia grande ya no deja la pantalla pensando',
 'Al marcar una familia en el mantenimiento de familias (o al guardar una campana que alcanza a muchos productos), Nesto avisaba a la tienda producto a producto: con una familia de cientos de referencias la pantalla se quedaba un buen rato pensando y a veces se quedaba a medias. Ahora se avisa de todos de una vez y la operacion es inmediata, sea cual sea el tamano de la familia.', 'Nesto', 1, 'sa');

SELECT Version, Titulo FROM Novedades WHERE Version = '1.10.25.0';
