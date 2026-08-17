-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.19.2
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.19.2). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.19.2.
-- Se omiten a propósito (internas o sin efecto visible todavía para el usuario):
--  - Verifactu en sombra: autocurado del nombre fiscal (#383), vinculaciones LIFO al
--    facturar rectificativas a mano (#38), series EV/UL declarando y cliente MATERIALES
--    CURSOS como simplificada — todo sandbox, sin efecto fiscal hasta el 01/12/26.
--  - Barrido del filtro de escáneres de ELMAH (#336): interno.
--  - Endpoint BuscarPago y CanalesExternosPagosService sin EF (Nesto#340): interno,
--    la pantalla de pagos se comporta igual.
--  - Idempotencia y reintento por deadlock server-side (#384): el usuario lo percibe a
--    través de la entrada de "gastos de remesa" de abajo.

DECLARE @version VARCHAR(23) = '1.10.19.2';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Remesas: el aviso de movimientos negativos muestra el detalle',
 'Al crear una remesa, si un cliente tiene movimientos negativos pendientes el aviso muestra ahora QUÉ son (importe, concepto y fecha de cada uno, por ejemplo "-162,00 € S/Pago a cuenta reserva curso"), para decidir con criterio si conviene liquidarlos o no. El mensaje deja claro que los recibos seleccionados se remesarán al banco igualmente al continuar; cancelar es solo para ir a liquidar primero, y ya no se sugiere desmarcar efectos (eso dejaba recibos sin cobrar).', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Conciliación bancaria: los gastos de remesa cuadran con los apuntes del banco',
 'Desde que las remesas van por vencimientos, el banco carga las comisiones en varios apuntes (uno por cada abono) y no en dos. Al contabilizar los gastos, ahora se crea una factura por CADA apunte del banco, con su número de factura real, para que el punteo cuadre uno a uno. Sigue bastando con seleccionar un solo apunte: el programa localiza los demás de la misma remesa. Además, si algo falla a mitad, volver a pulsar el botón crea solo las facturas que falten, sin duplicar las ya contabilizadas.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Cargar fichero del banco (cuaderno 43): avisos claros en vez de error técnico',
 'Si el fichero era de una cuenta no dada de alta en Bancos, venía vacío o ya estaba contabilizado, aparecía un error técnico sin pista ("La secuencia no contiene elementos"). Ahora el aviso dice exactamente qué pasa y qué hay que hacer.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Agencias: los envíos internacionales cambian automáticamente a ASM/GLS',
 'La agencia Sending, sin uso desde febrero, se ha retirado del programa. El cambio automático de agencia en pedidos con país extranjero, que antes apuntaba a Sending, ahora selecciona ASM/GLS. Los envíos históricos de Sending se siguen viendo con normalidad.', 'Nesto', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'Verifactu (pruebas): menos avisos repetidos por fichas con el nombre cambiado',
 'Cuando un cliente cambia de apellidos (por ejemplo al casarse) y la AEAT rechazaba sus facturas en la fase de pruebas de Verifactu, el aviso se repetía a diario aunque se corrigiera el NIF de la ficha. Ahora, al corregir la ficha se corrige también el nombre en las facturas pendientes, y si el nombre correcto ya se conoce, el programa lo aplica solo.', 'NestoAPI', SUSER_SNAME());
