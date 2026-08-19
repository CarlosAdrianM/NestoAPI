-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.20.0
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.20.0). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.20.0.
-- Se omiten a propósito (internas o sin efecto visible para el usuario):
--  - Reclasificación contable de Amazon (555 -> 440/proveedor 999, #390) y el cambio de
--    cuenta de las promociones: asientos internos, la operativa de la ventana no cambia.
--  - CRUD de envíos de agencias por la API (slice A2, Nesto#340) y el DTO completo de los
--    listados (Nesto#448): la ventana se comporta igual (los fixes visibles van aparte).
--  - GRANT de EnviosHistoria, alta de series RV/RC, filtro de escáneres (help): internos.
--  - Exclusión de los clientes de facturas simplificadas del listado de NIF incorrectos:
--    limpieza del listado, sin acción del usuario.

DECLARE @version VARCHAR(23) = '1.10.20.0';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Plantilla de ventas: cambiar de almacén a mitad de pedido',
 'Ya se puede cambiar el almacén aunque el pedido tenga líneas metidas: los stocks y los colores se recalculan al momento para el nuevo almacén sin perder las cantidades introducidas. Si alguna línea del pedido se queda sin stock suficiente en el almacén nuevo, sale un aviso con el detalle (qué producto, cuántas pedidas y cuántas disponibles).', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'NIF incorrectos: marcar un cliente como "no censado"',
 'Para los casos en que se facturó por error a una ficha y no hay forma de conseguir el NIF real del cliente (ni de contactar con él): un botón nuevo marca la ficha como "no censado" y sus facturas pendientes se declaran a Verifactu como destinatario no censado, dejando de dar errores a diario. Es el último recurso: si el NIF se puede conseguir, se usa "Corregir NIF" como siempre.', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Balances y Pérdidas y Ganancias: fechas a mes cerrado',
 'Los informes de Balance y de Pérdidas y Ganancias se piden ahora a mes cerrado: "Actual" va del 1 de enero al último día del mes anterior, y "Anterior" hasta el último día de dos meses atrás, para comparar dos cierres mensuales. En enero, "Actual" es el año pasado completo. "Personalizar" sigue funcionando igual.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'NIF incorrectos: el país intracomunitario viene sugerido',
 'Cuando el NIF de la ficha parece un NIF-IVA intracomunitario (por ejemplo IT0280027), la pantalla lo detecta: muestra el país sugerido en una columna nueva y, al seleccionar la fila, deja preseleccionados el tipo (NIF-IVA) y el país, de forma que "Marcar como extranjero" queda a un solo clic. La decisión sigue siendo de quien revisa: se puede cambiar o ignorar.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Las facturas rectificativas salen en su propia serie (RV / RC)',
 'Al crear una rectificativa desde "Copiar factura", el pedido copiado sale ya en la serie de rectificativas (RV para la serie general, RC para cursos), como exige el reglamento de facturación, y se declara a Verifactu como rectificativa con sus facturas vinculadas. Las copias normales mantienen su serie de siempre.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Crear cliente: página de datos fiscales mejor alineada',
 'El campo País tiene ahora su propia línea, con la misma separación y alineación que NIF y Nombre Fiscal (antes quedaba pegado al NIF y descentrado).', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Agencias: volver a modificar el reembolso de un envío tramitado',
 'Al cambiar el reembolso de un envío ya tramitado (por ejemplo, quitárselo), el programa daba "se ha producido un error" sin más explicación y no guardaba nada. Ya funciona, y si algo fallara, el mensaje ahora dice el motivo real y el error queda registrado para informática.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Agencias: tramitar envíos de ASM/GLS volvía a fallar',
 'Desde la versión anterior, todos los envíos de ASM daban "Sequence contains no matching elements" al tramitarlos (los de Innovatrans no). Corregido; además, los errores de tramitación quedan ahora registrados para poder diagnosticarlos sin repetir la operación.', 'Nesto', SUSER_SNAME());
