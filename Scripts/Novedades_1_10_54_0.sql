/*
    Novedades de la versión 1.10.54.0 de Nesto (03/11/2026).
    Nesto #587 (Sin contacto este mes), #588 (fechas futuras con la fecha de entrega de la API), #594 (Enviar reposición ALG:
    enviadas y reimprimir), #595 (aviso de comisión con condiciones de pago efectivas), #596/#597 (inventario: rellenar y
    contabilizar desde Nesto, épica NestoAPI#784), #598 (código interno al imprimir, NestoAPI#791);
    NestoAPI #777 (festivos autonómicos), #780 (picking automático no sale en festivo), #776 (códigos de barras no válidos
    limpiados), #792 (fin de mes: una factura por tipo de IVA), #785 (reembolso de la etiqueta pendiente), #796 (orden del
    bloque de cheques), #718 (IVA Global), #783 (recordatorios de respuesta), #797/Nesto#605 (muestras MMP sin código), Nesto#603 (409 y avisos de la etiqueta pendiente), Nesto#604 (casilla de eventos), Nesto#599 (detalle del modo de servicio), #782 (sync Odoo no graba códigos no válidos) y el agradecimiento a Miguel y Andre (key users).

    ORDEN:
      1) Scripts sa YA LANZADOS 03/11: Issue784_prdContabilizarInventario_PorAlmacen, Issue784_Inventarios_PorCodigoBarras,
         Issue784_InventariosFilasSueltas, Issue776_GrantDelete, Issue778, OneShots del 03/11.
      2) Scripts/Issue783_EsperandoRespuestaDe.sql (sa) ANTES de publicar NestoAPI 1dff4f39; después, reencolar en /hangfire los
         6 jobs de CorreoEntregaAgenciaJobs (144991, 144999, 145007, 145015, 145032, 145033) y volver a anunciar el evento 4.
      3) Publicar la ClickOnce (Clean + Rebuild) → 1.10.54.0 y Ariadna 2.28.
      4) DESPUÉS: ESTE script y Novedades_Ariadna_2_28.sql. Luego AvisarNuevaVersionNesto.ps1 -Version 1.10.54.0 (con el OK de Carlos).
      NO duplicar: las sugerencias 962 (Manuel) y 911 (Sancho) ya están marcadas Implementada en 1.10.54.0 por la API.

    Ejecutar en SSMS contra NV como sa. Los literales van con N'...' (columnas nvarchar).
*/

SET NOCOUNT ON;
USE NV;

DECLARE @version VARCHAR(23) = '1.10.54.0';
DECLARE @fecha DATE = '2026-11-03';

DECLARE @novedades TABLE (Categoria nvarchar(40), Titulo nvarchar(400), Descripcion nvarchar(2000), Perfiles nvarchar(200));

INSERT INTO @novedades (Categoria, Titulo, Descripcion, Perfiles) VALUES
    ('Nuevo', N'Gracias, Miguel y Andre',
     N'La furgoneta bulto a bulto, la firma con lo que se firma, el cobro con tarjeta, las reposiciones que se recogen leyendo y el packing con fotos han salido adelante porque Miguel y Andre los han probado desde el primer día, nos han contado con detalle cada cosa que no cuadraba y han tenido la paciencia de volver a probar cada arreglo. Gracias de corazón a los dos: sois nuestros «key users» de este mes. Y gracias a todos los que nos escribís por «Novedades»: lo leemos todo.',
     NULL),
    ('Nuevo', N'Inventario desde Nesto: rellenar, contar, comprobar y contabilizar',
     N'La pantalla de Inventario de las tiendas está preparada para el inventario del 21 de noviembre. Antes de contar hay que pulsar «Rellenar inventario»: Nesto trae todos los productos con stock de la tienda, así lo que no se cuente queda a cero y no se olvida nada. Al contar se puede leer el código de barras o teclear la referencia (Nesto apunta cuál de las dos cosas se hizo). Al terminar, «Contabilizar» hace primero las comprobaciones (que no queden filas de otro almacén, que no haya movimientos pendientes…) y enseña las diferencias antes de aplicarlas: a PVP para las tiendas y a coste para Compras, Almacén, Administración y Dirección. Cada almacén se contabiliza por separado: ya no se puede arrastrar sin querer el stock de otra tienda.',
     N'Tiendas,Almacén,Compras,Administración,Dirección'),
    ('Nuevo', N'Clientes para contactar: «Sin contacto este mes»',
     N'En la lista de clientes para contactar, los que no han tenido ningún contacto (llamada, visita o rapport de cualquier compañero) desde el día 1 salen con un punto naranja y el texto «Sin contacto este mes», para verlos de un vistazo. Gracias, Sancho, por la idea.',
     N'Vendedores,Tiendas,Dirección'),
    ('Mejorado', N'Clientes para contactar: el orden de los cheques regalo',
     N'Los clientes con cheque regalo pendiente de canjear siguen saliendo todos en prioridad máxima hasta que caduque el cheque (no se salta a ninguno), pero dentro de ese bloque van primero los que llevan más días sin contacto, y los que se contactaron ayer, por cualquier compañero, pasan al final con el motivo «(hablado ayer)». Así no se repite la misma llamada dos días seguidos. Gracias, Alberto.',
     N'Vendedores,Tiendas,Dirección'),
    ('Mejorado', N'Enviar reposición con Algete: las enviadas y «Reimprimir etiquetas»',
     N'En «Enviar reposición», con el almacén Algete, se ven también las reposiciones ya enviadas y pendientes de recibir, aunque el envío se haya hecho desde Ariadna, y con «Reimprimir etiquetas» se vuelven a imprimir las etiquetas de los bultos y el impreso si la impresora falló.',
     N'Almacén,Tiendas'),
    ('Mejorado', N'Lista de pedidos: «fechas futuras» de verdad',
     N'El aviso de fechas futuras de la lista de pedidos lo calcula ahora el servidor con la misma fecha de entrega que usa el reparto (incluido el día de ruta de nuestra furgoneta), así coincide con lo que de verdad se va a servir.',
     N'Vendedores,Tiendas,Almacén'),
    ('Corregido', N'Festivos: el 2 de noviembre y las fiestas autonómicas',
     N'El 2 de noviembre era festivo en Madrid y Nesto no lo sabía: la fecha de entrega de los pedidos y el vencimiento de la remesa lo contaban como laborable. Ya cuentan todas las fiestas autonómicas de 2026 y, en festivo, no sale el picking automático ni los avisos de la furgoneta ni de proveedores con fecha vencida.',
     N'Vendedores,Tiendas,Almacén,Administración'),
    ('Corregido', N'Plantilla: aviso de comisión por reembolso con el cliente que paga por banco',
     N'Al desmarcar «servir junto» en la plantilla, el aviso de que el reembolso lleva comisión salía aunque el cliente pagara por banco. Ahora la plantilla mira las condiciones de pago que se aplican de verdad a ese pedido antes de avisar. Gracias, Manuel.',
     N'Vendedores,Tiendas'),
    ('Corregido', N'Pedidos con IVA «Global»: las líneas nuevas ya no salen al 21 %',
     N'Al añadir líneas, portes o la comisión de reembolso a un pedido con IVA Global (empresa 3), las nuevas salían al 21 % y las viejas a cero. Ahora todas las líneas llevan el IVA de la cabecera y el reembolso de la etiqueta es lo que cobrará la factura.',
     N'Vendedores,Tiendas,Administración'),
    ('Corregido', N'Reembolso de una etiqueta pendiente creada antes del picking',
     N'Si la etiqueta se creó a 0 € antes de preparar el pedido y se factura después, al registrarla se recalcula el reembolso con la factura. Y una etiqueta a 0 € de un pedido contra reembolso con factura pendiente de cobro ya no se imprime: Nesto avisa para que se revise el reembolso.',
     N'Almacén,Administración'),
    ('Mejorado', N'Códigos de barras no válidos, limpiados',
     N'Se han dejado en blanco los códigos de barras que no eran tales («0», «1», la propia referencia, EAN con el dígito de control mal…) en todas las empresas: 2.760 productos. A cada uno se le dará su código interno (empieza por 29) la primera vez que se imprima su etiqueta. Gracias, Compras, por la lista.',
     N'Compras,Almacén,Tiendas'),
    ('Mejorado', N'Muestras sin código de barras: se mandan y se reciben sin etiqueta',
     N'Las muestras (subgrupo MMP) que no traen código de barras del fabricante ya no hay que etiquetarlas. En «Enviar reposición» no salen como «sin código: imprima su etiqueta» y se mandan tal cual, y al recibir la reposición se cuentan tecleando su referencia o la cantidad, sin pasar por «Sin etiqueta». Lo pidió Almacén.',
     N'Almacén,Tiendas,Compras'),
    ('Mejorado', N'Etiqueta pendiente: el reembolso recalculado se ve al registrarla',
     N'Al registrar una etiqueta que se creó pendiente antes del picking, Nesto enseña el aviso de reembolso recalculado y la etiqueta sale con el importe bueno. Si el pedido es contra reembolso, la etiqueta va a 0 € y la factura está pendiente de cobro, Nesto no la imprime: enseña el motivo y deja el envío en pendientes para que se le ponga el reembolso.',
     N'Almacén,Administración'),
    ('Mejorado', N'Plantilla: por qué el pedido sale «Todo junto», producto a producto',
     N'La línea gris bajo «Servir» que explica el modo de servicio propuesto lleva ahora un tooltip con el detalle por producto: cuál sale desmontando un kit (y cuál), cuál llega con la reposición de una tienda y cuándo puede salir, cuál espera al proveedor y con qué fecha. Así se ve que no es un fallo cuando no se puede elegir «Ahora lo que tenemos». Gracias, Manuel.',
     N'Vendedores,Tiendas'),
    ('Mejorado', N'Novedades: los recordatorios van a quien se le hizo la pregunta',
     N'Cuando una novedad queda «esperando respuesta», el recordatorio del día siguiente le llega a la persona a la que se le preguntó, no a quien la creó.',
     NULL);

INSERT INTO Novedades (Version, Fecha, Categoria, Titulo, Descripcion, Ambito, Publicada, Usuario, Perfiles)
SELECT @version, @fecha, n.Categoria, n.Titulo, n.Descripcion, 'Nesto', 1, 'sa', n.Perfiles
FROM @novedades n
WHERE NOT EXISTS (SELECT 1 FROM Novedades x WHERE x.Version = @version AND x.Titulo = n.Titulo);

SELECT @@ROWCOUNT AS NovedadesNestoInsertadasAhora;
