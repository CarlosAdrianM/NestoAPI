-- Novedades para la ventana "Qué hay nuevo" de la versión 1.10.19.3
-- Lenguaje de usuario, solo lo que el usuario percibe. La Version debe ser <= a la
-- ClickOnce publicada (1.10.19.3). Ambito: Nesto / NestoAPI. Publicada = 1 (default).
-- Ejecutar contra la BD NV al publicar Nesto 1.10.19.3.
-- Se omiten a propósito (internas o sin efecto visible para el usuario):
--  - Certificado AEAT desde el almacén de Windows + proceso de renovación (#388): interno.
--  - Listados de la ventana de Agencias servidos por la API (slice A1, Nesto#340): la
--    ventana se comporta igual.
--  - Carrera del job Verifactu con la facturación (#385), carrera al registrar
--    dispositivos push (#389), reintento por deadlock (#364, la parte de reintento),
--    saneo de ClientePrincipal (#331), área HelpPage eliminada (#334), filtro de
--    escáneres de ELMAH: internos.

DECLARE @version VARCHAR(23) = '1.10.19.3';

INSERT INTO dbo.Novedades ([Version], Categoria, Titulo, Descripcion, Ambito, Usuario) VALUES
-- ============================ NUEVO ============================
(@version, 'Nuevo', 'Informes: Balance y Cuenta de Pérdidas y Ganancias de Pymes',
 'En la pestaña Informes, grupo Contabilidad, se pueden generar el Balance Pymes (BPY) y la Cuenta de Pérdidas y Ganancias Pymes (PGP) en PDF, con el formato vertical de los modelos oficiales, comparativa con el año anterior y porcentajes de variación. Las fechas salen del selector común (Actual = año en curso, Anterior = año pasado completo) y la casilla "Incluir Global" agrega las dos empresas en un solo informe.', 'Nesto', SUSER_SNAME()),

(@version, 'Nuevo', 'Rapports: los teléfonos del cliente, a un clic',
 'Al seleccionar un cliente en el buscador de la izquierda de la ventana de Rapports aparecen sus teléfonos directamente, sin tener que cargar ni crear ningún rapport. Cada teléfono va por separado: un clic lo selecciona entero y con el botón derecho se copia, listo para pegar en la centralita. En el detalle del rapport los teléfonos funcionan igual (antes se copiaban todos juntos en una sola cadena).', 'Nesto', SUSER_SNAME()),

-- ============================ MEJORADO ============================
(@version, 'Mejorado', 'Conciliación bancaria: los abonos de remesa cuadran uno a uno con el banco',
 'El banco abona cada remesa en varios ingresos (separando los recibos nuevos FRST de los recurrentes RCUR, y por fecha de cargo). Ahora la contabilización de la remesa genera los asientos con esa misma separación y con la fecha en que el banco abona cada grupo, para que el punteo de la conciliación cuadre apunte a apunte. Las remesas antiguas se siguen viendo igual.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Remesas: las secuencias FRST/RCUR incoherentes ya no bloquean el fichero',
 'Si un cliente tenía la secuencia del mandato distinta entre sus contactos, el fichero de CUALQUIER remesa fallaba con un error técnico que no se veía (aunque ese cliente no estuviera en la remesa). Ahora, si es el mismo mandato, se unifica solo y el fichero se genera; y si de verdad hay un problema en la ficha, el aviso dice exactamente qué cliente y qué contactos hay que revisar.', 'NestoAPI', SUSER_SNAME()),

(@version, 'Mejorado', 'Aviso antes de eliminar una etiqueta de recogida',
 'Al desmarcar la casilla "Recoger producto" de un pedido, el programa pide confirmación antes de eliminar la etiqueta de recogida pendiente: si se había creado a mano en la pantalla de Agencias con dirección o reembolso personalizados, esos datos se perdían sin previo aviso. Además, si ya existe una etiqueta pendiente al marcar la casilla, el mensaje ahora lo explica claramente.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Ventana de NIF incorrectos: fuera las fichas anuladas',
 'La pantalla de clientes con NIF incorrecto ya no muestra fichas anuladas, que no hay que corregir.', 'Nesto', SUSER_SNAME()),

(@version, 'Mejorado', 'Si dos operaciones chocan en la base de datos, el programa lo resuelve solo',
 'Cuando dos contabilizaciones coinciden y chocan (interbloqueo), el servidor ahora reintenta automáticamente la operación (antes daba error a la primera). Y si aun así no puede, el mensaje de error indica con qué usuario y operación se produjo el choque, para poder coordinarse sin llamar a un administrador.', 'NestoAPI', SUSER_SNAME()),

-- ============================ CORREGIDO ============================
(@version, 'Corregido', 'El rapport a medio escribir ya no se pierde al ir a otra ventana',
 'Estando escribiendo un rapport, al abrir otra ventana (por ejemplo la Plantilla de Ventas para coger un pedido por teléfono) y volver, el cliente aparecía deseleccionado y el texto escrito se había borrado. Ahora todo queda como estaba.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Los buscadores de cliente ya no roban el foco al abrir la ventana',
 'Al abrir una ventana con buscador de clientes (Rapports, pedidos...), durante unos segundos no se podía escribir: el cursor se movía solo a otro sitio. Ahora el foco se coloca al instante y ningún buscador que cargue en segundo plano interrumpe lo que se está escribiendo.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'La ventana de conciliación ya no se queda pillada al contabilizar gastos',
 'Al contabilizar gastos de remesa desde la conciliación bancaria, la ventana podía quedarse bloqueada ("pillada") de forma intermitente. Corregido.', 'Nesto', SUSER_SNAME()),

(@version, 'Corregido', 'Dos cierres inesperados del programa',
 'Corregidos dos errores que cerraban el programa entero: pulsar "Modificar" en un pedido sin pedido cargado, y hacer doble clic justo sobre el texto de una celda en las ventanas de Comisiones y de pedidos.', 'Nesto', SUSER_SNAME());
