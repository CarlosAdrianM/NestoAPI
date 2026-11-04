/*
    Novedades de Ariadna 2.29 (04/11/26): Ariadna#85 (cerrar caja del reparto, NestoAPI#802), #86 (Cobrar una deuda: pregunta antes
    de dejarlo a cuenta), #87 («Leídas 2 de 2 · quedan 0»), #88 (siguiente caja en el packing), #89 (botón Leer a la derecha),
    #90 (familia y tamaño en la línea destacada); API: #803 (etiqueta de reparto propio desde el packing), #805 (picking colgado),
    #812 (picking entre dos PDA), Ariadna#90 API (familia y tamaño en Enviar reposición).
    Necesita la API del 04/11 (master tras 620ff191).
    Ejecutar en SSMS contra NV como sa cuando la 2.29 llegue a la prueba cerrada. Idempotente (compara por Ámbito + Título).
    NO duplicar: las sugerencias 958, 965, 968, 1004, 1007, 1008, 1012 (Ariadna#92), 1015 y 1018 (Ariadna#93) y 1017 (la etiqueta
    vuelve a facturar) las marca Implementadas en 2.29 el OneShot (la sugerencia ES la novedad); aquí no llevan fila.
*/
SET NOCOUNT ON;
USE NV;
DECLARE @n TABLE (Orden int, Categoria nvarchar(20), Titulo nvarchar(200), Descripcion nvarchar(max), Perfiles nvarchar(200));
INSERT INTO @n VALUES
(1, N'Nuevo', N'Gracias, Paloma y Andre',
 N'El botón «Leer» a la derecha, la familia y el tamaño en la línea, el texto de «quedan» que se entendía al revés, la siguiente caja en el packing y los productos que salían a los dos en el picking vienen de lo que Paloma y Andre nos han contado hoy por «Novedades», con captura. Gracias a los dos, y a Alfredo y Miguel, que siguen probándolo todo.',
 NULL),
(2, N'Nuevo', N'Reparto: cerrar la caja',
 N'En Reparto, menú «⋯» → «Cerrar caja» (o F8 en «Mis cobros»): Ariadna enseña lo que dice la contabilidad de su caja, por empresa y en total, y le pide cuánto dinero lleva. Si coincide, se hace el traspaso a Administración (uno por empresa) y su caja queda a cero; si no coincide, no se cierra y hay que verlo con Administración antes. Hace falta cobertura y que no queden entregas o cobros sin subir.',
 N'Almacén'),
(3, N'Mejorado', N'Cobrar una deuda: con facturas pendientes, pregunta antes de dejarlo todo a cuenta',
 N'Si el cliente tiene facturas pendientes y pulsa Cobrar sin marcar ninguna, Ariadna pregunta «¿Dejar los X € a cuenta?»: Intro o F2 marca todas (lo normal) y F7 lo deja a cuenta de verdad. Así un cobro no se queda sin liquidar por olvidar marcar la factura.',
 N'Almacén'),
(4, N'Corregido', N'Picking: Terminar ya no se bloquea por una falta de un producto que se quitó del pedido',
 N'Si se da por falta un producto cuya línea alguien ha sacado del pedido después de crear el picking, Terminar ya no da «no hay tanto que sacar»: la falta se ignora y el picking se cierra. Fue lo del picking 99998 esta mañana.',
 N'Almacén'),
(6, N'Corregido', N'Picking entre dos PDA: lo que lee el compañero se descuenta de su parada',
 N'Cuando dos PDA preparan el mismo picking, lo que lee una se apunta en la otra en la parada de la que se leyó (antes iba a la primera parada del producto, y los pendientes de ubicar salían a los dos). Fue lo del picking 100001. Lo avisó Alfredo.',
 N'Almacén'),
(7, N'Mejorado', N'Recibir: los avisos de la reposición se ven al abrirla',
 N'Si algún producto de la reposición se regularizó por inventario en el destino después de enviarla, al abrir la recepción Ariadna lo avisa («compruebe que no se cuenta dos veces»), igual que lo hace Nesto. Y si su Ariadna se queda anticuada, la propia app se lo dirá con un botón para actualizarla desde Play Store.',
 N'Almacén,Tiendas'),
(5, N'Nuevo', N'Packing: la etiqueta de un pedido de nuestra ruta',
 N'En Empaquetar, F8 «Etiqueta de agencia» también vale para los pedidos que van por reparto propio: crea el envío y saca las etiquetas de bulto RP, igual que las de GLS o CTT. Antes decía «la agencia 14 no se puede insertar desde el servidor».',
 N'Almacén'),
(9, N'Nuevo', N'Reparto: paradas de solo cobro (NC)',
 N'Las notas de cobro que crean desde Nesto salen en el reparto como «COBRO · NC…» con el importe y los efectos pendientes, sin bultos: «Cobrar» abre el cobro con el efectivo y el importe ya puestos (F6 tarjeta si se admite, F7 autorización sin cobro) y el cliente firma «Confirmo que he pagado…». Leer un NC abre su parada; no se carga en la furgoneta.',
 N'Almacén');

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT '2.29', '2026-11-04', n.Categoria, n.Titulo, n.Descripcion, 'Ariadna', 1, 'sa', n.Perfiles
FROM @n n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Ambito = 'Ariadna' AND x.Titulo = n.Titulo)
ORDER BY n.Orden;
SELECT @@ROWCOUNT AS NovedadesAriadnaInsertadasAhora;
