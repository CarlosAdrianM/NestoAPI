-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.12.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.12.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.12.0.
-- Se omiten a propósito (el usuario no las percibe o siguen en piloto):
--  - Filas indivisibles y comentario resaltado en picking/packing QuestPDF (#302): el
--    informe sigue en piloto con flag; se anunciará al extenderlo a todo el almacén.
--  - Informe Montar Kit en el servidor, ficha de cliente sin EF (1C.8 slice 4), policy
--    común de deadlocks (#288), tests huérfanos rescatados (#242): internos.
--  - Reponer el índice anti-duplicados de rapports (#298): operación de BD, no de release.

DECLARE @version VARCHAR(23) = '1.10.12.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Ya no se cobran portes al cliente porque a nosotros nos falte stock',
 'Al guardar un pedido, una línea de producto normal sin stock suficiente se descontaba de la base para calcular los portes, y un pedido que superaba de sobra el mínimo acababa con portes indebidos. Ya no ocurre; además, igual que en el picking, un pedido cuyos productos llegan a 150 EUR nunca lleva portes automáticos. Si veis pedidos recientes con portes extraños, revisadlos antes de facturar.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'El 3+1 (o N+M) de varias referencias en el mismo pedido ya se autoriza',
 'Meter la oferta de varias referencias a la vez (por ejemplo el 3+1 de cada crema de venta al público) se rechazaba con "No se encuentra autorización", aunque cada oferta fuera correcta; había que hacer un pedido por referencia. Ya se autorizan todas juntas, y las compras normales con su descuento habitual del mismo grupo ya no interfieren con la oferta.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Cuando una oferta no se autoriza, el mensaje dice el motivo real',
 'Antes salía el genérico "No se encuentra autorización para la oferta del producto X" aunque el sistema supiera exactamente qué pasaba. Ahora el mensaje explica el motivo concreto: qué referencia no se puede regalar y por qué, cuántas unidades caben gratis, el importe mínimo que falta, etc.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Los envíos de Innovatrans a ciertas poblaciones ya no fallan',
 'Los envíos a códigos postales que agrupan varias poblaciones (Avilés, San Agustín de Guadalix, Cala Rajada...) fallaban con "Canalización incorrecta" y había que sacarlos por otra agencia. Ahora, si la agencia rechaza el envío, se reintenta automáticamente con el nombre de población exacto de su catálogo; si aun así no casa, el aviso lista las poblaciones válidas de ese código postal.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Al liquidar movimientos del extracto, el aviso llega antes y con detalle',
 'Si dos movimientos no se pueden liquidar entre sí (importes del mismo signo o a cero, de clientes distintos, o el movimiento ya no existe), el aviso sale ahora antes de contabilizar y nombra el cliente, el movimiento y los importes, para localizar el problema a la primera.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Corregido', 'Error inesperado al guardar pedidos desde la app de vendedores',
 'Guardar cambios en un pedido desde la app podía fallar con un error técnico sin sentido cuando el pedido tenía vendedor de peluquería asignado. Corregido.', 'NestoAPI', SUSER_SNAME());
GO
